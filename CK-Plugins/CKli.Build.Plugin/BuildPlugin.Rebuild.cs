using CK.Core;
using CKli.Core;
using CKli.VersionTag.Plugin;
using CSemVer;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace CKli.Build.Plugin;

public sealed partial class BuildPlugin
{
    [Description( """
        Tries to rebuild the oldest releases until a success.
        Failing commits are tagged with a '+Deprecated' tag.
        """ )]
    [CommandPath( "repo rebuild old" )]
    public bool RebuildOld( IActivityMonitor monitor,
                            CKliEnv context,
                            [Description( "Warns only: doesn't create a 'Deprecated' tag on the failing commit." )]
                            bool warnOnly,
                            [Description( "Runs unit tests. They must be successful." )]
                            bool runTest,
                            [Description( "Consider all the Repos of the current World (even if current path is in a Repo)." )]
                            bool all )
    {
        IReadOnlyList<Repo>? repos = all
                                      ? World.GetAllDefinedRepo( monitor )
                                      : World.GetAllDefinedRepo( monitor, context.CurrentDirectory );
        if( repos == null )
        {
            return false;
        }
        foreach( var repo in repos )
        {
            using( monitor.OpenInfo( $"Rebuilding old releases of '{repo.DisplayPath}'." ) )
            {
                var versionTagInfo = _versionTags.Get( monitor, repo );
                foreach( var tag in versionTagInfo.LastStables.Reverse() )
                {
                    if( CoreBuild( monitor, context, versionTagInfo, tag.Commit, tag.Version, runTest, rebuild: true ) )
                    {
                        monitor.Info( ScreenType.CKliScreenTag, $"Version '{tag.Version.ParsedText}' of '{repo.DisplayPath}' is valid." );
                        break;
                    }
                    monitor.Warn( $"Version '{tag.Version.ParsedText}' of '{repo.DisplayPath}' cannot be rebuilt." );
                    if( !warnOnly )
                    {
                        string deprecatedTag = $"v{tag.Version.WithBuildMetaData( null )}+Deprecated";
                        monitor.Info( $"Adding '{deprecatedTag}' on '{tag.Commit.Sha}'." );
                        repo.GitRepository.Repository.Tags.Add( deprecatedTag, tag.Commit );
                    }
                }
            }
        }
        return true;
    }

    [Description( """
        Rebuild the specified version in the current repository.
        """ )]
    [CommandPath( "repo rebuild version" )]
    public bool RebuildVersion( IActivityMonitor monitor,
                                CKliEnv context,
                                [Description( "The version to rebuild." )]
                                string version,
                                [Description( "Don't run tests even if they have never locally run on this commit." )]
                                bool skipTests = false,
                                [Description( "Run tests even if they have already run successfully on this commit." )]
                                bool forceTests = false )
    {
        if( !HandleForceSkipTests( monitor, skipTests, forceTests, out bool? runTest ) )
        {
            return false;
        }
        var v = SVersion.TryParse( version );
        if( !v.IsValid )
        {
            monitor.Error( $"Invalid version argument: {v.ErrorMessage}." );
            return false;
        }
        var repo = World.GetDefinedRepo( monitor, context.CurrentDirectory );
        if( repo == null || !repo.GitRepository.CheckCleanCommit( monitor ) )
        {
            return false;
        }
        var versionTagInfo = _versionTags.Get( monitor, repo );
        if( !versionTagInfo.TagCommits.TryGetValue( v, out var tag ) )
        {
            monitor.Error( $"Unable to find version 'v{v}'." );
            return false;
        }
        if( CoreBuild( monitor, context, versionTagInfo, tag.Commit, tag.Version, runTest, rebuild: true ) )
        {
            monitor.Info( ScreenType.CKliScreenTag, $"Version '{tag.Version.ParsedText}' of '{repo.DisplayPath}' has been successfully rebuilt." );
            return true;
        }
        monitor.Error( "Build failed. See 'ckli log'." );
        return false;
    }
}
