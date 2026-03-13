using CKli;
using CKli.Core;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Threading.Tasks;
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
        var context = remotes.Clone( clonedFolder );
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
        }
        #endregion

        // The nuget.config can be fixed with a dirty folder (no need to commit).
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
    public async Task CKt_with_sample_dry_run_build_жbuild_publish_жpublish_Async()
    {
        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(with_sample)" );
        var context = remotes.Clone( clonedFolder ).SetScreen( new StringScreen( useDebugRenderer: true ) );
        var display = (StringScreen)context.Screen;

        #region publish
        {
            // From stack root (or if --all is specified): all solutions are pivots <==> none of them is.
            // (in this case *publish is the same as publish).
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "publish", "--branch", "stable", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            0 -[DARKGREEN] CKt-Core ▻ v1.0.0 [GREEN]→ v1.0.1[GRAY]⮐
            1 -[DARKGREEN] CKt-ActivityMonitor ▻ v0.1.0 [GREEN]→ v0.1.1[GRAY]⮐
            2 -[DARKGREEN] CKt-PerfectEvent ▻ v0.3.2 [GREEN]→ v0.3.3[GRAY]⮐
            3 -[DARKGREEN] CKt-Monitoring ▻ v0.2.3 [GREEN]→ v0.2.4[GRAY]⮐
            4 -[DARKGREEN] Samples/CKt-Sample-Monitoring ▻ v0.0.0 [GREEN]→ v0.0.1[GRAY]⮐
            5 -[DARKGREEN] Samples/CKt-App-Sample ▻ v0.0.0 [GREEN]→ v0.0.1[GRAY]⮐
            [BLACK,darkgreen]❰✓❱[GRAY,black]⮐
            
            """ );

            // From Samples/: the 2 samples are pivots, others are upstreams: they are ignored and the 2 pivots are
            // already available (in v0.0.0 built by the "ckli issue --fix" for the missing initial version), so there
            // is eventually nothing to do.
            display.Clear();
            var inSample = context.ChangeDirectory( "Samples" );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inSample, "publish", "--branch", "stable", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            [BLACK,yellow,Strikethrough]=>[P]  [GRAY,black] CKt-Core [Regular]⮐
            [BLACK,yellow,Strikethrough]=>[P]  [GRAY,black] CKt-ActivityMonitor [Regular]⮐
            [BLACK,yellow,Strikethrough]=>[P]  [GRAY,black] CKt-PerfectEvent [Regular]⮐
            [BLACK,yellow,Strikethrough]=>[P]  [GRAY,black] CKt-Monitoring [Regular]⮐
            [BLACK,yellow]  [P]  [GRAY,black] Samples/CKt-Sample-Monitoring ▻ v0.0.0 ⮐
            [BLACK,yellow]  [P]  [GRAY,black] Samples/CKt-App-Sample ▻ v0.0.0 ⮐
            [BLACK,darkgreen]❰✓❱[GRAY,black]⮐
            
            """ );

            // From Samples/CKt-Sample-Monitoring: the App sample is "nothing" (not related to pivots), all the other
            // are ignored and the CKt-Sample-Monitoring is already available in v0.0.0, there's nothing to do.
            display.Clear();
            var inSampleMonitoring = inSample.ChangeDirectory( "CKt-Sample-Monitoring" );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleMonitoring, "publish", "--branch", "stable", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            [BLACK,yellow,Strikethrough]=>[P]  [GRAY,black] CKt-Core [Regular]⮐
            [BLACK,yellow,Strikethrough]=>[P]  [GRAY,black] CKt-ActivityMonitor [Regular]⮐
            [BLACK,yellow,Strikethrough]=>[P]  [GRAY,black] CKt-PerfectEvent [Regular]⮐
            [BLACK,yellow,Strikethrough]=>[P]  [GRAY,black] CKt-Monitoring [Regular]⮐
            [BLACK,yellow]  [P]  [GRAY,black] Samples/CKt-Sample-Monitoring ▻ v0.0.0 ⮐
                   [Strikethrough] Samples/CKt-App-Sample [Regular]⮐
            [BLACK,darkgreen]❰✓❱[GRAY,black]⮐

            """ );

            // From Samples/CKt-App-Sample: same as above but CKt-App-Sample pivot replaces CKt-Sample-Monitoring.
            display.Clear();
            var inAppSample = inSample.ChangeDirectory( "CKt-App-Sample" );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inAppSample, "publish", "--branch", "stable", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            [BLACK,yellow,Strikethrough]=>[P]  [GRAY,black] CKt-Core [Regular]⮐
            [BLACK,yellow,Strikethrough]=>[P]  [GRAY,black] CKt-ActivityMonitor [Regular]⮐
                   [Strikethrough] CKt-PerfectEvent [Regular]⮐
                   [Strikethrough] CKt-Monitoring [Regular]⮐
                   [Strikethrough] Samples/CKt-Sample-Monitoring [Regular]⮐
            [BLACK,yellow]  [P]  [GRAY,black] Samples/CKt-App-Sample ▻ v0.0.0 ⮐
            [BLACK,darkgreen]❰✓❱[GRAY,black]⮐

            """ );

            // From CKt-PerfectEvent: the CKt-Monitoring and App sample are "nothing". CKt-Sample-Monitoring is a downstream repo
            // that must be published.
            display.Clear();
            var inPerfectEvent = context.ChangeDirectory( "CKt-PerfectEvent" );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "publish", "--branch", "stable", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
                [BLACK,yellow,Strikethrough]=>[P]  [GRAY,black] CKt-Core [Regular]⮐
                [BLACK,yellow,Strikethrough]=>[P]  [GRAY,black] CKt-ActivityMonitor [Regular]⮐
            0 -[DARKGREEN] [BLACK,yellow]  [P]  [DARKGREEN,black] CKt-PerfectEvent ▻ v0.3.2 [GREEN]→ v0.3.3[GRAY]⮐
                       [Strikethrough] CKt-Monitoring [Regular]⮐
            1 -[DARKGREEN] [BLACK,yellow]  [P]=>[DARKGREEN,black] Samples/CKt-Sample-Monitoring ▻ v0.0.0 [GREEN]→ v0.0.1[GRAY]⮐
                       [Strikethrough] Samples/CKt-App-Sample [Regular]⮐
            [BLACK,darkgreen]❰✓❱[GRAY,black]⮐
            
            """ );
        }
        #endregion

        #region *publish
        {
            // From stack root: all solutions are pivots <==> none of them is.
            display.Clear();
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "*publish", "--branch", "stable", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            0 -[DARKGREEN] CKt-Core ▻ v1.0.0 [GREEN]→ v1.0.1[GRAY]⮐
            1 -[DARKGREEN] CKt-ActivityMonitor ▻ v0.1.0 [GREEN]→ v0.1.1[GRAY]⮐
            2 -[DARKGREEN] CKt-PerfectEvent ▻ v0.3.2 [GREEN]→ v0.3.3[GRAY]⮐
            3 -[DARKGREEN] CKt-Monitoring ▻ v0.2.3 [GREEN]→ v0.2.4[GRAY]⮐
            4 -[DARKGREEN] Samples/CKt-Sample-Monitoring ▻ v0.0.0 [GREEN]→ v0.0.1[GRAY]⮐
            5 -[DARKGREEN] Samples/CKt-App-Sample ▻ v0.0.0 [GREEN]→ v0.0.1[GRAY]⮐
            [BLACK,darkgreen]❰✓❱[GRAY,black]⮐
            
            """ );

            // From Samples/: the 2 samples are pivots, others are upstreams.
            display.Clear();
            var inSample = context.ChangeDirectory( "Samples" );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inSample, "*publish", "--branch", "stable", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            0 -[DARKGREEN] [BLACK,yellow]=>[P]  [DARKGREEN,black] CKt-Core ▻ v1.0.0 [GREEN]→ v1.0.1[GRAY]⮐
            1 -[DARKGREEN] [BLACK,yellow]=>[P]  [DARKGREEN,black] CKt-ActivityMonitor ▻ v0.1.0 [GREEN]→ v0.1.1[GRAY]⮐
            2 -[DARKGREEN] [BLACK,yellow]=>[P]  [DARKGREEN,black] CKt-PerfectEvent ▻ v0.3.2 [GREEN]→ v0.3.3[GRAY]⮐
            3 -[DARKGREEN] [BLACK,yellow]=>[P]  [DARKGREEN,black] CKt-Monitoring ▻ v0.2.3 [GREEN]→ v0.2.4[GRAY]⮐
            4 -[DARKGREEN] [BLACK,yellow]  [P]  [DARKGREEN,black] Samples/CKt-Sample-Monitoring ▻ v0.0.0 [GREEN]→ v0.0.1[GRAY]⮐
            5 -[DARKGREEN] [BLACK,yellow]  [P]  [DARKGREEN,black] Samples/CKt-App-Sample ▻ v0.0.0 [GREEN]→ v0.0.1[GRAY]⮐
            [BLACK,darkgreen]❰✓❱[GRAY,black]⮐
            
            """ );

            // From Samples/CKt-Sample-Monitoring: the App sample is "nothing" (not related to pivots).
            // However, the App sample must be build because one of its upstream is built.
            display.Clear();
            var inSampleMonitoring = inSample.ChangeDirectory( "CKt-Sample-Monitoring" );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleMonitoring, "*publish", "--branch", "stable", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            0 -[DARKGREEN] [BLACK,yellow]=>[P]  [DARKGREEN,black] CKt-Core ▻ v1.0.0 [GREEN]→ v1.0.1[GRAY]⮐
            1 -[DARKGREEN] [BLACK,yellow]=>[P]  [DARKGREEN,black] CKt-ActivityMonitor ▻ v0.1.0 [GREEN]→ v0.1.1[GRAY]⮐
            2 -[DARKGREEN] [BLACK,yellow]=>[P]  [DARKGREEN,black] CKt-PerfectEvent ▻ v0.3.2 [GREEN]→ v0.3.3[GRAY]⮐
            3 -[DARKGREEN] [BLACK,yellow]=>[P]  [DARKGREEN,black] CKt-Monitoring ▻ v0.2.3 [GREEN]→ v0.2.4[GRAY]⮐
            4 -[DARKGREEN] [BLACK,yellow]  [P]  [DARKGREEN,black] Samples/CKt-Sample-Monitoring ▻ v0.0.0 [GREEN]→ v0.0.1[GRAY]⮐
            5 -[DARKGREEN]         Samples/CKt-App-Sample ▻ v0.0.0 [GREEN]→ v0.0.1[GRAY]⮐
            [BLACK,darkgreen]❰✓❱[GRAY,black]⮐

            """ );

            // From Samples/CKt-App-Sample: the monitoring, perfect event and sample monitoring are "nothing", but because of
            // the upstreams, every Repo must be built.
            display.Clear();
            var inAppSample = inSample.ChangeDirectory( "CKt-App-Sample" );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inAppSample, "*publish", "--branch", "stable", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            0 -[DARKGREEN] [BLACK,yellow]=>[P]  [DARKGREEN,black] CKt-Core ▻ v1.0.0 [GREEN]→ v1.0.1[GRAY]⮐
            1 -[DARKGREEN] [BLACK,yellow]=>[P]  [DARKGREEN,black] CKt-ActivityMonitor ▻ v0.1.0 [GREEN]→ v0.1.1[GRAY]⮐
            2 -[DARKGREEN]         CKt-PerfectEvent ▻ v0.3.2 [GREEN]→ v0.3.3[GRAY]⮐
            3 -[DARKGREEN]         CKt-Monitoring ▻ v0.2.3 [GREEN]→ v0.2.4[GRAY]⮐
            4 -[DARKGREEN]         Samples/CKt-Sample-Monitoring ▻ v0.0.0 [GREEN]→ v0.0.1[GRAY]⮐
            5 -[DARKGREEN] [BLACK,yellow]  [P]  [DARKGREEN,black] Samples/CKt-App-Sample ▻ v0.0.0 [GREEN]→ v0.0.1[GRAY]⮐
            [BLACK,darkgreen]❰✓❱[GRAY,black]⮐

            """ );

            // From CKt-PerfectEvent: the CKt-Monitoring and App sample are "nothing", but as usual, because of the upstreams,
            // every Repo must be built.
            display.Clear();
            var inPerfectEvent = context.ChangeDirectory( "CKt-PerfectEvent" );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "*publish", "--branch", "stable", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            0 -[DARKGREEN] [BLACK,yellow]=>[P]  [DARKGREEN,black] CKt-Core ▻ v1.0.0 [GREEN]→ v1.0.1[GRAY]⮐
            1 -[DARKGREEN] [BLACK,yellow]=>[P]  [DARKGREEN,black] CKt-ActivityMonitor ▻ v0.1.0 [GREEN]→ v0.1.1[GRAY]⮐
            2 -[DARKGREEN] [BLACK,yellow]  [P]  [DARKGREEN,black] CKt-PerfectEvent ▻ v0.3.2 [GREEN]→ v0.3.3[GRAY]⮐
            3 -[DARKGREEN]         CKt-Monitoring ▻ v0.2.3 [GREEN]→ v0.2.4[GRAY]⮐
            4 -[DARKGREEN] [BLACK,yellow]  [P]=>[DARKGREEN,black] Samples/CKt-Sample-Monitoring ▻ v0.0.0 [GREEN]→ v0.0.1[GRAY]⮐
            5 -[DARKGREEN]         Samples/CKt-App-Sample ▻ v0.0.0 [GREEN]→ v0.0.1[GRAY]⮐
            [BLACK,darkgreen]❰✓❱[GRAY,black]⮐
            
            """ );
        }
        #endregion

        #region build
        {
            // From stack root (or if --all is specified): all solutions are pivots <==> none of them is.
            // (in this case *build is the same as build).
            display.Clear();
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "build", "--branch", "stable", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            0 -[DARKGREEN] CKt-Core ▻ v1.0.0 [GREEN]→ v1.0.1--ci.4[GRAY]⮐
            1 -[DARKGREEN] CKt-ActivityMonitor ▻ v0.1.0 [GREEN]→ v0.1.1--ci.5[GRAY]⮐
            2 -[DARKGREEN] CKt-PerfectEvent ▻ v0.3.2 [GREEN]→ v0.3.3--ci.5[GRAY]⮐
            3 -[DARKGREEN] CKt-Monitoring ▻ v0.2.3 [GREEN]→ v0.2.4--ci.5[GRAY]⮐
            4 -[DARKGREEN] Samples/CKt-Sample-Monitoring ▻ v0.0.0 [GREEN]→ v0.0.1--ci.1[GRAY]⮐
            5 -[DARKGREEN] Samples/CKt-App-Sample ▻ v0.0.0 [GREEN]→ v0.0.1--ci.1[GRAY]⮐
            [BLACK,darkgreen]❰✓❱[GRAY,black]⮐
            
            """ );

            // From Samples/: the 2 samples are pivots, others are upstreams: they are ignored and the 2 pivots are
            // already available (in v0.0.0 built by the "ckli issue --fix" for the missing initial version), so there
            // is eventually nothing to do.
            display.Clear();
            var inSample = context.ChangeDirectory( "Samples" );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inSample, "build", "--branch", "stable", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            [BLACK,yellow,Strikethrough]=>[P]  [GRAY,black] CKt-Core [Regular]⮐
            [BLACK,yellow,Strikethrough]=>[P]  [GRAY,black] CKt-ActivityMonitor [Regular]⮐
            [BLACK,yellow,Strikethrough]=>[P]  [GRAY,black] CKt-PerfectEvent [Regular]⮐
            [BLACK,yellow,Strikethrough]=>[P]  [GRAY,black] CKt-Monitoring [Regular]⮐
            [BLACK,yellow]  [P]  [GRAY,black] Samples/CKt-Sample-Monitoring ▻ v0.0.0 ⮐
            [BLACK,yellow]  [P]  [GRAY,black] Samples/CKt-App-Sample ▻ v0.0.0 ⮐
            [BLACK,darkgreen]❰✓❱[GRAY,black]⮐
            
            """ );

            // From Samples/CKt-Sample-Monitoring: the App sample is "nothing" (not related to pivots), all the other
            // are ignored and the CKt-Sample-Monitoring is already available in v0.0.0, there's nothing to do.
            display.Clear();
            var inSampleMonitoring = inSample.ChangeDirectory( "CKt-Sample-Monitoring" );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleMonitoring, "build", "--branch", "stable", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            [BLACK,yellow,Strikethrough]=>[P]  [GRAY,black] CKt-Core [Regular]⮐
            [BLACK,yellow,Strikethrough]=>[P]  [GRAY,black] CKt-ActivityMonitor [Regular]⮐
            [BLACK,yellow,Strikethrough]=>[P]  [GRAY,black] CKt-PerfectEvent [Regular]⮐
            [BLACK,yellow,Strikethrough]=>[P]  [GRAY,black] CKt-Monitoring [Regular]⮐
            [BLACK,yellow]  [P]  [GRAY,black] Samples/CKt-Sample-Monitoring ▻ v0.0.0 ⮐
                   [Strikethrough] Samples/CKt-App-Sample [Regular]⮐
            [BLACK,darkgreen]❰✓❱[GRAY,black]⮐

            """ );

            // From Samples/CKt-App-Sample: same as above but CKt-App-Sample pivot replaces CKt-Sample-Monitoring.
            display.Clear();
            var inAppSample = inSample.ChangeDirectory( "CKt-App-Sample" );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inAppSample, "build", "--branch", "stable", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            [BLACK,yellow,Strikethrough]=>[P]  [GRAY,black] CKt-Core [Regular]⮐
            [BLACK,yellow,Strikethrough]=>[P]  [GRAY,black] CKt-ActivityMonitor [Regular]⮐
                   [Strikethrough] CKt-PerfectEvent [Regular]⮐
                   [Strikethrough] CKt-Monitoring [Regular]⮐
                   [Strikethrough] Samples/CKt-Sample-Monitoring [Regular]⮐
            [BLACK,yellow]  [P]  [GRAY,black] Samples/CKt-App-Sample ▻ v0.0.0 ⮐
            [BLACK,darkgreen]❰✓❱[GRAY,black]⮐

            """ );

            // From CKt-PerfectEvent: the CKt-Monitoring and App sample are "nothing". CKt-Sample-Monitoring is a downstream repo
            // that must be built.
            display.Clear();
            var inPerfectEvent = context.ChangeDirectory( "CKt-PerfectEvent" );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "build", "--branch", "stable", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
                [BLACK,yellow,Strikethrough]=>[P]  [GRAY,black] CKt-Core [Regular]⮐
                [BLACK,yellow,Strikethrough]=>[P]  [GRAY,black] CKt-ActivityMonitor [Regular]⮐
            0 -[DARKGREEN] [BLACK,yellow]  [P]  [DARKGREEN,black] CKt-PerfectEvent ▻ v0.3.2 [GREEN]→ v0.3.3--ci.4[GRAY]⮐
                       [Strikethrough] CKt-Monitoring [Regular]⮐
            1 -[DARKGREEN] [BLACK,yellow]  [P]=>[DARKGREEN,black] Samples/CKt-Sample-Monitoring ▻ v0.0.0 [GREEN]→ v0.0.1--ci.1[GRAY]⮐
                       [Strikethrough] Samples/CKt-App-Sample [Regular]⮐
            [BLACK,darkgreen]❰✓❱[GRAY,black]⮐
            
            """ );
        }
        #endregion

        #region *build
        {
            // From stack root: all solutions are pivots <==> none of them is.
            display.Clear();
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "*build", "--branch", "stable", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            0 -[DARKGREEN] CKt-Core ▻ v1.0.0 [GREEN]→ v1.0.1--ci.4[GRAY]⮐
            1 -[DARKGREEN] CKt-ActivityMonitor ▻ v0.1.0 [GREEN]→ v0.1.1--ci.5[GRAY]⮐
            2 -[DARKGREEN] CKt-PerfectEvent ▻ v0.3.2 [GREEN]→ v0.3.3--ci.5[GRAY]⮐
            3 -[DARKGREEN] CKt-Monitoring ▻ v0.2.3 [GREEN]→ v0.2.4--ci.5[GRAY]⮐
            4 -[DARKGREEN] Samples/CKt-Sample-Monitoring ▻ v0.0.0 [GREEN]→ v0.0.1--ci.1[GRAY]⮐
            5 -[DARKGREEN] Samples/CKt-App-Sample ▻ v0.0.0 [GREEN]→ v0.0.1--ci.1[GRAY]⮐
            [BLACK,darkgreen]❰✓❱[GRAY,black]⮐
            
            """ );

            // From Samples/: the 2 samples are pivots, others are upstreams.
            display.Clear();
            var inSample = context.ChangeDirectory( "Samples" );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inSample, "*build", "--branch", "stable", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            0 -[DARKGREEN] [BLACK,yellow]=>[P]  [DARKGREEN,black] CKt-Core ▻ v1.0.0 [GREEN]→ v1.0.1--ci.4[GRAY]⮐
            1 -[DARKGREEN] [BLACK,yellow]=>[P]  [DARKGREEN,black] CKt-ActivityMonitor ▻ v0.1.0 [GREEN]→ v0.1.1--ci.5[GRAY]⮐
            2 -[DARKGREEN] [BLACK,yellow]=>[P]  [DARKGREEN,black] CKt-PerfectEvent ▻ v0.3.2 [GREEN]→ v0.3.3--ci.5[GRAY]⮐
            3 -[DARKGREEN] [BLACK,yellow]=>[P]  [DARKGREEN,black] CKt-Monitoring ▻ v0.2.3 [GREEN]→ v0.2.4--ci.5[GRAY]⮐
            4 -[DARKGREEN] [BLACK,yellow]  [P]  [DARKGREEN,black] Samples/CKt-Sample-Monitoring ▻ v0.0.0 [GREEN]→ v0.0.1--ci.1[GRAY]⮐
            5 -[DARKGREEN] [BLACK,yellow]  [P]  [DARKGREEN,black] Samples/CKt-App-Sample ▻ v0.0.0 [GREEN]→ v0.0.1--ci.1[GRAY]⮐
            [BLACK,darkgreen]❰✓❱[GRAY,black]⮐
            
            """ );

            // From Samples/CKt-Sample-Monitoring: the App sample is "nothing" (not related to pivots).
            // However, the App sample must be build because one of its upstream is built.
            display.Clear();
            var inSampleMonitoring = inSample.ChangeDirectory( "CKt-Sample-Monitoring" );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleMonitoring, "*build", "--branch", "stable", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            0 -[DARKGREEN] [BLACK,yellow]=>[P]  [DARKGREEN,black] CKt-Core ▻ v1.0.0 [GREEN]→ v1.0.1--ci.4[GRAY]⮐
            1 -[DARKGREEN] [BLACK,yellow]=>[P]  [DARKGREEN,black] CKt-ActivityMonitor ▻ v0.1.0 [GREEN]→ v0.1.1--ci.5[GRAY]⮐
            2 -[DARKGREEN] [BLACK,yellow]=>[P]  [DARKGREEN,black] CKt-PerfectEvent ▻ v0.3.2 [GREEN]→ v0.3.3--ci.5[GRAY]⮐
            3 -[DARKGREEN] [BLACK,yellow]=>[P]  [DARKGREEN,black] CKt-Monitoring ▻ v0.2.3 [GREEN]→ v0.2.4--ci.5[GRAY]⮐
            4 -[DARKGREEN] [BLACK,yellow]  [P]  [DARKGREEN,black] Samples/CKt-Sample-Monitoring ▻ v0.0.0 [GREEN]→ v0.0.1--ci.1[GRAY]⮐
            5 -[DARKGREEN]         Samples/CKt-App-Sample ▻ v0.0.0 [GREEN]→ v0.0.1--ci.1[GRAY]⮐
            [BLACK,darkgreen]❰✓❱[GRAY,black]⮐

            """ );

            // From Samples/CKt-App-Sample: the monitoring, perfect event and sample monitoring are "nothing", but because of
            // the upstreams, every Repo must be built.
            display.Clear();
            var inAppSample = inSample.ChangeDirectory( "CKt-App-Sample" );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inAppSample, "*build", "--branch", "stable", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            0 -[DARKGREEN] [BLACK,yellow]=>[P]  [DARKGREEN,black] CKt-Core ▻ v1.0.0 [GREEN]→ v1.0.1--ci.4[GRAY]⮐
            1 -[DARKGREEN] [BLACK,yellow]=>[P]  [DARKGREEN,black] CKt-ActivityMonitor ▻ v0.1.0 [GREEN]→ v0.1.1--ci.5[GRAY]⮐
            2 -[DARKGREEN]         CKt-PerfectEvent ▻ v0.3.2 [GREEN]→ v0.3.3--ci.5[GRAY]⮐
            3 -[DARKGREEN]         CKt-Monitoring ▻ v0.2.3 [GREEN]→ v0.2.4--ci.5[GRAY]⮐
            4 -[DARKGREEN]         Samples/CKt-Sample-Monitoring ▻ v0.0.0 [GREEN]→ v0.0.1--ci.1[GRAY]⮐
            5 -[DARKGREEN] [BLACK,yellow]  [P]  [DARKGREEN,black] Samples/CKt-App-Sample ▻ v0.0.0 [GREEN]→ v0.0.1--ci.1[GRAY]⮐
            [BLACK,darkgreen]❰✓❱[GRAY,black]⮐

            """ );

            // From CKt-PerfectEvent: the CKt-Monitoring and App sample are "nothing", but as usual, because of the upstreams,
            // every Repo must be built.
            display.Clear();
            var inPerfectEvent = context.ChangeDirectory( "CKt-PerfectEvent" );
            (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "*build", "--branch", "stable", "--dry-run" )).ShouldBeTrue();
            display.ToString().ShouldBe( """
            0 -[DARKGREEN] [BLACK,yellow]=>[P]  [DARKGREEN,black] CKt-Core ▻ v1.0.0 [GREEN]→ v1.0.1--ci.4[GRAY]⮐
            1 -[DARKGREEN] [BLACK,yellow]=>[P]  [DARKGREEN,black] CKt-ActivityMonitor ▻ v0.1.0 [GREEN]→ v0.1.1--ci.5[GRAY]⮐
            2 -[DARKGREEN] [BLACK,yellow]  [P]  [DARKGREEN,black] CKt-PerfectEvent ▻ v0.3.2 [GREEN]→ v0.3.3--ci.5[GRAY]⮐
            3 -[DARKGREEN]         CKt-Monitoring ▻ v0.2.3 [GREEN]→ v0.2.4--ci.5[GRAY]⮐
            4 -[DARKGREEN] [BLACK,yellow]  [P]=>[DARKGREEN,black] Samples/CKt-Sample-Monitoring ▻ v0.0.0 [GREEN]→ v0.0.1--ci.1[GRAY]⮐
            5 -[DARKGREEN]         Samples/CKt-App-Sample ▻ v0.0.0 [GREEN]→ v0.0.1--ci.1[GRAY]⮐
            [BLACK,darkgreen]❰✓❱[GRAY,black]⮐
            
            """ );
        }
        #endregion

    }

    [Test]
    public async Task CKt_with_sample_build_PerfectEvent_Async()
    {
        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(with_sample)" );
        var context = remotes.Clone( clonedFolder );
        var display = (StringScreen)context.Screen;

        // From CKt-PerfectEvent (the NuGet.config has been renamed to nuget.config).
        // The CKt-Monitoring and App sample are "nothing".
        // CKt-Sample-Monitoring is a downstream repo that must be built.
        var inPerfectEvent = context.ChangeDirectory( "CKt-PerfectEvent" );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "build", "--dry-run" )).ShouldBeTrue();

        display.ToString().ShouldBe( """
            =>[P]   CKt-Core 
            =>[P]   CKt-ActivityMonitor 
        0 -   [P]   CKt-PerfectEvent ▻ v0.3.2 → v0.3.3--ci.4
                    CKt-Monitoring 
        1 -   [P]=> Samples/CKt-Sample-Monitoring ▻ v0.0.0 → v0.0.1--ci.1
                    Samples/CKt-App-Sample 
        ❰✓❱
        
        """ );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, inPerfectEvent, "build" )).ShouldBeTrue();
    }

}
