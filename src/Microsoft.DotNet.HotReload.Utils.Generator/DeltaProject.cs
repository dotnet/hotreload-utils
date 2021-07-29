// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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

namespace Microsoft.DotNet.HotReload.Utils.Generator
{
    /// Drives the creation of deltas from textual changes.
    public class DeltaProject
    {
        readonly EnC.ChangeMaker _changeMaker;
        readonly EnC.ChangeMakerService _changeMakerService;
        readonly EnC.EditAndContinueCapabilities _capabilities;

        readonly Solution _solution;
        readonly EmitBaseline _baseline;
        readonly ProjectId _baseProjectId;

        readonly DeltaNaming _nextName;

        public DeltaProject(BaselineArtifacts artifacts, EnC.EditAndContinueCapabilities capabilities) {
            _changeMaker = new EnC.ChangeMaker();
            _changeMakerService = artifacts.changeMakerService;
            _solution = artifacts.baselineSolution;
            _baseline = artifacts.emitBaseline;
            _baseProjectId = artifacts.baselineProjectId;
            _nextName = new DeltaNaming(artifacts.baselineOutputAsmPath, 1);
            _capabilities = capabilities;
        }

        internal DeltaProject (DeltaProject prev, Solution newSolution, EmitBaseline newBaseline)
        {
            _changeMaker = prev._changeMaker;
            _changeMakerService = prev._changeMakerService;
            _capabilities = prev._capabilities;
            _solution = newSolution;
            _baseline = newBaseline;
            _baseProjectId = prev._baseProjectId;
            _nextName = prev._nextName.Next ();
        }

        public Solution Solution => _solution;

        public EmitBaseline Baseline => _baseline;

        public ProjectId BaseProjectId => _baseProjectId;

        public EnC.EditAndContinueCapabilities EditAndContinueCapabilities => _capabilities;

        /// The default output function
        ///  Creates files with the specified DeltaNaming without any other side-effects
        public static DeltaOutputStreams DefaultMakeFileOutputs (DeltaNaming dinfo) {
            var metaStream = File.Create(dinfo.Dmeta);
            var ilStream = File.Create(dinfo.Dil);
            var pdbStream = File.Create(dinfo.Dpdb);
            return new DeltaOutputStreams(metaStream, ilStream, pdbStream);
        }

        /// Builds a delta for the specified document given a path to its updated contents and a revision count
        /// On failure throws a DiffyException and with exitStatus > 0
        public async Task<DeltaProject> BuildDelta (Delta delta, bool ignoreUnchanged = false,
                                                          Func<DeltaNaming, DeltaOutputStreams>? makeOutputs = default,
                                                          Action<DeltaNaming, DeltaOutputStreams>? outputsReady = default,
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
            Project oldProject = project;
            Task<Compilation?> oldCompilation = Task.Run (async() => await oldProject.GetCompilationAsync(ct), ct);
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

            (var fancyChanges, var diagnostics) = await _changeMakerService.EmitSolutionUpdateAsync (updatedDocument.Project.Solution, ct);

            if (diagnostics.Any()) {
                var sb = new StringBuilder();
                foreach (var diag in diagnostics) {
                    sb.AppendLine (diag.ToString ());
                }
                throw new DiffyException ($"Failed to emit delta for {document.Name}: {sb.ToString()}", exitStatus: 8);
            }
            foreach (var fancyChange in fancyChanges)
            {
                Console.WriteLine("change service made {0}", fancyChange.ModuleId);
            }

#if false
            Task<Compilation> updatedCompilation = Task.Run(async () => {
                var compilation = await project.GetCompilationAsync (ct);
                if (!CheckCompilationDiagnostics(compilation, $"delta {dinfo.Rev}"))
                    throw new DeltaCompilationException(exitStatus: 6);
                else
                    return compilation;
            }, ct);

            var editsCompilation = Task.Run(() => CompileEdits (oldProject, updatedDocument, ct), ct);

            Compilation updatedCompilationResult = await updatedCompilation;


            var (documentAnalysisResults, rudeEdits) = await editsCompilation;
            if (!rudeEdits.IsDefault && rudeEdits.Any() ) {
                throw new DeltaRudeEditException($"rude edits in revision {dinfo.Rev}", rudeEdits);
            }

            var oldCompilationResult = await oldCompilation;
            if (oldCompilationResult == null)
                throw new DeltaCompilationException("couldn't get old compilation");

            var edits = await GetProjectChangesAsync (oldCompilationResult, updatedCompilationResult, oldProject, project, documentAnalysisResults, ct);

            if (edits.IsDefault || !edits.Any()) {
                Console.WriteLine("no semantic changes");
                if (ignoreUnchanged)
                    return this;
                else
                    throw new DeltaCompilationException("no semantic changes in revision", exitStatus: 7);
            }
            var baseline = Baseline ?? throw new NullReferenceException ($"got a null baseline for revision {dinfo.Rev}");

            EmitDifferenceResult emitResult;
#endif

            await using (var output = makeOutputs != null ?  makeOutputs(dinfo) : DefaultMakeFileOutputs(dinfo)) {
                // emitResult = updatedCompilationResult.EmitDifference(baseline, edits, s=> false, output.MetaStream, output.IlStream, output.PdbStream, ct);
                // CheckEmitResult(emitResult);
                if (fancyChanges.Count() > 1) {
                    throw new DiffyException($"Expected only one module in the delta, got {fancyChanges.Count()}", exitStatus: 10);
                }
                var update = fancyChanges.First();
                output.MetaStream.Write(update.MetadataDelta.AsSpan());
                output.IlStream.Write(update.ILDelta.AsSpan());
                output.PdbStream.Write(update.PdbDelta.AsSpan());
                outputsReady?.Invoke(dinfo, output);
            }
            Console.WriteLine($"wrote {dinfo.Dmeta}");
            // return a new deltaproject that can build the next update
            // FIXME: the baseline is probably not needed for the changeMakerService now
            return new DeltaProject(this, project.Solution, /*emitResult.Baseline*/ null!);
        }

        Task<(EnC.DocumentAnalysisResultsWrapper, ImmutableArray<EnC.RudeEditDiagnosticWrapper>)> CompileEdits (Project project, Document updatedDocument, CancellationToken ct = default)
        {
            return _changeMaker.GetChanges(_capabilities, project, updatedDocument, ct);
        }

        Task<ImmutableArray<SemanticEdit>> GetProjectChangesAsync (Compilation oldCompilation, Compilation newCompilation, Project oldProject, Project newProject, EnC.DocumentAnalysisResultsWrapper results, CancellationToken ct = default)
        {
            return _changeMaker.GetProjectChangesAsync (oldCompilation, newCompilation, oldProject, newProject, results, ct);
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
