using CK.Core;
using CKli.LocalNuGetFeed.Plugin;
using CSemVer;
using LibGit2Sharp;
using System;
using System.Collections.Generic;

namespace CKli.VersionTag.Plugin;

public sealed class TagCommit : IComparable<TagCommit>, IEquatable<TagCommit>
{
    readonly SVersion _version;
    readonly Commit _commit;
    readonly string _sha;
    readonly string _contentSha;
    Tag _tag;
    string? _message;
    NuGetReleaseInfo? _releaseInfo;

    internal TagCommit( SVersion version, Commit commit, Tag tag )
    {
        _version = version;
        _commit = commit;
        _tag = tag;
        _sha = commit.Sha;
        _contentSha = commit.Tree.Sha;
        Throw.DebugAssert( MustBeFixed == MustBeFixedToAnnotated || MustBeFixedToLightweight );
    }

    public SVersion Version => _version;

    public Commit Commit => _commit;

    public string Sha => _sha;

    public string ContentSha => _contentSha;

    public Tag Tag => _tag;

    public bool MustBeFixed => _tag.IsAnnotated == !_version.IsPrerelease;

    public bool MustBeFixedToLightweight => _tag.IsAnnotated && _version.IsPrerelease;

    public bool MustBeFixedToAnnotated => !_tag.IsAnnotated && !_version.IsPrerelease;

    /// <summary>
    /// Gets the tag's message if this <see cref="Tag"/> is an Annotated tag. Null otherwise.
    /// </summary>
    public string? TagMessage => _message ??= _tag.Annotation?.Message;

    /// <summary>
    /// Gets the <see cref="NuGetReleaseInfo"/> if <see cref="TagMessage"/> is not null and
    /// the message can be parsed back. Null otherwise.
    /// </summary>
    public NuGetReleaseInfo? ReleaseInfo 
    {
        get
        {
            if( _releaseInfo == null )
            {
                var m = TagMessage;
                if( m == null ) return null;
                NuGetReleaseInfo.TryParseMessage( m, out _releaseInfo );
            }
            return _releaseInfo;
        }
    }

    /// <summary>
    /// Reverts the <see cref="SVersion.CompareTo(SVersion?)"/> result: we want the first
    /// <see cref="TagCommit"/> in <see cref="VersionTagInfo.LastStables"/> to be the
    /// latest one, not the oldest one.
    /// </summary>
    /// <param name="other">The other verioned tag commit.</param>
    /// <returns>The standard compare result.</returns>
    public int CompareTo( TagCommit? other ) => -_version.CompareTo( other?._version );

    public bool Equals( TagCommit? other ) => _version.Equals( other?._version );

    public override bool Equals( object? obj ) => Equals( obj as TagCommit );

    public override int GetHashCode() => _version.GetHashCode();

    public override string ToString() => $"Tag '{_version.ParsedText}' references Commit '{_sha}'";

    internal void UpdateRebuildReleaseTag( Tag t, string releaseMessage )
    {
        Throw.DebugAssert( t.IsAnnotated && t.Annotation.Message == releaseMessage );
        _message = releaseMessage;
        _tag = t;
        _releaseInfo = null;
    }
}
