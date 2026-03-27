using CK.Core;
using CKli.ArtifactHandler.Plugin;
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
    string? _releaseVersion;

    /// <summary>
    /// Gets the build date of this release.
    /// </summary>
    public DateTime BuildDate => _buildDate;

    /// <summary>
    /// Gets the release version string based on the <see cref="BuildDate"/>.
    /// The format is <see cref="SVersion"/> compatible: there is one release version per day, the hour and the minute appear in the <see cref="SVersion.BuildMetaData"/>.
    /// </summary>
    public string ReleaseVersion => _releaseVersion ??= _buildDate.ToString( "yyyy.M.d+HH.mm" );

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

    WorldReleaseInfo( DateTime buildDate, ImmutableArray<RepoPublishInfo> repos, int publishedLength )
    {
        _buildDate = buildDate;
        _repos = repos;
        _publishedLength = publishedLength;
    }

    internal static WorldReleaseInfo Create( DateTime buildDate, Roadmap roadmap )
    {
        var repoInfos = new RepoPublishInfo[roadmap.SolutionBuildCount];
        int publishedLength = 0;
        int i = 0;
        foreach( var s in roadmap.OrderedSolutions )
        {
            if( s.MustBuild )
            {
                var r = RepoPublishInfo.Create( i, s.BuildInfo );
                repoInfos[i++] = r;
                publishedLength += r.PublishedLength;
            }
        }
        Throw.DebugAssert( i == repoInfos.Length );
        return new WorldReleaseInfo( buildDate, ImmutableCollectionsMarshal.AsImmutableArray( repoInfos ), publishedLength );
    }

    internal static WorldReleaseInfo? Read( IActivityMonitor monitor, World world, ICKBinaryReader r, int version )
    {
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
        return new WorldReleaseInfo( buildDate, ImmutableCollectionsMarshal.AsImmutableArray( repos ), publishedLength );
    }

    public void Write( ICKBinaryWriter w )
    {
        w.Write( _buildDate );
        w.WriteNonNegativeSmallInt32( _repos.Length );
        foreach( var s in _repos ) s.Write( w );
        w.WriteNonNegativeSmallInt32( _publishedLength );
    }
}
