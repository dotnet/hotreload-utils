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
        readonly AsyncLocal<Project> _project;
        readonly AsyncLocal<EmitBaseline?> _baseline;

        readonly DocumentId _baseDocumentId;


        public RoslynDeltaProject(BaselineArtifacts artifacts)
            : this (artifacts.workspace, artifacts.baselineProjectId, artifacts.emitBaseline, artifacts.baselineDocumentId) {}

        private RoslynDeltaProject(Workspace workspace, ProjectId projectId, EmitBaseline baseline, DocumentId baseDocumentId)
            : this (workspace.CurrentSolution.GetProject(projectId)!, baseline, baseDocumentId) {}

        private RoslynDeltaProject (Project project, EmitBaseline baseline, DocumentId baseDocumentId) {
            _changeMaker = new ChangeMaker();
            _project = new AsyncLocal<Project>();
            _project.Value = project;
            _baseline = new AsyncLocal<EmitBaseline?>();
            _baseline.Value = baseline;
            _baseDocumentId = baseDocumentId; /*project.Documents.Where((doc) => doc.FilePath == baselinePath).First().Id;*/
        }


        /// Note Project changes as deltas are built
        public Project Project {
            get => _project.Value ?? throw new NullReferenceException("no Project async local value");
            internal set { _project.Value = value; }
        }

        public EmitBaseline? Baseline {
            get => _baseline.Value;
            internal set { _baseline.Value = value; }
        }

        public DocumentId BaseDocumentId => _baseDocumentId;

        /// Builds a delta for the specified document given a path to its updated contents and a revision count
        /// On failure throws a DiffyException and with exitStatus > 0
        public async Task BuildDelta (string deltaFile, DerivedArtifactInfo dinfo)
        {
            Console.WriteLine ($"parsing patch #{dinfo.Rev} from {deltaFile} and creating delta");

            Document document = Project.GetDocument(BaseDocumentId)!;

            Document updatedDocument;
            using (var contents = File.OpenRead (deltaFile)) {
                updatedDocument = document.WithText (SourceText.From (contents, Encoding.UTF8));
            }
            Project = updatedDocument.Project;
            if (updatedDocument.Id != BaseDocumentId)
                throw new Exception ("Unexpectedly, document Id of the delta != base document Id");

            var changes = await updatedDocument.GetTextChangesAsync (document);
            if (!changes.Any()) {
                Console.WriteLine ("no changes found");
                //FIXME can continue here and just ignore the revision
                throw new DiffyException ($"no changes in revision {dinfo.Rev}", exitStatus: 5);
            }

            Console.WriteLine ($"Found changes in {document.Name}");

            Task<Compilation> updatedCompilation = Task.Run(async () => {
                var compilation = await Project.GetCompilationAsync ();
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

            using (var metaStream = File.Create (dinfo.Dmeta))
            using (var ilStream   = File.Create (dinfo.Dil))
            using (var pdbStream  = File.Create (dinfo.Dpdb)) {
                EmitDifferenceResult emitResult = updatedCompilationResult.EmitDifference (baseline, edits, metaStream, ilStream, pdbStream, updatedMethods);
                CheckEmitResult(emitResult);

                metaStream.Flush();
                ilStream.Flush();
                pdbStream.Flush();

                // Update baseline for next delta
                Baseline = emitResult.Baseline;
            }
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
