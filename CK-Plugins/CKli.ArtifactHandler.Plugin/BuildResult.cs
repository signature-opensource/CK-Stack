using CK.Core;
using CKli.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;

namespace CKli.ArtifactHandler.Plugin;

/// <summary>
/// Captures the result of a successful repository build.
/// </summary>
public sealed partial class BuildResult
{
    readonly BuildContentInfo _buildContentInfo;
    readonly NormalizedPath _assetsFolder;
    readonly Repo _repo;
    readonly SVersion _version;
    readonly bool _skippedBuild;

    /// <summary>
    /// Initializes a build result. <see cref="SkippedBuild"/> is false.
    /// </summary>
    /// <param name="repo">The repository.</param>
    /// <param name="version">The built version.</param>
    /// <param name="consumed">The consumed sorted packages. Must be unique and sorted.</param>
    /// <param name="produced">
    /// The produced package identifiers.
    /// Must be unique, lexicographically sorted, have no <see cref="Path.GetInvalidFileNameChars()"/>, no comma and space characters.
    /// </param>
    /// <param name="assetsFolder">
    /// Assets folder is "$Local/&lt;world name&gt;/Assets/&lt;repo name&gt;/&lt;version&gt;".
    /// <see cref="NormalizedPath.IsEmptyPath"/> if there is no assets.
    /// </param>
    /// <param name="assetFileNames">
    /// The asset file names.
    /// Must be unique, lexicographically sorted, have no <see cref="Path.GetInvalidFileNameChars()"/>, no comma and space characters.
    /// </param>
    public BuildResult( Repo repo,
                        SVersion version,
                        ImmutableArray<PackageInstance> consumed,
                        ImmutableArray<string> produced,
                        NormalizedPath assetsFolder,
                        ImmutableArray<string> assetFileNames )
    {
        _buildContentInfo = new BuildContentInfo( [..consumed],
                                                  produced,
                                                  assetFileNames );
        if( !assetFileNames.IsEmpty ) _assetsFolder = assetsFolder;
        _repo = repo;
        _version = version;
    }

    /// <summary>
    /// Initializes a <see cref="SkippedBuild"/> result.
    /// </summary>
    /// <param name="repo">The repository.</param>
    /// <param name="version">The built version.</param>
    /// <param name="content">Existing content info.</param>
    /// <param name="assetsFolder">
    /// Assets folder is "$Local/&lt;world name&gt;/Assets/&lt;repo name&gt;/&lt;version&gt;".
    /// <see cref="NormalizedPath.IsEmptyPath"/> if there is no assets.
    /// </param>
    public BuildResult( Repo repo,
                        SVersion version,
                        BuildContentInfo content,
                        NormalizedPath assetsFolder )
    {
        Throw.CheckArgument( assetsFolder.IsEmptyPath == content.AssetFileNames.IsEmpty );
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
    /// Gets whether this result has no produced NuGet packages nor asset files.
    /// </summary>
    public bool IsEmpty => _assetsFolder.IsEmptyPath && _buildContentInfo.Produced.IsEmpty;

    /// <summary>
    /// Gets the assets folder in ""$Local/&lt;world name&gt;/Assets/&lt;repo name&gt;/&lt;version&gt;".
    /// <see cref="NormalizedPath.IsEmptyPath"/> if there is no assets.
    /// </summary>
    public NormalizedPath AssetsFolder => _assetsFolder;

    /// <summary>
    /// Gets the <see cref="BuildContentInfo.Produced"/> with this <see cref="Version"/>.
    /// </summary>
    public IEnumerable<PackageInstance> Produced => _buildContentInfo.Produced.Select( p => new PackageInstance( p, _version ) );

    /// <summary>
    /// Gets the <see cref="BuildContentInfo"/>.
    /// </summary>
    /// <returns>The build content info.</returns>
    public override string ToString() => _buildContentInfo.ToString();

    /// <summary>
    /// Calls 'dotnet package list --format json --no-restore' and parses the result. The resulting
    /// packages are all the top level packages from all the projects for all the target frameworks
    /// (the same <see cref="PackageInstance.PackageId"/> may appear with different versions if
    /// conditional package references exist with restricted NuGet version ranges).
    /// <para>
    /// We could have captured the Requested (the version bound) in addition to the Resolved package version.
    /// This would allow a better impact computation by early filtering out useless package upgrades. This
    /// is doable but we almost never use NuGet version ranges. This would complexify the system for no real gain.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="buildInfo">A build info description (used by log).</param>
    /// <returns>The set of incoming packages or null on error.</returns>
    /// <remarks>
    /// Collecting the package dependencies can be done in multiple ways:
    /// <list type="bullet">
    ///     <item>
    ///     By reading the obj/Configuration/ProjectName.csproj.nuget.dgspec.json file
    ///     that contains exactly what we need... But this is not really documented (we must iterate on
    ///     all the projects).
    ///     </item>
    ///     <item>
    ///     By reading the project.assets.json (that is documented) and extracts the top-level dependencies 
    ///     </item>
    ///     <item>
    ///     By using BuildAlyzer (see https://github.com/Buildalyzer/Buildalyzer, we must iterate on all the projects).
    ///     </item>
    ///     <item>
    ///     By calling 'dotnet package list --format json' and parsing the result. Contains exactly what we need
    ///     and can be called on the .sln (projects are handled).
    ///     </item>
    /// </list>
    /// =&gt; The simplest and most robust way is the 'dotnet package list --format json'.
    /// </remarks>
    public static bool GetConsumedPackages( IActivityMonitor monitor, Repo repo, string buildInfo, out ImmutableArray<PackageInstance> packages )
    {
        var stdOut = new StringBuilder();
        if( !repo.RunDotnet( monitor, "package list --format json --no-restore", stdOut ) )
        {
            packages = [];
            return false;
        }
        return PackageInstance.ReadConsumedPackages( monitor, stdOut.ToString(), buildInfo, out packages );
    }
}


