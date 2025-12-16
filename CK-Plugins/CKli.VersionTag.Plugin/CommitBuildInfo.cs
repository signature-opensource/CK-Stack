using CK.Core;
using CKli.Core;
using CSemVer;
using LibGit2Sharp;
using System;

namespace CKli.VersionTag.Plugin;

/// <summary>
/// Captures a commit and everything required to build it in a given version.
/// </summary>
public sealed class CommitBuildInfo
{
    readonly VersionTagInfo _tagInfo;
    readonly SVersion _version;
    readonly Commit _buildCommit;
    readonly string _toString;
    readonly bool _rebuilding;
    string? _informationalVersion;

    internal CommitBuildInfo( VersionTagInfo tagInfo, SVersion version, Commit buildCommit, bool rebuilding )
    {
        _tagInfo = tagInfo;
        _version = version;
        _buildCommit = buildCommit;
        _rebuilding = rebuilding;
        _toString = $"{tagInfo.Repo.DisplayPath}/{version}";
    }

    /// <summary>
    /// Gets the concerned Repo.
    /// </summary>
    public Repo Repo => _tagInfo.Repo;

    /// <summary>
    /// Gets the version to build.
    /// </summary>
    public SVersion Version => _version;

    /// <summary>
    /// Gets the build commit.
    /// </summary>
    public Commit BuildCommit => _buildCommit;

    /// <summary>
    /// Gets whether we are rebuilding an existing version.
    /// </summary>
    public bool Rebuilding => _rebuilding;

    /// <summary>
    /// Gets the informational version (see <see cref="CSemVer.InformationalVersion"/>).
    /// </summary>
    public string InformationalVersion
    {
        get
        {
            return _informationalVersion ??= CSemVer.InformationalVersion.BuildInformationalVersion( _version,
                                                                                                     _buildCommit.Sha,
                                                                                                     _buildCommit.Committer.When.UtcDateTime );
        }
    }

    /// <summary>
    /// Gets whether the build must use "Release" configuration: the version to build is a
    /// stable or a release candidate.
    /// </summary>
    public bool ReleaseConfiguration => !_version.IsPrerelease || _version.AsCSVersion?.PackageQuality == PackageQuality.ReleaseCandidate;

    /// <summary>
    /// Adds or update the <see cref="TagCommit"/> on the <see cref="BuildCommit"/> for <see cref="Version"/>
    /// with the provided <paramref name="releaseMessage"/>.
    /// <para>
    /// This is the last operation of a build. If this fails, this is a problem.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="context">The context.</param>
    /// <param name="releaseMessage">The non empty release message.</param>
    /// <returns>True on success, false on error.</returns>
    public bool ApplyReleaseBuildTag( IActivityMonitor monitor,
                                      CKliEnv context,
                                      string releaseMessage )
    {
        Throw.CheckArgument( !string.IsNullOrWhiteSpace( releaseMessage ) );
        try
        {
            var t = _tagInfo.Repo.GitRepository.Repository.Tags.Add( $"v{_version}",
                                                                     _buildCommit,
                                                                     context.Committer,
                                                                     releaseMessage,
                                                                     allowOverwrite: true );
            if( _tagInfo.TagCommits.TryGetValue( _version, out var exists ) )
            {
                Throw.DebugAssert( "When rebuilding an existing version, the build commit must be the same.", _buildCommit.Sha == exists.Sha );
                exists.UpdateVersionTag( t );
            }
            else
            {
                _tagInfo.AddTag( _version, _buildCommit, t );
            }
            return true;
        }
        catch( Exception ex )
        {
            // This should be a "World.Problem"
            // Problems are future new beasts that are serializable proto/persistent-issues with a
            // "bool StillApply( ... out World.Issue issue )". 
            monitor.Error( $"""
                Unexpecting error while applying 'v{_version}' on commit '{_buildCommit.Sha}' with release message:
                {releaseMessage}
                """, ex );
            return false;
        }
    }

    /// <summary>
    /// Gets "Repo/version".
    /// </summary>
    /// <returns>The "Repo/version" that identifies this build info.</returns>
    public override string ToString() => _toString;
}

