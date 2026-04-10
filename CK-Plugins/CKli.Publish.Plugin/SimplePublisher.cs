using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CKli.ReleaseDatabase.Plugin;
using CKli.VersionTag.Plugin;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using static CK.Core.ActivityMonitorErrorCounter;

namespace CKli.Publish.Plugin;

/// <summary>
/// Implements simple, sequential, publication process.
/// </summary>
sealed partial class SimplePublisher
{
    readonly PublishState _state;
    readonly PackageSender _packageSender;
    readonly ReleaseDatabasePlugin _releaseDatabase;
    readonly ArtifactHandlerPlugin _artifactHandler;
    readonly VersionTagPlugin _versionTag;

    GitHostingProvider? _hostingProvider;
    NormalizedPath _hostedRepoPath;
    string? _releaseId;

    public SimplePublisher( PublishState state,
                            PackageSender packageSender,
                            ReleaseDatabasePlugin releaseDatabase,
                            ArtifactHandlerPlugin artifactHandler,
                            VersionTagPlugin versionTag )
    {
        _state = state;
        _packageSender = packageSender;
        _releaseDatabase = releaseDatabase;
        _artifactHandler = artifactHandler;
        _versionTag = versionTag;
    }

    public async Task<bool> RunAsync( IActivityMonitor monitor, CancellationToken cancel )
    {
        var step = _state.PrimaryCursor;
        RepoPublishInfo? repo;
        while( !step.IsEndOfState )
        {
            switch( step.Location )
            {
                case PublishState.Cursor.LocType.BegOfRepo:
                    repo = _state.PrimaryCursor.Repo;
                    Throw.DebugAssert( repo != null );
                    step = await OnBegOfRepoAsync( monitor, repo, cancel ).ConfigureAwait( false );
                    break;
                case PublishState.Cursor.LocType.InPackage:
                    repo = _state.PrimaryCursor.Repo;
                    Throw.DebugAssert( repo != null );
                    step = await OnInPackagesAsync( monitor, repo, cancel ).ConfigureAwait( false );
                    break;
                case PublishState.Cursor.LocType.InFile:
                    repo = _state.PrimaryCursor.Repo;
                    Throw.DebugAssert( repo != null );
                    step = await OnInFilesAsync( monitor, repo, cancel ).ConfigureAwait( false );
                    break;
                case PublishState.Cursor.LocType.EndOfRepo:
                    repo = _state.PrimaryCursor.Repo;
                    Throw.DebugAssert( repo != null );
                    step = await OnEndOfRepoAsync( monitor, repo, cancel ).ConfigureAwait( false );
                    break;
                case PublishState.Cursor.LocType.EndOfWorld:
                    step = await OnEndOfWorldAsync( monitor, cancel ).ConfigureAwait( false );
                    break;
            }
            if( step == null ) return false;
        }
        _state.World.StackRepository.PushChanges( monitor );
        return true;
    }

    async Task<PublishState.Cursor?> OnBegOfRepoAsync( IActivityMonitor monitor, RepoPublishInfo repo, CancellationToken cancel )
    {
        if( !repo.Repo.GitRepository.RepositoryKey.TryGetHostingInfo( monitor, out _hostingProvider, out _hostedRepoPath ) )
        {
            monitor.Error( $"Unable to resolve Git hosting provider for '{repo.Repo.DisplayPath}' ({repo.Repo.OriginUrl})." );
            return null;
        }
        // If we have packages to push, we let the InPackages state create the release.
        // But if we have no packages, we create the release right now.
        return repo.BuildContentInfo.Produced.Length == 0
                    ? await CreateRelease( monitor, repo, 1, cancel ).ConfigureAwait( false )
                    : _state.ForwardPrimaryCursor( monitor, 1 );
    }

    async Task<PublishState.Cursor?> OnInPackagesAsync( IActivityMonitor monitor, RepoPublishInfo repo, CancellationToken cancel )
    {
        if( !await _packageSender.SendAsync( monitor, repo.BuildVersion, repo.BuildContentInfo.Produced, cancel ).ConfigureAwait( false ) )
        {
            return null;
        }
        return await CreateRelease( monitor, repo, repo.BuildContentInfo.Produced.Length, cancel ).ConfigureAwait( false );
    }

    async Task<PublishState.Cursor?> CreateRelease( IActivityMonitor monitor, RepoPublishInfo repo, int forwardLength, CancellationToken cancel )
    {
        // To create a release, hosting provider requires that the tag exists in the repository, so it's time to push it.
        var versionedTag = "v" + repo.BuildVersion.ToString();
        GitRepository r = repo.Repo.GitRepository;
        if( !r.PushTags( monitor, [versionedTag] ) )
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
        Throw.DebugAssert( _hostingProvider != null );
        _releaseId = await _hostingProvider.CreateDraftReleaseAsync( monitor, _hostedRepoPath, versionedTag, cancel ).ConfigureAwait( false );
        return _releaseId == null
                ? null
                : _state.ForwardPrimaryCursor( monitor, forwardLength );
    }

    async Task<PublishState.Cursor?> OnInFilesAsync( IActivityMonitor monitor, RepoPublishInfo repo, CancellationToken cancel )
    {
        Throw.DebugAssert( _hostingProvider != null && _releaseId != null );
        Throw.DebugAssert( repo.BuildContentInfo.AssetFileNames.Length > 0 );
        var folder = _artifactHandler.GetAssetsFolder( repo.Repo, repo.BuildVersion );
        if( !Directory.Exists( folder ) )
        {
            monitor.Error( $"""
                Expected folder '{folder}' to exist with files:
                '{repo.BuildContentInfo.AssetFileNames.Concatenate("', '")}'.
                """ );
            return null;
        }
        if( !await _hostingProvider.AddReleaseAssetsAsync( monitor, _hostedRepoPath, _releaseId, folder, cancel ).ConfigureAwait( false ) )
        {
            return null;
        }
        return _state.ForwardPrimaryCursor( monitor, repo.BuildContentInfo.AssetFileNames.Length );
    }

    async Task<PublishState.Cursor?> OnEndOfRepoAsync( IActivityMonitor monitor, RepoPublishInfo repo, CancellationToken cancel )
    {
        // Moves the release from local to published database.
        if( !_releaseDatabase.PublishRelease( monitor, repo.Repo, repo.BuildVersion ) )
        {
            return null;
        }
        // To consider that the war is won, we must first be sure that the published database
        // is pushed in the Stack repository... But this is a lot of commits (one for each repository)!
        // And this is not crucial because the published database is just an index (the tag matters),
        // so we postpone the stack push to the end of the world.
        // if( !repo.Repo.World.StackRepository.PushChanges( monitor ) ) return null; 

        // We are almost done: finalize the hosted release.
        Throw.DebugAssert( _hostingProvider != null && _releaseId != null );
        if( !await _hostingProvider.FinalizeReleaseAsync( monitor, _hostedRepoPath, _releaseId, cancel ).ConfigureAwait( false ) )
        {
            return null; 
        }
        // Resets the hosting provider and release state.
        _hostingProvider = null;
        _releaseId = null;
        // If the cleanup fails, we still consider this release done.
        _versionTag.CleanupLocalRelease( monitor, repo.Repo, repo.BuildVersion, repo.BuildContentInfo, removeFromNuGetGlobalCache: false );
        return _state.ForwardPrimaryCursor( monitor, 1 );
    }

    async Task<PublishState.Cursor?> OnEndOfWorldAsync( IActivityMonitor monitor, CancellationToken cancel )
    {
        var world = _state.PrimaryCursor.World;
        Throw.DebugAssert( world != null );
        if( !_state.World.StackRepository.PushChanges( monitor ) )
        {
            return null;
        }

        monitor.Info( $"Published '{world.Title}'." );
        return _state.ForwardPrimaryCursor( monitor, 1 );
    }

}
