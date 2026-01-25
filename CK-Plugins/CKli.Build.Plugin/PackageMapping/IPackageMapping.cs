using CSemVer;

namespace CKli.Build.Plugin;

/// <summary>
/// Primary package mapping interface. This is a read only view of <see cref="PackageMapper"/>
/// or a wrapper that can adapt version mapping.
/// </summary>
interface IPackageMapping
{
    /// <summary>
    /// Gets whether there is at least one mapping.
    /// </summary>
    bool IsEmpty { get; }

    /// <summary>
    /// Gets whether a package has at least one mapping.
    /// </summary>
    /// <param name="packageId">The package identifier.</param>
    /// <returns>True if the package has version mapping, false otherwise.</returns>
    bool HasMapping( string packageId );

    /// <summary>
    /// Gets the mapped version.
    /// </summary>
    /// <param name="packageId">The package identifier.</param>
    /// <param name="from">The origin version.</param>
    /// <returns>Null when not mapped.</returns>
    SVersion? GetMappedVersion( string packageId, SVersion from );
}
