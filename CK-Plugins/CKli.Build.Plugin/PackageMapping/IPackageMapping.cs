using System.Diagnostics.CodeAnalysis;

namespace CKli.Build.Plugin;

/// <summary>
/// Primary package mapping interface.
/// </summary>
interface IPackageMapping
{
    /// <summary>
    /// Gets whether there is at least one mapping.
    /// </summary>
    bool IsEmpty { get; }

    /// <summary>
    /// Gets the mapping for the package.
    /// </summary>
    /// <param name="packageId">The package identifier.</param>
    /// <param name="map">The version map.</param>
    /// <returns>Whether the mapping exists.</returns>
    bool TryGetMapping( string packageId, [NotNullWhen( true )] out IPackageVersionMapping? map );
}
