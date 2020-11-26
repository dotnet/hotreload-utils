using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Diffy.AsyncUtils {
    public static class AsyncExtensions {
        public static IAsyncEnumerable<T> Asynchronously<T> (this IEnumerable<T> e) {
            IAsyncEnumerable<T> a = new AsyncEnumerableAdapter<T>(e);
            return a;
        }
    }

    internal class AsyncEnumerableAdapter<T> : IAsyncEnumerable<T> {
        readonly IEnumerable<T> enumerable;

        internal AsyncEnumerableAdapter(IEnumerable<T> enumerable) {
            this.enumerable = enumerable;
        }

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default) {
            return new AsyncEnumeratorAdapter<T> (enumerable.GetEnumerator());
        }
    }
    internal class AsyncEnumeratorAdapter<T> : IAsyncEnumerator<T> {
        readonly IEnumerator<T> enumerator;
        internal AsyncEnumeratorAdapter(IEnumerator<T> enumerator) {
            this.enumerator = enumerator;
        }

        public T Current => enumerator.Current;

        public ValueTask<bool> MoveNextAsync() {
            return ValueTask.FromResult(enumerator.MoveNext());
        }

        public ValueTask DisposeAsync () {
            return ValueTask.CompletedTask;
        }

    }
}
