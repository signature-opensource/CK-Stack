using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CSemVer;
using LibGit2Sharp;
using System;

namespace CKli.VersionTag.Plugin;

public sealed class TagCommit : IComparable<TagCommit>, IEquatable<TagCommit>
{
    readonly SVersion _version;
    readonly Commit _commit;
    readonly string _sha;
    readonly string _contentSha;
    readonly bool _isFakeVersion;
    readonly bool _isDeprecatedVersion;
    Tag _tag;
    string? _message;
    BuildContentInfo? _buildContentInfo;

    internal TagCommit( SVersion version, Commit commit, Tag tag )
    {
        _version = version;
        _commit = commit;
        _tag = tag;
        _sha = commit.Sha;
        _contentSha = commit.Tree.Sha;
        _isFakeVersion = _version.BuildMetaData.Equals( "Fake", StringComparison.OrdinalIgnoreCase );
        _isDeprecatedVersion = !_isFakeVersion && _version.BuildMetaData.Contains( "Deprecated", StringComparison.OrdinalIgnoreCase );
    }

    public SVersion Version => _version;

    public Commit Commit => _commit;

    /// <summary>
    /// Gets this commit sha.
    /// </summary>
    public string Sha => _sha;

    /// <summary>
    /// Gets the content sha of this commit.
    /// </summary>
    public string ContentSha => _contentSha;

    /// <summary>
    /// Gets whether this version tag is "+Fake" one: it is here only
    /// to enables gaps between versions that would otherwise be rejected.
    /// </summary>
    public bool IsFakeVersion => _isFakeVersion;

    /// <summary>
    /// Gets whether this version tag is "+Deprecated" one: an actual 
    /// version exists but for any reason it must not be used anymore.
    /// </summary>
    public bool IsDeprecatedVersion => _isDeprecatedVersion;

    /// <summary>
    /// Gets whether this version tag is nor a +Fake nor a +Deprecated one.
    /// </summary>
    public bool IsRegularVersion => !_isDeprecatedVersion && !_isFakeVersion;

    /// <summary>
    /// The tag object.
    /// </summary>
    public Tag Tag => _tag;

    /// <summary>
    /// Gets the tag's message if this <see cref="Tag"/> is an Annotated tag. Null otherwise.
    /// </summary>
    public string? TagMessage => _message ??= _tag.Annotation?.Message;

    /// <summary>
    /// Gets the build content info if <see cref="TagMessage"/> is not null, <see cref="IsFakeVersion"/>
    /// is false and the message can be parsed back. Null otherwise.
    /// </summary>
    public BuildContentInfo? BuildContentInfo 
    {
        get
        {
            if( _buildContentInfo == null && !_isFakeVersion )
            {
                var m = TagMessage;
                if( m == null ) return null;
                BuildContentInfo.TryParse( m, out _buildContentInfo );
            }
            return _buildContentInfo;
        }
    }

    /// <summary>
    /// Reverts the <see cref="SVersion.CompareTo(SVersion?)"/> result: we want the first
    /// <see cref="TagCommit"/> in <see cref="VersionTagInfo.LastStables"/> to be the
    /// latest one, not the oldest one.
    /// </summary>
    /// <param name="other">The other versioned tag commit.</param>
    /// <returns>The standard compare result.</returns>
    public int CompareTo( TagCommit? other ) => -_version.CompareTo( other?._version );

    public bool Equals( TagCommit? other ) => _version.Equals( other?._version );

    public override bool Equals( object? obj ) => Equals( obj as TagCommit );

    public override int GetHashCode() => _version.GetHashCode();

    public override string ToString() => _isFakeVersion
                                            ? $"Fake '{_version.ParsedText}'"
                                            : $"Tag '{_version.ParsedText}' references Commit '{_sha}'";

    internal void UpdateVersionTag( Tag t )
    {
        Throw.DebugAssert( t.IsAnnotated );
        _message = t.Annotation.Message;
        _tag = t;
        _buildContentInfo = null;
    }
}
