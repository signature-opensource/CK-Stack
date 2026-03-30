using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.BranchModel.Plugin;
using CKli.Build.Plugin;
using CKli.Core;
using CSemVer;
using System;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace CKli.Publish.Plugin;

sealed class WorldReleaseInfo
{
    readonly DateTime _buildDate;
    readonly ImmutableArray<RepoPublishInfo> _repos;
    readonly int _publishedLength;
    string _title;

    /// <summary>
    /// Gets the build date of this release.
    /// </summary>
    public DateTime BuildDate => _buildDate;

    /// <summary>
    /// Gets the title.
    /// </summary>
    public string Title => _title;

    /// <summary>
    /// Gets the published repositories information.
    /// <para>
    /// This is never empty.
    /// </para>
    /// </summary>
    public ImmutableArray<RepoPublishInfo> Repos => _repos;

    /// <summary>
    /// Gets the number of "items" to publish: the sum of the <see cref="Repos"/>'s <see cref="RepoPublishInfo.PublishedLength"/>
    /// plus one for this final set.
    /// </summary>
    public int PublishedLength => _publishedLength;

    WorldReleaseInfo( string title, DateTime buildDate, ImmutableArray<RepoPublishInfo> repos, int publishedLength )
    {
        _title = title;
        _buildDate = buildDate;
        _repos = repos;
        _publishedLength = publishedLength;
    }

    /// <summary>
    /// Creates from a <see cref="Roadmap"/>.
    /// </summary>
    /// <param name="buildDate">The build date to consider.</param>
    /// <param name="roadmap">The built roadmap.</param>
    /// <returns>A new world release.</returns>
    internal static WorldReleaseInfo Create( DateTime buildDate, Roadmap roadmap )
    {
        var repoInfos = new RepoPublishInfo[roadmap.SolutionBuildCount];
        int publishedLength = 0;
        int i = 0;
        foreach( var s in roadmap.OrderedSolutions )
        {
            if( s.MustBuild )
            {
                Throw.DebugAssert( s.BuildInfo.BuildResult != null );
                var r = new RepoPublishInfo( i, s.VersionInfo.BaseBuild.Version, s.BuildInfo.BuildResult );
                repoInfos[i++] = r;
                publishedLength += r.PublishedLength;
            }
        }
        Throw.DebugAssert( i == repoInfos.Length );
        return new WorldReleaseInfo( buildDate.ToString( "yyyy.M.d+HH.mm" ), buildDate, ImmutableCollectionsMarshal.AsImmutableArray( repoInfos ), publishedLength );
    }

    /// <summary>
    /// Creates from a <see cref="FixWorkflow"/>.
    /// </summary>
    /// <param name="buildDate">The build date to consider.</param>
    /// <param name="fixWorkflow">The fix built.</param>
    /// <param name="results">The build results.</param>
    /// <returns>A new world release.</returns>
    internal static WorldReleaseInfo Create( DateTime buildDate, FixWorkflow fixWorkflow, ImmutableArray<BuildResult> results )
    {
        var repoInfos = new RepoPublishInfo[results.Length];
        int publishedLength = 0;
        for( int i = 0; i < results.Length; i++ )
        {
            var b = results[i];
            var r = new RepoPublishInfo( i, SVersion.Create( b.Version.Major, b.Version.Minor, b.Version.Patch - 1), b );
            repoInfos[i++] = r;
            publishedLength += r.PublishedLength;
        }
        return new WorldReleaseInfo( fixWorkflow.ToString(), buildDate, ImmutableCollectionsMarshal.AsImmutableArray( repoInfos ), publishedLength );
    }


    internal static WorldReleaseInfo? Read( IActivityMonitor monitor, World world, ICKBinaryReader r, int version )
    {
        var title = r.ReadString();
        var buildDate = r.ReadDateTime();
        int c = r.ReadNonNegativeSmallInt32();
        var repos = new RepoPublishInfo[c];
        for( int i = 0; i < repos.Length; i++ )
        {
            var repo = RepoPublishInfo.Read( monitor, world, r, version );
            if( repo == null ) return null;
            repos[i] = repo;
        }
        int publishedLength = r.ReadNonNegativeSmallInt32();
        return new WorldReleaseInfo( title, buildDate, ImmutableCollectionsMarshal.AsImmutableArray( repos ), publishedLength );
    }

    public void Write( ICKBinaryWriter w )
    {
        w.Write( _buildDate );
        w.Write( _title );
        w.WriteNonNegativeSmallInt32( _repos.Length );
        foreach( var s in _repos ) s.Write( w );
        w.WriteNonNegativeSmallInt32( _publishedLength );
    }

}
