using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CSemVer;
using Microsoft.VisualBasic;
using System.Collections.Generic;

namespace CKli.ReleaseDatabase.Plugin;

/// <summary>
/// Captures the information for the release of a <see cref="Repo"/> in a <see cref="Version"/>.
/// </summary>
public sealed class RepoReleaseInfo
{
    readonly ReleaseDatabasePlugin _releaseDatabase;
    readonly Repo _repo;
    readonly RepoKey _repoKey;
    readonly BuildContentInfo _content;
    readonly List<RepoReleaseInfo> _directProducers;
    readonly HashSet<RepoReleaseInfo> _allProducers;
    readonly bool _isLocal;
    IReadOnlyList<RepoReleaseInfo>? _directConsumers;

    internal RepoReleaseInfo( ReleaseDatabasePlugin releaseDatabase,
                              Repo repo,
                              RepoKey repoKey,
                              BuildContentInfo buildContent,
                              List<RepoReleaseInfo> directProducers,
                              HashSet<RepoReleaseInfo> allProducers,
                              bool isLocal )
    {
        _releaseDatabase = releaseDatabase;
        _repo = repo;
        _repoKey = repoKey;
        _content = buildContent;
        _directProducers = directProducers;
        _allProducers = allProducers;
        _isLocal = isLocal;
    }

    /// <summary>
    /// Gets the released repository.
    /// </summary>
    public Repo Repo => _repo;

    /// <summary>
    /// Gets the released version.
    /// </summary>
    public SVersion Version => _repoKey.Version;

    /// <summary>
    /// Gets the Repo's release content.
    /// </summary>
    public BuildContentInfo Content => _content;

    /// <summary>
    /// Gets the direct producers of this release.
    /// </summary>
    public IReadOnlyList<RepoReleaseInfo> DirectProducers => _directProducers;

    /// <summary>
    /// Gets the closure of all producers of this release.
    /// </summary>
    public IReadOnlySet<RepoReleaseInfo> AllProducers => _allProducers;

    /// <summary>
    /// Gets whether this is a local, not yet published release.
    /// </summary>
    public bool IsLocal => _isLocal;

    /// <summary>
    /// Gets whether all <see cref="BuildContentInfo.Produced"/> NuGet packages are in "$Local/&lt;world name&gt;/NuGet"
    /// and all <see cref="BuildContentInfo.AssetFileNames"/> are in their folder.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="assetsFolder">
    /// Outputs the "$Local/&lt;world name&gt;/Assets/&lt;repo name&gt;/&lt;version&gt;" where artifacts are.
    /// This is <see cref="NormalizedPath.IsEmptyPath"/> if <see cref="BuildContentInfo.AssetFileNames"/> is empty.
    /// </param>
    /// <returns>True if all the assets are locally available.</returns>
    public bool HasAllLocalArtifacts( IActivityMonitor monitor, out NormalizedPath assetsFolder )
    {
        return _releaseDatabase._artifactHandler.HasAllArtifacts( monitor, Repo, Version, Content, out assetsFolder );
    }

    /// <summary>
    /// Gets the direct consumers of this release.
    /// <para>
    /// This list is built on demand and cached.
    /// </para>
    /// </summary>
    /// <param name="monitor">The required monitor.</param>
    /// <returns>The list of direct consumers.</returns>
    public IReadOnlyList<RepoReleaseInfo> GetDirectConsumers( IActivityMonitor monitor )
    {
        return _directConsumers ??= _releaseDatabase.GetDirectConsumers( monitor, this );
    }

    /// <summary>
    /// Overridden to return the Repo display path and the released version.
    /// </summary>
    /// <returns>Repo display path/released version.</returns>
    public override string ToString() => $"{_repo.DisplayPath}/{_repoKey.Version}";

}
