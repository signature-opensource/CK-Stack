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
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

public class S1ᅳInitializedᅳTests
{
    /// <summary>
    /// <see cref="CKli.BranchModel.Plugin.BranchModelPlugin.FixStartAsync"/>
    /// <see cref="CKli.BranchModel.Plugin.BranchModelPlugin.FixInfo"/>
    /// <see cref="CKli.Build.Plugin.BuildPlugin.FixBuildAsync"/>
    /// </summary>
    /// <returns></returns>
    [Test]
    public async Task CKt_local_fix_Async()
    {
        Helper.RemoveFileSystemWritePAT();
        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(initialized)" );
        var context = remotes.Clone( clonedFolder );
        var display = (StringScreen)context.Screen;

        // cd CK-Core.
        var cktCoreContext = context.ChangeDirectory( "CKt-Core" );

        // From CKt_init:
        var localNuGetFeed = context.CurrentStackPath.Combine( "$Local/NuGet" );
        var initialPackages = Directory.EnumerateFiles( localNuGetFeed )
                                     .Select( p => Path.GetFileName( p ) )
                                     .Order()
                                     .ToArray();
        initialPackages.ShouldBe( [
                    "CKt.ActivityMonitor.0.1.0.nupkg",
                    "CKt.Core.1.0.0.nupkg",
                    "CKt.Monitoring.0.2.3.nupkg",
                    "CKt.PerfectEvent.0.2.0.nupkg",
                    "CKt.PerfectEvent.0.2.1.nupkg",
                    "CKt.PerfectEvent.0.3.0.nupkg",
                    "CKt.PerfectEvent.0.3.2.nupkg"
                    ] );

        // No v2 yet => "Unable to find any version to fix for 'v2'.".
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, cktCoreContext, "fix", "start", "v2" )).ShouldBeFalse();
            logs.ShouldContain( "Unable to find any version to fix for 'v2'." );
        }

        // v1.0 is the last stable. No way.
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, cktCoreContext, "fix", "start", "v1.0" )).ShouldBeFalse();
            logs.ShouldContain( """
                The version to fix 'v1.0.0' is the current last stable version.
                Use the regular 'ckli build/publish' or 'ckli ci build/publish' workflows to produce a fix.
                """ );
        }

        // Let's build a v1.1 of CKt-Core. The commit message that starts with "feat:" (conventional commit) triggers
        // a minor's increment.
        // We don't need to publish here. The fact that the v1.1 is not published is not relevant for the fix.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, cktCoreContext, "checkout", "dev/stable" )).ShouldBeTrue();
        TestHelper.TouchAndCommit( cktCoreContext.CurrentDirectory, "dev/stable", "feat: some feature." );
        (await CKliCommands.ExecAsync( TestHelper.Monitor, cktCoreContext, "build" )).ShouldBeTrue();

        // Updates the packages available locally.
        var afterProductionBuild = Directory.EnumerateFiles( localNuGetFeed )
                                            .Select( p => Path.GetFileName( p ) )
                                            .Order()
                                            .ToArray();
        afterProductionBuild.Except( initialPackages ).ShouldBe(
            [
              "CKt.ActivityMonitor.0.2.0.nupkg",
              "CKt.Core.1.1.0.nupkg",
              "CKt.Monitoring.0.3.0.nupkg",
              "CKt.PerfectEvent.0.4.0.nupkg"
            ] );

        // Now we can do a: ckli fix start v1.0
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, cktCoreContext, "fix", "start", "v1.0" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            Fixing 'v1.0.0' on CKt-Core:
            0 - CKt-Core            -> 1.0.1 (fix/v1.0) 
            1 - CKt-ActivityMonitor -> 0.1.1 (fix/v0.1) 
            2 - CKt-PerfectEvent    -> 0.2.2 (fix/v0.2) 
            3 - CKt-PerfectEvent    -> 0.3.3 (fix/v0.3) 
            4 - CKt-Monitoring      -> 0.2.4 (fix/v0.2) 
            ❰✓❱

            """ );

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, cktCoreContext, "fix", "info" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            Fixing 'v1.0.0' on CKt-Core:
            0 - CKt-Core            -> 1.0.1 (fix/v1.0) 
            1 - CKt-ActivityMonitor -> 0.1.1 (fix/v0.1) 
            2 - CKt-PerfectEvent    -> 0.2.2 (fix/v0.2) 
            3 - CKt-PerfectEvent    -> 0.3.3 (fix/v0.3) 
            4 - CKt-Monitoring      -> 0.2.4 (fix/v0.2) 
            ❰✓❱

            """ );

        // ckli fix build
        // This applies the Net8 Migration: there is a change in the code base, so we build the fixes. 
        using( TestHelper.Monitor.OpenInfo( "First 'ckli fix build' => triggers the Net8 migration (this handles NuGet.config => nuget.config)." ) )
        {
            // ckli fix build
            // This applies the Net8 Migration: there is a change in the code base, so we build the fixes. 
            (await CKliCommands.ExecAsync( TestHelper.Monitor, cktCoreContext, "fix", "build" )).ShouldBeTrue();

            var files = Directory.EnumerateFiles( localNuGetFeed )
                             .Select( p => Path.GetFileName( p ) )
                             .Except( afterProductionBuild )
                             .Order()
                             .ToArray();
            // The 2 CKt.PerfectEvent have a commit depth of 3 because:
            //
            // 1 - Starting 'fix/v0.3' (this commit can be amended).
            // 2 - Net8 migration applied.
            // 3 - Updates: CKt.ActivityMonitor: 0.1.0 -> 0.1.1-local.fix.2
            //
            files.ShouldBe( [
                    "CKt.ActivityMonitor.0.1.1-local.fix.2.nupkg",
                    "CKt.Core.1.0.1-local.fix.2.nupkg",
                    "CKt.Monitoring.0.2.4-local.fix.2.nupkg",
                    "CKt.PerfectEvent.0.2.2-local.fix.3.nupkg",
                    "CKt.PerfectEvent.0.3.3-local.fix.3.nupkg"
                    ] );

        }

        TestHelper.TouchAndCommit( cktCoreContext.CurrentDirectory.Combine( "../CKt-ActivityMonitor/CKt.ActivityMonitor" ), "fix/v0.1" );

        using( TestHelper.Monitor.OpenInfo( "Second 'ckli fix build' (CKt.ActivityMonitor has changed)." ) )
        {
            using( TestHelper.Monitor.CollectTexts( out var logs ) )
            {
                (await CKliCommands.ExecAsync( TestHelper.Monitor, cktCoreContext, "fix", "build" )).ShouldBeTrue();
                logs.ShouldContain( "Useless build for 'CKt-Core/1.0.1-local.fix.2' skipped." );
            }

            var files = Directory.EnumerateFiles( localNuGetFeed )
                                 .Select( p => Path.GetFileName( p ) )
                                 .Except( afterProductionBuild )
                                 .Order()
                                 .ToArray();
            files.ShouldBe( [
                    "CKt.ActivityMonitor.0.1.1-local.fix.2.nupkg",
                    "CKt.ActivityMonitor.0.1.1-local.fix.3.nupkg",
                    "CKt.Core.1.0.1-local.fix.2.nupkg",
                    "CKt.Monitoring.0.2.4-local.fix.2.nupkg",
                    "CKt.Monitoring.0.2.4-local.fix.3.nupkg",
                    "CKt.PerfectEvent.0.2.2-local.fix.3.nupkg",
                    "CKt.PerfectEvent.0.2.2-local.fix.4.nupkg",
                    "CKt.PerfectEvent.0.3.3-local.fix.3.nupkg",
                    "CKt.PerfectEvent.0.3.3-local.fix.4.nupkg"
                ] );
        }

        using( TestHelper.Monitor.OpenInfo( "Third 'ckli fix build' (no change in the code base: there's nothing to fix)." ) )
        {
            // ckli fix build
            using( TestHelper.Monitor.CollectTexts( out var logs ) )
            {
                (await CKliCommands.ExecAsync( TestHelper.Monitor, cktCoreContext, "fix", "build" )).ShouldBeTrue();
                logs.ShouldContain( "Useless build for 'CKt-Core/1.0.1-local.fix.2' skipped." );
                logs.ShouldContain( "Useless build for 'CKt-ActivityMonitor/0.1.1-local.fix.3' skipped." );
                logs.ShouldContain( "Useless build for 'CKt-PerfectEvent/0.2.2-local.fix.4' skipped." );
                logs.ShouldContain( "Useless build for 'CKt-PerfectEvent/0.3.3-local.fix.4' skipped." );
                logs.ShouldContain( "Useless build for 'CKt-Monitoring/0.2.4-local.fix.3' skipped." );
            }
        }

        // ckli fix cancel
        (await CKliCommands.ExecAsync( TestHelper.Monitor, cktCoreContext, "fix", "cancel" )).ShouldBeTrue();

        // ckli fix start v1.0
        (await CKliCommands.ExecAsync( TestHelper.Monitor, cktCoreContext, "fix", "start", "v1.0" )).ShouldBeTrue();

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, cktCoreContext, "fix", "info" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            Fixing 'v1.0.0' on CKt-Core:
            0 - CKt-Core            -> 1.0.1 (fix/v1.0) 
            1 - CKt-ActivityMonitor -> 0.1.1 (fix/v0.1) 
            2 - CKt-PerfectEvent    -> 0.2.2 (fix/v0.2) 
            3 - CKt-PerfectEvent    -> 0.3.3 (fix/v0.3) 
            4 - CKt-Monitoring      -> 0.2.4 (fix/v0.2) 
            ❰✓❱

            """ );
    }

    [Test]
    public async Task CKt_add_sample_and_ci_Async()
    {
        Helper.SetFileSystemWritePAT();
        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(initialized)" );
        var context = remotes.Clone( clonedFolder, Helper.ConfigureFakeFeeds );
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
            │ │ Can be fixed by creating it from 'main'.
            > Samples/CKt-App-Sample (1)
            │ > Missing root branch 'stable'.
            │ │ Can be fixed by creating it from 'main'.
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
            │ │ This can be fixed by creating a 'v0.0.0+fake' on 'stable' branch.
            > Samples/CKt-App-Sample (2)
            │ > Content issues.
            │ │ Branch: stable (1 content issue)
            │ │ > File 'nuget.config' must be created.
            │ > Missing initial version.
            │ │ This can be fixed by creating a 'v0.0.0+fake' on 'stable' branch.
            ❰✓❱

            """ );
        // ... so we commit.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inSampleFolder, "commit", "Initialized CKt-Sample-Monitoring and CKt-App-Sample." )).ShouldBeTrue();

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
            1 -  CKt-Core                      v1.0.0      → v1.0.1--ci.3 🡡 (CodeChange)               
            2 -  CKt-ActivityMonitor           v0.1.0      → v0.1.1--ci.4 🡡 (UpstreamBuild, CodeChange)
            3 ╓  CKt-PerfectEvent              v0.3.2      → v0.3.3--ci.4 🡡 (UpstreamBuild, CodeChange)
            4 ║  CKt-Monitoring                v0.2.3      → v0.2.4--ci.4 🡡 (UpstreamBuild, CodeChange)
            5 ╙  Samples/CKt-App-Sample        v0.0.0+fake → v0.0.1--ci.3 🡡 (UpstreamBuild, CodeChange)
            6 -  Samples/CKt-Sample-Monitoring v0.0.0+fake → v0.0.1--ci.3 🡡 (UpstreamBuild, CodeChange)
            Required build for 6 repositories across the 6 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            🡡 6 repositories can be published.
            ❰✓❱

            """ );

        // Everything has been built but nothing has been published.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "ci", "build" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            -  CKt-Core                      v1.0.1--ci.3 🡡
            -  CKt-ActivityMonitor           v0.1.1--ci.4 🡡
            ╓  CKt-PerfectEvent              v0.3.3--ci.4 🡡
            ║  CKt-Monitoring                v0.2.4--ci.4 🡡
            ╙  Samples/CKt-App-Sample        v0.0.1--ci.3 🡡
            -  Samples/CKt-Sample-Monitoring v0.0.1--ci.3 🡡
            There is nothing to build across the 6 repositories.
            🡡 6 repositories can be published.
            ❰✓❱
            
            """ );


        // Now we publish.
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "ci", "publish" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            -  CKt-Core                      v1.0.1--ci.3 🡡
            -  CKt-ActivityMonitor           v0.1.1--ci.4 🡡
            ╓  CKt-PerfectEvent              v0.3.3--ci.4 🡡
            ║  CKt-Monitoring                v0.2.4--ci.4 🡡
            ╙  Samples/CKt-App-Sample        v0.0.1--ci.3 🡡
            -  Samples/CKt-Sample-Monitoring v0.0.1--ci.3 🡡
            There is nothing to build across the 6 repositories.
            🡡 6 repositories must be published.
            ❰✓❱
            
            """ );

        var (nugetOrgFeed, sosFeed) = Helper.GetFakeFeedPaths( clonedFolder.Path );

        // CI build: nuget.org is not concerned: out fake nuget.org oly contains the canary package.
        Directory.GetDirectories( nugetOrgFeed ).Select( p => Path.GetFileName( p ) ).ShouldBe( ["ck.canarypackage"] );

        // The other feed has the packages.
        var existingPackages = Directory.GetDirectories( sosFeed )
                                        .SelectMany( p => Directory.GetDirectories( p )
                                                                   .Select( pp => new PackageInstance( Path.GetFileName( p ),
                                                                                                       SVersion.Parse( Path.GetFileName( pp ) ) ) ) );
        existingPackages.Select( p => p.ToString() )
                        .ShouldBe( ["ck.canarypackage@1.0.0",
                                    "ckt.activitymonitor@0.1.1--ci.4",
                                    "ckt.core@1.0.1--ci.3",
                                    "ckt.monitoring@0.2.4--ci.4",
                                    "ckt.perfectevent@0.3.3--ci.4",
                                    "ckt.sample.monitoring@0.0.1--ci.3",
                                    "ckt.someapp@0.0.1--ci.3"], ignoreOrder: true );

        // The FileSystemHostingProvider received the asset files.
        var appRemoteReleases = Path.Combine( TestHelper.CKliRemotesPath, "bare", remotes.FullName, "CKt-App-Sample", "Releases" );
        Directory.GetFiles( appRemoteReleases, "*", SearchOption.AllDirectories )
                 .Select( p => new NormalizedPath( p ) )
                 .Select( p => p.RemoveParts( 0, p.Parts.Count - 2 ).ToString() )
                 .ShouldBe( ["v0.0.1--ci.3/ZipDemo.zip"] );

        var sampleRemoteReleases = Path.Combine( TestHelper.CKliRemotesPath, "bare", remotes.FullName, "CKt-Sample-Monitoring", "Releases" );
        Directory.GetFiles( sampleRemoteReleases, "*", SearchOption.AllDirectories )
                 .Select( p => new NormalizedPath( p ) )
                 .Select( p => p.RemoveParts( 0, p.Parts.Count - 2 ).ToString() )
                 .ShouldBe( ["v0.0.1--ci.3/Install-0.0.1--ci.3.txt"] );

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


}
