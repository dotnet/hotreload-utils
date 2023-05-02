using System;
using System.Collections.Generic;
using System.Reflection;

using Xunit;

namespace EncCapabilitiesCompatTest {
    public class EncCapabilitiesCompatTest {

        public struct CapDescriptor {
            public string Name;
            public int Value;
        }

        static readonly Microsoft.DotNet.HotReload.Utils.Generator.EnC.ChangeMaker ChangeMaker = new ();

        internal static IReadOnlyList<CapDescriptor> MakeDescriptor (Type ty) {
            List<CapDescriptor> l = new ();
            if (!ty.IsEnum) {
                throw new Exception($"Type {ty} is not an enumeration");
            }
            var underlying = ty.GetEnumUnderlyingType();
            if (underlying != typeof(int)) {
                throw new Exception($"Underlying type of {ty} is {underlying}, not Int32");
            }
            foreach (var enumName in ty.GetEnumNames()) {
                if (!Enum.TryParse(ty, enumName, out var v))
                    throw new Exception ($"Could not get value of enumeration constant {enumName} of type {ty}");
                if (v == null)
                    throw new Exception ($"enumeration value of {ty}.{enumName} was null");
                int intVal = (int)v;
                l.Add (new CapDescriptor {Name = enumName, Value = intVal});
            }
            return l;
        }

        public static IReadOnlyList<CapDescriptor> GetGeneratorCapabilities() => MakeDescriptor(typeof(Microsoft.DotNet.HotReload.Utils.Generator.EnC.EditAndContinueCapabilities));
        public static IReadOnlyList<CapDescriptor> GetRoslynCapabilities() => MakeDescriptor(ChangeMaker.EditAncContinueCapabilitiesType);


        [Fact]
        public static void TestFailing()
        {
            Assert.True(false, "This test should fail");
        }

        [Fact]
        public static void SameCapabilities () {
            // Check that roslyn and hotreload-utils have the same EnC capabilities defined.
            // Should help to keep hotreload-utils up to date when Roslyn pushes changes.

            IReadOnlyList<CapDescriptor> generatorCaps = GetGeneratorCapabilities();
            IReadOnlyList<CapDescriptor> roslynCaps = GetRoslynCapabilities();

            // TODO: Maybe collect all mismatches and just assert once at the end?
            // Or use a [Theory] for each cap that compares it against the whole list from the other set
            foreach (var roslynCap in roslynCaps) {
                bool found = false;
                foreach (var generatorCap in generatorCaps) {
                    if (roslynCap.Name != generatorCap.Name)
                        continue;
                    found = true;
                    Assert.True (roslynCap.Value == generatorCap.Value, $"Capability {roslynCap.Name} in Roslyn has value {roslynCap.Value} and {generatorCap.Value} in hotreload-utils.");
                }
                Assert.True (found, $"Capability {roslynCap.Name} in Roslyn with value {roslynCap.Value} not present in hotreload-utils");
            }

            foreach (var generatorCap in generatorCaps) {
                bool found = false;
                foreach (var roslynCap in roslynCaps) {
                    if (roslynCap.Name == generatorCap.Name) {
                        // if it's in both it has the same value, by the previous loop
                        found = true;
                        break;
                    }
                }
                Assert.True (found, $"Capability {generatorCap.Name} in hotreload-utils with value {generatorCap.Value} not present in Roslyn");
            }
        }
    }
}
