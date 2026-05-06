using CK.PerfectEvent;
using CKli;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CSemVer;
using LibGit2Sharp;
using NUnit.Framework;
using Shouldly;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;


[TestFixture]
public class S3ᅳSamplePublishedᅳTests
{

    [Test]
    public async Task cowork_Async()
    {
        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(sample_published)" );

        var bob = remotes.Clone( clonedFolder.Path.AppendPart( "Bob" ), Helper.ConfigureFakeFeeds );
        var bobDisplay = (StringScreen)bob.Screen;

        var tim = remotes.Clone( clonedFolder.Path.AppendPart( "Tim" ), Helper.ConfigureFakeFeeds );
        var timDisplay = (StringScreen)tim.Screen;

        (await CKliCommands.ExecAsync( TestHelper.Monitor, tim, "ckli", "status" )).ShouldBeTrue();
        timDisplay.ToString().ShouldBe( """

            """ );

    }

    [Test]
    public async Task with_deprecation_Async()
    {
        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(sample_published)" );
        var context = remotes.Clone( clonedFolder, Helper.ConfigureFakeFeeds );
        var display = (StringScreen)context.Screen;

        // Let's deprecate the current CKt-PerfectEvent v0.3.3 package (in 30.days).

        var inPerfectEvent = context.ChangeDirectory( "CKt-PerfectEvent" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "version", "deprecate", "v0.3.3", "--days", "30", "--reason", "For fun." )).ShouldBeTrue();

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "publish", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            - →·   CKt-Core                      v1.0.0           
            - →·   CKt-ActivityMonitor           v0.1.0           
            ╓  ⊙   CKt-PerfectEvent              v0.3.3+deprecated
            ║      CKt-Monitoring                v0.2.3           
            ╙      Samples/CKt-App-Sample        v0.0.1           
            -  ·→  Samples/CKt-Sample-Monitoring v0.0.1+deprecated
            There is nothing to build from the 1 pivots out of 6 repositories.
            (Using '*publish' may detect required builds in upstreams repositories.)
            Nothing to publish (the 6 repositories are already published)
            ❰✓❱

            """ );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "version", "deprecate", "v0.3.3", "--immediate", "--allow-update" )).ShouldBeTrue();

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            ❰✓❱

            """ );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "publish", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            - →·   CKt-Core                      v1.0.0           
            - →·   CKt-ActivityMonitor           v0.1.0           
            ╓  ⊙   CKt-PerfectEvent              v0.3.3+deprecated
            ║      CKt-Monitoring                v0.2.3           
            ╙      Samples/CKt-App-Sample        v0.0.1           
            -  ·→  Samples/CKt-Sample-Monitoring v0.0.1+deprecated
            There is nothing to build from the 1 pivots out of 6 repositories.
            (Using '*publish' may detect required builds in upstreams repositories.)
            Nothing to publish (the 6 repositories are already published)
            ❰✓❱

            """ );
    }

}
