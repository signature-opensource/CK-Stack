using CK.Core;
using CSemVer;
using System;

namespace CKli.ArtifactHandler.Plugin;

public sealed record LocalNuGetPackageInstance( NormalizedPath LocalPath, string PackageId, SVersion Version ) : IComparable<LocalNuGetPackageInstance>
{
    /// <summary>
    /// Gets the package instance.
    /// </summary>
    public PackageInstance PackageInstance => new PackageInstance( PackageId, Version );

    /// <inheritdoc cref="PackageInstance.CompareTo(PackageInstance)"/>
    public int CompareTo( LocalNuGetPackageInstance? other )
    {
        return other == null ? 1 : PackageInstance.CompareTo( other.PackageInstance );
    }

    /// <summary>
    /// Returns <see cref="PackageInstance.ToString()"/>.
    /// Can be parsed back by <see cref="PackageInstance.TryMatch(ref System.ReadOnlySpan{char}, out PackageInstance)"/>.
    /// </summary>
    /// <returns>The "package/version" string.</returns>
    public override string ToString() => PackageInstance.ToString();
}
