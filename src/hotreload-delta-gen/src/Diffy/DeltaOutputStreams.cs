using System;
using System.IO;
using System.Threading.Tasks;

namespace Diffy
{
    public sealed class DeltaOutputStreams : IAsyncDisposable {
        public Stream MetaStream {get; private set;}
        public Stream  IlStream {get; private set;}
        public Stream PdbStream {get; private set;}

        public DeltaOutputStreams(Stream dmeta, Stream dil, Stream dpdb) {
            MetaStream = dmeta;
            IlStream = dil;
            PdbStream = dpdb;
        }

        public void Dispose () {
            MetaStream?.Dispose();
            IlStream?.Dispose();
            PdbStream?.Dispose();
        }

        public async ValueTask DisposeAsync () {
            if  (MetaStream != null) await MetaStream.DisposeAsync();
            if  (IlStream != null) await IlStream.DisposeAsync();
            if  (PdbStream != null) await PdbStream.DisposeAsync();
        }

    }
}
