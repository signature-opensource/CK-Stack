using CK.Core;
using CKli.Core;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.Versioning;
using CSemVer;
using System.Diagnostics.CodeAnalysis;

namespace CKli.LocalNuGetFeed.Plugin;

public sealed class LocalNuGetFeedPlugin : PrimaryPluginBase
{
    readonly NormalizedPath _localFeedPath;

    public LocalNuGetFeedPlugin( PrimaryPluginContext primaryContext )
        : base( primaryContext )
    {
        _localFeedPath = World.StackRepository.StackWorkingFolder.Combine( "$Local/LocalFeed" );
        Directory.CreateDirectory( _localFeedPath );
    }

    public NormalizedPath LocalFeedPath => _localFeedPath;

    public LocalNuGetPackageInstance? Add( IActivityMonitor monitor, NormalizedPath nugetFilePath, bool copyFile = false )
    {
        if( !TryParseNupkgFileName( nugetFilePath.LastPart, out var version, out var packageIdLength ) )
        {
            monitor.Error( $"Unable to parse expected .nupkg file name from '{nugetFilePath}'." );
            return null;
        }
        string packageId = nugetFilePath.LastPart.Substring( 0, packageIdLength );
        var target = _localFeedPath.AppendPart( nugetFilePath.LastPart );
        if( File.Exists( target ) )
        {
            if( !FileHelper.DeleteFile( monitor, target )
                || !NuGetHelper.ClearGlobalCache( monitor, packageId, version.ParsedText ) )
            {
                return null;
            }
        }
        try
        {
            if( copyFile )
            {
                File.Copy( nugetFilePath, target );
            }
            else
            {
                File.Move( nugetFilePath, target );
            }
            return new LocalNuGetPackageInstance( target, packageId, version );
        }
        catch( Exception ex )
        {
            monitor.Error( $"While adding nuget package '{nugetFilePath.LastPart}' to NuGet local feed.", ex );
            return null;
        }
    }

    static bool TryParseNupkgFileName( ReadOnlySpan<char> s, [NotNullWhen(true)]out SVersion? v, out int packageIdLength )
    {
        var h = s;
        for( ; ; )
        {
            var nextDot = h.IndexOf( '.' );
            // Consider 2 consecutive .. to be invalid.
            if( nextDot <= 0 ) break;
            h = h.Slice( nextDot + 1 );
            if( h.Length > 0 && char.IsAsciiDigit( h[0] ) )
            {
                packageIdLength = s.Length - h.Length - 1;
                v = SVersion.TryParse( ref h );
                // Here we allow a starting digit in the package id because this is legit (tested):
                // Successfully created package '...\package\debug\Truc.0Machin.1.0.0.nupkg
                if( v.IsValid )
                {
                    return h.TryMatch( ".nupkg" ) && h.Length == 0;
                }
            }
        }
        v = null;
        packageIdLength = 0;
        return false;
    }
}
