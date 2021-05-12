using System;

namespace Microsoft.DotNet.HotReload.Utils.Generator.EnC
{

    /// Copied from https://github.com/dotnet/roslyn/blob/e8e6d30fe462edd48ce13f6438e91d26876c17bb/src/Features/Core/Portable/EditAndContinue/EditAndContinueCapabilities.cs
    /// Keep in sync
    /// <summary>
    /// The capabilities that the runtime has with respect to edit and continue
    /// </summary>
    [Flags]
    internal enum EditAndContinueCapabilities
    {
        None = 0,

        /// <summary>
        /// Edit and continue is generally available with the set of capabilities that Mono 6, .NET Framework and .NET 5 have in common.
        /// </summary>
        Baseline = 1 << 0,

        /// <summary>
        /// Adding a static or instance method to an existing type.
        /// </summary>
        AddMethodToExistingType = 1 << 1,

        /// <summary>
        /// Adding a static field to an existing type.
        /// </summary>
        AddStaticFieldToExistingType = 1 << 2,

        /// <summary>
        /// Adding an instance field to an existing type.
        /// </summary>
        AddInstanceFieldToExistingType = 1 << 3,

        /// <summary>
        /// Creating a new type definition.
        /// </summary>
        NewTypeDefinition = 1 << 4
    }

}
