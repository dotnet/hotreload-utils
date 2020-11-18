using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
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


        public static async Task<RoslynBaselineMsbuildProject> Make (Config config) {
            (var solution, var projectId, var documentId) = await PrepareMSBuildProject(config);
            return new RoslynBaselineMsbuildProject(solution, projectId, documentId);
        }

        static async Task<(Solution, ProjectId, DocumentId)> PrepareMSBuildProject (Config config)
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
                    var project = await msw.OpenProjectAsync (config.ProjectPath);
                    var baselinePath = Path.GetFullPath (config.SourcePath);

                    var baselineDocumentId = project.Documents.Where((doc) => doc.FilePath == baselinePath).First().Id;
                    return (msw.CurrentSolution, project.Id, baselineDocumentId);
        }


        public override Task<BaselineArtifacts> PrepareBaseline () {
            var project = solution.GetProject(projectId)!;

            if (!ConsumeBaseline (project, out string? outputAsm, out EmitBaseline? emitBaseline))
                    throw new Exception ("could not consume baseline");
            var artifacts = new BaselineArtifacts() {
                baselineSolution = solution,
                baselineProjectId = projectId,
                baselineDocumentId = _baselineDocumentId,
                baselineOutputAsmPath = outputAsm,
                emitBaseline = emitBaseline
            };
            return Task.FromResult(artifacts);

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
