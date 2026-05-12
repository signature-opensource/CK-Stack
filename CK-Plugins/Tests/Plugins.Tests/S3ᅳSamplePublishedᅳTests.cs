using CK.Core;
using CK.PerfectEvent;
using CKli;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CSemVer;
using LibGit2Sharp;
using NUnit.Framework;
using Shouldly;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;


[TestFixture]
public class S3ᅳSamplePublishedᅳTests
{
    [TestCase( true )]
    [TestCase( false )]
    public async Task coworking_Async( bool useCheckout )
    {
        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(sample_published)" );

        // Shares the "FakeFeed/" folder in the cloned folder.
        var bob = remotes.Clone( clonedFolder.Path.AppendPart( "Bob" ),
                                 allowDuplicateStack: false,
                                 (monitor, stackPath, plugins) => Helper.ConfigureFakeFeeds( monitor, stackPath.RemoveLastPart(), plugins ) );
        var bobDisplay = (StringScreen)bob.Screen;

        var tim = remotes.Clone( clonedFolder.Path.AppendPart( "Tim" ),
                                 allowDuplicateStack: true,
                                 ( monitor, stackPath, plugins ) => Helper.ConfigureFakeFeeds( monitor, stackPath.RemoveLastPart(), plugins ) );
        var timDisplay = (StringScreen)tim.Screen;

        (await CKliCommands.ExecAsync( TestHelper.Monitor, tim, "status" )).ShouldBeTrue();

        // Bob has the same status.
        timDisplay.ToString().Replace( TestHelper.CKliStackWorkingFolder, "<Stack>" ).ShouldBe( """
            > Public stack CKt (6 repositories)
            │  <Stack>/CK-Plugins/Tests/Plugins.Tests/Cloned/coworking_Async/Tim/DuplicateOf-CKt/.PublicStack
            │  file:///<Stack>/CK-Plugins/Tests/Plugins.Tests/Remotes/bare/CKt(sample_published)/CKt-Stack
              CKt-Core                      dev/stable ↑0↓0 file:///<Stack>/CK-Plugins/Tests/Plugins.Tests/Remotes/bare/CKt(sample_published)/CKt-Core              
              CKt-ActivityMonitor           dev/stable ↑0↓0 file:///<Stack>/CK-Plugins/Tests/Plugins.Tests/Remotes/bare/CKt(sample_published)/CKt-ActivityMonitor   
              CKt-PerfectEvent              stable     ↑0↓0 file:///<Stack>/CK-Plugins/Tests/Plugins.Tests/Remotes/bare/CKt(sample_published)/CKt-PerfectEvent      
              CKt-Monitoring                dev/stable ↑0↓0 file:///<Stack>/CK-Plugins/Tests/Plugins.Tests/Remotes/bare/CKt(sample_published)/CKt-Monitoring        
              Samples/CKt-Sample-Monitoring stable     ↑0↓0 file:///<Stack>/CK-Plugins/Tests/Plugins.Tests/Remotes/bare/CKt(sample_published)/CKt-Sample-Monitoring 
              Samples/CKt-App-Sample        stable     ↑0↓0 file:///<Stack>/CK-Plugins/Tests/Plugins.Tests/Remotes/bare/CKt(sample_published)/CKt-App-Sample        
            ❰✓❱

            """ );

        // Bob starts to work on CK-PerfectEvent: he creates the "dev/stable" branch and adds a commit (with a breaking change).
        var bobPerfectEvent = bob.ChangeDirectory( "CKt-PerfectEvent" );
        if( useCheckout )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, bobPerfectEvent, "checkout", "dev/stable" )).ShouldBeTrue();
        }
        else
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, bobPerfectEvent, "exec", "git", "branch", "dev/stable" )).ShouldBeTrue();
        }
        TestHelper.TouchAndCommit( bobPerfectEvent.CurrentDirectory,
                                   branchName: "dev/stable",
                                   "fix!: This is a breaking change because of the exclamation mark.",
                                   fileName: "Bob-work.txt" );

        // Tim publishes a new version of CK-PerfectEvent.
        var timPerfectEvent = tim.ChangeDirectory( "CKt-PerfectEvent" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, timPerfectEvent, "checkout", "dev/stable" )).ShouldBeTrue();
        TestHelper.TouchAndCommit( timPerfectEvent.CurrentDirectory,
                                   branchName: null,
                                   fileName: "Tim-work.txt" );
        timDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, timPerfectEvent, "publish" )).ShouldBeTrue();
        timDisplay.ToString().ShouldBe( """
          - →·   CKt-Core                      v1.0.0
          - →·   CKt-ActivityMonitor           v0.1.0
        1 ╓  ⊙   CKt-PerfectEvent              v0.3.3 → v0.3.4 🡡 (CodeChange)   
          ║      CKt-Monitoring                v0.2.3
          ╙      Samples/CKt-App-Sample        v0.0.1
        2 -  ·→  Samples/CKt-Sample-Monitoring v0.0.1 → v0.0.2 🡡 (UpstreamBuild)
        Required build for 2 from the 1 pivots out of 6 repositories.
        (No dependency updates other than the ones from the upstreams are needed.)
        🡡 2 repositories must be published.
        ❰✓❱

        """ );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, timPerfectEvent, "checkout", "dev/stable" )).ShouldBeTrue();
        TestHelper.TouchAndCommit( timPerfectEvent.CurrentDirectory,
                                   branchName: null,
                                   fileName: "Tim-work.txt" );

        timDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, timPerfectEvent, "ci", "publish" )).ShouldBeTrue();
        timDisplay.ToString().ShouldBe( """
          - →·   CKt-Core                      v1.0.1--ci.3
          - →·   CKt-ActivityMonitor           v0.1.1--ci.4
        1 ╓  ⊙   CKt-PerfectEvent              v0.3.4       → v0.3.5--ci.2 🡡 (DependencyUpdate, CodeChange)            
                                                                              U CKt.ActivityMonitor: 0.1.0 → 0.1.1--ci.4
          ║      CKt-Monitoring                v0.2.4--ci.4
        2 ╙      Samples/CKt-App-Sample        v0.0.1       → v0.0.2--ci.1 🡡 (DependencyUpdate)                        
                                                                              U CKt.ActivityMonitor: 0.1.0 → 0.1.1--ci.4
        3 -  ·→  Samples/CKt-Sample-Monitoring v0.0.2       → v0.0.3--ci.1 🡡 (UpstreamBuild)                           
        Required build for 3 from the 1 pivots out of 6 repositories.
        U 2 updates from upstreams (not using '*publish' here).
        🡡 3 repositories must be published.
        ❰✓❱

        """ );

        // Bob ckli pulls. Its "dev/stable" is now no more a tracking branch because the "refs/remotes/origin/dev/stable" branch
        // has been pruned.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bob, "pull" )).ShouldBeTrue();

        // Branch issue! Bob cannot build immediately.
        bobDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bobPerfectEvent, "publish" )).ShouldBeFalse();

        // Bob.
        bobDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bob, "issue" )).ShouldBeTrue();
        bobDisplay.ToString().ShouldBe( """
        > CKt-PerfectEvent (1)
        │ > Desynchronized branches.
        │ │ - Branch 'stable' has 1 commits that must be in 'dev/stable'.
        │ │ Base branches can be merged without conflict into the desynchronized branches.
        ❰✓❱

        """ );

        bobDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bob, "issue", "--fix" )).ShouldBeTrue();
        bobDisplay.ToString().ShouldBe( """
        ❰✓❱

        """ );

        // Bob publishes (also from CK-PerfectEvent).
        bobDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, bobPerfectEvent, "publish" )).ShouldBeTrue();
        bobDisplay.ToString().ShouldBe( """
          - →·   CKt-Core                      v1.0.0
          - →·   CKt-ActivityMonitor           v0.1.0
        1 ╓  ⊙   CKt-PerfectEvent              v0.3.4 → v0.4.0 🡡 (CodeChange)   
          ║      CKt-Monitoring                v0.2.3
          ╙      Samples/CKt-App-Sample        v0.0.1
        2 -  ·→  Samples/CKt-Sample-Monitoring v0.0.2 → v0.1.0 🡡 (UpstreamBuild)
        Required build for 2 from the 1 pivots out of 6 repositories.
        (No dependency updates other than the ones from the upstreams are needed.)
        🡡 2 repositories must be published.
        ❰✓❱

        """ );

        // Tim ckli pulls, but before he creates the dev/stable branch.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, tim, "checkout", "dev/stable" )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, timPerfectEvent, "pull" )).ShouldBeTrue();

        timDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, timPerfectEvent, "issue" )).ShouldBeTrue();
        timDisplay.ToString().ShouldBe( """
        > CKt-PerfectEvent (1)
        │ > Desynchronized branches.
        │ │ - Branch 'stable' has 1 commits that must be in 'dev/stable'.
        │ │ Base branches can be merged without conflict into the desynchronized branches.
        ❰✓❱

        """ );

        timDisplay.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, tim, "issue", "--fix" )).ShouldBeTrue();
        timDisplay.ToString().ShouldBe( """
        ❰✓❱

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
