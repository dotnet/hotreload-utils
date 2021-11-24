// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;

namespace Microsoft.DotNet.HotReload.Utils.Generator;

public class BaselineProject {

    private readonly Solution solution;

    private readonly ProjectId projectId;

    private readonly EnC.ChangeMakerService changeMakerService;

    private BaselineProject (EnC.ChangeMakerService changeMakerService, Solution solution, ProjectId projectId)
    {
        this.solution = solution;
        this.projectId = projectId;
        this.changeMakerService = changeMakerService;
    }


    public static async Task<BaselineProject> Make (Config config, EnC.EditAndContinueCapabilities capabilities, CancellationToken ct = default) {
        (var changeMakerService, var solution, var projectId) = await PrepareMSBuildProject(config, capabilities, ct);
        return new BaselineProject(changeMakerService, solution, projectId);
    }

    static async Task<(EnC.ChangeMakerService, Solution, ProjectId)> PrepareMSBuildProject (Config config, EnC.EditAndContinueCapabilities capabilities, CancellationToken ct = default)
    {
                Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace msw;
                // https://stackoverflow.com/questions/43386267/roslyn-project-configuration says I have to specify at least a Configuration property
                // to get an output path, is that true?
                var props = new Dictionary<string,string> (config.Properties);
                msw = Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace.Create(props);
                msw.LoadMetadataForReferencedProjects = true;
                msw.WorkspaceFailed += (_sender, diag) => {
                    bool warning = diag.Diagnostic.Kind == WorkspaceDiagnosticKind.Warning;
                    if (!warning)
                        Console.WriteLine ($"msbuild failed opening project {config.ProjectPath}");
                    Console.WriteLine ($"MSBuildWorkspace {diag.Diagnostic.Kind}: {diag.Diagnostic.Message}");
                    if (!warning)
                        throw new DiffyException ("failed workspace", 1);
                };
                Microsoft.Build.Framework.ILogger? logger = null;
#if false
                logger = new Microsoft.Build.Logging.BinaryLogger () {
                    Parameters = "/tmp/enc.binlog"
                };
#endif
                var project = await msw.OpenProjectAsync (config.ProjectPath, logger, null, ct);

                return (new EnC.ChangeMakerService(msw.Services, capabilities), msw.CurrentSolution, project.Id);
    }


    public async Task<BaselineArtifacts> PrepareBaseline (CancellationToken ct = default) {
        await changeMakerService.StartSessionAsync(solution, ct);
        var project = solution.GetProject(projectId)!;

        // gets a snapshot of the text of the baseline document in memory
        // without this, roslyn doesn't appear to read the text until
        // the document is really needed for the first time (when building a delta),
        // at which point it may have already been changed on disk to a newer version.
        var t = Task.Run (async () => {
            foreach (var doc in project.Documents) {
                await doc.GetTextAsync();
                if (ct.IsCancellationRequested)
                    break;
            }
        }, ct);
        if (!ConsumeBaseline (project, out string? outputAsm))
                throw new Exception ("could not consume baseline");
        var artifacts = new BaselineArtifacts() {
            baselineSolution = solution,
            baselineProjectId = projectId,
            baselineOutputAsmPath = outputAsm,
            docResolver = new DocResolver (project),
            changeMakerService = changeMakerService
        };
        await t;
        return artifacts;

    }

    static bool ConsumeBaseline (Project project, [NotNullWhen(true)] out string? outputAsm)
    {
        outputAsm = project.OutputFilePath;
        if (outputAsm == null) {
            Console.Error.WriteLine ("msbuild project doesn't have an output path");
            return false;
        }
        if (!File.Exists(outputAsm)) {
            Console.Error.WriteLine ("msbuild project output assembly {0} doesn't exist.  Build the project first", outputAsm);
            return false;
        }
        return true;
    }
}
