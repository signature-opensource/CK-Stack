using CK.Core;
using CSemVer;
using System;

namespace CKli.ArtifactHandler.Plugin;

public sealed record LocalNuGetPackageInstance( NormalizedPath LocalPath, string PackageId, SVersion Version ) : IComparable<LocalNuGetPackageInstance>
{
    /// <summary>
    /// Gets the package instance.
    /// </summary>
    public NuGetPackageInstance PackageInstance => new NuGetPackageInstance( PackageId, Version );

    /// <inheritdoc cref="NuGetPackageInstance.CompareTo(NuGetPackageInstance)"/>
    public int CompareTo( LocalNuGetPackageInstance? other )
    {
        return other == null ? 1 : PackageInstance.CompareTo( other.PackageInstance );
    }

    /// <summary>
    /// Returns <see cref="NuGetPackageInstance.ToString()"/>.
    /// Can be parsed back by <see cref="NuGetPackageInstance.TryMatch(ref System.ReadOnlySpan{char}, out NuGetPackageInstance)"/>.
    /// </summary>
    /// <returns>The "package/version" string.</returns>
    public override string ToString() => PackageInstance.ToString();
}
