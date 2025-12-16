using CK.Core;
using CKli.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;

namespace CKli.ArtifactHandler.Plugin;

public sealed class RepoArtifactInfo : RepoInfo
{
    readonly ArtifactHandlerPlugin _artifactHandler;

    public RepoArtifactInfo( ArtifactHandlerPlugin artifactHandler, Repo repo )
        : base( repo )
    {
        _artifactHandler = artifactHandler;
    }

    /// <summary>
    /// Moves the ".nupkg" in <paramref name="buildOutputPath"/> folder to the NuGet local feed.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="version">The built version.</param>
    /// <param name="buildOutputPath">The build output folder that contains the ".nupkg".</param>
    /// <param name="packageIdentifiers">The sorted package names. May be empty if the solution doesn't publish packages.</param>
    /// <returns>True on success, false on error.</returns>
    public bool PublishToNuGetLocalFeed( IActivityMonitor monitor,
                                         SVersion version,
                                         string buildOutputPath,
                                         out ImmutableArray<string> packageIdentifiers )
    {
        Throw.CheckArgument( buildOutputPath != null );
        var result = ImmutableArray.CreateBuilder<string>();
        try
        {
            var versionString = version.ToString();
            foreach( var a in Directory.EnumerateFiles( buildOutputPath ) )
            {
                var fileName = Path.GetFileName( a.AsSpan() );
                var ext = Path.GetExtension( fileName );
                if( ext.Equals( ".nupkg", StringComparison.Ordinal ) )
                {
                    var p = MoveNuGetPackage( monitor, _artifactHandler.LocalFeedNuGetPath, a, versionString, fileName, ext );
                    if( p == null )
                    {
                        packageIdentifiers = default;
                        return false;
                    }

                    result.Add( p );
                }
                else
                {
                    monitor.Warn( $"Unexpected file '{fileName}' build by '{Repo.DisplayPath}' in '{version}'. Ignored." );
                }
            }
            result.Sort( StringComparer.Ordinal );
            packageIdentifiers = result.DrainToImmutable();
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( $"While publishing nuget packages of '{Repo.DisplayPath}' in '{version}'.", ex );
            packageIdentifiers = default;
            return false;
        }

        static string? MoveNuGetPackage( IActivityMonitor monitor,
                                        NormalizedPath localFeedNuGetPath,
                                        string fullPath,
                                        string versionString,
                                        ReadOnlySpan<char> fileName,
                                        ReadOnlySpan<char> ext )
        {
            var artifactName = CheckFileNamePatternAndGetArtifactName( monitor, versionString, fileName, ext );
            if( artifactName.IsEmpty )
            {
                monitor.Error( $"Invalid package file name '{fileName}'. Expecting 'PackageName.{versionString}{ext}'." );
                return null;
            }
            string packageId = new string( artifactName );
            var target = $"{localFeedNuGetPath}/{fileName}";
            if( File.Exists( target ) )
            {
                if( !FileHelper.DeleteFile( monitor, target )
                    || !NuGetHelper.ClearGlobalCache( monitor, packageId, versionString ) )
                {
                    return null;
                }
            }
            try
            {
                File.Move( fullPath, target );
                return packageId;
            }
            catch( Exception ex )
            {
                monitor.Error( $"While adding nuget package '{fileName}' to NuGet local feed.", ex );
                return null;
            }
        }
    }

    /// <summary>
    /// Handles the files in the Repo "<see cref="ArtifactHandlerPlugin.DeployFolderName"/>/<see cref="ArtifactHandlerPlugin.DeployAssetsName"/>/"
    /// folder by copying them to "<see cref="ArtifactHandlerPlugin.LocalFeedAssetsPath"/>/<see cref="ArtifactHandlerPlugin.DeployAssetsName"/>/<paramref name="version"/>"
    /// folder and zipping any directories.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="version">The built version.</param>
    /// <param name="assetsFolder">The resulting folder in <see cref="ArtifactHandlerPlugin.LocalFeedAssetsPath"/> that contains the files.</param>
    /// <param name="fileNames">The sorted file names in the <paramref name="assetsFolder"/>. Can be empty if no files nor directories have been generated.</param>
    /// <returns>True on success, false on error.</returns>
    public bool PublishGeneratedAssets( IActivityMonitor monitor,
                                        SVersion version,
                                        out NormalizedPath assetsFolder,
                                        out ImmutableArray<string> fileNames )
    {
        assetsFolder = _artifactHandler.GetAssetsFolder( Repo, version );
        fileNames = [];
        var input = Repo.WorkingFolder.AppendPart( ArtifactHandlerPlugin.DeployFolderName ).AppendPart( ArtifactHandlerPlugin.DeployAssetsName );
        if( !Directory.Exists( input ) )
        {
            monitor.Error( $"Missing expected '{input}' folder." );
            return false;
        }
        if( Directory.Exists( assetsFolder ) )
        {
            monitor.Trace( $"Cleaning up existing '{assetsFolder}' folder." );
            if( !FileHelper.DeleteFolder( monitor, assetsFolder ) )
            {
                return false;
            }
        }
        try
        {
            var files = ImmutableArray.CreateBuilder<string>();
            Directory.CreateDirectory( assetsFolder );
            foreach( var f in Directory.EnumerateFiles( input ) )
            {
                var fName = GetCleanName( f );
                File.Copy( f, Path.Combine( assetsFolder, fName ) );
                files.Add( fName );
            }
            foreach( var d in Directory.EnumerateDirectories( input ) )
            {
                var fName = GetCleanName( d ) + ".zip";
                ZipFile.CreateFromDirectory( d, Path.Combine( assetsFolder, fName ) );
                files.Add( fName );
            }
            files.Sort( StringComparer.Ordinal );
            fileNames = files.DrainToImmutable();
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( $"Error while handling '{Repo.DisplayPath}' assets.", ex );
            return false;
        }

        static string GetCleanName( string f )
        {
            return new string( Path.GetFileName( f.AsSpan() ).Trim() ).Replace( ',', '_' ).Replace( ' ', '_' );
        }
    }

    static ReadOnlySpan<char> CheckFileNamePatternAndGetArtifactName( IActivityMonitor monitor,
                                                                      string versionString,
                                                                      ReadOnlySpan<char> fileName,
                                                                      ReadOnlySpan<char> ext )
    {
        if( fileName.Length > 1 + versionString.Length + ext.Length )
        {
            var artifactNameAndVersion = fileName[0..^ext.Length];
            var idxDotVersion = artifactNameAndVersion.Length - versionString.Length - 1;
            if( artifactNameAndVersion[idxDotVersion] == '.'
                && artifactNameAndVersion.Slice( idxDotVersion + 1 ).Equals( versionString, StringComparison.Ordinal ) )
            {
                return artifactNameAndVersion.Slice( 0, idxDotVersion );
            }
        }
        return default;
    }
}
