using CK.Core;
using CKli.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security;
using System.Text;
using System.Xml.Linq;

namespace CKli.ArtifactHandler.Plugin;

/// <summary>
/// Models a NuGet feed.
/// </summary>
public sealed class NuGetFeed
{
    readonly string _name;
    readonly NormalizedPath _url;
    readonly NuGetFeedCredentials? _pushCredentials;
    readonly NuGetFeedCredentials? _fakeReadCredentials;
    readonly PackageQualityFilter _pushQualityFilter;

    /// <summary>
    /// Initializes a new NuGet feed.
    /// </summary>
    /// <param name="name">The feed name.</param>
    /// <param name="url">The url to the NuGet feed</param>
    /// <param name="pushCredentials">See <see cref="PushCredentials"/>.</param>
    /// <param name="pushQualityFilter">See <see cref="PushQualityFilter"/>.</param>
    /// <param name="fakeReadCredentials">See <see cref="FakeReadCredentials"/>.</param>
    public NuGetFeed( string name,
                      NormalizedPath url,
                      NuGetFeedCredentials? pushCredentials,
                      PackageQualityFilter pushQualityFilter,
                      NuGetFeedCredentials? fakeReadCredentials )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( name );
        Throw.CheckArgument( !url.IsEmptyPath );
        Throw.CheckArgument( "The fake credentials must not be an AIP key.",
                              fakeReadCredentials == null || fakeReadCredentials.UserNameKey != null );
        _name = name;
        _url = url;
        _pushCredentials = pushCredentials;
        _fakeReadCredentials = fakeReadCredentials;
        _pushQualityFilter = pushQualityFilter;
    }

    internal static NuGetFeed Create( XElement e )
    {
        var name = (string)e.Attribute( "Name" )!;
        var url = (string?)e.Attribute( "Url" );
        var p = NuGetFeedCredentials.Create( e.Element( "PushCredentials" ) );
        if( !PackageQualityFilter.TryParse( (string?)e.Attribute( "PushQualityFilter" ), out var q  ) )
        {
            Throw.ArgumentException( nameof( PushQualityFilter ) );
        }
        var r = NuGetFeedCredentials.Create( e.Element( "FakeReadCredentials" ) );
        return new NuGetFeed( name, url, p, q, r );
    }

    /// <summary>
    /// The feed name.
    /// </summary>
    public string Name => _name;

    /// <summary>
    /// The url to the NuGet feed (as it appears in the <c>nuget.config</c> file).
    /// </summary>
    public NormalizedPath Url => _url;

    /// <summary>
    /// An optional public credentials that allows to make any private NuGet feed de facto
    /// public: these credentials are written as-is in the repositories <c>nuget.config</c>
    /// files. See https://learn.microsoft.com/en-us/nuget/reference/nuget-config-file#packagesourcecredentials.
    /// <para>
    /// This workarounds NuGet servers that even for public feeds (of public packages) require
    /// an authentication.
    /// This is the case of <see href="https://docs.github.com/fr/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry#authenticating-with-a-personal-access-token">GitHub</see>
    /// </para>
    /// </summary>
    public NuGetFeedCredentials? FakeReadCredentials => _fakeReadCredentials;

    /// <summary>
    /// The required credentials to be able to push packages to the remote feed.
    /// When not specified, CKli will never try to push any package to this feed.
    /// <para>
    /// Notes to developers/testers of CKli: This also applies to a local folder feed.
    /// To allow write to the local folder, you can use the same key as the one required
    /// to push to a local git repository (that is "FILESYSTEM_GIT") and register
    /// a "don't care" value in the secret store:
    /// <code>
    /// dotnet user-secrets set FILESYSTEM_GIT "don't care" --id CKli-Test
    /// </code>
    /// Note: the <c>CKli-Test</c> depends on the test host that is running.
    /// </para>
    /// </summary>
    public NuGetFeedCredentials? PushCredentials => _pushCredentials;

    /// <summary>
    /// Gets the min/max of <see cref="PackageQuality"/> to push packages to this feed.
    /// Defaults to "CI-Stable": the feed accepts all qualities.
    /// </summary>
    public PackageQualityFilter PushQualityFilter => _pushQualityFilter;

    internal bool Push( IActivityMonitor monitor, string pushFolder, ISecretsStore secretsStore )
    {
        Throw.DebugAssert( _pushCredentials != null );
        var secret = secretsStore.TryGetRequiredSecret( monitor, _pushCredentials.SecretKey );
        if( secret == null )
        {
            return false;
        }
        if( _pushCredentials.IsAPIKey )
        {
            if( !RunDotNetNuGetPush( monitor,
                                     $"""nuget push "*.nupkg" --skip-duplicate -k "{secret}" -s "{_url}" """,
                                     pushFolder ) )
            {
                return false; 
            }
        }
        else
        {
            var secUser = secretsStore.TryGetRequiredSecret( monitor, _pushCredentials.UserNameKey );
            if( secUser == null )
            {
                return false;
            }
            // https://learn.microsoft.com/en-us/nuget/reference/nuget-config-file#packagesourcecredentials

            // Okay... Its bad to use string based Xml generation, but its' easier...
            secUser = SecurityElement.Escape( secUser );

            var sName = SecurityElement.Escape( _name );
            var sUrl = SecurityElement.Escape( _url );
            var sSecret = SecurityElement.Escape( secret );

            var eName = sName.Replace( " ", "_x0020_" );

            var configFile = Path.Combine( pushFolder, "nuget.config" );
            File.WriteAllText( configFile, $"""
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="{sName}" value="{sUrl}" />
                  </packageSources>
                  <{eName}>
                    <add key="Username" value="{secUser}" />
                    <add key="ClearTextPassword" value="{sSecret}" />
                  </{eName}>
                </configuration>
                """ );
            try
            {
                if( !RunDotNetNuGetPush( monitor,
                                         $"""nuget push "*.nupkg" --skip-duplicate --configfile "nuget.config" """,
                                         pushFolder ) )
                {
                    return false;
                }
            }
            finally
            {
                FileHelper.DeleteFile( monitor, configFile );
            }
        }
        return true;

        static bool RunDotNetNuGetPush( IActivityMonitor monitor, string args, string pushFolder )
        {
            int? ret = ProcessRunner.RunProcess( monitor,
                                      "dotnet",
                                      args,
                                      pushFolder );
            if( ret is null || ret != 0 )
            {
                monitor.Error( $"Process 'dotnet nuget push' returned '{(ret.HasValue ? "timed out" : ret)}'." );
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Returns the "Name (Url)".
    /// </summary>
    /// <returns>The "Name (Url)".</returns>
    public override string ToString() => $"{Name} ({Url})";
}
