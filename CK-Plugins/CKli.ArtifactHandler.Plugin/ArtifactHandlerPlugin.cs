using CK.Core;
using CKli.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace CKli.ArtifactHandler.Plugin;

public sealed class ArtifactHandlerPlugin : PrimaryRepoPlugin<RepoArtifactInfo>
{
    public const string DeployFolderName = "Deployment";
    public const string DeployAssetsName = "Assets";


    readonly NormalizedPath _localNuGetPath;
    readonly NormalizedPath _localAssetsPath;
    ImmutableArray<NuGetFeed> _feeds;
    XDocument? _defaultNugetConfig;

    public ArtifactHandlerPlugin( PrimaryPluginContext context )
        : base( context )
    {
        _localNuGetPath = World.Name.LocalDataFolder.AppendPart( "NuGet" );
        _localAssetsPath = World.Name.LocalDataFolder.AppendPart( DeployAssetsName );
        Directory.CreateDirectory( _localNuGetPath );
        Directory.CreateDirectory( _localAssetsPath );
    }

    /// <summary>
    /// Gets the "<see cref="LocalWorldName.LocalDataFolder"/>/NuGet" folder.
    /// </summary>
    public NormalizedPath LocalNuGetPath => _localNuGetPath;

    /// <summary>
    /// Gets the "<see cref="LocalWorldName.LocalDataFolder"/>/Assets" folder.
    /// </summary>
    public NormalizedPath LocalAssetsPath => _localAssetsPath;

    /// <summary>
    /// Gets the &lt;ArtifactHandler&gt;/&lt;NuGet&gt;/&lt;Feed&gt; configurations.
    /// <para>
    /// This is cached once obtained.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="feeds">The configured feeds.</param>
    /// <returns>True on success, false on error.</returns>
    public bool GetConfiguredNuGetFeeds( IActivityMonitor monitor, out ImmutableArray<NuGetFeed> feeds )
    {
        feeds = _feeds;
        if( feeds.IsDefault )
        {
            try
            {
                feeds = PrimaryPluginContext.Configuration.XElement.Elements( XNames.NuGet )
                                                           .Elements( XNames.Feed )
                                                           .Select( NuGetFeed.Create )
                                                           .ToImmutableArray();
                // Handling default here: when there's no configured feeds, we automatically add
                // the nuget.org public feed.
                if( feeds.Length == 0 )
                {
                    var nugetOrg = new NuGetFeed( "NuGet",
                                                  "https://api.nuget.org/v3/index.json",
                                                  pushCredentials: new NuGetFeedCredentials( "NUGET_ORG_PUSH_API_KEY", null ),
                                                  pushQualityFilter: new VersionQualityFilter( "pre", includeMin: true, null, true, false ),
                                                  fakeReadCredentials: null );
                    PrimaryPluginContext.Configuration.Edit( monitor, ( monitor, e ) =>
                    {
                        e.Ensure( XNames.NuGet ).Add( nugetOrg.ToXml() );
                    } );
                    feeds = [nugetOrg];
                    monitor.Info( $"ArtifactHandler plugin configuration has been Initialized with 'https://nuget.org' feed." );
                }
                _feeds = feeds;
            }
            catch( Exception ex )
            {
                monitor.Error( $"Error while reading <ArtifactHandler>. This must be fixed manually.", ex );
                feeds = default;
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Gets the content of a <c>nuget.config</c> file based on the configured feeds
    /// (see <see cref="GetConfiguredNuGetFeeds(IActivityMonitor, out ImmutableArray{NuGetFeed})"/>).
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>The NuGet configuration file content or null on error.</returns>
    public XDocument? GetDefaultNuGetConfig( IActivityMonitor monitor )
    {
        if( _defaultNugetConfig == null )
        {
            if( !GetConfiguredNuGetFeeds( monitor, out var feeds ) )
            {
                return null;
            }
            var root = new XElement( "configuration",
                            new XElement( "packageSources",
                                    new XElement( "clear" ),
                                    feeds.Select( f => new XElement( "add", new XAttribute( "key", f.Name ), new XAttribute( "value", f.Url ) ) ) ),
                            new XElement( "packageSourceMapping",
                                    feeds.Select( f => new XElement( "packageSource", new XAttribute( "key", f.Name ),
                                                            new XElement( "package", new XAttribute( "pattern", "*" ) ) ) ) ),
                            GetPackageSourceCredentials( feeds ) );

            static XElement? GetPackageSourceCredentials( ImmutableArray<NuGetFeed> feeds )
            {
                if( feeds.Any( f => f.FakeReadCredentials != null ) )
                {
                    return new XElement( "packageSourceCredentials",
                                         feeds.Where( f => f.FakeReadCredentials != null )
                                              .Select( f => new XElement( f.Name.Replace( " ", "_x0020_" ),
                                                                 new XElement( "add",
                                                                    new XAttribute( "key", "Username" ),
                                                                    new XAttribute( "value", f.FakeReadCredentials!.UserNameKey ?? "" ) ),
                                                                 new XElement( "add",
                                                                    new XAttribute( "key", "ClearTextPassword" ),
                                                                    new XAttribute( "value", f.FakeReadCredentials!.SecretKey ) ) ) ) );
                }
                return null;
            }
            _defaultNugetConfig = new XDocument( root );
        }
        return _defaultNugetConfig;
    }

    /// <summary>
    /// Gets the local folder for assets.
    /// </summary>
    /// <param name="repo">The repository.</param>
    /// <param name="version">The version.</param>
    /// <returns>The assets local folder (may not exist).</returns>
    public NormalizedPath GetAssetsFolder( Repo repo, SVersion version )
    {
        return _localAssetsPath.AppendPart( repo.DisplayPath.LastPart ).AppendPart( version.ToString() );
    }

    protected override RepoArtifactInfo Create( IActivityMonitor monitor, Repo repo )
    {
        return new RepoArtifactInfo( this, repo );
    }

    /// <summary>
    /// Analyzes <see cref="LocalNuGetPath"/> to check whether all produced packages and files are locally available.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="repo">The released repository.</param>
    /// <param name="version">The released version.</param>
    /// <param name="buildContentInfo">The release info.</param>
    /// <param name="assetsFolder">
    /// Outputs the "$Local/&lt;world name&gt;/Assets/&lt;repo name&gt;/&lt;version&gt;" where artifacts are.
    /// This is <see cref="NormalizedPath.IsEmptyPath"/> if <see cref="BuildContentInfo.AssetFileNames"/> is empty.
    /// </param>
    /// <returns>True if the release's packages and asset files are locally available, false otherwise.</returns>
    public bool HasAllArtifacts( IActivityMonitor monitor,
                                 Repo repo,
                                 SVersion version,
                                 BuildContentInfo buildContentInfo,
                                 out NormalizedPath assetsFolder )
    {
        List<string>? missingPackages = null;
        List<string>? missingAssetFileNames = null;
        if( buildContentInfo.Produced.Length > 0 )
        {
            foreach( var p in buildContentInfo.Produced )
            {
                if( !File.Exists( Path.Combine( _localNuGetPath, $"{p}.{version}.nupkg" ) ) )
                {
                    missingPackages ??= new List<string>();
                    missingPackages.Add( p );
                }
            }
        }
        if( buildContentInfo.AssetFileNames.Length > 0 )
        {
            assetsFolder = GetAssetsFolder( repo, version );
            if( !Directory.Exists( assetsFolder ) )
            {
                missingAssetFileNames = [.. buildContentInfo.AssetFileNames];
            }
            else
            {
                foreach( var f in buildContentInfo.AssetFileNames )
                {
                    if( !File.Exists( assetsFolder.AppendPart( f ) ) )
                    {
                        missingAssetFileNames ??= new List<string>();
                        missingAssetFileNames.Add( f );
                    }

                }
            }
        }
        else
        {
            assetsFolder = default;
        }
        if( missingPackages == null && missingAssetFileNames == null )
        {
            return true;
        }
        if( missingPackages != null )
        {
            monitor.Info( $"""
            Missing {missingPackages.Count} local packages produced by '{repo.DisplayPath}' for version '{version}':
            {missingPackages.Concatenate()}
            """ );
        }
        if( missingAssetFileNames != null )
        {
            monitor.Info( $"""
            Missing {missingAssetFileNames.Count} asset files produced by '{repo.DisplayPath}' for version '{version}':
            {missingAssetFileNames.Concatenate()}
            """ );
        }
        return false;
    }

    /// <summary>
    /// This should only be called by the VersionTag plugin.
    /// <para>
    /// This is idempotent.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="repo">The source repository.</param>
    /// <param name="version">The release to destroy.</param>
    /// <param name="buildContentInfo">The build content.</param>
    /// <param name="removeFromNuGetGlobalCache">
    /// False to let the package in the NuGet global cache (if it exists).
    /// The global cache is "%userprofile%\.nuget\packages" on windows and "~/.nuget/packages" on Mac/Linux.
    /// </param>
    /// <returns>True on success, false if deleting some artifacts failed.</returns>
    public bool DestroyLocalRelease( IActivityMonitor monitor, Repo repo, SVersion version, BuildContentInfo buildContentInfo, bool removeFromNuGetGlobalCache = true )
    {
        bool success = true;
        if( buildContentInfo.Produced.Length > 0 )
        {
            foreach( var p in buildContentInfo.Produced )
            {
                if( removeFromNuGetGlobalCache ) NuGetHelper.Cache.RemovePackage( monitor, p, version );
                success &= FileHelper.DeleteFile( monitor, Path.Combine( _localNuGetPath, $"{p}.{version}.nupkg" ) );
            }
        }
        var assetsFolder = GetAssetsFolder( repo, version );
        success &= FileHelper.DeleteFolder( monitor, assetsFolder );
        return success;
    }

    /// <summary>
    /// Removes all NuGet packages and artifacts that are from a <see cref="SVersionExtensions.IsLocalFix(SVersion)"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    public void DestroyAllLocalFixRelease( IActivityMonitor monitor )
    {
        foreach( var packagePath in Directory.EnumerateFiles( _localNuGetPath ) )
        {
            var name = Path.GetFileName( packagePath.AsSpan() );
            if( PackageInstance.TryParseNupkgFileName( name, out var version, out int packageLength ) )
            {
                if( version.IsLocalFix() )
                {
                    FileHelper.DeleteFile( monitor, packagePath );
                    string packageId = new( name.Slice( 0, packageLength ) );
                    NuGetHelper.Cache.RemovePackage( monitor, packageId, version );
                    monitor.Trace( $"Deleted local fix package '{name}'." );
                }
            }
            else
            {
                monitor.Warn( ActivityMonitor.Tags.ToBeInvestigated, $"Found file name '{name}' in '{_localNuGetPath}' that is not a valid NuGet package name." );
            }
        }
        foreach( var repo in Directory.EnumerateDirectories( _localAssetsPath ) )
        {
            foreach( var version in Directory.EnumerateDirectories( repo ) )
            {
                var name = Path.GetFileName( version ).AsSpan();
                if( SVersion.TryParse( Path.GetFileName( version ), out var v ) )
                {
                    if( v.IsLocalFix() )
                    {
                        FileHelper.DeleteFolder( monitor, version );
                        monitor.Trace( $"Deleted local fix assets '{Path.GetFileName( repo.AsSpan() )}/{name}'." );
                    }
                }
                else
                {
                    monitor.Warn( ActivityMonitor.Tags.ToBeInvestigated, $"Found directory name '{name}' in '{repo}' that is not a valid version." );
                }
            }

        }
    }

}
