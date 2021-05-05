// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;

namespace Microsoft.DotNet.HotReload.Utils.Generator
{
    public class BaselineProject {

        private readonly Solution solution;

        private readonly ProjectId projectId;


        private BaselineProject (Solution solution, ProjectId projectId)
        {
            this.solution = solution;
            this.projectId = projectId;
        }


        public static async Task<BaselineProject> Make (Config config, CancellationToken ct = default) {
            (var solution, var projectId) = await PrepareMSBuildProject(config, ct);
            return new BaselineProject(solution, projectId);
        }

        static async Task<(Solution, ProjectId)> PrepareMSBuildProject (Config config, CancellationToken ct = default)
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
                    var project = await msw.OpenProjectAsync (config.ProjectPath, null, ct);

                    return (msw.CurrentSolution, project.Id);
        }


        public async Task<BaselineArtifacts> PrepareBaseline (CancellationToken ct = default) {
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
            if (!ConsumeBaseline (project, out string? outputAsm, out EmitBaseline? emitBaseline))
                    throw new Exception ("could not consume baseline");
            var artifacts = new BaselineArtifacts() {
                baselineSolution = solution,
                baselineProjectId = projectId,
                baselineOutputAsmPath = outputAsm,
                emitBaseline = emitBaseline,
                docResolver = new DocResolver (project)
            };
            await t;
            return artifacts;

        }

        static bool ConsumeBaseline (Project project, [NotNullWhen(true)] out string? outputAsm, [NotNullWhen(true)] out EmitBaseline? baseline)
        {
            baseline = null;
            outputAsm = project.OutputFilePath;
            if (outputAsm == null) {
                Console.Error.WriteLine ("msbuild project doesn't have an output path");
                return false;
            }
            if (!File.Exists(outputAsm)) {
                Console.Error.WriteLine ("msbuild project output assembly {0} doesn't exist.  Build the project first", outputAsm);
                return false;
            }

            var baselineMetadata = ModuleMetadata.CreateFromFile(outputAsm);
            baseline = EmitBaseline.CreateInitialBaseline(baselineMetadata, (handle) => default);
            return true;
        }
    }
}
