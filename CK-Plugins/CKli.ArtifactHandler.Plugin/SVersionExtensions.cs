using CSemVer;
using System;

namespace CKli;

public static class SVersionExtensions
{
    public static bool IsLocalFix( this SVersion version ) => version.IsPrerelease && version.Prerelease.StartsWith( "local.fix.", StringComparison.Ordinal );
}
