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
    /// Drives the creation of deltas from textual changes.
    public class DeltaProject
    {
        readonly EnC.ChangeMaker _changeMaker;

        readonly Solution _solution;
        readonly EmitBaseline _baseline;
        readonly ProjectId _baseProjectId;

        readonly DeltaNaming _nextName;

        public DeltaProject(BaselineArtifacts artifacts) {
            _changeMaker = new EnC.ChangeMaker();
            _solution = artifacts.baselineSolution;
            _baseline = artifacts.emitBaseline;
            _baseProjectId = artifacts.baselineProjectId;
            _nextName = new DeltaNaming(artifacts.baselineOutputAsmPath, 1);
        }

        internal DeltaProject (DeltaProject prev, Solution newSolution, EmitBaseline newBaseline)
        {
            _changeMaker = prev._changeMaker;
            _solution = newSolution;
            _baseline = newBaseline;
            _baseProjectId = prev._baseProjectId;
            _nextName = prev._nextName.Next ();
        }

        public Solution Solution => _solution;

        public EmitBaseline Baseline => _baseline;

        public ProjectId BaseProjectId => _baseProjectId;

        private static DeltaOutputStreams MakeFileOutputs (DeltaNaming dinfo) {
            var metaStream = File.Create(dinfo.Dmeta);
            var ilStream = File.Create(dinfo.Dil);
            var pdbStream = File.Create(dinfo.Dpdb);
            return new DeltaOutputStreams(metaStream, ilStream, pdbStream);
        }

        /// Builds a delta for the specified document given a path to its updated contents and a revision count
        /// On failure throws a DiffyException and with exitStatus > 0
        public async Task<DeltaProject> BuildDelta (Delta delta, bool ignoreUnchanged = false,
                                                          Func<DeltaNaming, DeltaOutputStreams>? makeOutputs = default,
                                                          Action<DeltaOutputStreams>? outputsReady = default,
                                                          CancellationToken ct = default)
        {
            var change = delta.Change;
            var dinfo = _nextName;

            Console.WriteLine ($"parsing patch #{dinfo.Rev} from {change.Update} and creating delta");

            Project project = Solution.GetProject(BaseProjectId)!;

            DocumentId baseDocumentId = change.Document;

            Document document = project.GetDocument(baseDocumentId)!;

            Document updatedDocument;
            await using (var contents = File.OpenRead (change.Update)) {
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
            return new DeltaProject(this, project.Solution, emitResult.Baseline);
        }

        Task<(ImmutableArray<SemanticEdit>, ImmutableArray<EnC.RudeEditDiagnosticWrapper>)> CompileEdits (Document document, Document updatedDocument, CancellationToken ct = default)
        {
            return _changeMaker.GetChanges(document, updatedDocument, ct);
        }


        /// <returns>true if compilation succeeded, or false if there were errors</returns>
        private static bool CheckCompilationDiagnostics ([NotNullWhen(true)] Compilation? compilation, string diagnosticPrefix)
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
        private static bool CheckEmitResult (EmitResult emitResult)
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
