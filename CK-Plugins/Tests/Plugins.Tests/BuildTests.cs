using CK.Core;
using CKli;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Linq;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;


    [TestFixture]
public class BuildTests
{
    [Test]
    public async Task CKt_add_sample_Async()
    {
        Helper.SetFileSystemWritePAT();
        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(initialized)" );
        var context = remotes.Clone( clonedFolder, ConfigureFakeFeeds );
        var display = (StringScreen)context.Screen;

        var inSampleFolder = context.ChangeDirectory( "Samples" ); 
        var newRepo1 = TestHelper.CKliRemotesPath.AppendPart( "bare" ).Combine( "CKt(initialized)/CKt-Sample-Monitoring" );
        var newRepoUrl1 = $"file://{newRepo1}";
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "maintenance", "hosting", "create", newRepoUrl1 )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleFolder, "repo", "add", newRepoUrl1 )).ShouldBeTrue();

        var newRepo2 = TestHelper.CKliRemotesPath.AppendPart( "bare" ).Combine( "CKt(initialized)/CKt-App-Sample" );
        var newRepoUrl2 = $"file://{newRepo2}";
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "maintenance", "hosting", "create", newRepoUrl2 )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleFolder, "repo", "add", newRepoUrl2 )).ShouldBeTrue();

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

            (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleMonitoring, "checkout", "dev/stable", "--create" )).ShouldBeTrue();

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
                File.WriteAllText( $"Assets/Install-{args[0]}.txt", $"I'm the install manual of CKt-Sample-Monitoring version '{args[0]}'." );
                """ );
            File.WriteAllText( deployFolder.AppendPart( ".gitignore" ), "Assets/" );
        }
        #endregion

        #region Initializing Samples/CKt-App-Sample
        {
            var inSampleApp = inSampleFolder.ChangeDirectory( "CKt-App-Sample" );
            Directory.Exists( inSampleApp.CurrentDirectory ).ShouldBeTrue();

            (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleApp, "checkout", "dev/stable", "--create" )).ShouldBeTrue();

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
                Directory.CreateDirectory( "Assets/ZipDemo" );
                File.WriteAllText( $"Assets/ZipDemo/Install-{args[0]}.txt", "I'm the install manual of CKt.SomeApp version '{args[0]}'." );
                File.WriteAllText( $"Assets/ZipDemo/AnotherFile.txt", "Another file..." );

                """ );
            File.WriteAllText( deployFolder.AppendPart( ".gitignore" ), "Assets/" );
        }
        #endregion


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
        display.Clear();

        // No more issue.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            ❰✓❱

            """ );

        // Let's build and publish the CI versions.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "build", "--publish" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 -  CKt-Core                      v1.0.0 → v1.0.1--ci.4 (CodeChange)          
            2 -  CKt-ActivityMonitor           v0.1.0 → v0.1.1--ci.5 (Upstream, CodeChange)
            3 ╓  CKt-PerfectEvent              v0.3.2 → v0.3.3--ci.5 (Upstream, CodeChange)
            4 ║  CKt-Monitoring                v0.2.3 → v0.2.4--ci.5 (Upstream, CodeChange)
            5 ╙  Samples/CKt-App-Sample        v0.0.0 → v0.0.1--ci.1 (Upstream)            
            6 -  Samples/CKt-Sample-Monitoring v0.0.0 → v0.0.1--ci.1 (Upstream)            
            Required build for 6 repositories across the 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            ❰✓❱

            """ );

    }

    [Explicit]
    [Test]
    public async Task REMOTES_CKt_add_sample_to_with_sample_Async()
    {
        FileHelper.DeleteFolder( TestHelper.Monitor, TestHelper.CKliRemotesPath.AppendPart( "CKt(with_sample)" ) );
        await CKt_add_sample_Async();
        TestHelper.CKliCreateRemoteFolderFromCloned( "CKt_add_sample_Async", "CKt", "(with_sample)" );
    }

    [Test]
    public async Task CKt_with_sample_dry_run_build_and_жbuild_Async()
    {
        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(with_sample)" );
        var context = remotes.Clone( clonedFolder );
        var display = (StringScreen)context.Screen;

        #region build
        {
            // From stack root (or if --all is specified): all solutions are pivots <==> none of them is.
            // (in this case *build is the same as build).
            display.Clear();
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "build", "--branch", "stable", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            1 -  CKt-Core                      v1.0.0 → v1.0.1--ci.4 (CodeChange)          
            2 -  CKt-ActivityMonitor           v0.1.0 → v0.1.1--ci.5 (Upstream, CodeChange)
            3 ╓  CKt-PerfectEvent              v0.3.2 → v0.3.3--ci.5 (Upstream, CodeChange)
            4 ║  CKt-Monitoring                v0.2.3 → v0.2.4--ci.5 (Upstream, CodeChange)
            5 ╙  Samples/CKt-App-Sample        v0.0.0 → v0.0.1--ci.1 (Upstream)            
            6 -  Samples/CKt-Sample-Monitoring v0.0.0 → v0.0.1--ci.1 (Upstream)            
            ❰✓❱
            
            """ );

            // From Samples/: the 2 samples are pivots, others are upstreams: they are ignored because this is a build, not a *build
            // and the 2 pivots are already available (in v0.0.0 built by the "ckli issue --fix" for the missing initial version),
            // so there is eventually nothing to do.
            display.Clear();
            var inSample = context.ChangeDirectory( "Samples" );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inSample, "build", "--branch", "stable", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            - →·   CKt-Core                      v1.0.0 
            - →·   CKt-ActivityMonitor           v0.1.0 
            ╓ →·   CKt-PerfectEvent              v0.3.2 
            ║ →·   CKt-Monitoring                v0.2.3 
            ╙  ⊙   Samples/CKt-App-Sample        v0.0.0 
            -  ⊙   Samples/CKt-Sample-Monitoring v0.0.0 
            ❰✓❱
            
            """ );

            // From Samples/CKt-Sample-Monitoring: the App sample is "nothing" (not related to pivots), all the other
            // are ignored and the CKt-Sample-Monitoring is already available in v0.0.0, there's nothing to do.
            display.Clear();
            var inSampleMonitoring = inSample.ChangeDirectory( "CKt-Sample-Monitoring" );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleMonitoring, "build", "--branch", "stable", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            - →·   CKt-Core                      v1.0.0 
            - →·   CKt-ActivityMonitor           v0.1.0 
            ╓ →·   CKt-PerfectEvent              v0.3.2 
            ║ →·   CKt-Monitoring                v0.2.3 
            ╙      Samples/CKt-App-Sample        v0.0.0 
            -  ⊙   Samples/CKt-Sample-Monitoring v0.0.0 
            ❰✓❱

            """ );

            // From Samples/CKt-App-Sample: same as above but CKt-App-Sample pivot replaces CKt-Sample-Monitoring.
            display.Clear();
            var inAppSample = inSample.ChangeDirectory( "CKt-App-Sample" );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inAppSample, "build", "--branch", "stable", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            - →·   CKt-Core                      v1.0.0 
            - →·   CKt-ActivityMonitor           v0.1.0 
            ╓      CKt-PerfectEvent              v0.3.2 
            ║      CKt-Monitoring                v0.2.3 
            ╙  ⊙   Samples/CKt-App-Sample        v0.0.0 
            -      Samples/CKt-Sample-Monitoring v0.0.0 
            ❰✓❱

            """ );

            // From CKt-PerfectEvent: the CKt-Monitoring and App sample are "nothing". CKt-Sample-Monitoring is a downstream repo
            // that must be built.
            display.Clear();
            var inPerfectEvent = context.ChangeDirectory( "CKt-PerfectEvent" );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "build", "--branch", "stable", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
              - →·   CKt-Core                      v1.0.0 
              - →·   CKt-ActivityMonitor           v0.1.0 
            1 ╓  ⊙   CKt-PerfectEvent              v0.3.2 → v0.3.3--ci.4 (CodeChange)
              ║      CKt-Monitoring                v0.2.3 
              ╙      Samples/CKt-App-Sample        v0.0.0 
            2 -  ·→  Samples/CKt-Sample-Monitoring v0.0.0 → v0.0.1--ci.1 (Upstream)  
            ❰✓❱
            
            """ );
        }
        #endregion

        #region *build
        {
            // From stack root: all solutions are pivots <==> none of them is.
            display.Clear();
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "*build", "--branch", "stable", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            1 -  CKt-Core                      v1.0.0 → v1.0.1--ci.4 (CodeChange)          
            2 -  CKt-ActivityMonitor           v0.1.0 → v0.1.1--ci.5 (Upstream, CodeChange)
            3 ╓  CKt-PerfectEvent              v0.3.2 → v0.3.3--ci.5 (Upstream, CodeChange)
            4 ║  CKt-Monitoring                v0.2.3 → v0.2.4--ci.5 (Upstream, CodeChange)
            5 ╙  Samples/CKt-App-Sample        v0.0.0 → v0.0.1--ci.1 (Upstream)            
            6 -  Samples/CKt-Sample-Monitoring v0.0.0 → v0.0.1--ci.1 (Upstream)            
            ❰✓❱
            
            """ );

            // From Samples/: the 2 samples are pivots, others are upstreams.
            display.Clear();
            var inSample = context.ChangeDirectory( "Samples" );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inSample, "*build", "--branch", "stable", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            1 - →·   CKt-Core                      v1.0.0 → v1.0.1--ci.4 (CodeChange)          
            2 - →·   CKt-ActivityMonitor           v0.1.0 → v0.1.1--ci.5 (Upstream, CodeChange)
            3 ╓ →·   CKt-PerfectEvent              v0.3.2 → v0.3.3--ci.5 (Upstream, CodeChange)
            4 ║ →·   CKt-Monitoring                v0.2.3 → v0.2.4--ci.5 (Upstream, CodeChange)
            5 ╙  ⊙   Samples/CKt-App-Sample        v0.0.0 → v0.0.1--ci.1 (Upstream)            
            6 -  ⊙   Samples/CKt-Sample-Monitoring v0.0.0 → v0.0.1--ci.1 (Upstream)            
            ❰✓❱
            
            """ );

            // From Samples/CKt-Sample-Monitoring: the App sample is "nothing" (not related to pivots).
            // However, the App sample must be build because one of its upstream is built.
            display.Clear();
            var inSampleMonitoring = inSample.ChangeDirectory( "CKt-Sample-Monitoring" );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleMonitoring, "*build", "--branch", "stable", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            1 - →·   CKt-Core                      v1.0.0 → v1.0.1--ci.4 (CodeChange)          
            2 - →·   CKt-ActivityMonitor           v0.1.0 → v0.1.1--ci.5 (Upstream, CodeChange)
            3 ╓ →·   CKt-PerfectEvent              v0.3.2 → v0.3.3--ci.5 (Upstream, CodeChange)
            4 ║ →·   CKt-Monitoring                v0.2.3 → v0.2.4--ci.5 (Upstream, CodeChange)
            5 ╙      Samples/CKt-App-Sample        v0.0.0 → v0.0.1--ci.1 (Upstream)            
            6 -  ⊙   Samples/CKt-Sample-Monitoring v0.0.0 → v0.0.1--ci.1 (Upstream)            
            ❰✓❱

            """ );

            // From Samples/CKt-App-Sample: the monitoring, perfect event and sample monitoring are "nothing", but because of
            // the upstreams, every Repo must be built.
            display.Clear();
            var inAppSample = inSample.ChangeDirectory( "CKt-App-Sample" );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inAppSample, "*build", "--branch", "stable", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            1 - →·   CKt-Core                      v1.0.0 → v1.0.1--ci.4 (CodeChange)          
            2 - →·   CKt-ActivityMonitor           v0.1.0 → v0.1.1--ci.5 (Upstream, CodeChange)
            3 ╓      CKt-PerfectEvent              v0.3.2 → v0.3.3--ci.5 (Upstream, CodeChange)
            4 ║      CKt-Monitoring                v0.2.3 → v0.2.4--ci.5 (Upstream, CodeChange)
            5 ╙  ⊙   Samples/CKt-App-Sample        v0.0.0 → v0.0.1--ci.1 (Upstream)            
            6 -      Samples/CKt-Sample-Monitoring v0.0.0 → v0.0.1--ci.1 (Upstream)            
            ❰✓❱

            """ );

            // From CKt-PerfectEvent: the CKt-Monitoring and App sample are "nothing", but as usual, because of the upstreams,
            // every Repo must be built.
            display.Clear();
            var inPerfectEvent = context.ChangeDirectory( "CKt-PerfectEvent" );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "*build", "--branch", "stable", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            1 - →·   CKt-Core                      v1.0.0 → v1.0.1--ci.4 (CodeChange)          
            2 - →·   CKt-ActivityMonitor           v0.1.0 → v0.1.1--ci.5 (Upstream, CodeChange)
            3 ╓  ⊙   CKt-PerfectEvent              v0.3.2 → v0.3.3--ci.5 (Upstream, CodeChange)
            4 ║      CKt-Monitoring                v0.2.3 → v0.2.4--ci.5 (Upstream, CodeChange)
            5 ╙      Samples/CKt-App-Sample        v0.0.0 → v0.0.1--ci.1 (Upstream)            
            6 -  ·→  Samples/CKt-Sample-Monitoring v0.0.0 → v0.0.1--ci.1 (Upstream)            
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
        var context = remotes.Clone( clonedFolder, ConfigureFakeFeeds ).SetScreen( new StringScreen( useDebugRenderer: true ) );
        var display = (StringScreen)context.Screen;

        // From CKt-PerfectEvent (the NuGet.config has been renamed to nuget.config).
        // The CKt-Monitoring and App sample are "nothing".
        // CKt-Sample-Monitoring is a downstream repo that must be built.
        // We inject <IsPackable>false</IsPackable> in CKt.Sample.Monitoring.csproj in "nonPackableSample" mode.
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
            nonPackableSample
            ? """
                - [BLACK,darkyellow]→· [GRAY,black]  [DARKGRAY]CKt-Core[GRAY]                      [DARKBLUE]v1.0.0 [GRAY]⮐
                - [BLACK,darkyellow]→· [GRAY,black]  [DARKGRAY]CKt-ActivityMonitor[GRAY]           [DARKBLUE]v0.1.0 [GRAY]⮐
              1 ╓ [BLACK,darkyellow] ⊙ [GRAY,black]  [GREEN]CKt-PerfectEvent[GRAY]              [DARKBLUE]v0.3.2 [GREEN]→ v0.3.3[GRAY] [Italic](CodeChange)[Regular]          ⮐
                ║      [DARKGRAY]CKt-Monitoring[GRAY]                [DARKBLUE]v0.2.3 [GRAY]⮐
                ╙      [DARKGRAY]Samples/CKt-App-Sample[GRAY]        [DARKBLUE]v0.0.0 [GRAY]⮐
              2 - [BLACK,darkyellow] ·→[GRAY,black]  [GREEN]Samples/CKt-Sample-Monitoring[GRAY] [DARKBLUE]v0.0.0 [GREEN]→ v0.0.1[GRAY] [Italic](Upstream, CodeChange)[Regular]⮐
              [BLACK,darkgreen]❰✓❱[GRAY,black]⮐
          
              """
            : """
                - [BLACK,darkyellow]→· [GRAY,black]  [DARKGRAY]CKt-Core[GRAY]                      [DARKBLUE]v1.0.0 [GRAY]⮐
                - [BLACK,darkyellow]→· [GRAY,black]  [DARKGRAY]CKt-ActivityMonitor[GRAY]           [DARKBLUE]v0.1.0 [GRAY]⮐
              1 ╓ [BLACK,darkyellow] ⊙ [GRAY,black]  [GREEN]CKt-PerfectEvent[GRAY]              [DARKBLUE]v0.3.2 [GREEN]→ v0.3.3[GRAY] [Italic](CodeChange)[Regular]⮐
                ║      [DARKGRAY]CKt-Monitoring[GRAY]                [DARKBLUE]v0.2.3 [GRAY]⮐
                ╙      [DARKGRAY]Samples/CKt-App-Sample[GRAY]        [DARKBLUE]v0.0.0 [GRAY]⮐
              2 - [BLACK,darkyellow] ·→[GRAY,black]  [GREEN]Samples/CKt-Sample-Monitoring[GRAY] [DARKBLUE]v0.0.0 [GREEN]→ v0.0.1[GRAY] [Italic](Upstream)[Regular]  ⮐
              [BLACK,darkgreen]❰✓❱[GRAY,black]⮐
             
              """ );

        var nugetOrg = clonedFolder.Combine( "FakeFeed/nuget.org" );
        var sosFeed = clonedFolder.Combine( "FakeFeed/Signature-OpenSource" );

        Directory.Exists( nugetOrg.AppendPart( "ckt.perfectevent" ) ).ShouldBeTrue();
        Directory.Exists( sosFeed.AppendPart( "ckt.perfectevent" ) ).ShouldBeTrue();

        Directory.Exists( nugetOrg.AppendPart( "ckt.sample.monitoring" ) ).ShouldBe( !nonPackableSample );
        Directory.Exists( sosFeed.AppendPart( "ckt.sample.monitoring" ) ).ShouldBe( !nonPackableSample );


    }

    static void ConfigureFakeFeeds( IActivityMonitor monitor, NormalizedPath clonedFolder, XElement plugins )
    {
        var nugetOrg = clonedFolder.Combine( "FakeFeed/nuget.org" );
        var sosFeed = clonedFolder.Combine( "FakeFeed/Signature-OpenSource" );
        NuGetHelper.EnsureLocalFeed( monitor, nugetOrg );
        NuGetHelper.EnsureLocalFeed( monitor, sosFeed );
        foreach( var f in plugins.Elements( "ArtifactHandler" ).Elements( "NuGet" ).Elements( "Feed" ) )
        {
            var url = f.Attribute( "Url" ).ShouldNotBeNull();
            url.SetValue( url.Value switch
            {
                "https://api.nuget.org/v3/index.json" => $"file://{nugetOrg}",
                "https://pkgs.dev.azure.com/Signature-OpenSource/Feeds/_packaging/NetCore3/nuget/v3/index.json" => $"file://{sosFeed}",
                _ => Throw.NotSupportedException<string>()
            } );
            var key = f.Element( "PushCredentials" )?.Attribute( "SecretKey" );
            key.ShouldNotBeNull().SetValue( "FILESYSTEM_GIT" );
        }
    }
}
