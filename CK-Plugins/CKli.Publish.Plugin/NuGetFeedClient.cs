using CK.Core;
using CSemVer;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
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
    readonly string _feedUrl;
    readonly string _apiKey;
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
        _sourceRepository = Repository.Factory.GetCoreV3( feedUrl );
        _cacheContext = new SourceCacheContext { NoCache = skipCache };
    }

    /// <inheritdoc/>
    public void Dispose() => _cacheContext.Dispose();

    /// <summary>
    /// Gets all versions of a package available on the feed.
    /// NuGet versions that cannot be mapped to a valid <see cref="SVersion"/> are skipped with a warning.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="packageId">The package identifier.</param>
    /// <param name="includePrerelease">Whether to include prerelease versions. Defaults to true.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The list of versions, or null on error.</returns>
    public async Task<IReadOnlyList<SVersion>?> GetVersionsAsync( IActivityMonitor monitor,
                                                                   string packageId,
                                                                   bool includePrerelease = true,
                                                                   CancellationToken cancellationToken = default )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( packageId );
        using var _ = monitor.OpenTrace( $"Getting versions of '{packageId}' from '{_feedUrl}'." );
        try
        {
            var resource = await _sourceRepository.GetResourceAsync<FindPackageByIdResource>( cancellationToken );
            var nugetVersions = await resource.GetAllVersionsAsync( packageId, _cacheContext, new LoggerAdapter( monitor ), cancellationToken );
            var result = new List<SVersion>();
            foreach( var nv in nugetVersions )
            {
                if( !includePrerelease && nv.IsPrerelease ) continue;
                if( SVersion.TryParse( nv.ToNormalizedString(), out var sv ) )
                {
                    result.Add( sv );
                }
                else
                {
                    monitor.Warn( $"Cannot map NuGet version '{nv}' to SVersion. Skipped." );
                }
            }
            monitor.CloseGroup( $"Found {result.Count} version(s)." );
            return result;
        }
        catch( Exception ex )
        {
            monitor.Error( $"Failed to get versions of '{packageId}' from '{_feedUrl}'.", ex );
            return null;
        }
    }

    /// <summary>
    /// Sends an HTTP DELETE for the specified package version.
    /// <para>
    /// Whether this performs a hard-delete or an unlist depends entirely on the server:
    /// nuget.org unlists the package (it remains restorable but is hidden from search),
    /// while most private feeds (BaGet, Gitea, Nexus, Azure Artifacts…) perform a hard-delete.
    /// Use <see cref="UnlistAsync(IActivityMonitor, string, SVersion, CancellationToken)"/>
    /// when the intent is specifically to unlist rather than destroy the package.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="packageId">The package identifier.</param>
    /// <param name="version">The version to delete.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>True on success, false on error.</returns>
    public Task<bool> DeleteAsync( IActivityMonitor monitor,
                                   string packageId,
                                   SVersion version,
                                   CancellationToken cancellationToken = default )
        => SendDeleteAsync( monitor, packageId, version, "delete", cancellationToken );

    /// <summary>
    /// Sends an HTTP DELETE for each of the specified package versions.
    /// See <see cref="DeleteAsync(IActivityMonitor, string, SVersion, CancellationToken)"/> for
    /// the distinction between delete and unlist.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="packageId">The package identifier.</param>
    /// <param name="versions">The versions to delete.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>True if all operations succeeded, false if any failed.</returns>
    public async Task<bool> DeleteAsync( IActivityMonitor monitor,
                                         string packageId,
                                         IEnumerable<SVersion> versions,
                                         CancellationToken cancellationToken = default )
    {
        bool success = true;
        foreach( var v in versions )
            success &= await SendDeleteAsync( monitor, packageId, v, "delete", cancellationToken );
        return success;
    }

    /// <summary>
    /// Sends an HTTP DELETE with the intent to unlist the specified package version.
    /// <para>
    /// On nuget.org this hides the version from search but keeps it restorable.
    /// On most private feeds this is equivalent to a hard-delete (same protocol operation).
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="packageId">The package identifier.</param>
    /// <param name="version">The version to unlist.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>True on success, false on error.</returns>
    public Task<bool> UnlistAsync( IActivityMonitor monitor,
                                   string packageId,
                                   SVersion version,
                                   CancellationToken cancellationToken = default )
        => SendDeleteAsync( monitor, packageId, version, "unlist", cancellationToken );

    /// <summary>
    /// Sends an HTTP DELETE with the intent to unlist each of the specified package versions.
    /// See <see cref="UnlistAsync(IActivityMonitor, string, SVersion, CancellationToken)"/> for
    /// the distinction between delete and unlist.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="packageId">The package identifier.</param>
    /// <param name="versions">The versions to unlist.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>True if all operations succeeded, false if any failed.</returns>
    public async Task<bool> UnlistAsync( IActivityMonitor monitor,
                                         string packageId,
                                         IEnumerable<SVersion> versions,
                                         CancellationToken cancellationToken = default )
    {
        bool success = true;
        foreach( var v in versions )
            success &= await SendDeleteAsync( monitor, packageId, v, "unlist", cancellationToken );
        return success;
    }

    async Task<bool> SendDeleteAsync( IActivityMonitor monitor,
                                      string packageId,
                                      SVersion version,
                                      string operation,
                                      CancellationToken cancellationToken )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( packageId );
        Throw.CheckNotNullArgument( version );
        using var _ = monitor.OpenTrace( $"Sending {operation} request for '{packageId}@{version}' on '{_feedUrl}'." );
        try
        {
            var resource = await _sourceRepository.GetResourceAsync<PackageUpdateResource>( cancellationToken );
            await resource.Delete( packageId,
                                   version.ToString(),
                                   _ => _apiKey,
                                   _ => true,
                                   noServiceEndpoint: false,
                                   new LoggerAdapter( monitor ) );
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( $"Failed to {operation} '{packageId}@{version}' on '{_feedUrl}'.", ex );
            return false;
        }
    }

    /// <summary>
    /// Pushes a single .nupkg file to the feed.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="nupkgFilePath">The path to the .nupkg file.</param>
    /// <param name="skipDuplicate">Whether to skip (rather than fail) if the package version already exists. Defaults to true.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>True on success, false on error.</returns>
    public Task<bool> PushAsync( IActivityMonitor monitor,
                                 string nupkgFilePath,
                                 bool skipDuplicate = true,
                                 CancellationToken cancellationToken = default )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( nupkgFilePath );
        return PushCoreAsync( monitor, [nupkgFilePath], skipDuplicate, cancellationToken );
    }

    /// <summary>
    /// Pushes all .nupkg files matching <paramref name="searchPattern"/> in <paramref name="directory"/> to the feed.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="directory">The directory to search.</param>
    /// <param name="searchPattern">File search pattern.</param>
    /// <param name="skipDuplicate">Whether to skip (rather than fail) if a package version already exists. Defaults to true.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>True on success, false on error.</returns>
    public Task<bool> PushAsync( IActivityMonitor monitor,
                                 string directory,
                                 string searchPattern,
                                 bool skipDuplicate = true,
                                 CancellationToken cancellationToken = default )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( directory );
        Throw.CheckNotNullOrWhiteSpaceArgument( searchPattern );
        return PushCoreAsync( monitor, Directory.GetFiles( directory, searchPattern ), skipDuplicate, cancellationToken );
    }

    /// <summary>
    /// Pushes a set of .nupkg files to the feed.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="nupkgFilePaths">Paths to the .nupkg files to push.</param>
    /// <param name="skipDuplicate">Whether to skip (rather than fail) if a package version already exists. Defaults to true.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>True on success, false on error.</returns>
    public Task<bool> PushAsync( IActivityMonitor monitor,
                                 IEnumerable<string> nupkgFilePaths,
                                 bool skipDuplicate = true,
                                 CancellationToken cancellationToken = default )
    {
        Throw.CheckNotNullArgument( nupkgFilePaths );
        return PushCoreAsync( monitor, nupkgFilePaths.ToArray(), skipDuplicate, cancellationToken );
    }

    async Task<bool> PushCoreAsync( IActivityMonitor monitor,
                                    IList<string> paths,
                                    bool skipDuplicate,
                                    CancellationToken cancellationToken )
    {
        if( paths.Count == 0 )
        {
            monitor.Warn( $"PushAsync: no packages to push to '{_feedUrl}'." );
            return true;
        }
        using var _ = monitor.OpenTrace( $"Pushing {paths.Count} package(s) to '{_feedUrl}'." );
        try
        {
            var resource = await _sourceRepository.GetResourceAsync<PackageUpdateResource>( cancellationToken );
            await resource.Push( paths,
                                 symbolSource: null,
                                 timeoutInSecond: 300,
                                 disableBuffering: false,
                                 getApiKey: _ => _apiKey,
                                 getSymbolApiKey: null,
                                 noServiceEndpoint: false,
                                 skipDuplicate: skipDuplicate,
                                 symbolPackageUpdateResource: null,
                                 new LoggerAdapter( monitor ) );
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( $"Failed to push package(s) to '{_feedUrl}'.", ex );
            return false;
        }
    }
}
