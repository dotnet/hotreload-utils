using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace Diffy
{

    /// Stuff about the artifacts for a given revision of a baseline assembly
    public class DerivedArtifactInfo {
        readonly string _dmeta;
        readonly string _dil;
        readonly string _dpdb;
        readonly int _rev;

        public DerivedArtifactInfo (string baseAssemblyPath, int rev) {
            _rev = rev;
            _dmeta = baseAssemblyPath + "." + rev + ".dmeta";
            _dil = baseAssemblyPath + "." + rev + ".dil";
            _dpdb = baseAssemblyPath + "." + rev + ".dpdb";
        }
        public string Dmeta => _dmeta;
        public string Dil => _dil;
        public string Dpdb => _dpdb;
        public int Rev => _rev;
    }

    public sealed class DeltaOutputStreams : IAsyncDisposable {
        public Stream MetaStream {get; private set;}
        public Stream  IlStream {get; private set;}
        public Stream PdbStream {get; private set;}

        public DeltaOutputStreams(Stream dmeta, Stream dil, Stream dpdb) {
            MetaStream = dmeta;
            IlStream = dil;
            PdbStream = dpdb;
        }

        public void Dispose () {
            MetaStream?.Dispose();
            IlStream?.Dispose();
            PdbStream?.Dispose();
        }

        public async ValueTask DisposeAsync () {
            if  (MetaStream != null) await MetaStream.DisposeAsync();
            if  (IlStream != null) await IlStream.DisposeAsync();
            if  (PdbStream != null) await PdbStream.DisposeAsync();
        }

    }

    /// Drives the creation of deltas from textual changes.
    public class RoslynDeltaProject
    {
        readonly ChangeMaker _changeMaker;

        readonly Solution _solution;
        readonly EmitBaseline _baseline;
        readonly ProjectId _baseProjectId;


        public RoslynDeltaProject(BaselineArtifacts artifacts) {
            _changeMaker = new ChangeMaker();
            _solution = artifacts.baselineSolution;
            _baseline = artifacts.emitBaseline;
            _baseProjectId = artifacts.baselineProjectId;
        }

        internal RoslynDeltaProject (RoslynDeltaProject prev, Solution newSolution, EmitBaseline newBaseline)
        {
            _changeMaker = prev._changeMaker;
            _solution = newSolution;
            _baseline = newBaseline;
            _baseProjectId = prev._baseProjectId;
        }

        public Solution Solution => _solution;

        public EmitBaseline Baseline => _baseline;

        public ProjectId BaseProjectId => _baseProjectId;

        private static DeltaOutputStreams MakeFileOutputs (DerivedArtifactInfo dinfo) {
            var metaStream = File.Create(dinfo.Dmeta);
            var ilStream = File.Create(dinfo.Dil);
            var pdbStream = File.Create(dinfo.Dpdb);
            return new DeltaOutputStreams(metaStream, ilStream, pdbStream);
        }

        /// Builds a delta for the specified document given a path to its updated contents and a revision count
        /// On failure throws a DiffyException and with exitStatus > 0
        public async Task<RoslynDeltaProject> BuildDelta (Plan.Change<DocumentId,string> delta, DerivedArtifactInfo dinfo, bool ignoreUnchanged = false,
                                                          Func<DerivedArtifactInfo, DeltaOutputStreams>? makeOutputs = default,
                                                          Action<DeltaOutputStreams>? outputsReady = default,
                                                          CancellationToken ct = default)
        {
            Console.WriteLine ($"parsing patch #{dinfo.Rev} from {delta.Update} and creating delta");

            Project project = Solution.GetProject(BaseProjectId)!;

            DocumentId baseDocumentId = delta.Document;

            Document document = project.GetDocument(baseDocumentId)!;

            Document updatedDocument;
            await using (var contents = File.OpenRead (delta.Update)) {
                Solution updatedSolution = Solution.WithDocumentText (baseDocumentId, SourceText.From (contents, Encoding.UTF8));
                updatedDocument = updatedSolution.GetDocument(baseDocumentId)!;
            }
            if (updatedDocument.Project.Id != BaseProjectId)
                throw new Exception ("Unexpectedly, project Id of the delta != base project Id");
            if (updatedDocument.Id != baseDocumentId)
                throw new Exception ("Unexpectedly, document Id of the delta != base document Id");
            project = updatedDocument.Project;

            var changes = await updatedDocument.GetTextChangesAsync (document, ct);
            if (!changes.Any()) {
                Console.WriteLine ("no changes found");
                if (ignoreUnchanged)
                    return this;
                //FIXME can continue here and just ignore the revision
                throw new DiffyException ($"no changes in revision {dinfo.Rev}", exitStatus: 5);
            }

            Console.WriteLine ($"Found changes in {document.Name}");

            Task<Compilation> updatedCompilation = Task.Run(async () => {
                var compilation = await project.GetCompilationAsync (ct);
                if (!CheckCompilationDiagnostics(compilation, $"delta {dinfo.Rev}"))
                    throw new DeltaCompilationException(exitStatus: 6);
                else
                    return compilation;
            }, ct);

            var editsCompilation = Task.Run(() => CompileEdits (document, updatedDocument, ct), ct);

            Compilation updatedCompilationResult = await updatedCompilation;

            var (edits, rudeEdits) = await editsCompilation;
            if (!rudeEdits.IsDefault && rudeEdits.Any() ) {
                throw new DeltaRudeEditException($"rude edits in revision {dinfo.Rev}", rudeEdits);
            }
            if (edits.IsDefault || !edits.Any()) {
                Console.WriteLine("no semantic changes");
                if (ignoreUnchanged)
                    return this;
                else
                    throw new DeltaCompilationException("no semantic changes in revision", exitStatus: 7);
            }
            var baseline = Baseline ?? throw new NullReferenceException ($"got a null baseline for revision {dinfo.Rev}");
            var updatedMethods = new List<System.Reflection.Metadata.MethodDefinitionHandle> ();

            EmitDifferenceResult emitResult;
            await using (var output = makeOutputs != null ?  makeOutputs(dinfo) : MakeFileOutputs(dinfo)) {
                emitResult = updatedCompilationResult.EmitDifference(baseline, edits, output.MetaStream, output.IlStream, output.PdbStream, updatedMethods, ct);
                CheckEmitResult(emitResult);
                outputsReady?.Invoke(output);
            }
            Console.WriteLine($"wrote {dinfo.Dmeta}");
            // return a new deltaproject that can build the next update
            return new RoslynDeltaProject(this, project.Solution, emitResult.Baseline);
        }

        Task<(ImmutableArray<SemanticEdit>, ImmutableArray<ChangeMaker.RudeEditDiagnosticWrapper>)> CompileEdits (Document document, Document updatedDocument, CancellationToken ct = default)
        {
            return _changeMaker.GetChanges(document, updatedDocument, ct);
        }


        /// <returns>true if compilation succeeded, or false if there were errors</returns>
        internal static bool CheckCompilationDiagnostics ([NotNullWhen(true)] Compilation? compilation, string diagnosticPrefix)
        {
            if (compilation == null) {
                Console.WriteLine ($"{diagnosticPrefix} compilation was null");
                return false;
            } else {
                bool failed = false;
                foreach (var diag in compilation.GetDiagnostics ().Where (d => d.Severity == DiagnosticSeverity.Error)) {
                    Console.WriteLine ($"{diagnosticPrefix} --- {diag}");
                    failed = true;
                }

                return !failed;
            }

        }

        /// <summary>Check <see cref="EmitResult"/> or <see cref="EmitDifferenceResult"/> for failures</summary>
        public static bool CheckEmitResult (EmitResult emitResult)
        {
            if (!emitResult.Success) {
                Console.WriteLine ("Emit failed");
                foreach (var diag in emitResult.Diagnostics.Where (d => d.Severity == DiagnosticSeverity.Error))
                    Console.WriteLine (diag);
            }
            return emitResult.Success;
        }

    }
}
