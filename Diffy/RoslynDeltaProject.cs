using System;
using System.Collections.Generic;
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

    /// Drives the creation of deltas from textual changes.
    public class RoslynDeltaProject
    {
        readonly ChangeMaker _changeMaker;

        readonly Solution _solution;
        readonly EmitBaseline _baseline;
        readonly ProjectId _baseProjectId;
        readonly DocumentId _baseDocumentId;



        public RoslynDeltaProject(BaselineArtifacts artifacts) {
            _changeMaker = new ChangeMaker();
            _solution = artifacts.baselineSolution;
            _baseline = artifacts.emitBaseline;
            _baseProjectId = artifacts.baselineProjectId;
            _baseDocumentId = artifacts.baselineDocumentId;
        }

        internal RoslynDeltaProject (RoslynDeltaProject prev, Solution newSolution, EmitBaseline newBaseline)
        {
            _changeMaker = prev._changeMaker;
            _solution = newSolution;
            _baseline = newBaseline;
            _baseProjectId = prev._baseProjectId;
            _baseDocumentId = prev._baseDocumentId;
        }

        public Solution Solution => _solution;

        public EmitBaseline Baseline => _baseline;

        public ProjectId BaseProjectId => _baseProjectId;
        public DocumentId BaseDocumentId => _baseDocumentId;

        /// Builds a delta for the specified document given a path to its updated contents and a revision count
        /// On failure throws a DiffyException and with exitStatus > 0
        public async Task<RoslynDeltaProject> BuildDelta (string deltaFile, DerivedArtifactInfo dinfo)
        {
            Console.WriteLine ($"parsing patch #{dinfo.Rev} from {deltaFile} and creating delta");

            Project project = Solution.GetProject(BaseProjectId)!;

            Document document = project.GetDocument(BaseDocumentId)!;

            Document updatedDocument;
            using (var contents = File.OpenRead (deltaFile)) {
                Solution updatedSolution = Solution.WithDocumentText (BaseDocumentId, SourceText.From (contents, Encoding.UTF8));
                updatedDocument = updatedSolution.GetDocument(BaseDocumentId)!;
            }
            if (updatedDocument.Project.Id != BaseProjectId)
                throw new Exception ("Unexpectedly, project Id of the delta != base project Id");
            if (updatedDocument.Id != BaseDocumentId)
                throw new Exception ("Unexpectedly, document Id of the delta != base document Id");
            project = updatedDocument.Project;

            var changes = await updatedDocument.GetTextChangesAsync (document);
            if (!changes.Any()) {
                Console.WriteLine ("no changes found");
                //FIXME can continue here and just ignore the revision
                throw new DiffyException ($"no changes in revision {dinfo.Rev}", exitStatus: 5);
            }

            Console.WriteLine ($"Found changes in {document.Name}");

            Task<Compilation> updatedCompilation = Task.Run(async () => {
                var compilation = await project.GetCompilationAsync ();
                if (!CheckCompilationDiagnostics(compilation, $"delta {dinfo.Rev}"))
                    throw new DeltaCompilationException(exitStatus: 6);
                else
                    return compilation;
            });

            var editsCompilation = Task.Run(() => CompileEdits (document, updatedDocument));

            Compilation updatedCompilationResult = await updatedCompilation;

            var edits = await editsCompilation;
            var baseline = Baseline ?? throw new NullReferenceException ($"got a null baseline for revision {dinfo.Rev}");
            var updatedMethods = new List<System.Reflection.Metadata.MethodDefinitionHandle> ();

            using var metaStream = File.Create(dinfo.Dmeta);
            using var ilStream = File.Create(dinfo.Dil);
            using var pdbStream = File.Create(dinfo.Dpdb);
            EmitDifferenceResult emitResult = updatedCompilationResult.EmitDifference(baseline, edits, metaStream, ilStream, pdbStream, updatedMethods);
            CheckEmitResult(emitResult);

            metaStream.Flush();
            ilStream.Flush();
            pdbStream.Flush();
            Console.WriteLine($"wrote {dinfo.Dmeta}");
            // return a new deltaproject that can build the next update
            return new RoslynDeltaProject(this, project.Solution, emitResult.Baseline);
        }

        Task<IEnumerable<SemanticEdit>> CompileEdits (Document document, Document updatedDocument)
        {
            return _changeMaker.GetChanges(document, updatedDocument);
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
