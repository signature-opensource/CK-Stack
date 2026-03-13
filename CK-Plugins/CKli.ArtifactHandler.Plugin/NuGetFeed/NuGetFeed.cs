using CK.Core;
using CKli.Core;
using System.IO;
using System.Security;
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
    readonly VersionQualityFilter _pushQualityFilter;

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
                      VersionQualityFilter pushQualityFilter,
                      NuGetFeedCredentials? fakeReadCredentials )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( name );
        Throw.CheckArgument( !url.IsEmptyPath );
        Throw.CheckArgument( "The fake credentials must not be an API key.",
                              fakeReadCredentials == null || fakeReadCredentials.UserNameKey != null );
        _name = name;
        _url = url;
        _pushCredentials = pushCredentials;
        _fakeReadCredentials = fakeReadCredentials;
        _pushQualityFilter = pushQualityFilter;
    }

    internal static NuGetFeed Create( XElement e )
    {
        var name = (string)e.Attribute( XNames.Name )!;
        var url = (string?)e.Attribute( XNames.Url );
        VersionQualityFilter q = default;
        var sQ = (string?)e.Attribute( XNames.PushQualityFilter );
        if( !string.IsNullOrWhiteSpace( sQ ) && !VersionQualityFilter.TryParse( sQ, out q ) )
        {
            Throw.ArgumentException( nameof( PushQualityFilter ) );
        }
        var p = NuGetFeedCredentials.Create( e.Element( XNames.PushCredentials ) );
        var r = NuGetFeedCredentials.Create( e.Element( XNames.FakeReadCredentials ) );
        return new NuGetFeed( name, url, p, q, r );
    }

    internal XElement ToXml() => new XElement( XNames.Feed,
                                               new XAttribute( XNames.Name, _name ),
                                               new XAttribute( XNames.Url, _url ),
                                               new XAttribute( XNames.PushQualityFilter, _pushQualityFilter ),
                                               _pushCredentials?.ToXml( XNames.PushCredentials ),
                                               _fakeReadCredentials?.ToXml( XNames.FakeReadCredentials ) );

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
    /// This is the case of <see href="https://docs.github.com/fr/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry#authenticating-with-a-personal-access-token">GitHub</see>.
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
    /// Note: the name use here (<c>CKli-Test</c>) depends on the test host that is running.
    /// </para>
    /// </summary>
    public NuGetFeedCredentials? PushCredentials => _pushCredentials;

    /// <summary>
    /// Gets the filter that can restrict pushed versions of packages into this feed.
    /// Defaults to "[,].ci": the feed accepts all versions.
    /// </summary>
    public VersionQualityFilter PushQualityFilter => _pushQualityFilter;

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
