using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;

namespace Diffy
{
    public class RoslynBaselineMsbuildProject : RoslynBaselineProject {

        readonly DocumentId _baselineDocumentId;
        private RoslynBaselineMsbuildProject (Solution solution, ProjectId projectId, DocumentId documentId)
            : base (solution, projectId) {
                _baselineDocumentId = documentId;
            }


        public static async Task<RoslynBaselineMsbuildProject> Make (Config config, CancellationToken ct = default) {
            (var solution, var projectId, var documentId) = await PrepareMSBuildProject(config, ct);
            return new RoslynBaselineMsbuildProject(solution, projectId, documentId);
        }

        static async Task<(Solution, ProjectId, DocumentId)> PrepareMSBuildProject (Config config, CancellationToken ct = default)
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
                    var baselinePath = Path.GetFullPath (config.SourcePath);

                    var baselineDocumentId = project.Documents.Where((doc) => doc.FilePath == baselinePath).First().Id;
                    return (msw.CurrentSolution, project.Id, baselineDocumentId);
        }


        public async override Task<BaselineArtifacts> PrepareBaseline (CancellationToken ct = default) {
            var project = solution.GetProject(projectId)!;

            // gets a snapshot of the text of the baseline document in memory
            // without this, roslyn doesn't appear to read the text until
            // the document is really needed for the first time (when building a delta),
            // at which point it may have already been changed on disk to a newer version.
            var t = Task.Run (async () => {
                var doc = solution.GetDocument(_baselineDocumentId);
                if (doc != null)
                    await doc.GetTextAsync(ct);

            }, ct);
            if (!ConsumeBaseline (project, out string? outputAsm, out EmitBaseline? emitBaseline))
                    throw new Exception ("could not consume baseline");
            var artifacts = new BaselineArtifacts() {
                baselineSolution = solution,
                baselineProjectId = projectId,
                baselineDocumentId = _baselineDocumentId,
                baselineOutputAsmPath = outputAsm,
                emitBaseline = emitBaseline
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
