using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace CKli.Build.Plugin;

/// <summary>
/// Captures the result of a <see cref="RepoBuilder.Build(IActivityMonitor, SVersion, bool, bool?)"/>.
/// </summary>
public sealed partial class BuildResult
{
    readonly BuildContentInfo _buildContentInfo;
    readonly NormalizedPath _assetsFolder;
    readonly Repo? _repo;
    readonly SVersion? _version;

    BuildResult()
    {
        _buildContentInfo = null!;
    }

    /// <summary>
    /// Gets the failed result.
    /// </summary>
    public static readonly BuildResult Failed = new BuildResult(); 

    internal BuildResult( Repo repo,
                          SVersion version,
                          SortedSet<NuGetPackageInstance> consumedPackages,
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

    /// <summary>
    /// Gets whether the build succeeded.
    /// </summary>
    [MemberNotNullWhen( true, nameof( Repo ), nameof( _repo ), nameof( Version ), nameof( _version ) )]
    public bool Success => _version != null;

    /// <summary>
    /// Gets the repository. Always available when <see cref="Success"/> is true.
    /// </summary>
    public Repo? Repo => _repo;

    /// <summary>
    /// Gets the version built. Always available when <see cref="Success"/> is true.
    /// </summary>
    public SVersion? Version => _version;

    /// <summary>
    /// Gets the build content.
    /// </summary>
    public BuildContentInfo Content
    {
        get
        {
            Throw.CheckState( Success );
            return _buildContentInfo;
        }
    }

    /// <summary>
    /// Gets the assets folder in "$Local/Assets". <see cref="NormalizedPath.IsEmptyPath"/>
    /// if there is no assets.
    /// </summary>
    public NormalizedPath AssetsFolder => _assetsFolder;

    /// <summary>
    /// Gets the <see cref="BuildContentInfo"/> on success, "Failed" otherwise.
    /// </summary>
    /// <returns>The build content info.</returns>
    public override string ToString() => Success ? "Failed" : _buildContentInfo.ToString();
}


