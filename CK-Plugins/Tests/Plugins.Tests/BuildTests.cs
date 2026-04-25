using CK.Core;
using CKli;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CSemVer;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;


    [TestFixture]
public class BuildTests
{
    [Test]
    public async Task CKt_add_sample_and_ci_Async()
    {
        Helper.SetFileSystemWritePAT();
        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(initialized)" );
        var context = remotes.Clone( clonedFolder, ConfigureFakeFeeds );
        var display = (StringScreen)context.Screen;

        var inSampleFolder = context.ChangeDirectory( "Samples" );

        var newRepo1 = TestHelper.CKliRemotesPath.AppendPart( "bare" ).Combine( "CKt(initialized)/CKt-Sample-Monitoring" );
        var newRepoUrl1 = $"file://{newRepo1}";
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleFolder, "repo", "create", newRepoUrl1 )).ShouldBeTrue();

        var newRepo2 = TestHelper.CKliRemotesPath.AppendPart( "bare" ).Combine( "CKt(initialized)/CKt-App-Sample" );
        var newRepoUrl2 = $"file://{newRepo2}";
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleFolder, "repo", "create", newRepoUrl2 )).ShouldBeTrue();

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            > Samples/CKt-Sample-Monitoring (1)
            │ > Missing root branch 'stable'.
            │ │ Can be fixed by creating it from 'master'.
            > Samples/CKt-App-Sample (1)
            │ > Missing root branch 'stable'.
            │ │ Can be fixed by creating it from 'master'.
            ❰✓❱

            """ );
        // This one can be fixed with a dirty folder (no need to commit). 
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleFolder, "issue", "--fix" )).ShouldBeTrue();
        display.Clear();

        #region Initializing Samples/CKt-Sample-Monitoring
        {
            var inSampleMonitoring = inSampleFolder.ChangeDirectory( "CKt-Sample-Monitoring" );
            Directory.Exists( inSampleMonitoring.CurrentDirectory ).ShouldBeTrue();

            (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleMonitoring, "checkout", "dev/stable" )).ShouldBeTrue();

            var path = inSampleMonitoring.CurrentDirectory.AppendPart( "CKt.Sample.Monitoring" );
            Directory.CreateDirectory( path );
            File.WriteAllText( path.AppendPart( "CKt.Sample.Monitoring.csproj" ), """
                <Project Sdk="Microsoft.NET.Sdk">

                    <PropertyGroup>
                        <TargetFramework>net8.0</TargetFramework>
                        <Nullable>enable</Nullable>
                        <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
                    </PropertyGroup>

                    <ItemGroup>
                        <PackageReference Include="CKt.Monitoring" Version="0.2.3" />
                        <PackageReference Include="CKt.PerfectEvent" Version="0.3.2" />
                    </ItemGroup>

                </Project>

                """ );
            File.WriteAllText( path.AppendPart( "PreserveAssemblyReference.cs" ), """
                using System;

                namespace CKt.Sample.Monitoring;

                public record PreserveAssemblyReference( CKt.Monitoring.PreserveAssemblyReference Monitoring,
                                                         CKt.PerfectEvent.PreserveAssemblyReference PerfectEvent );

                """ );

            (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleMonitoring, "exec", "dotnet", "new", "sln" )).ShouldBeTrue();
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleMonitoring, "exec", "dotnet", "sln", "add", "CKt.Sample.Monitoring/CKt.Sample.Monitoring.csproj" )).ShouldBeTrue();

            var deployFolder = inSampleMonitoring.CurrentDirectory.AppendPart( ArtifactHandlerPlugin.DeployFolderName );
            Directory.CreateDirectory( deployFolder );
            File.WriteAllText( deployFolder.AppendPart( "GenerateAssets.cs" ), """
                #:property PublishAot=false
                File.WriteAllText( $"Assets/Install-{args[0]}.txt", $"I'm the install manual of CKt-Sample-Monitoring version '{args[0]}'." );
                """ );
            File.WriteAllText( deployFolder.AppendPart( ".gitignore" ), "Assets/" );
        }
        #endregion

        #region Initializing Samples/CKt-App-Sample
        {
            var inSampleApp = inSampleFolder.ChangeDirectory( "CKt-App-Sample" );
            Directory.Exists( inSampleApp.CurrentDirectory ).ShouldBeTrue();

            (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleApp, "checkout", "dev/stable" )).ShouldBeTrue();

            var path = inSampleApp.CurrentDirectory.AppendPart( "CKt.SomeApp" );
            Directory.CreateDirectory( path );
            File.WriteAllText( path.AppendPart( "CKt.SomeApp.csproj" ), """
            <Project Sdk="Microsoft.NET.Sdk">

                <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <Nullable>enable</Nullable>
                    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
                </PropertyGroup>

                <ItemGroup>
                    <PackageReference Include="CKt.ActivityMonitor" Version="0.1.0" />
                </ItemGroup>

            </Project>

            """ );
            File.WriteAllText( path.AppendPart( "PreserveAssemblyReference.cs" ), """
            using System;

            namespace CKt.SomeApp;

            public record PreserveAssemblyReference( CKt.ActivityMonitor.PreserveAssemblyReference ActivityMonitor );

            """ );

            (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleApp, "exec", "dotnet", "new", "sln" )).ShouldBeTrue();
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleApp, "exec", "dotnet", "sln", "add", "CKt.SomeApp/CKt.SomeApp.csproj" )).ShouldBeTrue();

            var deployFolder = inSampleApp.CurrentDirectory.AppendPart( ArtifactHandlerPlugin.DeployFolderName );
            Directory.CreateDirectory( deployFolder );
            File.WriteAllText( deployFolder.AppendPart( "GenerateAssets.cs" ), """
                #:property PublishAot=false
                Directory.CreateDirectory( "Assets/ZipDemo" );
                File.WriteAllText( $"Assets/ZipDemo/Install-{args[0]}.txt", "I'm the install manual of CKt.SomeApp version '{args[0]}'." );
                File.WriteAllText( $"Assets/ZipDemo/AnotherFile.txt", "Another file..." );

                """ );
            File.WriteAllText( deployFolder.AppendPart( ".gitignore" ), "Assets/" );
        }
        #endregion


        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "checkout", "dev/stable" )).ShouldBeTrue();

        // The nuget.config can be fixed with a dirty folder (no need to pre-commit here).
        //
        // But the "Missing initial version." requires a clean working folder.
        // 
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            > Samples/CKt-Sample-Monitoring (2)
            │ > Content issues.
            │ │ Branch: stable (1 content issue)
            │ │ > File 'nuget.config' must be created.
            │ > Missing initial version.
            │ │ This can be fixed by building the 'v0.0.0' version from 'stable' branch.
            > Samples/CKt-App-Sample (2)
            │ > Content issues.
            │ │ Branch: stable (1 content issue)
            │ │ > File 'nuget.config' must be created.
            │ > Missing initial version.
            │ │ This can be fixed by building the 'v0.0.0' version from 'stable' branch.
            ❰✓❱

            """ );
        // ... so we commit.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleFolder, "commit", "Initialized files." )).ShouldBeTrue();

        // This created the missing nuget.config file: this is the work of the CommonFiles plugin
        // and the BranchModel/HotBranch/ContentIssue.
        //
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue", "--fix" )).ShouldBeTrue();

        // No more issue.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            ❰✓❱

            """ );

        // Let's build (but not publish yet) the CI versions.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "ci", "build" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 -  CKt-Core                      v1.0.0 → v1.0.1--ci.4 🡡 (CodeChange)          
            2 -  CKt-ActivityMonitor           v0.1.0 → v0.1.1--ci.5 🡡 (Upstream, CodeChange)
            3 ╓  CKt-PerfectEvent              v0.3.2 → v0.3.3--ci.5 🡡 (Upstream, CodeChange)
            4 ║  CKt-Monitoring                v0.2.3 → v0.2.4--ci.5 🡡 (Upstream, CodeChange)
            5 ╙  Samples/CKt-App-Sample        v0.0.0 → v0.0.1--ci.1 🡡 (Upstream)            
            6 -  Samples/CKt-Sample-Monitoring v0.0.0 → v0.0.1--ci.1 🡡 (Upstream)            
            Required build for 6 repositories across the 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            🡡 6 repositories can be published.
            ❰✓❱

            """ );

        // Everything has been built but nothing has been published.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "ci", "build" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            -  CKt-Core                      v1.0.1--ci.4 🡡
            -  CKt-ActivityMonitor           v0.1.1--ci.5 🡡
            ╓  CKt-PerfectEvent              v0.3.3--ci.5 🡡
            ║  CKt-Monitoring                v0.2.4--ci.5 🡡
            ╙  Samples/CKt-App-Sample        v0.0.1--ci.1 🡡
            -  Samples/CKt-Sample-Monitoring v0.0.1--ci.1 🡡
            There is nothing to build across the 6 repositories.
            🡡 6 repositories can be published.
            ❰✓❱
            
            """ );


        // Now we publish.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "ci", "publish" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            -  CKt-Core                      v1.0.1--ci.4 🡡
            -  CKt-ActivityMonitor           v0.1.1--ci.5 🡡
            ╓  CKt-PerfectEvent              v0.3.3--ci.5 🡡
            ║  CKt-Monitoring                v0.2.4--ci.5 🡡
            ╙  Samples/CKt-App-Sample        v0.0.1--ci.1 🡡
            -  Samples/CKt-Sample-Monitoring v0.0.1--ci.1 🡡
            There is nothing to build across the 6 repositories.
            🡡 6 repositories must be published.
            ❰✓❱
            
            """ );

        var (nugetOrgFeed, sosFeed) = GetFakeFeedPaths( clonedFolder );

        // CI build: nuget.org is not concerned.
        Directory.GetDirectories( nugetOrgFeed ).Select( p => Path.GetFileName( p ) ).ShouldBe( ["ck.canarypackage"] );

        // The other feed have the packages.
        var existingPackages = Directory.GetDirectories( sosFeed )
                                        .SelectMany( p => Directory.GetDirectories( p )
                                                                   .Select( pp => new PackageInstance( Path.GetFileName( p ),
                                                                                                       SVersion.Parse( Path.GetFileName( pp ) ) ) ) );
        existingPackages.Select( p => p.ToString() )
                        .ShouldBe( ["ck.canarypackage@1.0.0",
                                    "ckt.activitymonitor@0.1.1--ci.5",
                                    "ckt.core@1.0.1--ci.4",
                                    "ckt.monitoring@0.2.4--ci.5",
                                    "ckt.perfectevent@0.3.3--ci.5",
                                    "ckt.sample.monitoring@0.0.1--ci.1",
                                    "ckt.someapp@0.0.1--ci.1"], ignoreOrder: true );

        // The FileSystemHostingProvider received the asset files.
        var appRemoteReleases = Path.Combine( TestHelper.CKliRemotesPath, "bare", remotes.FullName, "CKt-App-Sample", "Releases" );
        Directory.GetFiles( appRemoteReleases, "*", SearchOption.AllDirectories )
                 .Select( p => new NormalizedPath( p ) )
                 .Select( p => p.RemoveParts( 0, p.Parts.Count - 2 ).ToString() )
                 .ShouldBe( ["v0.0.1--ci.1/ZipDemo.zip"] );

        var sampleRemoteReleases = Path.Combine( TestHelper.CKliRemotesPath, "bare", remotes.FullName, "CKt-Sample-Monitoring", "Releases" );
        Directory.GetFiles( sampleRemoteReleases, "*", SearchOption.AllDirectories )
                 .Select( p => new NormalizedPath( p ) )
                 .Select( p => p.RemoveParts( 0, p.Parts.Count - 2 ).ToString() )
                 .ShouldBe( ["v0.0.1--ci.1/Install-0.0.1--ci.1.txt"] );

        // The "PublishState.bin" has been removed.
        File.Exists( context.CurrentStackPath.Combine( "$Local/PublishState.bin" ) ).ShouldBeFalse();

        // No issue.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            ❰✓❱

            """ );
    }

    [Explicit]
    [Test]
    public async Task REMOTES_CKt_add_sample_to_with_sample_Async()
    {
        FileHelper.DeleteFolder( TestHelper.Monitor, TestHelper.CKliRemotesPath.AppendPart( "CKt(with_sample)" ) );
        await CKt_add_sample_and_ci_Async();
        TestHelper.CKliCreateRemoteFolderFromCloned( "CKt_add_sample_and_ci_Async", "CKt", "(with_sample)" );
    }

    [Test]
    public async Task CKt_with_sample_ci_build_and_жbuild_Async()
    {
        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(with_sample)" );
        var context = remotes.Clone( clonedFolder );
        var display = (StringScreen)context.Screen;

        // From stack root (or if --all is specified): all solutions are pivots <==> none of them is.
        // (in this case *build is the same as build).
        // The CKt(with_sample) has been "ckli ci publish": there's nothing to build and noting to publish.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "ci", "publish", "--branch", "stable", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
        -  CKt-Core                      v1.0.1--ci.4
        -  CKt-ActivityMonitor           v0.1.1--ci.5
        ╓  CKt-PerfectEvent              v0.3.3--ci.5
        ║  CKt-Monitoring                v0.2.4--ci.5
        ╙  Samples/CKt-App-Sample        v0.0.1--ci.1
        -  Samples/CKt-Sample-Monitoring v0.0.1--ci.1
        There is nothing to build across the 6 repositories.
        Nothing to publish (the 6 repositories are already published)
        ❰✓❱
        
        """ );

        // If we "ckli publish" (that is the same as "ckli *publish" here), the 6 repositories must be published.  
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "publish", "--branch", "stable", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
        1 -  CKt-Core                      v1.0.0 → v1.0.1 🡡 (CodeChange)          
        2 -  CKt-ActivityMonitor           v0.1.0 → v0.1.1 🡡 (Upstream, CodeChange)
        3 ╓  CKt-PerfectEvent              v0.3.2 → v0.3.3 🡡 (Upstream, CodeChange)
        4 ║  CKt-Monitoring                v0.2.3 → v0.2.4 🡡 (Upstream, CodeChange)
        5 ╙  Samples/CKt-App-Sample        v0.0.0 → v0.0.1 🡡 (Upstream, CodeChange)
        6 -  Samples/CKt-Sample-Monitoring v0.0.0 → v0.0.1 🡡 (Upstream, CodeChange)
        Required build for 6 repositories across the 6 repositories.
        (No dependency updates other than the ones from the upstreams are needed.)
        🡡 6 repositories must be published.
        ❰✓❱
        
        """ );

        // To test the "ci build" and "ci *build", we touch the CKt-Core and CKt-PerfectEvent repositories.
        await Helper.TouchProjectAndCommitAsync( context, "CKt-Core/CKt.Core/CKt.Core.csproj" );
        await Helper.TouchProjectAndCommitAsync( context, "CKt-PerfectEvent/CKt.PerfectEvent/CKt.PerfectEvent.csproj" );

        #region ci build
        {
            // From stack root (or if --all is specified): all solutions are pivots <==> none of them is.
            // (in this case *build is the same as build).
            // Since CKt-Core is dirty, it implies the 5 other ones.
            display.Clear();
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "ci", "build", "--branch", "stable", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            1 -  CKt-Core                      v1.0.1--ci.4 → v1.0.1--ci.5 🡡 (CodeChange)          
            2 -  CKt-ActivityMonitor           v0.1.1--ci.5 → v0.1.1--ci.6 🡡 (Upstream)            
            3 ╓  CKt-PerfectEvent              v0.3.3--ci.5 → v0.3.3--ci.7 🡡 (Upstream, CodeChange)
            4 ║  CKt-Monitoring                v0.2.4--ci.5 → v0.2.4--ci.6 🡡 (Upstream)            
            5 ╙  Samples/CKt-App-Sample        v0.0.1--ci.1 → v0.0.1--ci.2 🡡 (Upstream)            
            6 -  Samples/CKt-Sample-Monitoring v0.0.1--ci.1 → v0.0.1--ci.2 🡡 (Upstream)            
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
            - →·   CKt-Core                      v1.0.1--ci.4
            - →·   CKt-ActivityMonitor           v0.1.1--ci.5
            ╓ →·   CKt-PerfectEvent              v0.3.3--ci.5
            ║ →·   CKt-Monitoring                v0.2.4--ci.5
            ╙  ⊙   Samples/CKt-App-Sample        v0.0.1--ci.1
            -  ⊙   Samples/CKt-Sample-Monitoring v0.0.1--ci.1
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
            - →·   CKt-Core                      v1.0.1--ci.4
            - →·   CKt-ActivityMonitor           v0.1.1--ci.5
            ╓ →·   CKt-PerfectEvent              v0.3.3--ci.5
            ║ →·   CKt-Monitoring                v0.2.4--ci.5
            ╙      Samples/CKt-App-Sample        v0.0.1--ci.1
            -  ⊙   Samples/CKt-Sample-Monitoring v0.0.1--ci.1
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
            - →·   CKt-Core                      v1.0.1--ci.4
            - →·   CKt-ActivityMonitor           v0.1.1--ci.5
            ╓      CKt-PerfectEvent              v0.3.3--ci.5
            ║      CKt-Monitoring                v0.2.4--ci.5
            ╙  ⊙   Samples/CKt-App-Sample        v0.0.1--ci.1
            -      Samples/CKt-Sample-Monitoring v0.0.1--ci.1
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
              - →·   CKt-Core                      v1.0.1--ci.4
              - →·   CKt-ActivityMonitor           v0.1.1--ci.5
            1 ╓  ⊙   CKt-PerfectEvent              v0.3.3--ci.5 → v0.3.3--ci.6 🡡 (CodeChange)
              ║      CKt-Monitoring                v0.2.4--ci.5
              ╙      Samples/CKt-App-Sample        v0.0.1--ci.1
            2 -  ·→  Samples/CKt-Sample-Monitoring v0.0.1--ci.1 → v0.0.1--ci.2 🡡 (Upstream)  
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
            1 -  CKt-Core                      v1.0.1--ci.4 → v1.0.1--ci.5 🡡 (CodeChange)          
            2 -  CKt-ActivityMonitor           v0.1.1--ci.5 → v0.1.1--ci.6 🡡 (Upstream)            
            3 ╓  CKt-PerfectEvent              v0.3.3--ci.5 → v0.3.3--ci.7 🡡 (Upstream, CodeChange)
            4 ║  CKt-Monitoring                v0.2.4--ci.5 → v0.2.4--ci.6 🡡 (Upstream)            
            5 ╙  Samples/CKt-App-Sample        v0.0.1--ci.1 → v0.0.1--ci.2 🡡 (Upstream)            
            6 -  Samples/CKt-Sample-Monitoring v0.0.1--ci.1 → v0.0.1--ci.2 🡡 (Upstream)            
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
            1 - →·   CKt-Core                      v1.0.1--ci.4 → v1.0.1--ci.5 🡡 (CodeChange)          
            2 - →·   CKt-ActivityMonitor           v0.1.1--ci.5 → v0.1.1--ci.6 🡡 (Upstream)            
            3 ╓ →·   CKt-PerfectEvent              v0.3.3--ci.5 → v0.3.3--ci.7 🡡 (Upstream, CodeChange)
            4 ║ →·   CKt-Monitoring                v0.2.4--ci.5 → v0.2.4--ci.6 🡡 (Upstream)            
            5 ╙  ⊙   Samples/CKt-App-Sample        v0.0.1--ci.1 → v0.0.1--ci.2 🡡 (Upstream)            
            6 -  ⊙   Samples/CKt-Sample-Monitoring v0.0.1--ci.1 → v0.0.1--ci.2 🡡 (Upstream)            
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
            1 - →·   CKt-Core                      v1.0.1--ci.4 → v1.0.1--ci.5 🡡 (CodeChange)          
            2 - →·   CKt-ActivityMonitor           v0.1.1--ci.5 → v0.1.1--ci.6 🡡 (Upstream)            
            3 ╓ →·   CKt-PerfectEvent              v0.3.3--ci.5 → v0.3.3--ci.7 🡡 (Upstream, CodeChange)
            4 ║ →·   CKt-Monitoring                v0.2.4--ci.5 → v0.2.4--ci.6 🡡 (Upstream)            
            5 ╙      Samples/CKt-App-Sample        v0.0.1--ci.1 → v0.0.1--ci.2 🡡 (Upstream)            
            6 -  ⊙   Samples/CKt-Sample-Monitoring v0.0.1--ci.1 → v0.0.1--ci.2 🡡 (Upstream)            
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
            1 - →·   CKt-Core                      v1.0.1--ci.4 → v1.0.1--ci.5 🡡 (CodeChange)          
            2 - →·   CKt-ActivityMonitor           v0.1.1--ci.5 → v0.1.1--ci.6 🡡 (Upstream)            
            3 ╓      CKt-PerfectEvent              v0.3.3--ci.5 → v0.3.3--ci.7 🡡 (Upstream, CodeChange)
            4 ║      CKt-Monitoring                v0.2.4--ci.5 → v0.2.4--ci.6 🡡 (Upstream)            
            5 ╙  ⊙   Samples/CKt-App-Sample        v0.0.1--ci.1 → v0.0.1--ci.2 🡡 (Upstream)            
            6 -      Samples/CKt-Sample-Monitoring v0.0.1--ci.1 → v0.0.1--ci.2 🡡 (Upstream)            
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
            1 - →·   CKt-Core                      v1.0.1--ci.4 → v1.0.1--ci.5 🡡 (CodeChange)          
            2 - →·   CKt-ActivityMonitor           v0.1.1--ci.5 → v0.1.1--ci.6 🡡 (Upstream)            
            3 ╓  ⊙   CKt-PerfectEvent              v0.3.3--ci.5 → v0.3.3--ci.7 🡡 (Upstream, CodeChange)
            4 ║      CKt-Monitoring                v0.2.4--ci.5 → v0.2.4--ci.6 🡡 (Upstream)            
            5 ╙      Samples/CKt-App-Sample        v0.0.1--ci.1 → v0.0.1--ci.2 🡡 (Upstream)            
            6 -  ·→  Samples/CKt-Sample-Monitoring v0.0.1--ci.1 → v0.0.1--ci.2 🡡 (Upstream)            
            Required build for 6 from the 1 pivots out of 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            🡡 6 repositories can be published.
            ❰✓❱
            
            """ );
        }
        #endregion

    }

    [TestCase( "NonPackableSample" )]
    [TestCase( "SampleIsPackable" )]
    public async Task CKt_publish_PerfectEvent_Async( string mode )
    {
        var nonPackableSample = mode == "NonPackableSample";
        var clonedFolder = TestHelper.InitializeClonedFolder( $"CKt_publish_PerfectEvent-{mode}" );
        var remotes = TestHelper.OpenRemotes( "CKt(with_sample)" );
        var context = remotes.Clone( clonedFolder, ConfigureFakeFeeds );
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
              1 ╓  ⊙   CKt-PerfectEvent              v0.3.2    → v0.3.3 🡡 (DependencyUpdate, CodeChange)            
                                                                           U CKt.ActivityMonitor: 0.1.1--ci.5 → 0.1.0
                ║      CKt-Monitoring                v0.2.3   
                ╙      Samples/CKt-App-Sample        v0.0.0 🡡
              2 -  ·→  Samples/CKt-Sample-Monitoring v0.0.0    → v0.0.1 🡡 (Upstream, CodeChange)                    
              Required build for 2 from the 1 pivots out of 6 repositories.
              U 1 updates from upstreams (not using '*publish' here).
              🡡 3 repositories must be published.
              ❰✓❱
          
              """ );

        var (nugetOrgFeed, sosFeed) = GetFakeFeedPaths( clonedFolder );

        Directory.Exists( nugetOrgFeed.AppendPart( "ckt.perfectevent" ) ).ShouldBeTrue();
        Directory.Exists( sosFeed.AppendPart( "ckt.perfectevent" ) ).ShouldBeTrue();

        Directory.Exists( nugetOrgFeed.AppendPart( "ckt.sample.monitoring" ) ).ShouldBe( !nonPackableSample );
        Directory.Exists( sosFeed.AppendPart( "ckt.sample.monitoring" ) ).ShouldBe( !nonPackableSample );

    }


    static (NormalizedPath NuGetOrgPath, NormalizedPath SignatureOSPath) GetFakeFeedPaths( NormalizedPath clonedFolder )
    {
        return (clonedFolder.Combine( "FakeFeed/nuget.org" ), clonedFolder.Combine( "FakeFeed/Signature-OpenSource" ));

    }

    static void ConfigureFakeFeeds( IActivityMonitor monitor, NormalizedPath clonedFolder, XElement plugins )
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
                _ => Throw.NotSupportedException<string>()
            } );
            var key = f.Element( "PushCredentials" )?.Attribute( "SecretKey" );
            key.ShouldNotBeNull().SetValue( "FILESYSTEM_GIT" );
        }
    }
}
