using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using DocumentId = Microsoft.CodeAnalysis.DocumentId;

namespace Diffy {
    public class Runner {
        readonly Config config;
        public  Runner (Config config) {
            this.config = config;
        }

        public struct RunnerChange {
            public readonly Plan.Change<DocumentId,string> Delta;
            public readonly DerivedArtifactInfo Info;

            public RunnerChange (Plan.Change<DocumentId,string> delta, DerivedArtifactInfo info) {
                Delta = delta;
                Info = info;
            }
        }

        public async Task<BaselineArtifacts> SetupBaseline (CancellationToken ct = default) {
            RoslynBaselineProject? baselineProject;
            InitMSBuild();
            baselineProject = await Diffy.RoslynBaselineProject.Make (config, ct);

            var baselineArtifacts = await baselineProject.PrepareBaseline(ct);

            Console.WriteLine ("baseline ready");
            return baselineArtifacts;
        }

        public IAsyncEnumerable<RunnerChange> SetupDerivedInputs (BaselineArtifacts baselineArtifacts, CancellationToken ct = default) {

            IAsyncEnumerable<RunnerChange> derivedInputs;

            if (config.Live) {
                derivedInputs = Livecoding (baselineArtifacts, config.LiveCodingWatchDir, config.LiveCodingWatchPattern, ct);
            } else {
                derivedInputs = ScriptedPlanInputs (config, baselineArtifacts, ct);
            }

            return derivedInputs;
        }

        private static async IAsyncEnumerable<RunnerChange> ScriptedPlanInputs (Config config, BaselineArtifacts baselineArtifacts, [EnumeratorCancellation] CancellationToken ct = default)
        {
            var scriptPath = config.ScriptPath;
            var parser = new Diffy.Script.Json.Parser(scriptPath);
            IReadOnlyCollection<Plan.Change<string,string>> parsed;
            using (var scriptStream = new FileStream(scriptPath, FileMode.Open)) {
                parsed = await parser.ReadAsync (scriptStream, ct);
            }
            string outputAsm = baselineArtifacts.baselineOutputAsmPath;
            var resolver = baselineArtifacts.docResolver;
            var artifacts = parsed.Select((c, idx) => new RunnerChange(Plan.Change.Create(ResolveForScript(resolver, c.Document), c.Update), new DerivedArtifactInfo(outputAsm, 1+idx)));
            foreach (var a in artifacts) {
                yield return a;
                if (ct.IsCancellationRequested)
                    break;
            }
        }

        private static DocumentId ResolveForScript (RoslynDocResolver resolver, string relativePath) {
            if (resolver.TryResolveDocumentId(relativePath, out var id))
                return id;
            throw new DiffyException($"Could not find {relativePath} in {resolver.Project.Name}", exitStatus: 12);
        }
        public async Task GenerateDeltas (RoslynDeltaProject deltaProject, IAsyncEnumerable<RunnerChange> derivedInputs,
                                          Func<DerivedArtifactInfo,DeltaOutputStreams>? makeOutputs = null,
                                          Action<DeltaOutputStreams>? outputsReady = null,
                                          CancellationToken ct =  default)
        {
            await foreach (var input in derivedInputs.WithCancellation(ct)) {
                Console.WriteLine ("got a change");
                /* fixme: why does FSW sometimes queue up 2 events in quick succession after a single save? */
                deltaProject = await deltaProject.BuildDelta (input.Delta, input.Info, ignoreUnchanged: config.Live, makeOutputs: makeOutputs, outputsReady: outputsReady, ct: ct);
            }
        }

        private static void InitMSBuild ()
        {
            Microsoft.Build.Locator.MSBuildLocator.RegisterDefaults();
        }

        public static async IAsyncEnumerable<RunnerChange> Livecoding (BaselineArtifacts baselineArtifacts, string watchDir, string pattern, [EnumeratorCancellation] CancellationToken cancellationToken= default) {
            int rev = 1;
            var last = DateTime.UtcNow;
            var interval = TimeSpan.FromMilliseconds(250); /* FIXME: make this configurable */
            string outputAsm = baselineArtifacts.baselineOutputAsmPath;
            var docResolver = baselineArtifacts.docResolver;
            var baselineProjectId = baselineArtifacts.baselineProjectId;

            using var fswgen = new Diffy.FSWGen (watchDir, pattern);
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
                    var fp = fsevent.FullPath;
                    if (!docResolver.TryResolveDocumentId(fp, out var id)) {
                        Console.WriteLine ($"ignoring change in {fp} which is not in {baselineProjectId}");
                        continue;
                    }

                    yield return new RunnerChange (Plan.Change.Create(id, fp), new DerivedArtifactInfo(outputAsm, rev));
                    ++rev;
                }
            }
        }

    }
}
