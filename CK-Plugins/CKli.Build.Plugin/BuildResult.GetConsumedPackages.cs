using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.VersionTag.Plugin;
using System.Collections.Immutable;
using System.Text;

namespace CKli.Build.Plugin;

public partial class BuildResult
{
    /// <summary>
    /// Calls 'dotnet package list --format json --no-restore' and parses the result. The resulting
    /// packages are all the top level packages from all the projects for all the target frameworks
    /// (the same <see cref="NuGetPackageInstance.PackageId"/> may appear with different versions if
    /// conditional package references exist with restricted NuGet version ranges).
    /// <para>
    /// We could have captured the Requested (the version bound) in addition to the Resolved package version.
    /// This would allow a better impact computation by early filtering out useless package upgrades. This
    /// is doable but we almost never use NuGet version ranges. This would complexify the system for no real gain.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="buildInfo">The build info that has been built.</param>
    /// <returns>The set of incoming packages or null on error.</returns>
    /// <remarks>
    /// Collecting the package dependencies can be done in multiple ways:
    /// - By reading the obj/Configuration/ProjectName.csproj.nuget.dgspec.json file
    ///   that contains exactly what we need... But this is not really documented (we must iterate on
    ///   all the projects).
    /// - By reading the project.assets.json (that is documented) and extracts the top-level
    ///   dependencies 
    /// - By using BuildAlyzer (see https://github.com/Buildalyzer/Buildalyzer, we must iterate on all the projects).
    /// - By calling 'dotnet package list --format json' and parsing the result. Contains exactly what we need
    ///   and can be called on the .sln (projects are handled).
    ///
    /// => The simplest and most robust way is the 'dotnet package list --format json'.
    /// 
    /// </remarks>
    internal static bool GetConsumedPackages( IActivityMonitor monitor, CommitBuildInfo buildInfo, out ImmutableArray<NuGetPackageInstance> packages )
    {
        var stdOut = new StringBuilder();
        if( !BuildPlugin.RunDotnet( monitor, buildInfo.Repo, "package list --format json --no-restore", stdOut ) )
        {
            packages = [];
            return false;
        }
        return NuGetPackageInstance.ReadConsumedPackages( monitor, stdOut.ToString(), buildInfo, out packages );
    }

}
