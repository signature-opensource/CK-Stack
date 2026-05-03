using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CSemVer;
using LibGit2Sharp;
using System;
using System.Diagnostics.CodeAnalysis;

namespace CKli.VersionTag.Plugin;

/// <summary>
/// Captures a <see cref="Commit"/> and its version <see cref="Tag"/>.
/// <para>
/// This is comparable but reverts the <see cref="SVersion.CompareTo(SVersion?)"/> order.
/// </para>
/// </summary>
public sealed class TagCommit : IComparable<TagCommit>, IEquatable<TagCommit>
{
    readonly SVersion _version;
    readonly Commit _commit;
    readonly string _sha;
    readonly bool _isFakeVersion;
    Tag _tag;
    string? _message;
    BuildContentInfo? _buildContentInfo;
    DeprecatedTagInfo? _deprecatedInfo;

    internal TagCommit( SVersion version, Commit commit, Tag tag, bool isFakeVersion, DeprecatedTagInfo? deprecatedInfo )
    {
        _version = version;
        _commit = commit;
        _tag = tag;
        _isFakeVersion = isFakeVersion;
        _deprecatedInfo = deprecatedInfo;
        _sha = commit.Sha;
    }

    /// <summary>
    /// Gets the version.
    /// </summary>
    public SVersion Version => _version;

    /// <summary>
    /// Gets the commit.
    /// </summary>
    public Commit Commit => _commit;

    /// <summary>
    /// Gets this commit sha.
    /// </summary>
    public string Sha => _sha;

    /// <summary>
    /// Gets whether this version tag is "+fake" one: it is here only
    /// to enables gaps between versions that would otherwise be rejected.
    /// </summary>
    public bool IsFakeVersion => _isFakeVersion;

    /// <summary>
    /// Gets whether this version tag is "+deprecated" one. <see cref="DeprecatedInfo"/> is necessarily not null.
    /// </summary>
    [MemberNotNullWhen( true, nameof( DeprecatedInfo ) )]
    public bool IsDeprecatedVersion => _deprecatedInfo != null;

    /// <summary>
    /// Gets the deprecated info parsed from <see cref="TagMessage"/> if <see cref="IsDeprecatedVersion"/> is true.
    /// </summary>
    public DeprecatedTagInfo? DeprecatedInfo => _deprecatedInfo;

    /// <summary>
    /// Gets whether this version tag is not a "+fake" nor a "+deprecated" one.
    /// </summary>
    public bool IsRegularVersion => !IsDeprecatedVersion && !_isFakeVersion;

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
            if( _buildContentInfo == null )
            {
                if( _deprecatedInfo != null )
                {
                    _buildContentInfo = _deprecatedInfo.ContentInfo;
                }
                else if( !_isFakeVersion )
                {
                    var msg = TagMessage;
                    if( msg == null ) return null;
                    _ = BuildContentInfo.TryParse( msg, out _buildContentInfo );
                }
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

    public override string ToString() => $"Tag '{_version.ParsedText}' references Commit '{_sha}'";

    internal void UpdateVersionTag( Tag t )
    {
        Throw.DebugAssert( t.IsAnnotated );
        _message = t.Annotation.Message;
        _tag = t;
        _buildContentInfo = null;
    }
}
