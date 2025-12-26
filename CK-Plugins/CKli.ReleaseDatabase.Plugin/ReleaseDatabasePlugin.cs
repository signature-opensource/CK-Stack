using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace CKli.ReleaseDatabase.Plugin;

public sealed class ReleaseDatabasePlugin : PrimaryPluginBase
{
    readonly ArtifactHandlerPlugin _artifactHandler;
    readonly ReleaseDB _local;
    readonly ReleaseDB _published;

    public ReleaseDatabasePlugin( PrimaryPluginContext context, ArtifactHandlerPlugin artifactHandler )
        : base( context )
    {
        _artifactHandler = artifactHandler;
        var stackFolder = World.StackRepository.StackWorkingFolder;
        _published = new ReleaseDB( null, stackFolder.Combine( $"{World.Name.FullName}.PublishedRelease.cache" ) );
        _local = new ReleaseDB( _published, stackFolder.Combine( $"$Local/{World.Name.FullName}.LocalRelease.cache" ) );
    }



    /// <summary>
    /// Gets the <see cref="ReleaseInfo"/> for a released version of a repository.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="repo">The released repository.</param>
    /// <param name="version">The released version.</param>
    /// <returns>The information or null on error: the released version doesn't exist.</returns>
    public ReleaseInfo? GetReleaseInfo( IActivityMonitor monitor, Repo repo, SVersion version )
    {
        var key = new RepoKey( repo.CKliRepoId.Value, version );

        var content = _local.Find( monitor, repo, version, out var isLocal );
        if( content != null )
        {
            var externalDependencies = new HashSet<NuGetPackageInstance>();
            var predecessors = new Dictionary<RepoKey, ReleaseInfo.Dependency>();
            CollectDependencies( monitor, content, externalDependencies, predecessors, 0 );
            var producers = predecessors.Values.ToArray();
            producers.Sort( (p1,p2) => p1.SortKey.CompareTo( p2.SortKey ) );
            return new ReleaseInfo( repo, version, content, externalDependencies, ImmutableCollectionsMarshal.AsImmutableArray( producers ) );
        }
        monitor.Error( $"No release exist for '{repo.DisplayPath}/{version}'." );
        return null;
    }

    void CollectDependencies( IActivityMonitor monitor,
                              BuildContentInfo content,
                              HashSet<NuGetPackageInstance> externalDependencies,
                              Dictionary<RepoKey, ReleaseInfo.Dependency> predecessors,
                              int rank )
    {
        foreach( var consumed in content.Consumed )
        {
            if( !_local.FindProducer( monitor, consumed, out RepoKey? producer, out BuildContentInfo? producerContent ) )
            {
                externalDependencies.Add( consumed );
            }
            else
            {
                if( predecessors.TryGetValue( producer, out var exists ) )
                {
                    exists.UpdateRank( rank );
                }
                else
                {
                    var repoId = new RandomId( producer.RepoId );
                    var r = World.FindByCKliRepoId( repoId );
                    if( r == null )
                    {
                        if( externalDependencies.Add( consumed ) )
                        {
                            monitor.Warn( $"""
                                Unknown CKliRepoId '{repoId}' among World's repositories.
                                Package '{consumed}' is considered as an external dependency.
                                """ );
                        }
                    }
                    else
                    {
                        predecessors.Add( producer, new ReleaseInfo.Dependency( r, producer.Version, producerContent, rank ) );
                        CollectDependencies( monitor, producerContent, externalDependencies, predecessors, rank + 1 );
                    }
                }
            }
        }
    }

    public void GetSuccessors( IActivityMonitor monitor, Repo repo, SVersion version )
    {
        var c = _local.Find( monitor, repo, version, out var isLocal );
        if( c != null )
        {
            // Collecting in a dictionary: duplicated RepoKey is removed.
            var successors = new Dictionary<RepoKey, BuildContentInfo>();
            foreach( var id in c.Produced )
            {
                var consumed = new NuGetPackageInstance( id, version );
                _local.CollectReferences( monitor, consumed, successors );
                if( !isLocal ) _published.CollectReferences( monitor, consumed, successors );
            }
            // Among these successors, there are transitive dependencies:
            // it is all the (Repo,Version) that consume a package produced by another successor*!
            // They must be removed...
            foreach( var s in successors )
            {
                foreach( var p in s.Value.Produced )
                {

                }
            }
        }
    }

    /// <summary>
    /// Called with existing version tags. This initialize the Local release database and checks that an already
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
}
