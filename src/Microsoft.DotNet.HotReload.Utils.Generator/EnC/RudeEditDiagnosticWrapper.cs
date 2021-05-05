// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.CodeAnalysis.Text;

namespace Microsoft.DotNet.HotReload.Utils.Generator.EnC
{
    // wrap the string representation of
    /// Microsoft.CodeAnalysis.EditAndContinue.RudeEditDiagnostic struct
    /// which is unfortunately internal.
    public struct RudeEditDiagnosticWrapper {
        public string KindWrapper {get; init;}
        public TextSpan Span {get; init;}

        public RudeEditDiagnosticWrapper (string kind, TextSpan span) {
            KindWrapper = kind;
            Span = span;
        }
    }
}
