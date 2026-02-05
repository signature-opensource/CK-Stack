using CSemVer;
using System.Diagnostics.CodeAnalysis;

namespace CKli.ShallowSolution.Plugin;

public static class PackageMappingExtensions
{
    /// <summary>
    /// Gets the mapped version.
    /// </summary>
    /// <param name="mapping">This mapping.</param>
    /// <param name="packageId">The package identifier.</param>
    /// <param name="from">The origin version.</param>
    /// <param name="to">The mapped version.</param>
    /// <returns>True on success, false on error.</returns>
    public static bool TryGetMappedVersion( this IPackageMapping mapping, string packageId, SVersion from, [NotNullWhen( true )] out SVersion? to )
    {
        return (to = mapping.GetMappedVersion( packageId, from )) != null;
    }
}
