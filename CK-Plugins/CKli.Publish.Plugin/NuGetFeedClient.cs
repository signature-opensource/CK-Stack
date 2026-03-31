using CK.Core;
using CSemVer;
using NuGet.Packaging;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CKli.Publish.Plugin;

/// <summary>
/// Simplified client for interacting with a NuGet feed: list versions, push packages,
/// delete or unlist package versions. Authentication uses an API key.
/// </summary>
public sealed partial class NuGetFeedClient : IDisposable
{
    const int _perPackagePushTimeoutSecond = 60;

    readonly string _feedUrl;
    readonly string _apiKey;
    readonly string? _localPath;
    readonly SourceRepository _sourceRepository;
    readonly SourceCacheContext _cacheContext;

    /// <summary>
    /// Initializes a new <see cref="NuGetFeedClient"/>.
    /// </summary>
    /// <param name="feedUrl">The NuGet feed URL (V3 index.json or V2 endpoint).</param>
    /// <param name="apiKey">The API key used for push and delete operations.</param>
    /// <param name="skipCache">
    /// When true (the default), bypasses the NuGet on-disk metadata cache and always hits
    /// the network. This ensures <see cref="GetVersionsAsync"/> always reflects the actual
    /// current state of the feed, which is critical for management operations (push, delete,
    /// unlist). Set to false only for read-only auditing scenarios where slightly stale
    /// metadata is acceptable and throughput matters.
    /// </param>
    public NuGetFeedClient( string feedUrl, string apiKey, bool skipCache = true )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( feedUrl );
        Throw.CheckNotNullOrWhiteSpaceArgument( apiKey );
        _feedUrl = feedUrl;
        _apiKey = apiKey;
        if( Uri.TryCreate( feedUrl, UriKind.Absolute, out var uri ) && uri.IsFile )
        {
            _localPath = uri.LocalPath;
        }
        _sourceRepository = Repository.Factory.GetCoreV3( feedUrl );
        _cacheContext = new SourceCacheContext { NoCache = skipCache };
    }

    /// <inheritdoc/>
    public void Dispose() => _cacheContext.Dispose();

    /// <summary>
    /// Gets all versions of a package available on the feed.
    /// NuGet versions that cannot be mapped to a valid <see cref="SVersion"/> are skipped with a warning.
    /// </summary>
    /// <param name="logger">The logger to use.</param>
    /// <param name="packageId">The package identifier.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The list of versions, or null on error.</returns>
    public async Task<IReadOnlyList<SVersion>?> GetVersionsAsync( IActivityLineEmitter logger,
                                                                  string packageId,
                                                                  CancellationToken cancellationToken = default )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( packageId );
        logger.Trace( $"Getting versions of '{packageId}' from '{_feedUrl}'." );
        try
        {
            var result = new List<SVersion>();
            if( _localPath != null )
            {
                // Read the V3 expanded folder structure directly: {root}/{id}/{version}/
                var idFolder = Path.Combine( _localPath, packageId.ToLowerInvariant() );
                if( Directory.Exists( idFolder ) )
                {
                    foreach( var versionDir in Directory.GetDirectories( idFolder ) )
                    {
                        var versionName = Path.GetFileName( versionDir );
                        if( SVersion.TryParse( versionName, out var sv ) )
                            result.Add( sv );
                        else
                            logger.Warn( $"Cannot map local feed version folder '{versionName}' to SVersion. Skipped." );
                    }
                }
            }
            else
            {
                var resource = await _sourceRepository.GetResourceAsync<FindPackageByIdResource>( cancellationToken );
                var nugetVersions = await resource.GetAllVersionsAsync( packageId, _cacheContext, new LoggerAdapter( logger ), cancellationToken );
                foreach( var nv in nugetVersions )
                {
                    var nugetVersion = nv.ToFullString();
                    if( SVersion.TryParse( nugetVersion, out var sv ) )
                        result.Add( sv );
                    else
                        logger.Warn( $"Cannot map NuGet version '{nugetVersion}' to SVersion. Skipped." );
                }
            }
            logger.Trace( $"Found {result.Count} version(s) for '{packageId}' in '{_feedUrl}'." );
            return result;
        }
        catch( Exception ex )
        {
            logger.Error( $"Failed to get versions of '{packageId}' from '{_feedUrl}'.", ex );
            return null;
        }
    }

    /// <summary>
    /// Sends a delete request for the specified package version.
    /// <para>
    /// Whether this performs a hard-delete or an unlist depends entirely on the server:
    /// nuget.org unlists the package (it remains restorable but is hidden from search),
    /// while most private feeds (BaGet, Gitea, Nexus, Azure Artifacts…) perform a hard-delete.
    /// </para>
    /// </summary>
    /// <param name="logger">The logger to use.</param>
    /// <param name="packageId">The package identifier.</param>
    /// <param name="version">The version to delete.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>True on success, false on error.</returns>
    public async Task<bool> DeleteAsync( IActivityLineEmitter logger,
                                         string packageId,
                                         SVersion version,
                                         CancellationToken cancellationToken = default )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( packageId );
        Throw.CheckNotNullArgument( version );
        logger.Trace( $"Sending delete request for '{packageId}@{version}' on '{_feedUrl}'." );
        try
        {
            if( _localPath != null )
            {
                // Delete the V3 expanded folder structure directly: {root}/{id}/{version}/
                var versionFolder = Path.Combine( _localPath, packageId.ToLowerInvariant(), version.ToString().ToLowerInvariant() );
                Directory.Delete( versionFolder, recursive: true );
            }
            else
            {
                var resource = await _sourceRepository.GetResourceAsync<PackageUpdateResource>( cancellationToken );
                await resource.Delete( packageId,
                                       version.ToString(),
                                       _ => _apiKey,
                                       _ => true,
                                       noServiceEndpoint: false,
                                       new LoggerAdapter( logger ) );
            }
            return true;
        }
        catch( Exception ex )
        {
            logger.Error( $"Failed to delete '{packageId}@{version}' on '{_feedUrl}'.", ex );
            return false;
        }
    }

    /// <summary>
    /// Sends a delete request for each of the specified package versions.
    /// See <see cref="DeleteAsync(IActivityMonitor, string, SVersion, CancellationToken)"/> for
    /// the distinction between delete and unlist.
    /// </summary>
    /// <param name="logger">The logger to use.</param>
    /// <param name="packageId">The package identifier.</param>
    /// <param name="versions">The versions to delete.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>True if all operations succeeded, false if any failed.</returns>
    public async Task<bool> DeleteAsync( IActivityLineEmitter logger,
                                         string packageId,
                                         IEnumerable<SVersion> versions,
                                         CancellationToken cancellationToken = default )
    {
        bool success = true;
        foreach( var v in versions )
            success &= await DeleteAsync( logger, packageId, v,  cancellationToken );
        return success;
    }


    /// <summary>
    /// Pushes a single .nupkg file to the feed.
    /// </summary>
    /// <param name="logger">The logger to use.</param>
    /// <param name="nupkgFilePath">The path to the .nupkg file.</param>
    /// <param name="skipDuplicate">Whether to skip (rather than fail) if the package version already exists. Defaults to true.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>True on success, false on error.</returns>
    public Task<bool> PushAsync( IActivityLineEmitter logger,
                                 string nupkgFilePath,
                                 bool skipDuplicate = true,
                                 CancellationToken cancellationToken = default )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( nupkgFilePath );
        return PushCoreAsync( logger, [nupkgFilePath], skipDuplicate, cancellationToken );
    }

    /// <summary>
    /// Pushes a set of .nupkg files to the feed.
    /// </summary>
    /// <param name="logger">The logger to use.</param>
    /// <param name="nupkgFilePaths">Paths to the .nupkg files to push.</param>
    /// <param name="skipDuplicate">Whether to skip (rather than fail) if a package version already exists. Defaults to true.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>True on success, false on error.</returns>
    public Task<bool> PushAsync( IActivityLineEmitter logger,
                                 IEnumerable<string> nupkgFilePaths,
                                 bool skipDuplicate = true,
                                 CancellationToken cancellationToken = default )
    {
        Throw.CheckNotNullArgument( nupkgFilePaths );
        return PushCoreAsync( logger, nupkgFilePaths.ToArray(), skipDuplicate, cancellationToken );
    }

    async Task<bool> PushCoreAsync( IActivityLineEmitter logger,
                                    IList<string> paths,
                                    bool skipDuplicate,
                                    CancellationToken cancellationToken )
    {
        if( paths.Count == 0 )
        {
            logger.Warn( $"PushAsync: no packages to push to '{_feedUrl}'." );
            return true;
        }
        logger.Trace( $"Pushing {paths.Count} package(s) to '{_feedUrl}'." );
        var nugetLogger = new LoggerAdapter( logger );
        try
        {
            if( _localPath != null )
            {
                // PackageUpdateResource.Push calls ClientPolicyContext.GetClientPolicy(settings, log)
                // internally with null settings for file:// sources, which throws ArgumentNullException.
                // OfflineFeedUtility is the proper NuGet API for local V3 feeds.
                var extractionContext = new PackageExtractionContext( PackageSaveMode.Defaultv3,
                                                                     XmlDocFileSaveMode.None,
                                                                     clientPolicyContext: null,
                                                                     logger: nugetLogger );
                foreach( var path in paths )
                {
                    var addContext = new OfflineFeedAddContext( path,
                                                               _localPath,
                                                               nugetLogger,
                                                               throwIfSourcePackageIsInvalid: true,
                                                               throwIfPackageExistsAndInvalid: true,
                                                               throwIfPackageExists: !skipDuplicate,
                                                               extractionContext );
                    await OfflineFeedUtility.AddPackageToSource( addContext, cancellationToken );
                }
            }
            else
            {
                var resource = await _sourceRepository.GetResourceAsync<PackageUpdateResource>( cancellationToken );
                await resource.Push( paths,
                                     symbolSource: null,
                                     timeoutInSecond: paths.Count * _perPackagePushTimeoutSecond,
                                     disableBuffering: false,
                                     getApiKey: _ => _apiKey,
                                     getSymbolApiKey: null,
                                     noServiceEndpoint: false,
                                     skipDuplicate: skipDuplicate,
                                     symbolPackageUpdateResource: null,
                                     nugetLogger );
            }
            return true;
        }
        catch( Exception ex )
        {
            logger.Error( $"Failed to push package(s) to '{_feedUrl}'.", ex );
            return false;
        }
    }
}
