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




        private static void PrintUsage(){
            Console.WriteLine("roslynildiff.exe -msbuild:project.csproj [-p:Key=Value ...] [-live|-script:script.json]");
        }
        static bool ParseArgs (string[] args, [NotNullWhen(true)] out Diffy.Config? config)
        {
            // FIXME: not all these options make sense together
            var builder = Diffy.Config.Builder();

            config = null;

            for (int i = 0; i < args.Length; i++) {
                const string msbuildOptPrefix = "-msbuild:";
                const string scriptOptPrefix = "-script:";
                string fn = args [i];
                if (fn.StartsWith(msbuildOptPrefix)) {
                    builder.ProjectPath = fn[msbuildOptPrefix.Length..];
                } else if (fn == "-live") {
                    builder.Live = true;
                } else if (fn.StartsWith("-p:")) {
                    var s = fn[3..];
                    if (s.IndexOf('=') is int j && j > 0 && j+1 < s.Length) {
                        var k = s[0..j];
                        var v = s[(j + 1)..];
                        // Console.WriteLine ($"got <{k}>=<{v}>");
                        builder.Properties.Add(KeyValuePair.Create(k,v));
                    } else {
                        PrintUsage ();
                        Console.WriteLine("\t-p option needs a key=value pair");
                        return false;
                    }
                } else if (fn.StartsWith(scriptOptPrefix)) {
                    builder.ScriptPath = fn[scriptOptPrefix.Length..];
                } else {
                    PrintUsage();
                    Console.WriteLine ($"\tUnexpected trailing option {fn}");
                    return false;
                }
            }

            if (String.IsNullOrEmpty(builder.ProjectPath)) {
                PrintUsage();
                Console.WriteLine ("\tmsbuild project is required");
                return false;
            }

            if (!Xor(builder.Live, !String.IsNullOrEmpty(builder.ScriptPath))) {
                PrintUsage();
                Console.WriteLine("\tExactly one of -live or -script:script.json is required");
                return false;
            }

            config = builder.Bake();
            return true;
        }

        private static bool Xor (bool a, bool b) {
            return !(a == b);
        }

    }
}
