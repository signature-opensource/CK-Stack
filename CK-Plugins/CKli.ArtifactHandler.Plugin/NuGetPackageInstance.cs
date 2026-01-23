using CK.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace CKli.ArtifactHandler.Plugin;

public readonly record struct NuGetPackageInstance( string PackageId, SVersion Version ) : IComparable<NuGetPackageInstance>
{
    /// <summary>
    /// Tries to match "xxx@version" pattern. The <paramref name="head"/> is not forwarded on error.
    /// </summary>
    /// <param name="head">The head.</param>
    /// <param name="instance">The instance on success.</param>
    /// <returns>True on success, false otherwise.</returns>
    public static bool TryMatch( ref ReadOnlySpan<char> head, out NuGetPackageInstance instance )
    {
        instance = default;
        int idx = head.IndexOf( '@' );
        if( idx <= 0 ) return false;
        var rest = head.Slice( idx + 1 );
        var v = SVersion.TryParse( ref rest );
        if( !v.IsValid ) return false;

        instance = new NuGetPackageInstance( new string( head.Slice( 0, idx ) ), v );
        head = rest;
        return true;
    }

    /// <summary>
    /// Supports ordering by <see cref="PackageId"/> and <see cref="Version"/>.
    /// </summary>
    /// <param name="other">The other package.</param>
    /// <returns>Standard comparison value.</returns>
    public int CompareTo( NuGetPackageInstance other )
    {
        int cmp = StringComparer.Ordinal.Compare( PackageId, other.PackageId );
        return cmp == 0 ? Version.CompareTo( other.Version ) : cmp;
    }

    /// <summary>
    /// Parses the result of a "dotnet package list" call.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="jsonPackageList">The json string to parse.</param>
    /// <param name="buildInfo">Any object: its ToString() method will be used for error and warning logs.</param>
    /// <param name="packages">The consumed package instances.</param>
    /// <returns>True on success, false on error.</returns>
    public static bool ReadConsumedPackages( IActivityMonitor monitor,
                                             string jsonPackageList,
                                             object buildInfo,
                                             out ImmutableArray<NuGetPackageInstance> packages )
    {
        try
        {
            using var d = JsonDocument.Parse( jsonPackageList );
            if( !ReadProblems( monitor, buildInfo, d ) )
            {
                packages = [];
                return false;
            }
            packages = ReadPackages( d );
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( $"While reading Package list for '{buildInfo}'.", ex );
            packages = [];
            return false;
        }

        static bool ReadProblems( IActivityMonitor monitor, object buildInfo, JsonDocument d )
        {
            bool hasWarning = false;
            if( d.RootElement.TryGetProperty( "problems"u8, out var problems ) )
            {
                foreach( var p in problems.EnumerateArray() )
                {
                    if( p.GetProperty( "level"u8 ).GetString() == "error" )
                    {
                        monitor.Error( $"Package list for '{buildInfo}' has errors. See logs." );
                        return false;
                    }
                    else
                    {
                        hasWarning = true;
                    }
                }
            }
            if( hasWarning )
            {
                monitor.Warn( $"Package list for '{buildInfo}' has warnings. See logs." );
            }
            return true;
        }

        static ImmutableArray<NuGetPackageInstance> ReadPackages( JsonDocument d )
        {
            var result = new SortedSet<NuGetPackageInstance>();
            foreach( var p in d.RootElement.GetProperty( "projects"u8 ).EnumerateArray() )
            {
                foreach( var f in p.GetProperty( "frameworks"u8 ).EnumerateArray() )
                {
                    foreach( var package in f.GetProperty( "topLevelPackages"u8 ).EnumerateArray() )
                    {
                        string? packageId = package.GetProperty( "id"u8 ).GetString();
                        if( string.IsNullOrWhiteSpace( packageId ) )
                        {
                            Throw.InvalidDataException( $"Null or empty 'topLevelPackages.id' property." );
                        }
                        result.Add( new NuGetPackageInstance( packageId,
                                                              SVersion.Parse( package.GetProperty( "resolvedVersion"u8 ).GetString() ) ) );
                    }
                }
            }
            return result.ToImmutableArray();
        }

    }


    /// <summary>
    /// Tries to parse a "package.version.nupkg" file name.
    /// </summary>
    /// <param name="fileName">The file name to parse.</param>
    /// <param name="version">The non null parsed version on success.</param>
    /// <param name="packageIdLength">The package name length.</param>
    /// <returns>True on success, false if the file name cannot be parsed.</returns>
    public static bool TryParseNupkgFileName( ReadOnlySpan<char> fileName,
                                              [NotNullWhen( true )] out SVersion? version,
                                              out int packageIdLength )
    {
        var h = fileName;
        for(; ; )
        {
            var nextDot = h.IndexOf( '.' );
            // Consider 2 consecutive .. to be invalid.
            if( nextDot <= 0 ) break;
            h = h.Slice( nextDot + 1 );
            if( h.Length > 0 && char.IsAsciiDigit( h[0] ) )
            {
                packageIdLength = fileName.Length - h.Length - 1;
                version = SVersion.TryParse( ref h );
                // Here we allow a starting digit in the package id because this is legit (tested):
                // Successfully created package '...\package\debug\Truc.0Machin.1.0.0.nupkg
                if( version.IsValid )
                {
                    return h.TryMatch( ".nupkg" ) && h.Length == 0;
                }
            }
        }
        version = null;
        packageIdLength = 0;
        return false;
    }

    /// <summary>
    /// Returns "<see cref="PackageId"/>@<see cref="Version"/>".
    /// </summary>
    /// <returns>The package/version string.</returns>
    public override string ToString() => $"{PackageId}@{Version}";
}
