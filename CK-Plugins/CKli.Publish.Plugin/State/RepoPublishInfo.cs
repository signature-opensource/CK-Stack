using CK.Core;
using CKli.Core;
using CSemVer;
using CKli.ArtifactHandler.Plugin;

namespace CKli.Publish.Plugin;

sealed class RepoPublishInfo
{
    readonly Repo _repo;
    readonly string _branchName;
    readonly int _index;
    readonly SVersion _baseVersion;
    readonly SVersion _buildVersion;
    readonly BuildContentInfo _buildContentInfo;

    /// <summary>
    /// Gets the Repo.
    /// </summary>
    public Repo Repo => _repo;

    /// <summary>
    /// Gets the branch name. This is a "dev/XXX" branch when <see cref="WorldReleaseInfo.IsCIBuild"/> is true.
    /// </summary>
    public string BranchName => _branchName;

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
                     string branchName,
                     int index,
                     SVersion baseVersion,
                     SVersion buildVersion,
                     BuildContentInfo buildContentInfo )
    {
        _repo = repo;
        _branchName = branchName;
        _index = index;
        _baseVersion = baseVersion;
        _buildVersion = buildVersion;
        _buildContentInfo = buildContentInfo;
    }

    internal RepoPublishInfo( int index, string branchName, SVersion baseVersion, BuildResult result )
        : this( result.Repo, branchName, index, baseVersion, result.Version, result.Content )
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
        var branchName = r.ReadString();
        var index = r.ReadNonNegativeSmallInt32();
        var baseVersion = ReadVersion( r );
        var buildVersion = ReadVersion( r );
        var buildContentInfo = new BuildContentInfo( r );

        return new RepoPublishInfo( repo,
                                    branchName,
                                    index,
                                    baseVersion,
                                    buildVersion,
                                    buildContentInfo );

        static SVersion ReadVersion( ICKBinaryReader r ) => SVersion.Parse( r.ReadString() );
    }

    public void Write( ICKBinaryWriter w )
    {
        w.Write( _repo.CKliRepoId.Value );
        w.Write( _branchName );
        w.WriteNonNegativeSmallInt32( _index );
        w.Write( _baseVersion.ToString() );
        w.Write( _buildVersion.ToString() );
        _buildContentInfo.Write( w );
    }
}
