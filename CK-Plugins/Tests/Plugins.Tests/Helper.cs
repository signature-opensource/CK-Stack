using CK.Core;
using CKli;
using CKli.Core;
using Shouldly;
using System;
using System.Threading.Tasks;
using System.Xml.Linq;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

public static class Helper
{
    /// <summary>
    /// When a test DOESN'T push (it has no impact on the remotes), we remove
    /// the secret: this ensures that the test doesn't push anything.
    /// </summary>
    public static void RemoveFileSystemWritePAT()
    {
        ProcessRunner.RunProcess( TestHelper.Monitor,
                                  "dotnet",
                                  """user-secrets remove FILESYSTEM_GIT --id CKli-CK""",
                                  Environment.CurrentDirectory )
                     .ShouldBe( 0 );
    }

    /// <summary>
    /// When a test pushes (from git local to remotes), we need the PAT
    /// for the "FILESYSTEM".
    /// <para>
    /// That is useless (credentials are not used on local file system) but it's
    /// good to not make an exception for this case.
    /// </para> 
    /// </summary>
    public static void SetFileSystemWritePAT()
    {
        ProcessRunner.RunProcess( TestHelper.Monitor,
                                  "dotnet",
                                  """user-secrets set FILESYSTEM_GIT "don't care" --id CKli-CK""",
                                  Environment.CurrentDirectory )
                     .ShouldBe( 0 );
    }


    public static (NormalizedPath NuGetOrgPath, NormalizedPath SignatureOSPath) GetFakeFeedPaths( NormalizedPath clonedFolder )
    {
        return (clonedFolder.Combine( "FakeFeed/nuget.org" ), clonedFolder.Combine( "FakeFeed/Signature-OpenSource" ));
    }

    public static void ConfigureFakeFeeds( IActivityMonitor monitor, NormalizedPath clonedFolder, XElement plugins )
    {
        var (nugetOrgFeed, sosFeed) = GetFakeFeedPaths( clonedFolder );
        NuGetHelper.EnsureLocalFeed( monitor, nugetOrgFeed );
        NuGetHelper.EnsureLocalFeed( monitor, sosFeed );
        foreach( var f in plugins.Elements( "ArtifactHandler" ).Elements( "NuGet" ).Elements( "Feed" ) )
        {
            var url = f.Attribute( "Url" ).ShouldNotBeNull();
            url.SetValue( url.Value switch
            {
                "https://api.nuget.org/v3/index.json" => $"file://{nugetOrgFeed}",
                "https://pkgs.dev.azure.com/Signature-OpenSource/Feeds/_packaging/NetCore3/nuget/v3/index.json" => $"file://{sosFeed}",
                _ => Throw.NotSupportedException<string>( url.Value )
            } );
            var key = f.Element( "PushCredentials" )?.Attribute( "SecretKey" );
            key.ShouldNotBeNull().SetValue( "FILESYSTEM_GIT" );
        }
    }
}
