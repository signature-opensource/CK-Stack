using CK.Core;
using CKli;
using CKli.Core;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

[TestFixture]
public class RemoteTests
{
    [Test]
    public async Task NuGetFeed_tests()
    {
        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(localFixed)" );
        var context = remotes.Clone( clonedFolder );

        // This has impact on the repositories if and only if the source name is not "NuGet"
        // (then it is normalized) because the source already exists in all "nuget.config" files.
        // 
        // ckli remote nuget add NuGet https://api.nuget.org/v3/index.json
        //(await CKliCommands.ExecAsync( TestHelper.Monitor, context, "remote", "nuget", "add", "NuGet", "https://api.nuget.org/v3/index.json" )).ShouldBeTrue();
    }
}
