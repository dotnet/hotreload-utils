using System.Threading.Tasks;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;

namespace Diffy
{

    public struct BaselineArtifacts {
        public Solution baselineSolution;
        /// the project we are working on
        public ProjectId baselineProjectId;

        /// the document that will be changing
        public DocumentId baselineDocumentId;

        /// absolute path of the baseline assembly
        public string baselineOutputAsmPath;

        public EmitBaseline emitBaseline;
    }
    public abstract class RoslynBaselineProject {

        protected RoslynBaselineProject (Solution solution, ProjectId projectId) {
            this.solution = solution;
            this.projectId = projectId;
        }

        protected readonly Solution solution;

        protected readonly ProjectId projectId;

        public abstract Task<BaselineArtifacts> PrepareBaseline ();
    }
}
