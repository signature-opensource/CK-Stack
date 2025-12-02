using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;

namespace CKli.Build.Plugin;

/// <summary>
/// Abstract factory of cached <see cref="RepoBuilder"/> per <see cref="Repo"/>.
/// Currently always associates a basic <see cref="RepoBuilder"/>.
/// </summary>
public sealed class RepositoryBuilderPlugin : PrimaryRepoPlugin<RepoBuilder>
{
    readonly ArtifactHandlerPlugin _artifactHandler;

    // This cache is common to all Repo in all World: this caches the Content Sha of
    // successfully passed tests. All RepoBuilder use it.
    // This is in the $Local (not in the git repository) to allow tests to run on each machine.
    // There is currently no housekeeping.
    LocalStringCache? _shaTestRunCache;

    public RepositoryBuilderPlugin( PrimaryPluginContext primaryContext, ArtifactHandlerPlugin artifactHandler )
        : base( primaryContext )
    {
        _artifactHandler = artifactHandler;
    }

    protected override RepoBuilder Create( IActivityMonitor monitor, Repo repo )
    {
        _shaTestRunCache ??= new LocalStringCache( repo.World.StackRepository, "TestRun.Sha" );
        return new RepoBuilder( repo, _shaTestRunCache, _artifactHandler.Get( monitor, repo ) );
    }
}
