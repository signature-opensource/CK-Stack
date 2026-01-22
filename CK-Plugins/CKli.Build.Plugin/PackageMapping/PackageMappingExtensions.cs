using CSemVer;
using System.Diagnostics.CodeAnalysis;

namespace CKli.Build.Plugin;

static class PackageMappingExtensions
{
    /// <summary>
    /// Gets the mapped version.
    /// </summary>
    /// <param name="packageId">The package identifier.</param>
    /// <param name="from">The origin version.</param>
    /// <returns>Null when not mapped.</returns>
    public static SVersion? GetMappedVersion( this IPackageMapping mapping, string packageId, SVersion from )
    {
        return mapping.TryGetMapping( packageId, out var map )
               ? map.Get( from )
               : null;
    }

    /// <summary>
    /// Gets the mapped version.
    /// </summary>
    /// <param name="packageId">The package identifier.</param>
    /// <param name="from">The origin version.</param>
    /// <param name="to">The mapped version.</param>
    /// <returns>True on success, false on error.</returns>
    public static bool TryGetMappedVersion( this IPackageMapping mapping, string packageId, SVersion from, [NotNullWhen( true )] out SVersion? to )
    {
        return (to = mapping.GetMappedVersion( packageId, from )) != null;
    }

    /// <summary>
    /// Gets the mapped version.
    /// </summary>
    /// <param name="from">The origin version.</param>
    /// <param name="to">Outputs the mapped version on success.</param>
    /// <returns>True on success, false if not found.</returns>
    public static bool TryGet( this IPackageVersionMapping mapping, SVersion from, [NotNullWhen( true )] out SVersion? to )
    {
        return (to = mapping.Get( from )) != null;
    }

}
