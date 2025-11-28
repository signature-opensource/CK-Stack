using CK.Core;
using CKli.Core;
using System;

namespace CKli.Build.Plugin;

/// <summary>
/// Abstract factory of cached <see cref="RepoBuilder"/> per <see cref="Repo"/>.
/// Currently always associates a basic <see cref="RepoBuilder"/>.
/// </summary>
public sealed class RepositoryBuilderPlugin : PrimaryRepoPlugin<RepoBuilder>
{
    // This cache is common to all Repo in all World: this caches the Content Sha of
    // successfully passed tests. All RepoBuilder use it.
    // This is in the $Local (not in the git repository) to allow tests to run on each machine.
    // There is currently no housekeeping.
    LocalStringCache? _shaTestRunCache;

    public RepositoryBuilderPlugin( PrimaryPluginContext primaryContext )
        : base( primaryContext )
    {
    }

    protected override RepoBuilder Create( IActivityMonitor monitor, Repo repo )
    {
        _shaTestRunCache ??= new LocalStringCache( repo.World.StackRepository, "TestRun.Sha" );
        return new RepoBuilder( repo, _shaTestRunCache );
    }
}
