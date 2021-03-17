using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

using Microsoft.CodeAnalysis;

namespace Microsoft.DotNet.HotReload.Utils.Generator.Runners
{

    /// Generate deltas by reading a script from a configuration file
    /// listing the changed versions of the project source files.
    public class ScriptRunner : Runner {
        public ScriptRunner (Config config) : base (config) {
            if (!string.IsNullOrEmpty(config.OutputSummaryPath)) {
                var writer = new JsonSummaryWriter(config.OutputSummaryPath);
                OutputsReady = writer.OutputsReady;
                OutputsDone = writer.OutputsDone;
            }
        }

        private class JsonSummaryWriter {
            private string OutputPath {get; }
            private List<OutputSummary.Delta> deltas;
            public JsonSummaryWriter(string outputPath) {
                OutputPath = outputPath;
                deltas = new List<OutputSummary.Delta>();
            }
            internal void OutputsReady(DeltaNaming names, DeltaOutputStreams _streams) {
                // FIXME: propagate the name of the updated assembly
                deltas.Add(new OutputSummary.Delta("", names.Dmeta, names.Dil, names.Dpdb));
            }

            internal async Task OutputsDone(CancellationToken ct = default(CancellationToken)) {
                using var s = File.OpenWrite(OutputPath);
                var summary = new OutputSummary.OutputSummary(deltas.ToArray());
                await System.Text.Json.JsonSerializer.SerializeAsync(s, summary, cancellationToken: ct);
            }

        }

        public override IAsyncEnumerable<Delta> SetupDeltas (BaselineArtifacts baselineArtifacts, CancellationToken ct = default)
        {
            return ScriptedPlanInputs (config, baselineArtifacts, ct);
        }

        private static async IAsyncEnumerable<Delta> ScriptedPlanInputs (Config config, BaselineArtifacts baselineArtifacts, [EnumeratorCancellation] CancellationToken ct = default)
        {
            var scriptPath = config.ScriptPath;
            var parser = new Microsoft.DotNet.HotReload.Utils.Generator.Script.Json.Parser(scriptPath);
            IReadOnlyCollection<Plan.Change<string,string>> parsed;
            using (var scriptStream = new FileStream(scriptPath, FileMode.Open)) {
                parsed = await parser.ReadAsync (scriptStream, ct);
            }
            var resolver = baselineArtifacts.docResolver;
            var artifacts = parsed.Select(c => new Delta(Plan.Change.Create(ResolveForScript(resolver, c.Document), c.Update)));
            foreach (var a in artifacts) {
                yield return a;
                if (ct.IsCancellationRequested)
                    break;
            }
        }
        private static DocumentId ResolveForScript (DocResolver resolver, string relativePath) {
            if (resolver.TryResolveDocumentId(relativePath, out var id))
                return id;
            throw new DiffyException($"Could not find {relativePath} in {resolver.Project.Name}", exitStatus: 12);
        }

    }
}
