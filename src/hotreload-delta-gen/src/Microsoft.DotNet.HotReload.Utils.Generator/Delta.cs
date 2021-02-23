using Microsoft.CodeAnalysis;

namespace Microsoft.DotNet.HotReload.Utils.Generator
{

    /// A Delta represents an input that is used to produce the metadata, IL and PDB emitted differences.
    /// It contains a Change which identifies the source document that changed and its updated contents
    public struct Delta {
            public readonly Plan.Change<DocumentId,string> Change;

            public Delta (Plan.Change<DocumentId,string> change) {
                Change = change;
            }
        }
}
