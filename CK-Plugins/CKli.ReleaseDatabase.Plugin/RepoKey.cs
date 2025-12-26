using CKli.Core;
using CSemVer;
using System;

namespace CKli.ReleaseDatabase.Plugin;

sealed class RepoKey : IEquatable<RepoKey>
{
    readonly ulong _repoId;
    readonly SVersion _version;
    readonly int _hashCode;

    public RepoKey( Repo repo, SVersion version )
        : this( repo.CKliRepoId.Value, version )
    {
    }

    public RepoKey( ulong repoId, SVersion version )
    {
        _repoId = repoId;
        _version = version;
        _hashCode = HashCode.Combine( repoId, version.GetHashCode() );
    }

    public ulong RepoId => _repoId;

    public SVersion Version => _version;

    public bool Equals( RepoKey? other ) => other != null && _repoId == other._repoId && _version == other._version;

    public override bool Equals( object? obj ) => Equals( obj as RepoKey );

    public override int GetHashCode() => _hashCode;
}
