using CSemVer;

namespace CKli.ShallowSolution.Plugin;

/// <summary>
/// Primary package mapping interface.
/// <para>
/// This mapping handles more than one version mapping for a package identifier. This supports
/// an aggressive programming model in which only the exact dependencies version are updated
/// (the default <see cref="PackageMapper"/> implements this).
/// </para>
/// <para>
/// More relaxed implementations are possible. See <see cref="BrutalPackageMapper"/> for fully
/// relaxed mappers.
/// </para>
/// </summary>
public interface IPackageMapping
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
