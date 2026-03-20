using CSemVer;
using System;

namespace CKli;

public static class SVersionExtensions
{
    public static bool IsLocalFix( this SVersion version ) => version.IsPrerelease && version.Prerelease.StartsWith( "local.fix.", StringComparison.Ordinal );

    public static bool IsCI( this SVersion version )
    {
        var r = version.Prerelease.AsSpan();
        return r.Length > 0 && (r.Contains( "-ci.", StringComparison.Ordinal ) || r.Contains( ".ci.", StringComparison.Ordinal ));
    }

}
