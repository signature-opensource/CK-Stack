using CSemVer;
using System.Diagnostics.CodeAnalysis;

namespace CKli.Build.Plugin;

/// <summary>
/// Version mapping for a package.
/// </summary>
interface IPackageVersionMapping
{
    /// <summary>
    /// Gets the mapped version.
    /// </summary>
    /// <param name="from">The origin version.</param>
    /// <returns>The mapped version or null.</returns>
    SVersion? Get( SVersion from );

}
