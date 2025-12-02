using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CKli.VersionTag.Plugin;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

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
    /// <param name="repo">The repository from which the currently checked out head will be analyzed.</param>
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
    internal static HashSet<NuGetPackageInstance>? GetConsumedPackages( IActivityMonitor monitor, CommitBuildInfo buildInfo )
    {
        var stdOut = new StringBuilder();
        if( !BuildPlugin.RunDotnet( monitor, buildInfo.Repo, "package list --format json --no-restore", stdOut ) )
        {
            return null;
        }
        try
        {
            using var d = JsonDocument.Parse( stdOut.ToString() );
            if( !ReadProblems( monitor, buildInfo, d ) )
            {
                return null;
            }
            return ReadPackages( d );
        }
        catch( Exception ex )
        {
            monitor.Error( $"While reading Package list for '{buildInfo}'.", ex );
            return null;
        }


        static bool ReadProblems( IActivityMonitor monitor, CommitBuildInfo buildInfo, JsonDocument d )
        {
            bool hasWarning = false;
            if( d.RootElement.TryGetProperty( "problems"u8, out var problems ) )
            {
                foreach( var p in problems.EnumerateArray() )
                {
                    if( p.GetProperty( "level"u8 ).GetString() == "error" )
                    {
                        monitor.Error( $"Package list for '{buildInfo}' has errors. See logs." );
                        return false;
                    }
                    else
                    {
                        hasWarning = true;
                    }
                }
            }
            if( hasWarning )
            {
                monitor.Warn( $"Package list for '{buildInfo}' has warnings. See logs." );
            }
            return true;
        }

        static HashSet<NuGetPackageInstance> ReadPackages( JsonDocument d )
        {
            var result = new HashSet<NuGetPackageInstance>();
            foreach( var p in d.RootElement.GetProperty( "projects"u8 ).EnumerateArray() )
            {
                foreach( var f in p.GetProperty( "frameworks"u8 ).EnumerateArray() )
                {
                    foreach( var package in f.GetProperty( "topLevelPackages"u8 ).EnumerateArray() )
                    {
                        string? packageId = package.GetProperty( "id"u8 ).GetString();
                        if( string.IsNullOrWhiteSpace( packageId ) )
                        {
                            Throw.InvalidDataException( $"Null or empty 'topLevelPackages.id' property." );
                        }
                        result.Add( new NuGetPackageInstance( packageId,
                                                              SVersion.Parse( package.GetProperty( "resolvedVersion"u8 ).GetString() ) ) );
                    }
                }
            }
            return result;
        }

    }
}
