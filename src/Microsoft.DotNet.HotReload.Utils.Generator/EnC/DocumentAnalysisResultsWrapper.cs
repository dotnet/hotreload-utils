
namespace Microsoft.DotNet.HotReload.Utils.Generator.EnC
{

    /// Wraps a Microsoft.CodeAnalysis.EditAndContinue.DocumentAnalysisResults class
    public class DocumentAnalysisResultsWrapper
    {
        public bool HasChanges { get; }
        public bool HasSyntaxErrors { get; }

        public object Underlying {get; init;}

        public DocumentAnalysisResultsWrapper(object underlying) {
            Underlying = underlying;
        }
    }
}
