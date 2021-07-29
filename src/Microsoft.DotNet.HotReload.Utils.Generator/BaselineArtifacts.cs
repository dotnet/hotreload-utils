// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Emit;

namespace Microsoft.DotNet.HotReload.Utils.Generator
{
    /// What we know about the base compilation
    public struct BaselineArtifacts {
        public Solution baselineSolution;
        /// the project we are working on
        /// FIXME: need to be more clever when there are project references
        public ProjectId baselineProjectId;

        /// absolute path of the baseline assembly
        public string baselineOutputAsmPath;

        public DocResolver docResolver;

        /// A stateful encapsulation of the series of changes that have been made to the baseline
        public EnC.ChangeMakerService changeMakerService;
    }

}
