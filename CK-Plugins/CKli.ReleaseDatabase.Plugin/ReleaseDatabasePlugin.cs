using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CSemVer;
using System.Collections.Generic;

namespace CKli.ReleaseDatabase.Plugin;

public sealed class ReleaseDatabasePlugin : PrimaryPluginBase
{
    internal readonly ArtifactHandlerPlugin _artifactHandler;
    readonly ReleaseDB _local;
    readonly ReleaseDB _published;
    readonly Dictionary<RepoKey, RepoReleaseInfo> _releaseInfo;

    public ReleaseDatabasePlugin( PrimaryPluginContext context, ArtifactHandlerPlugin artifactHandler )
        : base( context )
    {
        _artifactHandler = artifactHandler;
        var stackFolder = World.StackRepository.StackWorkingFolder;
        _published = new ReleaseDB( null, stackFolder.Combine( $"{World.Name.FullName}.PublishedRelease.cache" ) );
        _local = new ReleaseDB( _published, stackFolder.Combine( $"$Local/{World.Name.FullName}.LocalRelease.cache" ) );
        _releaseInfo = new Dictionary<RepoKey, RepoReleaseInfo>();
    }

    /// <summary>
    /// Gets the <see cref="RepoReleaseInfo"/> for a released version of a repository.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="repo">The released repository.</param>
    /// <param name="version">The released version.</param>
    /// <returns>The information or null on error: the released version doesn't exist.</returns>
    public RepoReleaseInfo? GetReleaseInfo( IActivityMonitor monitor, Repo repo, SVersion version )
    {
        var key = new RepoKey( repo.CKliRepoId, version );
        var content = _local.Find( monitor, key, out var isLocal );
        if( content == null )
        {
            monitor.Error( $"No release exist for '{repo.DisplayPath}/{key.Version}'." );
            return null;
        }
        return GetReleasedInfo( monitor, repo, key, content, isLocal );
    }

    RepoReleaseInfo GetReleasedInfo( IActivityMonitor monitor, Repo repo, RepoKey key, BuildContentInfo content, bool isLocal )
    {
        if( _releaseInfo.TryGetValue( key, out var r ) )
        {
            return r;
        }
        var directProducers = new List<RepoReleaseInfo>();
        var allProducers = new HashSet<RepoReleaseInfo>();
        foreach( var consumed in content.Consumed )
        {
            // If we can't find a producer for a consumed package, it is an external package.
            // If we can't find the Repo in the World, it is a "new" external package.
            // We don't track them.
            if( _local.FindProducer( monitor,
                                     consumed,
                                     out RepoKey? producerKey,
                                     out BuildContentInfo? producerContent,
                                     out var isLocalProducer ) )
            {
                var producer = World.FindByCKliRepoId( monitor, producerKey.RepoId );
                if( producer != null )
                {
                    var p = GetReleasedInfo( monitor, producer, producerKey, producerContent, isLocalProducer );
                    directProducers.Add( p );
                    allProducers.UnionWith( p.AllProducers );
                }
            }
        }
        for( int i = 0; i < directProducers.Count; i++ )
        {
            var producer = directProducers[i];
            if( allProducers.Contains( producer ) )
            {
                directProducers.RemoveAt( i-- );
            }
        }
        return new RepoReleaseInfo( this, repo, key, content, directProducers, allProducers, isLocal );
    }

    internal IReadOnlyList<RepoReleaseInfo> GetDirectConsumers( IActivityMonitor monitor, RepoReleaseInfo info )
    {
        // Collecting in a dictionary: duplicated RepoKey is removed,
        // this handles the Package -> Solution projection.
        var consumers = new Dictionary<RepoKey, (BuildContentInfo Content, bool IsLocal)>();
        foreach( var id in info.Content.Produced )
        {
            var consumed = new NuGetPackageInstance( id, info.Version );
            _local.CollectConsumers( monitor, consumed, consumers );
            if( !info.IsLocal ) _published.CollectConsumers( monitor, consumed, consumers );
        }
        // Among these consumers, there are transitive dependencies:
        // it is all the (Repo,Version) that consume a package produced by another consumer
        // but recursively:
        // - CK-Core -> CK.Core
        // - CK-ActivityMonitor -> CK.ActivityMonitor
        //      <- CK.Core
        // - CK-Monitoring -> CK.Monitoring
        //      <- CK.ActivityMonitor
        // - CK-XXX
        //      <- CK.Monitoring
        //      <- CK.Core
        // Here, the single direct consumer of CK-Core is CK.ActivityMonitor.
        // To evict CK-XXX as a direct consumer, we must first discover CK-Monitoring (that is not itself
        // a direct consumer of CK-Core).
        // Instead of implementing here the mirror of the RepoReleaseInfo, we use them (the "past") to filter
        // the direct consumers (the "future").
        //
        var consumerInfos = new List<RepoReleaseInfo>();
        foreach( var (consumerKey,(consumerContent, isLocal)) in consumers )
        {
            var consumer = World.FindByCKliRepoId( monitor, consumerKey.RepoId );
            // If we can't find the Repo in the World, this is weird but it may happen.
            // We warn and ignore it.
            if( consumer == null )
            {
                monitor.Warn( $"""
                    Unable to find RepoId='{consumerKey.RepoId}' in the current World.
                    This Repo has been removed since it as been built in version '{consumerKey.Version}'. Its content was:
                    {consumerContent}
                    It is ignored.
                    """ );
            }
            else
            {
                consumerInfos.Add( GetReleasedInfo( monitor, consumer, consumerKey, consumerContent, isLocal ) );
            }
        }
        for( int i = 0; i < consumerInfos.Count; i++ )
        {
            var candidate = consumerInfos[i];
            for( int j = 0; j < consumerInfos.Count; j++ )
            {
                if( i == j ) continue;
                var other = consumerInfos[j];
                if( other == candidate || other.AllProducers.Contains( candidate ) )
                {
                    consumerInfos.RemoveAt( i-- );
                    break;
                }
            }
        }
        return consumerInfos;
    }

    /// <summary>
    /// Called with existing version tags. This initializes the Local release database and checks that an already
    /// Published version has the same content as the one provided.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="repo">The repository.</param>
    /// <param name="versions">The tag released versions.</param>
    /// <returns>A manual issue with conflicting already released content info or null.</returns>
    public World.Issue? OnExistingVersionTags( IActivityMonitor monitor, Repo repo, IEnumerable<(SVersion, BuildContentInfo)> versions )
    {
        return _local.OnExistingVersionTags( monitor, World.ScreenType, repo, versions );
    }

    /// <summary>
    /// Called when we know for sure that a versioned release has been published.
    /// <para>
    /// This is idempotent: if the version is in the Local database, it is moved to the Published one
    /// and if it already published, nothing is done. This fails if the release cannot be found in any of
    /// the 2 databases.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="repo">The released repository.</param>
    /// <param name="version">The published version.</param>
    /// <returns>True on success, false on error.</returns>
    public bool PublishRelease( IActivityMonitor monitor, Repo repo, SVersion version )
    {
        return _local.PublishRelease( monitor, repo, version );
    }

    /// <summary>
    /// Called on each build (by the Build plugin CoreBuild method).
    /// <para>
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="repo">The repository.</param>
    /// <param name="version">The built version.</param>
    /// <param name="rebuild">Whether it is a rebuild of an existing version or a fresh build.</param>
    /// <param name="content">The build content.</param>
    /// <returns>True on success, false on error.</returns>
    public bool OnLocalBuild( IActivityMonitor monitor, Repo repo, SVersion version, bool rebuild, BuildContentInfo content )
    {
        return _local.OnLocalBuild( monitor, repo, version, rebuild, content );
    }

    /// <summary>
    /// This should only be called by the VersionTag plugin.
    /// <para>
    /// This is idempotent.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="repo">The source repository.</param>
    /// <param name="version">The release to destroy.</param>
    /// <returns>The content info it has been removed. Null if it didn't exist.</returns>
    public BuildContentInfo? DestroyLocalRelease( IActivityMonitor monitor, Repo repo, SVersion version )
    {
        return _local.DestroyLocalRelease( monitor, repo, version );
    }

    /// <summary>
    /// Destroys the local and published databases. None of them must already be loaded otherwise a <see cref="System.InvalidOperationException"/>
    /// is throw. The databases must be rebuilt.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    public void DestroyDatabases( IActivityMonitor monitor )
    {
        using( monitor.OpenTrace( "Deleting release databases." ) )
        {
            _published.Destroy( monitor, createBackup: false );
            _local.Destroy( monitor, createBackup: false );
        }
    }
}
