using CK.Core;
using CKli.Core;
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

    public SimplePublisher( PublishState state, PackageSender packageSender )
    {
        _state = state;
        _packageSender = packageSender;
    }

    public async Task<bool> RunAsync( IActivityMonitor monitor, System.Threading.CancellationToken cancel )
    {
        var step = _state.PrimaryCursor;
        while( !step.IsEndOfState )
        {
            switch( step.Location )
            {
                case PublishState.Cursor.LocType.BegOfRepo:
                    step = await OnBegOfRepoAsync( monitor );
                    break;
                case PublishState.Cursor.LocType.InPackage:
                    step = await OnInPackagesAsync( monitor );
                    break;
                case PublishState.Cursor.LocType.InFile:
                    step = await OnInFilesAsync( monitor );
                    break;
                case PublishState.Cursor.LocType.EndOfRepo:
                    step = await OnEndOfRepoAsync( monitor );
                    break;
                case PublishState.Cursor.LocType.EndOfWorld:
                    step = await OnEndOfWorldAsync( monitor );
                    break;
            }
            if( step == null ) return false;
        }
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
        if( await _packageSender.SendAsync( monitor, repo.BuildVersion, repo.BuildContentInfo.Produced ).ConfigureAwait( false ) )
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
        // Should create the Release (if hosting provider supports it) with the repo.ReleaseNotes...

        // Push the version tag.
        if( !repo.Repo.GitRepository.PushTags( monitor, ["v" + repo.BuildVersion.ToString()] ) )
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
