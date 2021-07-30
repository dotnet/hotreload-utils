using System;
using System.Collections.Generic;
using System.Threading;

namespace Microsoft.DotNet.HotReload.Utils.Generator.Util {
    public static class AsyncEnumerableExtras {
        public async static IAsyncEnumerable<T> Empty<T> () {
            await System.Threading.Tasks.Task.CompletedTask;
            yield break;
        }
    }
}
