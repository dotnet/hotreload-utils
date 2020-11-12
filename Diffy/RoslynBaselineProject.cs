using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;

namespace Diffy
{

    public struct BaselineArtifacts {
        public Workspace workspace;
        /// the project we are working on
        public ProjectId baselineProjectId;

        /// the document that will be changing
        public DocumentId baselineDocumentId;

        /// absolute path of the baseline assembly
        public string baselineOutputAsmPath;

        public EmitBaseline emitBaseline;
    }
    public abstract class RoslynBaselineProject {

        protected RoslynBaselineProject (Workspace workspace, ProjectId projectId) {
            this.workspace = workspace;
            this.projectId = projectId;
        }

        protected readonly Workspace workspace;

        protected readonly ProjectId projectId;

        public abstract Task<BaselineArtifacts> PrepareBaseline ();
    }
}
