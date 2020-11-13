using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace Diffy
{
    public class RoslynBaselineAdhocProject : RoslynBaselineProject
    {

        readonly DocumentId _documentId;
        readonly string _outputAsm;
        readonly string _outputPdb;

        private RoslynBaselineAdhocProject (Solution solution, ProjectId projectId, DocumentId documentId,
                                            string outputAsm, string outputPdb) : base (solution, projectId) {
            _documentId = documentId;
            _outputAsm = outputAsm;
            _outputPdb = outputPdb;
        }


        public static RoslynBaselineProject Make (Config config)
        {
            var (solution, projectId, baselineDocumentId) = PrepareAdhocProject (config);

            Directory.CreateDirectory(config.OutputDir);

            // FIXME: this is a mess
            //   1. this path stuff is part of the config, the defaults are just derived from the other args
            //   2. this hardcodes the assumption that the assembly name and the baseline source filename are the same.
            var projectName = config.ProjectName;
            var outputAsm = Path.Combine (config.OutputDir, projectName + ".dll");
            var outputPdb = Path.Combine (config.OutputDir, projectName + ".pdb");

            return new RoslynBaselineAdhocProject(solution, projectId, baselineDocumentId, outputAsm, outputPdb);
        }

        public async override Task<BaselineArtifacts> PrepareBaseline () {

            var baseline = await BuildBaseline ();

            var artifacts = new BaselineArtifacts() {
                baselineSolution = solution,
                baselineProjectId = projectId,
                baselineDocumentId = _documentId,
                baselineOutputAsmPath = _outputAsm,
                emitBaseline = baseline
            };
            return artifacts;
        }

        static (Solution, ProjectId, DocumentId) PrepareAdhocProject (Diffy.Config config)
        {
            Project project;
            {
                var adhoc = new AdhocWorkspace();
                project = adhoc.AddProject (config.ProjectName, LanguageNames.CSharp);
            }
            if (!config.Barebones) {
                switch (config.TfmType) {
                    case Diffy.TfmType.Netcore:
                        var spcPath = typeof(object).Assembly.Location;
                        var spcBase = Path.GetDirectoryName (spcPath)!;
                        if (config.BclBase != null)
                            spcBase = config.BclBase;
                        project = project.AddMetadataReference (MetadataReference.CreateFromFile (Path.Combine (spcBase, "System.Private.CoreLib.dll")));
                        project = project.AddMetadataReference (MetadataReference.CreateFromFile (Path.Combine (spcBase, "System.Runtime.dll")));
                        project = project.AddMetadataReference (MetadataReference.CreateFromFile (Path.Combine (spcBase, "System.dll")));
                        project = project.AddMetadataReference (MetadataReference.CreateFromFile (Path.Combine (spcBase, "System.Console.dll")));
                        project = project.AddMetadataReference (MetadataReference.CreateFromFile (Path.Combine (spcBase, "System.Linq.dll")));
                        break;
                    case Diffy.TfmType.MonoMono:
                        if (config.BclBase == null)
                            throw new Exception ("bcl base not specified for MonoMono compilation");
                        project = project.AddMetadataReference (MetadataReference.CreateFromFile (Path.Combine(config.BclBase, "mscorlib.dll")));
                        project = project.AddMetadataReference (MetadataReference.CreateFromFile (Path.Combine(config.BclBase, "System.Core.dll")));
                        project = project.AddMetadataReference (MetadataReference.CreateFromFile (Path.Combine(config.BclBase, "System.dll")));
                        break;
                    default:
                        throw new Exception($"unexpected TfmType {config.TfmType}");
                }
            }

            foreach (string lib in config.Libs) {
                project = project.AddMetadataReference (MetadataReference.CreateFromFile (lib));
            }
            project = project.WithCompilationOptions (new CSharpCompilationOptions (config.OutputKind));
            var baselinePath = Path.GetFullPath (config.SourcePath);
            DocumentId baselineDocumentId;
            using (var source = File.OpenRead(baselinePath)) {
                var document = project.AddDocument (name: config.Filename, text: SourceText.From (source, Encoding.UTF8),
                                                    folders: null, filePath: baselinePath);
                project = document.Project;
                baselineDocumentId = document.Id;
            }

            var solution = project.Solution;

            return (solution, project.Id, baselineDocumentId);
        }

        async Task<EmitBaseline> BuildBaseline ()
        {
            Console.WriteLine ("Building baseline...");

            var project = solution.GetProject(projectId)!;
            var outputAsm = _outputAsm;
            var outputPdb = _outputPdb;

            var compilation = await project.GetCompilationAsync();
            if (!RoslynDeltaProject.CheckCompilationDiagnostics (compilation, "base")) {
                throw new AdhocBaselineException (exitStatus: 3);
            }

            var baselineImage = new MemoryStream();
            var baselinePdb = new MemoryStream();
            EmitResult result = compilation.Emit (baselineImage, baselinePdb);
            if (!Diffy.RoslynDeltaProject.CheckEmitResult(result)) {
                throw new AdhocBaselineException (exitStatus: 4);
            }

            using (var baseLineFile = File.Create(outputAsm))
            {
                baselineImage.Seek(0, SeekOrigin.Begin);
                baselineImage.CopyTo(baseLineFile);
                baseLineFile.Flush();
            }
            using (var baseLinePdbFile = File.Create(outputPdb))
            {
                baselinePdb.Seek(0, SeekOrigin.Begin);
                baselinePdb.CopyTo(baseLinePdbFile);
                baseLinePdbFile.Flush();
            }

            baselineImage.Seek(0, SeekOrigin.Begin);
            baselinePdb.Seek (0, SeekOrigin.Begin);
            var baselineMetadata = ModuleMetadata.CreateFromStream (baselineImage);

            return EmitBaseline.CreateInitialBaseline (baselineMetadata, (handle) => default);
        }
    }
}
