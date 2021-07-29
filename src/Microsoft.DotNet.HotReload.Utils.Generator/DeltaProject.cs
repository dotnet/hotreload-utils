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
        readonly EnC.ChangeMakerService _changeMakerService;

        readonly Solution _solution;
        readonly ProjectId _baseProjectId;
        readonly DeltaNaming _nextName;

        public DeltaProject(BaselineArtifacts artifacts) {
            _changeMakerService = artifacts.changeMakerService;
            _solution = artifacts.baselineSolution;
            _baseProjectId = artifacts.baselineProjectId;
            _nextName = new DeltaNaming(artifacts.baselineOutputAsmPath, 1);
        }

        internal DeltaProject (DeltaProject prev, Solution newSolution)
        {
            _changeMakerService = prev._changeMakerService;
            _solution = newSolution;
            _baseProjectId = prev._baseProjectId;
            _nextName = prev._nextName.Next ();
        }

        public Solution Solution => _solution;

        public ProjectId BaseProjectId => _baseProjectId;

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

            Project oldProject = Solution.GetProject(BaseProjectId)!;

            DocumentId baseDocumentId = change.Document;

            Document oldDocument = oldProject.GetDocument(baseDocumentId)!;

            Document updatedDocument;
            Solution updatedSolution;
            await using (var contents = File.OpenRead (change.Update)) {
                updatedSolution = Solution.WithDocumentText (baseDocumentId, SourceText.From (contents, Encoding.UTF8));
                updatedDocument = updatedSolution.GetDocument(baseDocumentId)!;
            }
            if (updatedDocument.Project.Id != BaseProjectId)
                throw new Exception ("Unexpectedly, project Id of the delta != base project Id");
            if (updatedDocument.Id != baseDocumentId)
                throw new Exception ("Unexpectedly, document Id of the delta != base document Id");

            var changes = await updatedDocument.GetTextChangesAsync (oldDocument, ct);
            if (!changes.Any()) {
                Console.WriteLine ("no changes found");
                if (ignoreUnchanged)
                    return this;
                //FIXME can continue here and just ignore the revision
                throw new DiffyException ($"no changes in revision {dinfo.Rev}", exitStatus: 5);
            }

            Console.WriteLine ($"Found changes in {oldDocument.Name}");

            (var fancyChanges, var diagnostics) = await _changeMakerService.EmitSolutionUpdateAsync (updatedSolution, ct);

            if (diagnostics.Any()) {
                var sb = new StringBuilder();
                foreach (var diag in diagnostics) {
                    sb.AppendLine (diag.ToString ());
                }
                throw new DiffyException ($"Failed to emit delta for {oldDocument.Name}: {sb.ToString()}", exitStatus: 8);
            }
            foreach (var fancyChange in fancyChanges)
            {
                Console.WriteLine("change service made {0}", fancyChange.ModuleId);
            }

            await using (var output = makeOutputs != null ?  makeOutputs(dinfo) : DefaultMakeFileOutputs(dinfo)) {
                if (fancyChanges.Count() != 1) {
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
            return new DeltaProject(this, updatedSolution);
        }

    }
}
