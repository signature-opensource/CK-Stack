using CK.Core;
using CSemVer;

namespace CKli.LocalNuGetFeed.Plugin;

public sealed record LocalNuGetPackageInstance( NormalizedPath LocalPath, string PackageId, SVersion Version )
{
    public NuGetPackageInstance PackageInstance => new NuGetPackageInstance( PackageId, Version );

    /// <summary>
    /// Returns <see cref="NuGetPackageInstance.ToString()"/>.
    /// Can be parsed back by <see cref="NuGetPackageInstance.TryMatch(ref System.ReadOnlySpan{char}, out NuGetPackageInstance)"/>.
    /// </summary>
    /// <returns>The "package/version" string.</returns>
    public override string ToString() => PackageInstance.ToString();
}
