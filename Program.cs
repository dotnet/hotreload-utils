using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
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

            var project = PrepareProject (config).Result;

            string? outputAsm;
            EmitBaseline? baseline;
            if (config.TfmType == Diffy.TfmType.Msbuild) {
                if (!ConsumeBaseline (project, out outputAsm, out baseline))
                    throw new Exception ("could not consume baseline");
            } else {
                Directory.CreateDirectory(config.OutputDir);
                // FIXME: this is a mess
                //   1. this path stuff is part of the config, the defaults are just derived from the other args
                //   2. this hardcodes the assumption that the assembly name and the baseline source filename are the same.
                var projectName = config.ProjectName;
                outputAsm = Path.Combine (config.OutputDir, projectName + ".dll");
                var outputPdb = Path.Combine (config.OutputDir, projectName + ".pdb");
                if (!BuildBaseline (project, outputAsm, outputPdb, out baseline, out int exitStatus))
                    return exitStatus;
            }

            var deltaProject = new Diffy.RoslynDeltaProject (project, baseline, baselinePath);


            var derivedInputs = config.DeltaFiles.Select((deltaFile, idx) => (deltaFile, new Diffy.DerivedArtifactInfo(outputAsm, 1+idx)));
            foreach ((var deltaFile, var dinfo) in derivedInputs) {
                List<SemanticEdit> edits = new List<SemanticEdit> ();
                (bool success, int exitStatus) = deltaProject.BuildDelta (deltaFile, dinfo).Result;
                if (!success)
                    return exitStatus;
            }

            return 0;
        }

        private static void InitMSBuild ()
        {
            Microsoft.Build.Locator.MSBuildLocator.RegisterDefaults();
        }

        static async Task<Project> PrepareProject (Diffy.Config config)
        {

            Project project;
            switch (config.TfmType) {
                case Diffy.TfmType.Msbuild:
                    InitMSBuild ();
                    return await PrepareMSBuildProject (config);
                case Diffy.TfmType.Netcore:
                    //FIXME: hack
                    {
                        var adhoc = new AdhocWorkspace();
                        project = adhoc.AddProject (config.ProjectName, LanguageNames.CSharp);
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
                        project =  adhoc.AddProject (config.ProjectName, LanguageNames.CSharp);
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

            if (config.TfmType == Diffy.TfmType.Msbuild)
                throw new Exception ("msbuild projects shouldn't get here");

            foreach (string lib in config.Libs) {
                project = project.AddMetadataReference (MetadataReference.CreateFromFile (lib));
            }
            project = project.WithCompilationOptions (new CSharpCompilationOptions (config.OutputKind));
            using (var source = File.OpenRead(config.SourcePath)) {
                var document = project.AddDocument (name: config.Filename, text: SourceText.From (source, Encoding.UTF8), folders: null, filePath: Path.GetFullPath (config.SourcePath));
                project = document.Project;
            }
            return project;
        }

        static Task<Project> PrepareMSBuildProject (Diffy.Config config)
        {
                    Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace msw;
                    // https://stackoverflow.com/questions/43386267/roslyn-project-configuration says I have to specify at least a Configuration property
                    // to get an output path, is that true?
                    var props = new Dictionary<string,string> (config.Properties);
                    msw = Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace.Create(props);
                    msw.LoadMetadataForReferencedProjects = true;
                    msw.WorkspaceFailed += (_sender, diag) => {
                        Console.WriteLine ($"msbuild failed opening project {config.ProjectPath}");
                        Console.WriteLine ($"{diag.Diagnostic.Kind}: {diag.Diagnostic.Message}");
                        throw new Exception ("failed workspace");
                    };
                    return msw.OpenProjectAsync (config.ProjectPath);
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
                } else if (fn == "-mono") {
                    builder.TfmType = Diffy.TfmType.MonoMono;
                } else if (fn.StartsWith("-bcl:") || fn.StartsWith("-bcl=")) {
                    builder.BclBase = fn.Substring(5);
                } else if (fn.StartsWith ("-l:")) {
                    builder.Libs.Add (fn.Substring (3));
                } else if (fn == "-target:library") {
                    builder.OutputKind = OutputKind.DynamicallyLinkedLibrary;
                } else if (fn.StartsWith("-out:") || fn.StartsWith("-out=")) {
                    builder.OutputDir = fn.Substring(5);
                } else if (fn.StartsWith("-p:")) {
                    var s = fn.Substring(3);
                    if (s.IndexOf('=') is int j && j > 0 && j+1 < s.Length) {
                        var k = s.Substring(0, j);
                        var v = s.Substring(j+1);
                        // Console.WriteLine ($"got <{k}>=<{v}>");
                        builder.Properties.Add(KeyValuePair.Create(k,v));
                    } else {
                        throw new ArgumentException ("-p option needs a key=value pair");
                    }
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

        static bool ConsumeBaseline (Project project, [MaybeNullWhen(false)] out string outputAsm, [NotNullWhen(true)] out EmitBaseline? baseline)
        {
            baseline = null;
            outputAsm = project.OutputFilePath ;
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
        static bool BuildBaseline (Project project, string outputAsm, string outputPdb, [NotNullWhen(true)] out EmitBaseline? baseline, out int exitStatus)
        {
            Console.WriteLine ("Building baseline...");


            baseline = null;
            exitStatus = 0;
            if (!Diffy.RoslynDeltaProject.CheckCompilationDiagnostics (project.GetCompilationAsync(), "base", out var baseCompilation)) {
                exitStatus = 3;
                return false;
            }

            var baselineImage = new MemoryStream();
            var baselinePdb = new MemoryStream();
            EmitResult result = baseCompilation.Emit (baselineImage, baselinePdb);
            if (!Diffy.RoslynDeltaProject.CheckEmitResult(result)) {
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



    }
}
