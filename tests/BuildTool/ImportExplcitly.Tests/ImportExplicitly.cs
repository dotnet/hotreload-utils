using System;
using System.IO;
using Xunit;
using Microsoft.DotNet.RemoteExecutor;

using AssemblyExtensions = System.Reflection.Metadata.AssemblyExtensions;

namespace ImportExplicitly.Tests
{
    // FIXME: CollectionDefinition RunInParallel false
    public class ImportExplicitly
    {
        [Fact]
        public void TestDeltaExists()
        {
            // In Release configurations we can only check that the tool created the files
            var path = typeof(ImportExplicitly).Assembly.Location;
            Assert.True(File.Exists(path + ".1.dmeta"));
            Assert.True(File.Exists(path + ".1.dil"));
        }

        [Fact]
        public void TestGoodDelta()
        {
            // In a Debug configuration, we can try to apply the delta, if the right environment is set

            var options = new RemoteInvokeOptions();
            options.StartInfo.Environment.Add ("DOTNET_MODIFIABLE_ASSEMBLIES", "Debug");

            RemoteExecutor.Invoke(static () =>
            {
                string s = TargetClass.TargetMethod();

                Assert.Equal("OLD", s);

                ApplyUpdate();

                s = TargetClass.TargetMethod ();

                Assert.Equal("NEW", s);
            }, options).Dispose();
        }

        internal static void ApplyUpdate()
        {
            var path = typeof(ImportExplicitly).Assembly.Location;
            var dmeta = File.ReadAllBytes(path + ".1.dmeta");
            var dil = File.ReadAllBytes(path + ".1.dil");
            AssemblyExtensions.ApplyUpdate (typeof(ImportExplicitly).Assembly, dmeta, dil, ReadOnlySpan<byte>.Empty);
        }
    }
}
