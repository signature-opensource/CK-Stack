using CK.Core;
using CKli.Core;
using CKli.LocalNuGetFeed.Plugin;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.IO;

namespace CKli.Build.Plugin;

/// <summary>
/// Captures the result of a <see cref="RepoBuilder.Build(IActivityMonitor, SVersion, bool, bool?)"/>.
/// </summary>
public sealed partial class BuildResult
{
    readonly ImmutableArray<NormalizedPath> _artifacts;
    readonly HashSet<NuGetPackageInstance> _consumedPackages;
    readonly Repo? _repo;
    readonly SVersion? _version;
    string? _temporaryFolder;

    BuildResult() => _artifacts = [];

    /// <summary>
    /// Gets the failed result.
    /// </summary>
    public static readonly BuildResult Failed = new BuildResult(); 

    internal BuildResult( Repo repo,
                        SVersion version,
                        ImmutableArray<NormalizedPath> artifacts,
                        HashSet<NuGetPackageInstance> consumedPackages,
                        string temporaryFolder )
    {
        _artifacts = artifacts;
        _consumedPackages = consumedPackages;
        _repo = repo;
        _version = version;
        _temporaryFolder = temporaryFolder;
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
    /// Gets the artifacts. May be empty if build didn't produce artifacts (but this should be exceptional).
    /// </summary>
    public ImmutableArray<NormalizedPath> Artifacts => _artifacts;

    /// <summary>
    /// Optional temporary folder that should be deleted once the <see cref="Artifacts"/> have been handled.
    /// <para>
    /// <see cref="CleanupTemporaryFolder(IActivityMonitor)"/> sets this to null.
    /// </para>
    /// </summary>
    public string? TemporaryFolder => _temporaryFolder;

    /// <summary>
    /// Gets the packages that this <see cref="Repo"/> requires.
    /// </summary>
    public IReadOnlySet<NuGetPackageInstance> ConsumedPackages => _consumedPackages;

    /// <summary>
    /// Moves the <see cref="Artifacts"/> that are ".nupkg" files to <see cref="LocalNuGetFeedPlugin"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="localFeed">The local feed plugin.</param>
    /// <returns>The list of local NuGet packages or null on error.</returns>
    public List<LocalNuGetPackageInstance>? PublishToLocalNuGetFeed( IActivityMonitor monitor, LocalNuGetFeedPlugin localFeed )
    {
        Throw.CheckState( Success );
        var result = new List<LocalNuGetPackageInstance>();
        try
        {
            if( _artifacts.Length == 0 )
            {
                monitor.Warn( $"No package produced by '{_repo.DisplayPath}' for '{_version}'." );
            }
            else
            {
                foreach( var a in _artifacts )
                {
                    if( a.LastPart.EndsWith( ".nupkg" ) )
                    {
                        var p = localFeed.Add( monitor, a );
                        if( p == null ) return null;
                        result.Add( p );
                    }
                }
            }
            return result;
        }
        catch( Exception ex ) 
        {
            monitor.Error( "Error while publishing NuGet packages to local NuGet feed.", ex ); 
            return null;
        }
    }

    /// <summary>
    /// Helper that deletes the <see cref="TemporaryFolder"/> if it exists.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    public void CleanupTemporaryFolder( IActivityMonitor monitor )
    {
        if( _temporaryFolder != null && Directory.Exists( _temporaryFolder ) )
        {
            monitor.Trace( $"Deleting temporary folder '{_temporaryFolder}'." );
            FileHelper.DeleteFolder( monitor, _temporaryFolder );
            _temporaryFolder = null;
        }
    }

    internal void SetConsumedPackages( HashSet<NuGetPackageInstance> consumedPackages )
    {
        throw new NotImplementedException();
    }

}


