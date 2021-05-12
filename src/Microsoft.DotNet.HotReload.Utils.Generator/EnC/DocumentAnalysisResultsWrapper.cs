using System;

namespace Microsoft.DotNet.HotReload.Utils.Generator.EnC
{

    /// Wraps a Microsoft.CodeAnalysis.EditAndContinue.DocumentAnalysisResults class
    public class DocumentAnalysisResultsWrapper
    {
        // FIXME: TODO: get the HasChanges value from the underlying
        public bool HasChanges { get => throw new NotImplementedException (); }
        // FIXME: TODO: get the HasSyntaxErrors value from the underlying
        public bool HasSyntaxErrors { get => throw new NotImplementedException (); }

        public object Underlying {get; init;}

        public DocumentAnalysisResultsWrapper(object underlying) {
            Underlying = underlying;
        }


    }
}
