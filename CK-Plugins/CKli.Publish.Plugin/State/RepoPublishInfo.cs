using CK.Core;
using CKli.Core;
using System;
using System.Linq;
using CKli.Build.Plugin;
using CSemVer;
using CKli.ArtifactHandler.Plugin;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace CKli.Publish.Plugin;

sealed class RepoPublishInfo
{
    readonly Repo _repo;
    readonly int _index;
    readonly SVersion _baseVersion;
    readonly SVersion _buildVersion;
    readonly BuildContentInfo _buildContentInfo;

    /// <summary>
    /// Gets the Repo.
    /// </summary>
    public Repo Repo => _repo;

    /// <summary>
    /// Gets the index of this published repository in its <see cref="WorldReleaseInfo.Repos"/>.
    /// </summary>
    public int Index => _index;

    /// <summary>
    /// Gets the content that must be published.
    /// </summary>
    public BuildContentInfo BuildContentInfo => _buildContentInfo;

    /// <summary>
    /// Gets the number of "items" to publish: the <see cref="BuildContentInfo.Produced"/> packages plus the <see cref="BuildContentInfo.AssetFileNames"/> files
    /// plus two for the start and end of the <see cref="Repo"/> itself.
    /// </summary>
    public int PublishedLength => 1 + _buildContentInfo.Produced.Length + _buildContentInfo.AssetFileNames.Length + 1;

    /// <summary>
    /// Gets the built version of this repository.
    /// </summary>
    public SVersion BuildVersion => _buildVersion;

    RepoPublishInfo( Repo repo,
                     int index,
                     SVersion baseVersion,
                     SVersion buildVersion,
                     BuildContentInfo buildContentInfo )
    {
        _repo = repo;
        _index = index;
        _baseVersion = baseVersion;
        _buildVersion = buildVersion;
        _buildContentInfo = buildContentInfo;
    }

    internal RepoPublishInfo( int index, SVersion baseVersion, BuildResult result )
        : this( result.Repo, index, baseVersion, result.Version, result.Content )
    {
    }

    public static RepoPublishInfo? Read( IActivityMonitor monitor, World world, ICKBinaryReader r, int version )
    {
        var repoId = new RandomId( r.ReadUInt64() );
        var repo = world.FindByCKliRepoId( monitor, repoId );
        if( repo == null )
        {
            monitor.Error( $"Unable to restore Repo from ckli-repo identifier: '{repoId}'." );
            return null;
        }
        var index = r.ReadNonNegativeSmallInt32();
        var baseVersion = ReadVersion( r );
        var buildVersion = ReadVersion( r );
        var buildContentInfo = new BuildContentInfo( r );

        return new RepoPublishInfo( repo,
                                    index,
                                    baseVersion,
                                    buildVersion,
                                    buildContentInfo );

        static SVersion ReadVersion( ICKBinaryReader r ) => SVersion.Parse( r.ReadString() );
    }

    public void Write( ICKBinaryWriter w )
    {
        w.Write( _repo.CKliRepoId.Value );
        w.WriteNonNegativeSmallInt32( _index );
        w.Write( _baseVersion.ToString() );
        w.Write( _buildVersion.ToString() );
        _buildContentInfo.Write( w );
    }
}
