using CK.Core;
using CKli.ReleaseDatabase.Plugin;
using System;
using System.Threading.Tasks;

namespace CKli.Publish.Plugin;

/// <summary>
/// Implements simple, sequential, publication process.
/// </summary>
sealed partial class SimplePublisher
{
    readonly PublishState _state;
    readonly PackageSender _packageSender;
    readonly ReleaseDatabasePlugin _releaseDatabase;

    public SimplePublisher( PublishState state, PackageSender packageSender, ReleaseDatabasePlugin releaseDatabase )
    {
        _state = state;
        _packageSender = packageSender;
        _releaseDatabase = releaseDatabase;
    }

    public async Task<bool> RunAsync( IActivityMonitor monitor, System.Threading.CancellationToken cancel )
    {
        var step = _state.PrimaryCursor;
        while( !step.IsEndOfState )
        {
            switch( step.Location )
            {
                case PublishState.Cursor.LocType.BegOfRepo:
                    step = await OnBegOfRepoAsync( monitor ).ConfigureAwait( false );
                    break;
                case PublishState.Cursor.LocType.InPackage:
                    step = await OnInPackagesAsync( monitor ).ConfigureAwait( false );
                    break;
                case PublishState.Cursor.LocType.InFile:
                    step = await OnInFilesAsync( monitor ).ConfigureAwait( false );
                    break;
                case PublishState.Cursor.LocType.EndOfRepo:
                    step = await OnEndOfRepoAsync( monitor ).ConfigureAwait( false );
                    break;
                case PublishState.Cursor.LocType.EndOfWorld:
                    step = await OnEndOfWorldAsync( monitor ).ConfigureAwait( false );
                    break;
            }
            if( step == null ) return false;
        }
        _state.World.StackRepository.PushChanges( monitor );
        return true;
    }

    async Task<PublishState.Cursor?> OnBegOfRepoAsync( IActivityMonitor monitor )
    {
        return _state.ForwardPrimaryCursor( monitor, 1 );
    }

    async Task<PublishState.Cursor?> OnInPackagesAsync( IActivityMonitor monitor )
    {
        var repo = _state.PrimaryCursor.Repo;
        Throw.DebugAssert( repo != null && repo.BuildContentInfo.Produced.Length > 0 );
        if( !await _packageSender.SendAsync( monitor, repo.BuildVersion, repo.BuildContentInfo.Produced ).ConfigureAwait( false ) )
        {
            return null; 
        }
        
        return _state.ForwardPrimaryCursor( monitor, repo.BuildContentInfo.Produced.Length );
    }

    async Task<PublishState.Cursor?> OnInFilesAsync( IActivityMonitor monitor )
    {
        throw new NotSupportedException();
    }

    async Task<PublishState.Cursor?> OnEndOfRepoAsync( IActivityMonitor monitor )
    {
        var repo = _state.PrimaryCursor.Repo;
        Throw.DebugAssert( repo != null );
        // Push the version tag.
        Core.GitRepository r = repo.Repo.GitRepository;
        if( !r.PushTags( monitor, ["v" + repo.BuildVersion.ToString()] ) )
        {
            return null;
        }
        // Push the branch.
        var branch = r.GetBranch( monitor, repo.BranchName, missingLocalAndRemote: LogLevel.Error );
        if( branch == null )
        {
            return null;
        }
        if( !r.PushBranch( monitor, branch, autoCreateRemoteBranch: true ) )
        {
            return null;
        }
        // Moves the release from local to published database.
        if( !_releaseDatabase.PublishRelease( monitor, repo.Repo, repo.BuildVersion ) )
        {
            return null;
        }
        // Should create the Release (if hosting provider supports it).
        if( !r.RepositoryKey.TryGetHostingInfo(  monitor, out var hostingProvider, out _ ) )
        {
            return null;
        }
        return _state.ForwardPrimaryCursor( monitor, 1 );
    }

    async Task<PublishState.Cursor?> OnEndOfWorldAsync( IActivityMonitor monitor )
    {
        var world = _state.PrimaryCursor.World;
        Throw.DebugAssert( world != null );
        monitor.Info( $"Published '{world.Title}'." );
        return _state.ForwardPrimaryCursor( monitor, 1 );
    }

}
