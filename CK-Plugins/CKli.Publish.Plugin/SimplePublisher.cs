using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CKli.Publish.Plugin;

/// <summary>
/// Implements simple, sequential, publication process.
/// </summary>
sealed class SimplePublisher
{
    readonly ArtifactHandlerPlugin _artifactHandler;
    readonly PublishState _state;

    public SimplePublisher( ArtifactHandlerPlugin artifactHandler, PublishState state )
    {
        _artifactHandler = artifactHandler;
        _state = state;
    }

    public async Task<bool> RunAsync( IActivityMonitor monitor )
    {
        var step = _state.PrimaryCursor;
        while( !step.IsEndOfState )
        {
            switch( step.Location )
            {
                case PublishState.Cursor.LocType.InFile:
                    step = await PublishFilesAsync( monitor );
                    break;
                case PublishState.Cursor.LocType.InPackage:
                    step = await PublishPackagesAsync( monitor );
                    break;
                case PublishState.Cursor.LocType.EndOfRepo:
                    step = await PublishRepoAsync( monitor );
                    break;
                case PublishState.Cursor.LocType.EndOfWorld:
                    step = await PublishWorldAsync( monitor );
                    break;
            }
            if( step == null ) return false;
        }
        return true;
    }


    async Task<PublishState.Cursor?> PublishWorldAsync( IActivityMonitor monitor, PublishState.Cursor step )
    {
        var world = _state.PrimaryCursor.World;
        Throw.DebugAssert( world != null );
        monitor.Info( $"Release '{world.ReleaseVersion}' published." );
        return _state.ForwardPrimaryCursor( monitor, 1 );
    }

    async Task<PublishState.Cursor?> PublishRepoAsync( IActivityMonitor monitor )
    {
        var repo = _state.PrimaryCursor.Repo;
        Throw.DebugAssert( repo != null );
        // Should create the Release (if hosting provider supports it) with the repo.ReleaseNotes...

        // Push the version tag.
        if( !repo.Repo.GitRepository.PushTags( monitor, ["v" + repo.BuildVersion.ToString()] ) )
        {
            return null;
        }
        return _state.ForwardPrimaryCursor( monitor, 1 );
    }

    async Task<PublishState.Cursor?> PublishPackagesAsync( IActivityMonitor monitor )
    {
        var repo = _state.PrimaryCursor.Repo;
        Throw.DebugAssert( repo != null );
        if( !_artifactHandler.GetConfiguredNuGetFeeds( monitor, out var feeds ) )
        {
            return null;
        }
        feeds.Where( f => f.PushQualityFilter.Accepts( repo.BuildVersion ) ).ToList();
        repo.BuildContentInfo.Produced

    }

    async Task<PublishState.Cursor?> PublishFilesAsync( IActivityMonitor monitor )
    {
        throw new NotImplementedException();
    }

    sealed class PackageSender
    {
        readonly Repo _repo;
        readonly ArtifactHandlerPlugin _artifactHandler;

        PackageSender( Repo repo, ArtifactHandlerPlugin artifactHandler )
        {
            _repo = repo;
            _artifactHandler = artifactHandler;
        }

        public static PackageSender? Create( IActivityMonitor monitor, RepoPublishInfo repo, ArtifactHandlerPlugin artifactHandler )
        {
            if( !artifactHandler.GetConfiguredNuGetFeeds( monitor, out var feeds ) )
            {
                return null;
            }
            var targets = feeds.Where( f => f.PushQualityFilter.Accepts( repo.BuildVersion ) ).ToArray();
            if( targets.Length == 0 )
            {
                monitor.Error( $"No configured NuGet feeds accept version '{repo.BuildVersion}'. Unable to push '{repo.Repo.DisplayPath}' packages." );
                return null;
            }
            
        }

    }

}
