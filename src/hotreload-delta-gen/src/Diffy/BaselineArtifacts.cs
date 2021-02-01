using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;

namespace Diffy
{
    /// What we know about the base compilation
    public struct BaselineArtifacts {
        public Solution baselineSolution;
        /// the project we are working on
        /// FIXME: need to be more clever when there are project references
        public ProjectId baselineProjectId;

        /// absolute path of the baseline assembly
        public string baselineOutputAsmPath;

        public EmitBaseline emitBaseline;

        public DocResolver docResolver;
    }

}
