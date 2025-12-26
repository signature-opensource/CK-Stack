using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace CKli.ReleaseDatabase.Plugin;

/// <summary>
/// Captures the release information for the release of a <see cref="Repo"/> in a <see cref="SVersion"/>.
/// </summary>
public sealed class ReleaseInfo
{
    readonly Repo _repo;
    readonly SVersion _version;
    readonly BuildContentInfo _content;
    readonly HashSet<NuGetPackageInstance> _externalDependencies;
    readonly ImmutableArray<Dependency> _producers;

    internal ReleaseInfo( Repo repo,
                          SVersion version,
                          BuildContentInfo buildContent,
                          HashSet<NuGetPackageInstance> externalDependencies,
                          ImmutableArray<Dependency> producers )
    {
        _repo = repo;
        _version = version;
        _content = buildContent;
        _externalDependencies = externalDependencies;
        _producers = producers;
    }

    /// <summary>
    /// Gets the source repository of this release.
    /// </summary>
    public Repo Repo => _repo;

    /// <summary>
    /// Gets the released version.
    /// </summary>
    public SVersion Version => _version;

    /// <summary>
    /// Gets the build content.
    /// </summary>
    public BuildContentInfo Content => _content;

    /// <summary>
    /// Gets the repositories that produced this release ordered by <see cref="Dependency.Rank"/>
    /// and <see cref="Repo.Index"/>.
    /// </summary>
    public ImmutableArray<Dependency> Producers => _producers;

    /// <summary>
    /// Gets the set of external dependencies.
    /// </summary>
    public IReadOnlySet<NuGetPackageInstance> ExternalDependencies => _externalDependencies;

    /// <summary>
    /// Captures a <see cref="ReleaseInfo"/>'s <see cref="Producers"/>.
    /// </summary>
    public sealed class Dependency
    {
        readonly Repo _repo;
        readonly SVersion _version;
        readonly BuildContentInfo _content;
        int _rank;

        internal Dependency( Repo repo, SVersion version, BuildContentInfo content, int rank )
        {
            _repo = repo;
            _version = version;
            _content = content;
            _rank = rank;
        }

        /// <summary>
        /// Gets the source repository of this release.
        /// </summary>
        public Repo Repo => _repo;

        /// <summary>
        /// Gets the released version.
        /// </summary>
        public SVersion Version => _version;

        /// <summary>
        /// Gets the build content.
        /// </summary>
        public BuildContentInfo Content => _content;

        /// <summary>
        /// Gets the rank of this dependency. A 0 rank is a direct predecessor of the <see cref="ReleaseInfo"/>.
        /// </summary>
        public int Rank => _rank;

        internal void UpdateRank( int rank )
        {
            if( rank > _rank ) _rank = rank;
        }

        internal long SortKey => ((long)_rank) << 32 | (long)_repo.Index;
    }
}


