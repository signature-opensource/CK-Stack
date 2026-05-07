using CK.Core;
using CKli;
using CKli.Core;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Linq;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

[TestFixture]
public class S2ᅳWithSampleᅳTests
{
    [Test]
    public async Task CKt_with_sample_ci_build_and_жbuild_Async()
    {
        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(with_sample)" );
        var context = remotes.Clone( clonedFolder );
        var display = (StringScreen)context.Screen;

        // From stack root (or if --all is specified): all solutions are pivots <==> none of them is.
        // (in this case *build is the same as build).
        // The CKt(with_sample) has been "ckli ci publish": there's nothing to build and nothing to publish in ci.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "ci", "publish", "--branch", "stable", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
        -  CKt-Core                      v1.0.1--ci.3
        -  CKt-ActivityMonitor           v0.1.1--ci.4
        ╓  CKt-PerfectEvent              v0.3.3--ci.4
        ║  CKt-Monitoring                v0.2.4--ci.4
        ╙  Samples/CKt-App-Sample        v0.0.1--ci.3
        -  Samples/CKt-Sample-Monitoring v0.0.1--ci.3
        There is nothing to build across the 6 repositories.
        Nothing to publish (the 6 repositories are already published)
        ❰✓❱
        
        """ );

        // If we "ckli publish" (that is the same as "ckli *publish" here), the 6 repositories must be published.  
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "publish", "--branch", "stable", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
        1 -  CKt-Core                      v1.0.0      → v1.0.1 🡡 (CodeChange)                            
        2 -  CKt-ActivityMonitor           v0.1.0      → v0.1.1 🡡 (UpstreamBuild, CodeChange)             
        3 ╓  CKt-PerfectEvent              v0.3.2      → v0.3.3 🡡 (UpstreamBuild, CodeChange)             
        4 ║  CKt-Monitoring                v0.2.3      → v0.2.4 🡡 (UpstreamBuild, CodeChange)             
        5 ╙  Samples/CKt-App-Sample        v0.0.0+fake → v0.0.1 🡡 (UpstreamBuild, FakeVersion, CodeChange)
        6 -  Samples/CKt-Sample-Monitoring v0.0.0+fake → v0.0.1 🡡 (UpstreamBuild, FakeVersion, CodeChange)
        Required build for 6 repositories across the 6 repositories.
        (No dependency updates other than the ones from the upstreams are needed.)
        🡡 6 repositories must be published.
        ❰✓❱
        
        """ );

        // To test the "ci build" and "ci *build", we touch the CKt-Core and CKt-PerfectEvent repositories.
        TestHelper.TouchAndCommit( context.CurrentDirectory.AppendPart( "CKt-Core" ), branchName: null );
        TestHelper.TouchAndCommit( context.CurrentDirectory.AppendPart( "CKt-PerfectEvent" ), branchName: null );

        #region ci build
        {
            // From stack root (or if --all is specified): all solutions are pivots <==> none of them is.
            // (in this case *build is the same as build).
            // Since CKt-Core is dirty, it implies the 5 other ones.
            display.Clear();
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "ci", "build", "--branch", "stable", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            1 -  CKt-Core                      v1.0.1--ci.3 → v1.0.1--ci.4 🡡 (CodeChange)               
            2 -  CKt-ActivityMonitor           v0.1.1--ci.4 → v0.1.1--ci.5 🡡 (UpstreamBuild)            
            3 ╓  CKt-PerfectEvent              v0.3.3--ci.4 → v0.3.3--ci.6 🡡 (UpstreamBuild, CodeChange)
            4 ║  CKt-Monitoring                v0.2.4--ci.4 → v0.2.4--ci.5 🡡 (UpstreamBuild)            
            5 ╙  Samples/CKt-App-Sample        v0.0.1--ci.3 → v0.0.1--ci.4 🡡 (UpstreamBuild)            
            6 -  Samples/CKt-Sample-Monitoring v0.0.1--ci.3 → v0.0.1--ci.4 🡡 (UpstreamBuild)            
            Required build for 6 repositories across the 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            🡡 6 repositories can be published.
            ❰✓❱
            
            """ );

            // From Samples/: the 2 samples are pivots, others are upstreams: they are ignored because this is a build, not a *build
            // and the 2 pivots are already available (in v0.0.0 built by the "ckli issue --fix" for the missing initial version),
            // so there is eventually nothing to do.
            display.Clear();
            var inSample = context.ChangeDirectory( "Samples" );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inSample, "ci", "build", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            - →·   CKt-Core                      v1.0.1--ci.3
            - →·   CKt-ActivityMonitor           v0.1.1--ci.4
            ╓ →·   CKt-PerfectEvent              v0.3.3--ci.4
            ║ →·   CKt-Monitoring                v0.2.4--ci.4
            ╙  ⊙   Samples/CKt-App-Sample        v0.0.1--ci.3
            -  ⊙   Samples/CKt-Sample-Monitoring v0.0.1--ci.3
            There is nothing to build from the 2 pivots out of 6 repositories.
            (Using '*build' may detect required builds in upstreams repositories.)
            Nothing to publish (the 6 repositories are already published)
            ❰✓❱
            
            """ );

            // From Samples/CKt-Sample-Monitoring: the App sample is "nothing" (not related to pivots), all the other
            // are ignored and the CKt-Sample-Monitoring is already available in v0.0.0, there's nothing to do.
            display.Clear();
            var inSampleMonitoring = inSample.ChangeDirectory( "CKt-Sample-Monitoring" );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleMonitoring, "ci", "build", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            - →·   CKt-Core                      v1.0.1--ci.3
            - →·   CKt-ActivityMonitor           v0.1.1--ci.4
            ╓ →·   CKt-PerfectEvent              v0.3.3--ci.4
            ║ →·   CKt-Monitoring                v0.2.4--ci.4
            ╙      Samples/CKt-App-Sample        v0.0.1--ci.3
            -  ⊙   Samples/CKt-Sample-Monitoring v0.0.1--ci.3
            There is nothing to build from the 1 pivots out of 6 repositories.
            (Using '*build' may detect required builds in upstreams repositories.)
            Nothing to publish (the 6 repositories are already published)
            ❰✓❱

            """ );

            // From Samples/CKt-App-Sample: same as above but CKt-App-Sample pivot replaces CKt-Sample-Monitoring.
            display.Clear();
            var inAppSample = inSample.ChangeDirectory( "CKt-App-Sample" );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inAppSample, "ci", "build", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            - →·   CKt-Core                      v1.0.1--ci.3
            - →·   CKt-ActivityMonitor           v0.1.1--ci.4
            ╓      CKt-PerfectEvent              v0.3.3--ci.4
            ║      CKt-Monitoring                v0.2.4--ci.4
            ╙  ⊙   Samples/CKt-App-Sample        v0.0.1--ci.3
            -      Samples/CKt-Sample-Monitoring v0.0.1--ci.3
            There is nothing to build from the 1 pivots out of 6 repositories.
            (Using '*build' may detect required builds in upstreams repositories.)
            Nothing to publish (the 6 repositories are already published)
            ❰✓❱

            """ );

            // From CKt-PerfectEvent: the CKt-Monitoring and App sample are "nothing". CKt-Sample-Monitoring is a downstream repo
            // that must be built.
            display.Clear();
            var inPerfectEvent = context.ChangeDirectory( "CKt-PerfectEvent" );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "ci", "build", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
              - →·   CKt-Core                      v1.0.1--ci.3
              - →·   CKt-ActivityMonitor           v0.1.1--ci.4
            1 ╓  ⊙   CKt-PerfectEvent              v0.3.3--ci.4 → v0.3.3--ci.5 🡡 (CodeChange)   
              ║      CKt-Monitoring                v0.2.4--ci.4
              ╙      Samples/CKt-App-Sample        v0.0.1--ci.3
            2 -  ·→  Samples/CKt-Sample-Monitoring v0.0.1--ci.3 → v0.0.1--ci.4 🡡 (UpstreamBuild)
            Required build for 2 from the 1 pivots out of 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            🡡 2 repositories can be published.
            ❰✓❱
            
            """ );
        }
        #endregion

        #region *build
        {
            // From stack root: all solutions are pivots <==> none of them is.
            display.Clear();
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "ci", "*build", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            1 -  CKt-Core                      v1.0.1--ci.3 → v1.0.1--ci.4 🡡 (CodeChange)               
            2 -  CKt-ActivityMonitor           v0.1.1--ci.4 → v0.1.1--ci.5 🡡 (UpstreamBuild)            
            3 ╓  CKt-PerfectEvent              v0.3.3--ci.4 → v0.3.3--ci.6 🡡 (UpstreamBuild, CodeChange)
            4 ║  CKt-Monitoring                v0.2.4--ci.4 → v0.2.4--ci.5 🡡 (UpstreamBuild)            
            5 ╙  Samples/CKt-App-Sample        v0.0.1--ci.3 → v0.0.1--ci.4 🡡 (UpstreamBuild)            
            6 -  Samples/CKt-Sample-Monitoring v0.0.1--ci.3 → v0.0.1--ci.4 🡡 (UpstreamBuild)            
            Required build for 6 repositories across the 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            🡡 6 repositories can be published.
            ❰✓❱
            
            """ );

            // From Samples/: the 2 samples are pivots, others are upstreams.
            display.Clear();
            var inSample = context.ChangeDirectory( "Samples" );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inSample, "ci", "*build", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            1 - →·   CKt-Core                      v1.0.1--ci.3 → v1.0.1--ci.4 🡡 (CodeChange)               
            2 - →·   CKt-ActivityMonitor           v0.1.1--ci.4 → v0.1.1--ci.5 🡡 (UpstreamBuild)            
            3 ╓ →·   CKt-PerfectEvent              v0.3.3--ci.4 → v0.3.3--ci.6 🡡 (UpstreamBuild, CodeChange)
            4 ║ →·   CKt-Monitoring                v0.2.4--ci.4 → v0.2.4--ci.5 🡡 (UpstreamBuild)            
            5 ╙  ⊙   Samples/CKt-App-Sample        v0.0.1--ci.3 → v0.0.1--ci.4 🡡 (UpstreamBuild)            
            6 -  ⊙   Samples/CKt-Sample-Monitoring v0.0.1--ci.3 → v0.0.1--ci.4 🡡 (UpstreamBuild)            
            Required build for 6 from the 2 pivots out of 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            🡡 6 repositories can be published.
            ❰✓❱
            
            """ );

            // From Samples/CKt-Sample-Monitoring: the App sample is "nothing" (not related to pivots).
            // However, the App sample must be build because one of its upstream is built.
            display.Clear();
            var inSampleMonitoring = inSample.ChangeDirectory( "CKt-Sample-Monitoring" );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleMonitoring, "ci", "*build", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            1 - →·   CKt-Core                      v1.0.1--ci.3 → v1.0.1--ci.4 🡡 (CodeChange)               
            2 - →·   CKt-ActivityMonitor           v0.1.1--ci.4 → v0.1.1--ci.5 🡡 (UpstreamBuild)            
            3 ╓ →·   CKt-PerfectEvent              v0.3.3--ci.4 → v0.3.3--ci.6 🡡 (UpstreamBuild, CodeChange)
            4 ║ →·   CKt-Monitoring                v0.2.4--ci.4 → v0.2.4--ci.5 🡡 (UpstreamBuild)            
            5 ╙      Samples/CKt-App-Sample        v0.0.1--ci.3 → v0.0.1--ci.4 🡡 (UpstreamBuild)            
            6 -  ⊙   Samples/CKt-Sample-Monitoring v0.0.1--ci.3 → v0.0.1--ci.4 🡡 (UpstreamBuild)            
            Required build for 6 from the 1 pivots out of 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            🡡 6 repositories can be published.
            ❰✓❱

            """ );

            // From Samples/CKt-App-Sample: the monitoring, perfect event and sample monitoring are "nothing", but because of
            // the upstreams, every Repo must be built.
            display.Clear();
            var inAppSample = inSample.ChangeDirectory( "CKt-App-Sample" );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inAppSample, "ci", "*build", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            1 - →·   CKt-Core                      v1.0.1--ci.3 → v1.0.1--ci.4 🡡 (CodeChange)               
            2 - →·   CKt-ActivityMonitor           v0.1.1--ci.4 → v0.1.1--ci.5 🡡 (UpstreamBuild)            
            3 ╓      CKt-PerfectEvent              v0.3.3--ci.4 → v0.3.3--ci.6 🡡 (UpstreamBuild, CodeChange)
            4 ║      CKt-Monitoring                v0.2.4--ci.4 → v0.2.4--ci.5 🡡 (UpstreamBuild)            
            5 ╙  ⊙   Samples/CKt-App-Sample        v0.0.1--ci.3 → v0.0.1--ci.4 🡡 (UpstreamBuild)            
            6 -      Samples/CKt-Sample-Monitoring v0.0.1--ci.3 → v0.0.1--ci.4 🡡 (UpstreamBuild)            
            Required build for 6 from the 1 pivots out of 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            🡡 6 repositories can be published.
            ❰✓❱

            """ );

            // From CKt-PerfectEvent: the CKt-Monitoring and App sample are "nothing", but as usual, because of the upstreams,
            // every Repo must be built.
            display.Clear();
            var inPerfectEvent = context.ChangeDirectory( "CKt-PerfectEvent" );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "ci", "*build", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            1 - →·   CKt-Core                      v1.0.1--ci.3 → v1.0.1--ci.4 🡡 (CodeChange)               
            2 - →·   CKt-ActivityMonitor           v0.1.1--ci.4 → v0.1.1--ci.5 🡡 (UpstreamBuild)            
            3 ╓  ⊙   CKt-PerfectEvent              v0.3.3--ci.4 → v0.3.3--ci.6 🡡 (UpstreamBuild, CodeChange)
            4 ║      CKt-Monitoring                v0.2.4--ci.4 → v0.2.4--ci.5 🡡 (UpstreamBuild)            
            5 ╙      Samples/CKt-App-Sample        v0.0.1--ci.3 → v0.0.1--ci.4 🡡 (UpstreamBuild)            
            6 -  ·→  Samples/CKt-Sample-Monitoring v0.0.1--ci.3 → v0.0.1--ci.4 🡡 (UpstreamBuild)            
            Required build for 6 from the 1 pivots out of 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            🡡 6 repositories can be published.
            ❰✓❱
            
            """ );
        }
        #endregion

    }

    [Test]
    public async Task intermediate_build_error_Async()
    {
        Helper.SetFileSystemWritePAT();

        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(with_sample)" );
        var context = remotes.Clone( clonedFolder, Helper.ConfigureFakeFeeds );
        var display = (StringScreen)context.Screen;

        // Same context as CKt_publish_PerfectEvent_Async test above...
        //
        // ... except that we inject an error in the CKt-Sample-Monitoring build
        //     so that during the 2nd build (after the fix), the CKt-PerfectEvent is already
        //     available in v0.3.3: the fact that it must be updated in CKt-Sample-Monitoring must
        //     be detected and CKt-Sample-Monitoring must be built (v0.0.0 → v0.0.1).
        //
        var inPerfectEvent = context.ChangeDirectory( "CKt-PerfectEvent" );

        // Bug in CKt-Sample-Monitoring.
        var inSampleMonitoring = context.ChangeDirectory( "Samples/CKt-Sample-Monitoring" );
        File.WriteAllText( inSampleMonitoring.CurrentDirectory.Combine( "CKt.Sample.Monitoring/Bug.cs" ), "I'm not a valid C# file at all." );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleMonitoring, "commit", "Added bug file." )).ShouldBeTrue();

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "publish" )).ShouldBeFalse();
        display.ToString().ShouldBe(
              """
                - →·   CKt-Core                      v1.0.0     
                - →·   CKt-ActivityMonitor           v0.1.0     
              1 ╓  ⊙   CKt-PerfectEvent              v0.3.2      → v0.3.3 🡡 (DependencyUpdate, CodeChange)             
                                                                             U CKt.ActivityMonitor: 0.1.1--ci.4 → 0.1.0 
                ║      CKt-Monitoring                v0.2.3     
              2 ╙      Samples/CKt-App-Sample        v0.0.0+fake → v0.0.1 🡡 (FakeVersion, DependencyUpdate, CodeChange)
                                                                             U CKt.ActivityMonitor: 0.1.1--ci.4 → 0.1.0 
              3 -  ·→  Samples/CKt-Sample-Monitoring v0.0.0+fake → v0.0.1 🡡 (UpstreamBuild, FakeVersion, CodeChange)   
              Required build for 3 from the 1 pivots out of 6 repositories.
              U 2 updates from upstreams (not using '*publish' here).
              🡡 3 repositories must be published.
              ❌ Failed
          
              """ );

        // Fix the bug: The "dev/" branch has been recreated and checked out on error.
        //              => This commit is on the "dev/" branch.
        FileHelper.DeleteFile( TestHelper.Monitor, inSampleMonitoring.CurrentDirectory.Combine( "CKt.Sample.Monitoring/Bug.cs" ) );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleMonitoring, "commit", "Removed bug." )).ShouldBeTrue();

        // Note the "FakeVersion" that replaced the previous "UpstreamBuild": the upstream is already build and its reference
        // has been updated (so that the developer can use the actual, up-to-date code in the reference): we are left with
        // the FakeVersion reason.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "publish", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe(
              """
                - →·   CKt-Core                      v1.0.0     
                - →·   CKt-ActivityMonitor           v0.1.0     
                ╓  ⊙   CKt-PerfectEvent              v0.3.3 🡡  
                ║      CKt-Monitoring                v0.2.3     
                ╙      Samples/CKt-App-Sample        v0.0.1 🡡  
              1 -  ·→  Samples/CKt-Sample-Monitoring v0.0.0+fake → v0.0.1 🡡 (FakeVersion, CodeChange)
              Required build for 1 from the 1 pivots out of 6 repositories.
              (No dependency updates other than the ones from the upstreams are needed.)
              🡡 3 repositories must be published.
              ❰✓❱
          
              """ );

        // Make a code change in CKt-App-Sample: this doesn't change anything.
        var inAppSample = context.ChangeDirectory( "Samples/CKt-App-Sample" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inAppSample, "checkout", "dev/stable" )).ShouldBeTrue();
        File.WriteAllText( inAppSample.CurrentDirectory.Combine( "CKt.SomeApp/DoMore.cs" ), "// More feature..." );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inAppSample, "commit", "Added feature." )).ShouldBeTrue();

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "publish", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe(
              """
                - →·   CKt-Core                      v1.0.0     
                - →·   CKt-ActivityMonitor           v0.1.0     
                ╓  ⊙   CKt-PerfectEvent              v0.3.3 🡡  
                ║      CKt-Monitoring                v0.2.3     
                ╙      Samples/CKt-App-Sample        v0.0.1 🡡  
              1 -  ·→  Samples/CKt-Sample-Monitoring v0.0.0+fake → v0.0.1 🡡 (FakeVersion, CodeChange)
              Required build for 1 from the 1 pivots out of 6 repositories.
              (No dependency updates other than the ones from the upstreams are needed.)
              🡡 3 repositories must be published.
              ❰✓❱
          
              """ );

        // Make a code change in CKt-ActivityMonitor: this doesn't change anything with "publish", but with "*publish"
        // everything except CKt-Core is impacted: it's a Feature, Minors are incremented.
        var inActivityMonitor = context.ChangeDirectory( "CKt-ActivityMonitor" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inActivityMonitor, "checkout", "dev/stable" )).ShouldBeTrue();
        File.WriteAllText( inActivityMonitor.CurrentDirectory.Combine( "CKt.ActivityMonitor/Touch.cs" ), "// Touched." );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inActivityMonitor, "commit", "feat: new feature in ActivityMonitor." )).ShouldBeTrue();

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "publish", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe(
              """
                - →·   CKt-Core                      v1.0.0     
                - →·   CKt-ActivityMonitor           v0.1.0     
                ╓  ⊙   CKt-PerfectEvent              v0.3.3 🡡  
                ║      CKt-Monitoring                v0.2.3     
                ╙      Samples/CKt-App-Sample        v0.0.1 🡡  
              1 -  ·→  Samples/CKt-Sample-Monitoring v0.0.0+fake → v0.0.1 🡡 (FakeVersion, CodeChange)
              Required build for 1 from the 1 pivots out of 6 repositories.
              (No dependency updates other than the ones from the upstreams are needed.)
              🡡 3 repositories must be published.
              ❰✓❱
          
              """ );

        // CKt-Core has a code change (from its v1.0.0) but this is only a fix that must be built and published (v1.0.1).
        // The others have an increased Minor because of the "feat:" in ActivityMonitor.
        // ==> We build and publish this state.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "*publish" )).ShouldBeTrue();
        display.ToString().ShouldBe(
              """
              1 - →·   CKt-Core                      v1.0.0      → v1.0.1 🡡 (CodeChange)                            
              2 - →·   CKt-ActivityMonitor           v0.1.0      → v0.2.0 🡡 (UpstreamBuild, CodeChange)             
              3 ╓  ⊙   CKt-PerfectEvent              v0.3.3      → v0.4.0 🡡 (UpstreamBuild)                         
              4 ║      CKt-Monitoring                v0.2.3      → v0.3.0 🡡 (UpstreamBuild, CodeChange)             
              5 ╙      Samples/CKt-App-Sample        v0.0.1      → v0.1.0 🡡 (UpstreamBuild, CodeChange)             
              6 -  ·→  Samples/CKt-Sample-Monitoring v0.0.0+fake → v0.1.0 🡡 (UpstreamBuild, FakeVersion, CodeChange)
              Required build for 6 from the 1 pivots out of 6 repositories.
              (No dependency updates other than the ones from the upstreams are needed.)
              🡡 6 repositories must be published.
              ❰✓❱
          
              """ );

        // We don't have anymore +fake. Now tests the intermediate error on fully published repositories.

        // Make a MAJOR code change in CKt-PerfectEvent.
        // Because CKt-PerfectEvent is in 0.X.Y version, only the minor is incremented (SemVer rule).
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "checkout", "dev/stable" )).ShouldBeTrue();
        File.WriteAllText( inPerfectEvent.CurrentDirectory.Combine( "CKt.PerfectEvent/SomethingBig.cs" ), "// BIG" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "commit", "feat!: Big one." )).ShouldBeTrue();

        // Recreate the bug in CKt-Sample-Monitoring.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleMonitoring, "checkout", "dev/stable" )).ShouldBeTrue();
        File.WriteAllText( inSampleMonitoring.CurrentDirectory.Combine( "CKt.Sample.Monitoring/Bug.cs" ), "I'm not a valid C# file at all." );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleMonitoring, "commit", "Added bug file (2)!" )).ShouldBeTrue();

        // CKt-PerfectEvent impacts CKt-Sample-Monitoring but its build fails.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "publish", "-d" )).ShouldBeTrue();
        display.ToString().ShouldBe(
              """
                - →·   CKt-Core                      v1.0.1
                - →·   CKt-ActivityMonitor           v0.2.0
              1 ╓  ⊙   CKt-PerfectEvent              v0.4.0 → v0.5.0 🡡 (CodeChange)               
                ║      CKt-Monitoring                v0.3.0
                ╙      Samples/CKt-App-Sample        v0.1.0
              2 -  ·→  Samples/CKt-Sample-Monitoring v0.1.0 → v0.2.0 🡡 (UpstreamBuild, CodeChange)
              Required build for 2 from the 1 pivots out of 6 repositories.
              (No dependency updates other than the ones from the upstreams are needed.)
              🡡 2 repositories must be published.
              ❰✓❱
          
              """ );

        // Fix the bug (the "dev/" branch is checked out on error).
        FileHelper.DeleteFile( TestHelper.Monitor, inSampleMonitoring.CurrentDirectory.Combine( "CKt.Sample.Monitoring/Bug.cs" ) );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleMonitoring, "commit", "Removed bug (2)." )).ShouldBeTrue();

        // Everything now works.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "publish" )).ShouldBeTrue();
        display.ToString().ShouldBe(
              """
                - →·   CKt-Core                      v1.0.1
                - →·   CKt-ActivityMonitor           v0.2.0
              1 ╓  ⊙   CKt-PerfectEvent              v0.4.0 → v0.5.0 🡡 (CodeChange)   
                ║      CKt-Monitoring                v0.3.0
                ╙      Samples/CKt-App-Sample        v0.1.0
              2 -  ·→  Samples/CKt-Sample-Monitoring v0.1.0 → v0.2.0 🡡 (UpstreamBuild)
              Required build for 2 from the 1 pivots out of 6 repositories.
              (No dependency updates other than the ones from the upstreams are needed.)
              🡡 2 repositories must be published.
              ❰✓❱
          
              """ );

        // Nothing to do.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "publish" )).ShouldBeTrue();
        display.ToString().ShouldBe(
              """
              - →·   CKt-Core                      v1.0.1
              - →·   CKt-ActivityMonitor           v0.2.0
              ╓  ⊙   CKt-PerfectEvent              v0.5.0
              ║      CKt-Monitoring                v0.3.0
              ╙      Samples/CKt-App-Sample        v0.1.0
              -  ·→  Samples/CKt-Sample-Monitoring v0.2.0
              There is nothing to build from the 1 pivots out of 6 repositories.
              (Using '*publish' may detect required builds in upstreams repositories.)
              Nothing to publish (the 6 repositories are already published)
              ❰✓❱
          
              """ );

    }

    [Test]
    public async Task intermediate_ci_build_error_Async()
    {
        Helper.SetFileSystemWritePAT();

        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(with_sample)" );
        var context = remotes.Clone( clonedFolder, Helper.ConfigureFakeFeeds );
        var display = (StringScreen)context.Screen;

        // Same context as CKt_publish_PerfectEvent_Async test above...
        //
        // ... except that we inject an error in the CKt-Sample-Monitoring build
        //     so that during the 2nd build, the CKt-PerfectEvent is already available
        //     in v0.3.3: the fact that it must be updated in CKt-Sample-Monitoring must be
        //     detected and CKt-Sample-Monitoring must be built (v0.0.0 → v0.0.1).
        //
        // And because the initial state is fully ci built, we need to inject a code change in the
        // CKt-PerfectEvent to trigger its build.
        //
        var inPerfectEvent = context.ChangeDirectory( "CKt-PerfectEvent" );
        File.WriteAllText( inPerfectEvent.CurrentDirectory.Combine( "Touch.txt" ), "Touching CKt-PerfectEvent." );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "commit", "Touching CKt-PerfectEvent." )).ShouldBeTrue();

        var inSampleMonitoring = context.ChangeDirectory( "Samples/CKt-Sample-Monitoring" );
        File.WriteAllText( inSampleMonitoring.CurrentDirectory.Combine( "CKt.Sample.Monitoring/Bug.cs" ), "I'm not a valid C# file at all." );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleMonitoring, "commit", "Added bug file." )).ShouldBeTrue();

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "ci", "publish" )).ShouldBeFalse();
        display.ToString().ShouldBe(
              """
                - →·   CKt-Core                      v1.0.1--ci.3
                - →·   CKt-ActivityMonitor           v0.1.1--ci.4
              1 ╓  ⊙   CKt-PerfectEvent              v0.3.3--ci.4 → v0.3.3--ci.5 🡡 (CodeChange)               
                ║      CKt-Monitoring                v0.2.4--ci.4
                ╙      Samples/CKt-App-Sample        v0.0.1--ci.3
              2 -  ·→  Samples/CKt-Sample-Monitoring v0.0.1--ci.3 → v0.0.1--ci.5 🡡 (UpstreamBuild, CodeChange)
              Required build for 2 from the 1 pivots out of 6 repositories.
              (No dependency updates other than the ones from the upstreams are needed.)
              🡡 2 repositories must be published.
              ❌ Failed
          
              """ );

        // Fix the bug (just to be coherent: since we --dry-run, this is useless).
        FileHelper.DeleteFile( TestHelper.Monitor, inSampleMonitoring.CurrentDirectory.Combine( "CKt.Sample.Monitoring/Bug.cs" ) );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleMonitoring, "commit", "Removed bug." )).ShouldBeTrue();

        // Note the "UpstreamVersion" that replaced the previous "UpstreamBuild".
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "ci", "publish", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe(
              """
                - →·   CKt-Core                      v1.0.1--ci.3   
                - →·   CKt-ActivityMonitor           v0.1.1--ci.4   
                ╓  ⊙   CKt-PerfectEvent              v0.3.3--ci.5 🡡
                ║      CKt-Monitoring                v0.2.4--ci.4   
                ╙      Samples/CKt-App-Sample        v0.0.1--ci.3   
              1 -  ·→  Samples/CKt-Sample-Monitoring v0.0.1--ci.3    → v0.0.1--ci.6 🡡 (UpstreamVersion, CodeChange)
              Required build for 1 from the 1 pivots out of 6 repositories.
              (No dependency updates other than the ones from the upstreams are needed.)
              🡡 2 repositories must be published.
              ❰✓❱
          
              """ );

        // Make a code change in CKt-App-Sample: this doesn't change anything.
        var inAppSample = context.ChangeDirectory( "Samples/CKt-App-Sample" );
        File.WriteAllText( inAppSample.CurrentDirectory.Combine( "CKt.SomeApp/DoMore.cs" ), "// More feature..." );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inAppSample, "commit", "Added feature." )).ShouldBeTrue();

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "ci", "publish", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe(
              """
                - →·   CKt-Core                      v1.0.1--ci.3   
                - →·   CKt-ActivityMonitor           v0.1.1--ci.4   
                ╓  ⊙   CKt-PerfectEvent              v0.3.3--ci.5 🡡
                ║      CKt-Monitoring                v0.2.4--ci.4   
                ╙      Samples/CKt-App-Sample        v0.0.1--ci.3   
              1 -  ·→  Samples/CKt-Sample-Monitoring v0.0.1--ci.3    → v0.0.1--ci.6 🡡 (UpstreamVersion, CodeChange)
              Required build for 1 from the 1 pivots out of 6 repositories.
              (No dependency updates other than the ones from the upstreams are needed.)
              🡡 2 repositories must be published.
              ❰✓❱
          
              """ );

        // Make a code change in CKt-ActivityMonitor: this doesn't change anything with "publish", but with "*publish"
        // everything except CKt-Core is impacted.
        var inActivityMonitor = context.ChangeDirectory( "CKt-ActivityMonitor" );
        File.WriteAllText( inActivityMonitor.CurrentDirectory.Combine( "CKt.ActivityMonitor/Touch.cs" ), "// Touched." );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inActivityMonitor, "commit", "Touched." )).ShouldBeTrue();

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "ci", "publish", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe(
              """
                - →·   CKt-Core                      v1.0.1--ci.3   
                - →·   CKt-ActivityMonitor           v0.1.1--ci.4   
                ╓  ⊙   CKt-PerfectEvent              v0.3.3--ci.5 🡡
                ║      CKt-Monitoring                v0.2.4--ci.4   
                ╙      Samples/CKt-App-Sample        v0.0.1--ci.3   
              1 -  ·→  Samples/CKt-Sample-Monitoring v0.0.1--ci.3    → v0.0.1--ci.6 🡡 (UpstreamVersion, CodeChange)
              Required build for 1 from the 1 pivots out of 6 repositories.
              (No dependency updates other than the ones from the upstreams are needed.)
              🡡 2 repositories must be published.
              ❰✓❱
          
              """ );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "ci", "*publish", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe(
              """
                - →·   CKt-Core                      v1.0.1--ci.3
              1 - →·   CKt-ActivityMonitor           v0.1.1--ci.4 → v0.1.1--ci.5 🡡 (CodeChange)               
              2 ╓  ⊙   CKt-PerfectEvent              v0.3.3--ci.5 → v0.3.3--ci.6 🡡 (UpstreamBuild)            
              3 ║      CKt-Monitoring                v0.2.4--ci.4 → v0.2.4--ci.5 🡡 (UpstreamBuild)            
              4 ╙      Samples/CKt-App-Sample        v0.0.1--ci.3 → v0.0.1--ci.5 🡡 (UpstreamBuild, CodeChange)
              5 -  ·→  Samples/CKt-Sample-Monitoring v0.0.1--ci.3 → v0.0.1--ci.7 🡡 (UpstreamBuild, CodeChange)
              Required build for 5 from the 1 pivots out of 6 repositories.
              (No dependency updates other than the ones from the upstreams are needed.)
              🡡 5 repositories must be published.
              ❰✓❱
          
              """ );

    }


    [TestCase( "NonPackableSample" )]
    [TestCase( "SampleIsPackable" )]
    public async Task CKt_publish_with_sample_Async( string mode )
    {
        var remotes = TestHelper.OpenRemotes( "CKt(with_sample)" );

        var nonPackableSample = mode == "NonPackableSample";
        var clonedFolder = TestHelper.InitializeClonedFolder( $"CKt_publish_PerfectEvent-{mode}" );
        var context = remotes.Clone( clonedFolder, Helper.ConfigureFakeFeeds );
        var display = (StringScreen)context.Screen;

        // From CKt-PerfectEvent (the NuGet.config has been renamed to nuget.config).
        // The CKt-Monitoring and App sample are "nothing".
        // CKt-Sample-Monitoring is a downstream repo that must be built.
        // We inject <IsPackable>false</IsPackable> in CKt.Sample.Monitoring.csproj in "NonPackableSample" mode.
        var inPerfectEvent = context.ChangeDirectory( "CKt-PerfectEvent" );

        if( nonPackableSample )
        {
            NormalizedPath path = context.CurrentDirectory.Combine( "Samples/CKt-Sample-Monitoring/CKt.Sample.Monitoring/CKt.Sample.Monitoring.csproj" );
            var doc = XDocument.Load( path );
            doc.Root!.AddFirst( new XElement( "PropertyGroup", new XElement( "IsPackable", false ) ) );
            XmlHelper.SaveWithoutXmlDeclaration( doc, path );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "commit", "Make Sample project not packable." )).ShouldBeTrue();
            display.Clear();
        }

        // To be able to push to the FakeFeeds.
        Helper.SetFileSystemWritePAT();

        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "publish" )).ShouldBeTrue();

        display.ToString().ShouldBe(
              """
                - →·   CKt-Core                      v1.0.0     
                - →·   CKt-ActivityMonitor           v0.1.0     
              1 ╓  ⊙   CKt-PerfectEvent              v0.3.2      → v0.3.3 🡡 (DependencyUpdate, CodeChange)             
                                                                             U CKt.ActivityMonitor: 0.1.1--ci.4 → 0.1.0 
                ║      CKt-Monitoring                v0.2.3     
              2 ╙      Samples/CKt-App-Sample        v0.0.0+fake → v0.0.1 🡡 (FakeVersion, DependencyUpdate, CodeChange)
                                                                             U CKt.ActivityMonitor: 0.1.1--ci.4 → 0.1.0 
              3 -  ·→  Samples/CKt-Sample-Monitoring v0.0.0+fake → v0.0.1 🡡 (UpstreamBuild, FakeVersion, CodeChange)   
              Required build for 3 from the 1 pivots out of 6 repositories.
              U 2 updates from upstreams (not using '*publish' here).
              🡡 3 repositories must be published.
              ❰✓❱
          
              """ );

        var (nugetOrgFeed, sosFeed) = Helper.GetFakeFeedPaths( clonedFolder.Path );

        Directory.Exists( nugetOrgFeed.AppendPart( "ckt.perfectevent" ) ).ShouldBeTrue();
        Directory.Exists( sosFeed.AppendPart( "ckt.perfectevent" ) ).ShouldBeTrue();

        Directory.Exists( nugetOrgFeed.AppendPart( "ckt.sample.monitoring" ) ).ShouldBe( !nonPackableSample );
        Directory.Exists( sosFeed.AppendPart( "ckt.sample.monitoring" ) ).ShouldBe( !nonPackableSample );

    }

    [Explicit]
    [Test]
    public async Task REMOTES_CKt_with_sample_to_sample_published_Async()
    {
        FileHelper.DeleteFolder( TestHelper.Monitor, TestHelper.CKliRemotesPath.AppendPart( "CKt(sample_published)" ) );
        await CKt_publish_with_sample_Async( "NonPackableSample" );
        TestHelper.CKliCreateRemoteFolderFromCloned( "CKt_publish_PerfectEvent-NonPackableSample", "CKt", "(sample_published)" );
    }

}
