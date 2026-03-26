using CK.Core;
using CKli.Core;
using System;
using CKli.Build.Plugin;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace CKli.Publish.Plugin;

sealed class WorldPublishInfo
{
    readonly DateTime _buildDate;
    readonly ImmutableArray<RepoPublishInfo> _repos;

    /// <summary>
    /// Gets the build date of this release.
    /// </summary>
    public DateTime BuildDate => _buildDate;

    /// <summary>
    /// Gets the published repositories information.
    /// <para>
    /// This is never empty.
    /// </para>
    /// </summary>
    public ImmutableArray<RepoPublishInfo> Repos => _repos;

    WorldPublishInfo( DateTime buildDate, ImmutableArray<RepoPublishInfo> repos )
    {
        _buildDate = buildDate;
        _repos = repos;
    }

    internal static WorldPublishInfo Create( DateTime buildDate, Roadmap roadmap )
    {
        var repoInfos = new RepoPublishInfo[roadmap.SolutionBuildCount];
        int i = 0;
        foreach( var s in roadmap.OrderedSolutions )
        {
            if( s.MustBuild )
            {
                repoInfos[i++] = RepoPublishInfo.Create( i, s.BuildInfo );
            }
        }
        Throw.DebugAssert( i == repoInfos.Length );
        return new WorldPublishInfo( buildDate, ImmutableCollectionsMarshal.AsImmutableArray( repoInfos ) );
    }

    internal static WorldPublishInfo? Read( IActivityMonitor monitor, World world, ICKBinaryReader r, int version )
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
        return new WorldPublishInfo( buildDate, ImmutableCollectionsMarshal.AsImmutableArray( repos ) );
    }

    public void Write( ICKBinaryWriter w )
    {
        w.Write( _buildDate );
        w.WriteNonNegativeSmallInt32( _repos.Length );
        foreach( var s in _repos ) s.Write( w );
    }
}
