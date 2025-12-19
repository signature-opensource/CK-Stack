using CK.Core;
using CSemVer;
using System;

namespace CKli.ArtifactHandler.Plugin;

public readonly record struct NuGetPackageInstance( string PackageId, SVersion Version ) : IComparable<NuGetPackageInstance>
{
    /// <summary>
    /// Tries to match "xxx@version" pattern. The <paramref name="head"/> is not forwarded on error.
    /// </summary>
    /// <param name="head">The head.</param>
    /// <param name="instance">The instance on success.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool TryMatch( ref ReadOnlySpan<char> head, out NuGetPackageInstance instance )
    {
        instance = default;
        int idx = head.IndexOf( '@' );
        if( idx <= 0 ) return false;
        var rest = head.Slice( idx + 1 );
        var v = SVersion.TryParse( ref rest );
        if( !v.IsValid ) return false;

        instance = new NuGetPackageInstance( new string( head.Slice( 0, idx ) ), v );
        head = rest;
        return true;
    }

    /// <summary>
    /// Supports ordering by <see cref="PackageId"/> and <see cref="Version"/>.
    /// </summary>
    /// <param name="other">The other package.</param>
    /// <returns>Standard comparison value.</returns>
    public int CompareTo( NuGetPackageInstance other )
    {
        int cmp = StringComparer.Ordinal.Compare( PackageId, other.PackageId );
        return cmp == 0 ? Version.CompareTo( other.Version ) : cmp;
    }

    /// <summary>
    /// Returns "<see cref="PackageId"/>@<see cref="Version"/>".
    /// </summary>
    /// <returns>The package/version string.</returns>
    public override string ToString() => $"{PackageId}@{Version}";
}
