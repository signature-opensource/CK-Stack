using CSemVer;
using System;
using System.Threading.Tasks;

namespace CKli.LocalNuGetFeed.Plugin;

public readonly record struct NuGetPackageInstance( string PackageId, SVersion Version )
{
    /// <summary>
    /// Tries to match "xxx/version" pattern. The <paramref name="head"/> is not forwarded on error.
    /// </summary>
    /// <param name="head">The head.</param>
    /// <param name="instance">The instance on success.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool TryMatch( ref ReadOnlySpan<char> head, out NuGetPackageInstance instance )
    {
        instance = default;
        int idx = head.IndexOf( '/' );
        if(  idx <= 0 ) return false;
        var rest = head.Slice( idx + 1 );
        var v = SVersion.TryParse( ref rest );
        if( !v.IsValid ) return false;

        instance = new NuGetPackageInstance( new string( head.Slice( 0, idx ) ), v );
        head = rest;
        return true;
    }

    /// <summary>
    /// Returns "<see cref="PackageId"/>/<see cref="Version"/>".
    /// </summary>
    /// <returns>The package/version string.</returns>
    public override string ToString() => $"{PackageId}/{Version}";
}
