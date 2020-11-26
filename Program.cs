using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;



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

            return RunWithExitStatus(config).Result;

        }

        static async Task<int> RunWithExitStatus(Diffy.Config config)
        {
            try {
                await Run(config);
                return 0;
            } catch (Diffy.DiffyException exn) {
                Console.Error.WriteLine ($"failed: {exn.Message}");
                if (exn.ExitStatus == 0)
                    return 1; /* really shouldn't happen, but just in case */
                return exn.ExitStatus;
            }
        }
        static async Task Run (Diffy.Config config)
        {
            var runner = new Diffy.Runner (config);
            var baselineArtifacts = await runner.SetupBaseline ();

            var deltaProject = new Diffy.RoslynDeltaProject (baselineArtifacts);
            var derivedInputs = runner.SetupDerivedInputs (baselineArtifacts);

            await runner.GenerateDeltas (deltaProject, derivedInputs);
            Console.WriteLine ("done");
        }





        static bool ParseArgs (string[] args, [NotNullWhen(true)] out Diffy.Config? config)
        {
            // FIXME: not all these options make sense together
            var builder = Diffy.Config.Builder();

            for (int i = 0; i < args.Length; i++) {
                const string msbuildOptPrefix = "-msbuild:";
                string fn = args [i];
                if (fn.StartsWith(msbuildOptPrefix)) {
                    builder.ProjectType = Diffy.ProjectType.Msbuild;
                    builder.ProjectPath = fn[msbuildOptPrefix.Length..];
                } else if (fn == "-live") {
                    builder.Live = true;
                } else if (fn == "-mono") {
                    builder.TfmType = Diffy.TfmType.MonoMono;
                } else if (fn.StartsWith("-bcl:") || fn.StartsWith("-bcl=")) {
                    builder.BclBase = fn[5..];
                } else if (fn == "-empty") {
                    builder.Barebones = true;
                } else if (fn.StartsWith ("-l:")) {
                    builder.Libs.Add (fn[3..]);
                } else if (fn == "-target:library") {
                    builder.OutputKind = Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary;
                } else if (fn.StartsWith("-out:") || fn.StartsWith("-out=")) {
                    builder.OutputDir = fn[5..];
                } else if (fn.StartsWith("-p:")) {
                    var s = fn[3..];
                    if (s.IndexOf('=') is int j && j > 0 && j+1 < s.Length) {
                        var k = s[0..j];
                        var v = s[(j + 1)..];
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

            if (!builder.Live && builder.Files.Count <= 1) {
                Console.WriteLine("roslynildiff.exe originalfile.cs patch1.cs [patch2.cs patch3.cs ...]");
                config = null;
                return false;
            } else if (builder.Live && builder.Files.Count != 1) {
                Console.WriteLine("roslynildiff.exe -live watchfile.cs");
                config = null;
                return false;
            }

            config = builder.Bake();
            return true;
        }


    }
}
