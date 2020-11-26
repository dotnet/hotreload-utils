using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Diffy.AsyncUtils;

namespace Diffy {
    public class Runner {
        readonly Config config;
        public  Runner (Config config) {
            this.config = config;
        }

        public async Task<BaselineArtifacts> SetupBaseline (CancellationToken ct = default) {
            RoslynBaselineProject? baselineProject;
            if (config.ProjectType == Diffy.ProjectType.Msbuild) {
                InitMSBuild();
                baselineProject = await Diffy.RoslynBaselineMsbuildProject.Make (config, ct);
            } else {
                baselineProject = Diffy.RoslynBaselineAdhocProject.Make (config);
            }

            var baselineArtifacts = await baselineProject.PrepareBaseline(ct);

            Console.WriteLine ("baseline ready");
            return baselineArtifacts;
        }

        public IAsyncEnumerable<(string deltaFile, Diffy.DerivedArtifactInfo dinfo)> SetupDerivedInputs (BaselineArtifacts baselineArtifacts, CancellationToken ct = default) {

            string outputAsm = baselineArtifacts.baselineOutputAsmPath;
            IAsyncEnumerable<(string deltaFile, Diffy.DerivedArtifactInfo dinfo)> derivedInputs;

            if (config.Live) {
                derivedInputs = Livecoding (config.SourcePath, outputAsm, ct);
            } else {
                derivedInputs = config.DeltaFiles.Select((deltaFile, idx) => (deltaFile, new Diffy.DerivedArtifactInfo(outputAsm, 1+idx))).Asynchronously();
            }

            return derivedInputs;
        }
        public async Task GenerateDeltas (RoslynDeltaProject deltaProject, IAsyncEnumerable<(string deltaFile, Diffy.DerivedArtifactInfo dinfo)> derivedInputs,
                                          Func<DerivedArtifactInfo,DeltaOutputStreams>? makeOutputs = null,
                                          Action<DeltaOutputStreams>? outputsReady = null,
                                          CancellationToken ct =  default)
        {
            await foreach ((var deltaFile, var dinfo) in derivedInputs.WithCancellation(ct)) {
                Console.WriteLine ("got a change");
                /* fixme: why does FSW sometimes queue up 2 events in quick succession after a single save? */
                deltaProject = await deltaProject.BuildDelta (deltaFile, dinfo, ignoreUnchanged: config.Live, makeOutputs: makeOutputs, outputsReady: outputsReady, ct: ct);
            }
        }

        private static void InitMSBuild ()
        {
            Microsoft.Build.Locator.MSBuildLocator.RegisterDefaults();
        }

        public static async IAsyncEnumerable<(string deltaFile, Diffy.DerivedArtifactInfo dinfo)> Livecoding (string watchPath, string outputAsm, [EnumeratorCancellation] CancellationToken cancellationToken= default) {
            int rev = 1;
            var last = DateTime.UtcNow;
            var interval = TimeSpan.FromMilliseconds(250); /* FIXME: make this configurable */
            using var fswgen = new Diffy.FSWGen (Path.GetDirectoryName(watchPath) ?? ".", Path.GetFileName(watchPath));
            await foreach (var fsevent in fswgen.Watch(cancellationToken).ConfigureAwait(false)) {
                if ((fsevent.ChangeType & WatcherChangeTypes.Changed) != 0) {
                    var e = DateTime.UtcNow;
                    Console.WriteLine($"change in {fsevent.FullPath} is a {fsevent.ChangeType} at {e}");
                    if (e - last < interval) {
                        Console.WriteLine($"too soon {e-last}");
                        continue;
                    }
                    Console.WriteLine($"more than 250ms since last change");
                    last = e;
                    yield return (deltaFile: watchPath, new Diffy.DerivedArtifactInfo(outputAsm, rev));
                    ++rev;
                }
            }
        }

    }
}
