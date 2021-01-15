using System;
using System.IO;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis;

namespace Diffy {

    /// Maps a source file path to a DocumentId in a given Project
    public class RoslynDocResolver {

        private readonly Project project;

        private readonly ImmutableDictionary<string,DocumentId> docMap;
        public RoslynDocResolver(Project project) {
            this.project = project;
            this.docMap = BuildDocMap (project.Documents);
        }

        public Project Project { get => project; }

        private static ImmutableDictionary<string, DocumentId> BuildDocMap (IEnumerable<Document> docs)
        {
            var builder = ImmutableDictionary.CreateBuilder<string, DocumentId>();
            foreach (var doc in docs) {
                var key = doc.FilePath;
                var value = doc.Id;
                var kvp = KeyValuePair.Create(key!, value);
                builder.Add(kvp);
            }
            return builder.ToImmutable();
        }

        public bool TryResolveDocumentId (string relativePath, [NotNullWhen(true)] out DocumentId id) {
            var absolutePath = Path.GetFullPath(relativePath);
            return docMap.TryGetValue(absolutePath, out id!);
        }

    }
}
