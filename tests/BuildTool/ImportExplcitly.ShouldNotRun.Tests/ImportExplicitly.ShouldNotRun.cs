using System;
using System.IO;
using Xunit;

using AssemblyExtensions = System.Reflection.Metadata.AssemblyExtensions;

namespace ImportExplicitly.ShouldNotRun.Tests
{
    public class ImportExplicitlyShouldNotRun
    {
        [Fact]
        public void NoGeneratedDeltas()
        {
            // Check that the delta generator did not run - there should be no .dmeta files next to the assembly

            var path = Path.GetDirectoryName(typeof(ImportExplicitlyShouldNotRun).Assembly.Location);
            var metadataDeltas = Directory.GetFiles(path!, "*.dmeta");
            Assert.Empty(metadataDeltas);
        }

    }
}
