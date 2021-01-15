
namespace Diffy.Plan
{

    /// A plan is just a collection of changes
    /// where each change is some identification of the base document and
    /// some representation of the update.
    ///å
    /// For live coding, the collection will be an IAsyncEnumerable<Change<TDoc,TUpdate>>,
    /// for a scripted plan it will be some parsed immutable list of changes.
    ///
    /// Initially the changes are just Change<string,string>, but then RoslynResolvePlan will
    /// change it to a Chamge<DocumentId, string>, and when we go to apply the change, we
    /// will notionally have a Change<DocumentId, FileStream> where the update is a stream of
    /// the new document content.
    public struct Change<TDoc, TUpdate> {
        public readonly TDoc Document;
        public readonly TUpdate Update;

        public Change(TDoc doc, TUpdate update) {
            Document = doc;
            Update = update;
        }
    }

    public static class Change {
        public static Change<TDoc, TUpdate> Create<TDoc, TUpdate>(TDoc doc, TUpdate update) {
            return new Change<TDoc, TUpdate>(doc, update);
        }
    }
}
