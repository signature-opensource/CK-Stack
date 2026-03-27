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
    readonly BuildContentInfo? _baseContentInfo;
    readonly SVersion _buildVersion;
    readonly BuildContentInfo _buildContentInfo;
    readonly ImmutableArray<(string CommitId, string Author, string Committer, DateTimeOffset Date, string Text)> _commits;
    string _releaseNotes;

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
    /// plus one for the <see cref="Repo"/> itself.
    /// </summary>
    public int PublishedLength => _buildContentInfo.Produced.Length + _buildContentInfo.AssetFileNames.Length + 1;

    /// <summary>
    /// Gets the built version of this repository.
    /// </summary>
    public SVersion BuildVersion => _buildVersion;

    /// <summary>
    /// Gets the release notes. May be empty.
    /// </summary>
    public string ReleaseNotes => _releaseNotes;

    RepoPublishInfo( Repo repo,
                     int index,
                     SVersion baseVersion,
                     BuildContentInfo? baseContentInfo,
                     SVersion buildVersion,
                     BuildContentInfo buildContentInfo,
                     ImmutableArray<(string CommitId, string Author, string Committer, DateTimeOffset Date, string Text)> commits,
                     string releaseNotes )
    {
        _repo = repo;
        _index = index;
        _baseVersion = baseVersion;
        _baseContentInfo = baseContentInfo;
        _buildVersion = buildVersion;
        _buildContentInfo = buildContentInfo;
        _commits = commits;
        _releaseNotes = releaseNotes;
    }

    internal static RepoPublishInfo Create( int index, Roadmap.BuildInfo info )
    {
        Throw.DebugAssert( info.MustBuild && info.BuildResult != null );
        return new RepoPublishInfo( info.Solution.Repo,
                                    index,
                                    info.Solution.VersionInfo.BaseBuild.Version,
                                    info.Solution.VersionInfo.BaseBuildContentInfo,
                                    info.BuildResult.Version,
                                    info.BuildResult.Content,
                                    [.. info.Solution.VersionInfo.CommitsFromBaseBuild.Select( tc => (tc.Sha, tc.Author.Name, tc.Committer.Name, tc.Committer.When, tc.Message) )],
                                    String.Empty );

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
        var baseContentInfo = r.ReadBoolean() ? new BuildContentInfo( r ) : null;
        var buildVersion = ReadVersion( r );
        var buildContentInfo = new BuildContentInfo( r );
        int cc = r.ReadNonNegativeSmallInt32();
        var commits = new (string CommitId, string Author, string Committer, DateTimeOffset Date, string Text)[cc];
        for( int i = 0; i < commits.Length; i++ )
        {
            commits[i].CommitId = r.ReadString();
            commits[i].Author = r.ReadString();
            commits[i].Committer = r.ReadString();
            commits[i].Date = r.ReadDateTimeOffset();
            commits[i].Text = r.ReadString();
        }
        var releaseNotes = r.ReadString();

        return new RepoPublishInfo( repo,
                                    index,
                                    baseVersion,
                                    baseContentInfo,
                                    buildVersion,
                                    buildContentInfo,
                                    ImmutableCollectionsMarshal.AsImmutableArray( commits ),
                                    releaseNotes );

        static SVersion ReadVersion( ICKBinaryReader r ) => SVersion.Parse( r.ReadString() );
    }

    public void Write( ICKBinaryWriter w )
    {
        w.Write( _repo.CKliRepoId.Value );
        w.WriteNonNegativeSmallInt32( _index );
        w.Write( _baseVersion.ToString() );
        if( _baseContentInfo == null )
        {
            w.Write( false );
        }
        else
        {
            w.Write( true );
            _baseContentInfo.Write( w );
        }
        w.Write( _buildVersion.ToString() );
        _buildContentInfo.Write( w );
        w.WriteNonNegativeSmallInt32( _commits.Length );
        foreach( var commit in _commits )
        {
            w.Write( commit.CommitId );
            w.Write( commit.Author );
            w.Write( commit.Committer );
            w.Write( commit.Date );
            w.Write( commit.Text );
        }
        w.Write( _releaseNotes );
    }
}
