using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CSemVer;
using System;
using System.Collections.Immutable;

namespace CKli.Build.Plugin;

/// <summary>
/// Captures the result of a successful <see cref="RepoBuilder.Build(IActivityMonitor, SVersion, bool, bool?)"/>.
/// </summary>
public sealed partial class BuildResult
{
    readonly BuildContentInfo _buildContentInfo;
    readonly NormalizedPath _assetsFolder;
    readonly Repo _repo;
    readonly SVersion _version;
    readonly bool _skippedBuild;

    internal BuildResult( Repo repo,
                          SVersion version,
                          ImmutableArray<NuGetPackageInstance> consumedPackages,
                          ImmutableArray<string> producedPackages,
                          NormalizedPath assetsFolder,
                          ImmutableArray<string> assetFileNames )
    {
        Throw.DebugAssert( !assetFileNames.IsDefault );
        Throw.DebugAssert( producedPackages.IsSortedStrict( StringComparer.Ordinal.Compare ) );
        Throw.DebugAssert( assetFileNames.IsSortedStrict( StringComparer.Ordinal.Compare ) );
        _buildContentInfo = new BuildContentInfo( [..consumedPackages],
                                                  producedPackages,
                                                  assetFileNames );
        if( !assetFileNames.IsEmpty ) _assetsFolder = assetsFolder;
        _repo = repo;
        _version = version;
    }

    internal BuildResult( Repo repo,
                          SVersion version,
                          BuildContentInfo content,
                          NormalizedPath assetsFolder )
    {
        _repo = repo;
        _version = version;
        _buildContentInfo = content;
        _assetsFolder = assetsFolder;
        _skippedBuild = true;
    }


    /// <summary>
    /// Gets the repository.
    /// </summary>
    public Repo Repo => _repo;

    /// <summary>
    /// Gets the version built.
    /// </summary>
    public SVersion Version => _version;

    /// <summary>
    /// Gets whether the build has been skipped (the release database
    /// knows this release and all artifacts are already available locally).
    /// </summary>
    public bool SkippedBuild => _skippedBuild;

    /// <summary>
    /// Gets the build content.
    /// </summary>
    public BuildContentInfo Content => _buildContentInfo;

    /// <summary>
    /// Gets the assets folder in ""$Local/&lt;world name&gt;/Assets/&lt;repo name&gt;/&lt;version&gt;".
    /// <see cref="NormalizedPath.IsEmptyPath"/> if there is no assets.
    /// </summary>
    public NormalizedPath AssetsFolder => _assetsFolder;

    /// <summary>
    /// Gets the <see cref="BuildContentInfo"/>.
    /// </summary>
    /// <returns>The build content info.</returns>
    public override string ToString() => _buildContentInfo.ToString();
}


