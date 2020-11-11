using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using Document = Microsoft.CodeAnalysis.Document;

namespace RoslynILDiff
{
    /*
     * Limitations:
     * - Only works for changes to a single .cs file
     * - Doesn't understand .csproj
     */
    class Program
    {


        static int Main(string[] args)
        {
            if (!ParseArgs (args, out var config))
                return 2;

            var baselinePath = Path.GetFullPath (config.SourcePath);
            var filename = config.Filename;
            var filenameNoExt = Path.GetFileNameWithoutExtension(filename);
            var outputAsm = Path.Combine (config.OutputDir, filenameNoExt + ".dll");
            var outputPdb = Path.Combine (config.OutputDir, filenameNoExt + ".pdb");

            Directory.CreateDirectory(config.OutputDir);

            var project = PrepareProject (config, filenameNoExt).Result;

            int exitStatus = 0;

            if (!BuildBaseline (project, outputAsm, outputPdb, out EmitBaseline? baseline, out exitStatus))
                return exitStatus;



            var baseDocumnetId = project.Documents.Where((doc) => doc.FilePath == baselinePath).First().Id;
            int rev = 1;
            foreach (var file in config.DeltaFiles) {
                List<SemanticEdit> edits = new List<SemanticEdit> ();
                if (!BuildDelta (ref project, ref baseline, edits, outputAsm, baseDocumnetId, file, rev, out exitStatus))
                    return exitStatus;
                ++rev;
            }
            return 0;
        }

        private static void InitMSBuild ()
        {
            Microsoft.Build.Locator.MSBuildLocator.RegisterDefaults();
        }

        static async Task<Project> PrepareProject (Diffy.Config config, string projectName)
        {

            Project project;
            switch (config.TfmType) {
                case Diffy.TfmType.Msbuild:
                    InitMSBuild ();
                    Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace msw;
                    msw = Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace.Create();
                    msw.WorkspaceFailed += (_sender, diag) => {
                        Console.WriteLine ($"msbuild failed opening project {config.ProjectPath}");
                        Console.WriteLine ($"{diag.Diagnostic.Kind}: {diag.Diagnostic.Message}");
                        throw new Exception ("failed workspace");
                    };
                    project = await msw.OpenProjectAsync (config.ProjectPath);
                    break;
                case Diffy.TfmType.Netcore:
                    //FIXME: hack
                    {
                        var adhoc = new AdhocWorkspace();
                        project = adhoc.AddProject (projectName, LanguageNames.CSharp);
                    }
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
                    {
                        var adhoc = new AdhocWorkspace();
                        project =  adhoc.AddProject (projectName, LanguageNames.CSharp);
                    }
                    // FIXME: hack
                    if (config.BclBase == null)
                        throw new Exception ("bcl base not specified for MonoMono compilation");
                    project = project.AddMetadataReference (MetadataReference.CreateFromFile (Path.Combine(config.BclBase, "mscorlib.dll")));
                    project = project.AddMetadataReference (MetadataReference.CreateFromFile (Path.Combine(config.BclBase, "System.Core.dll")));
                    project = project.AddMetadataReference (MetadataReference.CreateFromFile (Path.Combine(config.BclBase, "System.dll")));
                    break;
                default:
                    throw new Exception($"unexpected TfmType {config.TfmType}");
            }

            if (config.TfmType != Diffy.TfmType.Msbuild) {
                foreach (string lib in config.Libs) {
                    project = project.AddMetadataReference (MetadataReference.CreateFromFile (lib));
                }
                project = project.WithCompilationOptions (new CSharpCompilationOptions (config.OutputKind));
                var document = project.AddDocument (name: config.Filename, text: SourceText.From (File.ReadAllText (config.SourcePath), Encoding.Unicode), folders: null, filePath: config.SourcePath);
                project = document.Project;
            }
            return project;
        }

        /// <summary>Waits for compilation to finish, and writes the errors, if any.</summary>
        /// <returns>true if compilation succeeded, or false if there were errors</returns>
        static bool CheckCompilationDiagnostics (Task<Compilation?> compilation, string diagnosticPrefix, [NotNullWhen(true)] out Compilation? result)
        {
            result = compilation.Result;
            if (result == null) {
                Console.WriteLine ($"{diagnosticPrefix} compilation was null");
                return false;
            } else {
                bool failed = false;
                foreach (var diag in result.GetDiagnostics ().Where (d => d.Severity == DiagnosticSeverity.Error)) {
                    Console.WriteLine ($"{diagnosticPrefix} --- {diag}");
                    failed = true;
                }

                return !failed;
            }

        }

        /// <summary>Check <see cref="EmitResult"/> or <see cref="EmitDifferenceResult"/> for failures</summary>
        static bool CheckEmitResult (EmitResult emitResult)
        {
            if (!emitResult.Success) {
                Console.WriteLine ("Emit failed");
                foreach (var diag in emitResult.Diagnostics.Where (d => d.Severity == DiagnosticSeverity.Error))
                    Console.WriteLine (diag);
            }
            return emitResult.Success;
        }

        static bool ParseArgs (string[] args, [NotNullWhen(true)] out Diffy.Config? config)
        {
            var builder = Diffy.Config.Builder();

            for (int i = 0; i < args.Length; i++) {
                const string msbuildOptPrefix = "-msbuild:";
                string fn = args [i];
                if (fn.StartsWith(msbuildOptPrefix)) {
                    builder.TfmType = Diffy.TfmType.Msbuild;
                    builder.ProjectPath = fn.Substring(msbuildOptPrefix.Length);
                }else if (fn == "-mono") {
                    builder.TfmType = Diffy.TfmType.MonoMono;
                } else if (fn.StartsWith("-bcl:") || fn.StartsWith("-bcl=")) {
                    builder.BclBase = fn.Substring(5);
                } else if (fn.StartsWith ("-l:")) {
                    builder.Libs.Add (fn.Substring (3));
                } else if (fn == "-target:library") {
                    builder.OutputKind = OutputKind.DynamicallyLinkedLibrary;
                } else if (fn.StartsWith("-out:") || fn.StartsWith("-out=")) {
                    builder.OutputDir = fn.Substring(5);
                } else if (!File.Exists (fn)) {
                    Console.WriteLine ($"File {fn} doesn't exist");
                    config = null;
                    return false;
                } else {
                    builder.Files.Add (fn);
                }
            }

            if (builder.Files.Count <= 1) {
                Console.WriteLine("roslynildiff.exe originalfile.cs patch1.cs [patch2.cs patch3.cs ...]");
                config = null;
                return false;
            }

            config = builder.Bake();
            return true;
        }

        static bool BuildBaseline (Project project, string outputAsm, string outputPdb, [MaybeNullWhen(false)] out EmitBaseline baseline, out int exitStatus)
        {
            Console.WriteLine ("Building baseline...");

            baseline = null;
            exitStatus = 0;
            if (!CheckCompilationDiagnostics (project.GetCompilationAsync(), "base", out var baseCompilation)) {
                exitStatus = 3;
                return false;
            }

            var baselineImage = new MemoryStream();
            var baselinePdb = new MemoryStream();
            EmitResult result = baseCompilation.Emit (baselineImage, baselinePdb);
            if (!CheckEmitResult(result)) {
                exitStatus = 4;
                return false;
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
            baseline = EmitBaseline.CreateInitialBaseline (baselineMetadata, (handle) => default);

            return true;
        }

        static bool BuildDelta (ref Project project, ref EmitBaseline baseline, List<SemanticEdit> edits, string outputAsm, DocumentId baseDocumentId, string deltaFile, int rev, out int exitStatus)
        {
            exitStatus = 0;
            string file = deltaFile;
            Console.WriteLine ($"parsing patch #{rev} from {file} and creating delta");

            Document document = project.GetDocument(baseDocumentId)!;

            string contents = File.ReadAllText (file);
            Document updatedDocument = document.WithText (SourceText.From (contents, Encoding.UTF8));
            project = updatedDocument.Project;
            if (updatedDocument.Id != baseDocumentId)
                throw new Exception ("Unexpectedly, document Id of the delta != base document Id");

            var changes = updatedDocument.GetTextChangesAsync (document).Result;
            if (!changes.Any()) {
                Console.WriteLine ("no changes found");
                exitStatus = 5;
                return false; //FIXME can continue here and just ignore the revision
            }

            Console.WriteLine ($"Found changes in {document.Name}");


            var updatedCompilation = project.GetCompilationAsync ();

            CompileEdits (document, updatedDocument, edits).Wait();

            if (!CheckCompilationDiagnostics(updatedCompilation, $"delta {rev}", out var updatedCompilationResult)) {
                exitStatus = 6;
                return false;
            }


            using (var metaStream = File.Create (outputAsm + "." + rev + ".dmeta"))
            using (var ilStream   = File.Create (outputAsm + "." + rev + ".dil"))
            using (var pdbStream  = File.Create (outputAsm + "." + rev + ".dpdb")) {
                var updatedMethods = new List<MethodDefinitionHandle> ();
                EmitDifferenceResult emitResult = updatedCompilationResult.EmitDifference (baseline, edits, metaStream, ilStream, pdbStream, updatedMethods);
                CheckEmitResult(emitResult);

                metaStream.Flush ();
                ilStream.Flush();
                pdbStream.Flush();

                // Update baseline and edits for next delta
                baseline = emitResult.Baseline;
                edits.Clear();
            }

            return true;
        }

        public static async Task CompileEdits (Document document, Document updatedDocument, List<SemanticEdit> edits)
        {
            var changeMaker = new Diffy.ChangeMaker();

            var edits2 = await changeMaker.GetChanges(document, updatedDocument);
            edits.AddRange(edits2);
        }

    }
}
