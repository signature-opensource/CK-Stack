using CK.Core;
using CKli;
using CKli.Core;
using LibGit2Sharp;
using NUnit.Framework;
using Shouldly;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

[TestFixture]
public class InitializationTests
{
    /// <summary>
    /// When a test DOESN'T push (it has no impact on the remotes), we remove
    /// the secret: this ensures that the test doesn't push anything.
    /// </summary>
    void RemoveFileSystemWritePAT()
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
    void SetFileSystemWritePAT()
    {
        ProcessRunner.RunProcess( TestHelper.Monitor,
                                  "dotnet",
                                  """user-secrets set FILESYSTEM_GIT "don't care" --id CKli-CK""",
                                  Environment.CurrentDirectory )
                     .ShouldBe( 0 );
    }

    /// <summary>
    /// <see cref="CKli.Net8Migration.Plugin.Net8MigrationPlugin.Migrate"/>
    /// <see cref="CKli.VersionTag.Plugin.VersionTagPlugin.RebuildReleaseDatabases"/>
    /// </summary>
    /// <returns></returns>
    [Test]
    public async Task CKt_init_Async()
    {
        SetFileSystemWritePAT();

        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(init)" );
        var context = remotes.Clone( clonedFolder );

        // ckli migrate net8
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "migrate", "net8" )).ShouldBeTrue();

        // ckli issue --fix
        // Note that this works only because the repository order is the dependency order of the new "stable" branch
        // because there is no "CKt.*" packages in real remote feeds.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue", "--fix" )).ShouldBeTrue();

        // ckli maintenance release-database rebuild
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "maintenance", "release-database", "rebuild" )).ShouldBeTrue();

        // ckli branch push stable
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "branch", "push", "stable" )).ShouldBeTrue();
    }

    [Explicit]
    [Test]
    public async Task REMOTES_CKt_init_to_initialized_Async()
    {
        FileHelper.DeleteFolder( TestHelper.Monitor, TestHelper.CKliRemotesPath.AppendPart( "CKt(initialized)" ) );
        await CKt_init_Async();
        TestHelper.CKliCreateRemoteFolderFromCloned( "CKt_init_Async", "CKt", "(initialized)" );
    }

    [Test]
    public async Task CKt_add_sample_Async()
    {
        SetFileSystemWritePAT();
        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(initialized)" );
        var context = remotes.Clone( clonedFolder );
        var display = (StringScreen)context.Screen;

        var newRepo = TestHelper.CKliRemotesPath.AppendPart( "bare" ).Combine( "CKt(initialized)/CKt-Sample-Monitoring" );
        var newRepoUrl = $"file://{newRepo}";
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "maintenance", "hosting", "create", newRepoUrl )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "repo", "add", newRepoUrl )).ShouldBeTrue();

        var inSample = context.ChangeDirectory( "CKt-Sample-Monitoring" );
        Directory.Exists( inSample.CurrentDirectory ).ShouldBeTrue();
        var path = inSample.CurrentDirectory.AppendPart( "CKt.Sample.Monitoring" );
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

        (await CKliCommands.ExecAsync( TestHelper.Monitor, inSample, "exec", "dotnet", "new", "sln" )).ShouldBeTrue();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, inSample, "exec", "dotnet", "sln", "add", "CKt.Sample.Monitoring/CKt.Sample.Monitoring.csproj" )).ShouldBeTrue();


        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            > CKt-Sample-Monitoring (1)
            │ > Missing root branch 'stable'.
            │ │ Can be fixed by creating it from 'master'.
            ❰✓❱

            """ );
        // This one can be fixed with a dirty folder. 
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue", "--fix" )).ShouldBeTrue();
        display.Clear();


        // This one cannot be fixed with a dirty folder. 
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            > CKt-Sample-Monitoring (1)
            │ > Missing initial version.
            │ │ This can be fixed by building the 'v0.0.0' version from 'stable' branch.
            ❰✓❱

            """ );

        using( var r = new Repository( inSample.CurrentDirectory ) )
        {
            Commands.Stage( r, "*" );
            r.Commit( "Initialized files.", context.Committer, context.Committer );
        }
        // Here we need to generate the nuget.config file.
        // The CommonFiles and the BranchModel/HotBranch/ContentIssue should handle this.
        //(await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue", "--fix" )).ShouldBeTrue();
        //display.Clear();
        //(await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue" )).ShouldBeTrue();
        //display.ToString().ShouldBe( """
        //    ❰✓❱

        //    """ );
    }


    /// <summary>
    /// <see cref="CKli.BranchModel.Plugin.BranchModelPlugin.FixStart"/>
    /// <see cref="CKli.BranchModel.Plugin.BranchModelPlugin.FixInfo"/>
    /// <see cref="CKli.Build.Plugin.BuildPlugin.FixBuildAsync"/>
    /// </summary>
    /// <returns></returns>
    [Test]
    public async Task CKt_local_fix_Async()
    {
        RemoveFileSystemWritePAT();
        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(initialized)" );
        var context = remotes.Clone( clonedFolder );

        // cd CK-Core.
        context = context.ChangeDirectory( "CKt-Core" );
        var display = (StringScreen)context.Screen;

        // From CKt_init:
        var localNuGetFeed = context.CurrentStackPath.Combine( "$Local/CKt/NuGet" );
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


        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "fix", "start", "v2" )).ShouldBeFalse();
            logs.ShouldContain( "Unable to find any version to fix for 'v2'." );
        }

        // ckli fix start v1.0
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "fix", "start", "v1.0" )).ShouldBeTrue();

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "fix", "info" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            Fixing 'v1.0.0' on CKt-Core:
            0 - CKt-Core            -> 1.0.1 (fix/v1.0) 
            1 - CKt-ActivityMonitor -> 0.1.1 (fix/v0.1) 
            2 - CKt-PerfectEvent    -> 0.2.2 (fix/v0.2) 
            3 - CKt-PerfectEvent    -> 0.3.3 (fix/v0.3) 
            4 - CKt-Monitoring      -> 0.2.4 (fix/v0.2) 
            ❰✓❱

            """ );


        using( TestHelper.Monitor.OpenInfo( "First 'ckli fix build' => triggers the Net8 migration." ) )
        {
            // ckli fix build
            // This applies the Net8 Migration: there is a change in the code base, so we build the fixes. 
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "fix", "build" )).ShouldBeTrue();

            var files = Directory.EnumerateFiles( localNuGetFeed )
                                 .Select( p => Path.GetFileName( p ) )
                                 .Except( initialPackages )
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

        TestHelper.ModifyAndCreateCommit( context, "../CKt-ActivityMonitor/CKt.ActivityMonitor", "fix/v0.1" );

        using( TestHelper.Monitor.OpenInfo( "Second 'ckli fix build' (CKt.ActivityMonitor has changed)." ) )
        {
            using( TestHelper.Monitor.CollectTexts( out var logs ) )
            {
                (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "fix", "build" )).ShouldBeTrue();
                logs.ShouldContain( "Useless build for 'CKt-Core/1.0.1-local.fix.2' skipped." );
            }

            var files = Directory.EnumerateFiles( localNuGetFeed )
                                 .Select( p => Path.GetFileName( p ) )
                                 .Except( initialPackages )
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
                (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "fix", "build" )).ShouldBeTrue();
                logs.ShouldContain( "Useless build for 'CKt-Core/1.0.1-local.fix.2' skipped." );
                logs.ShouldContain( "Useless build for 'CKt-ActivityMonitor/0.1.1-local.fix.3' skipped." );
                logs.ShouldContain( "Useless build for 'CKt-PerfectEvent/0.2.2-local.fix.4' skipped." );
                logs.ShouldContain( "Useless build for 'CKt-PerfectEvent/0.3.3-local.fix.4' skipped." );
                logs.ShouldContain( "Useless build for 'CKt-Monitoring/0.2.4-local.fix.3' skipped." );
            }
        }

        // ckli fix cancel
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "fix", "cancel" )).ShouldBeTrue();

        // ckli fix start v1.0
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "fix", "start", "v1.0" )).ShouldBeTrue();

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "fix", "info" )).ShouldBeTrue();
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

    [Explicit]
    [Test]
    public async Task REMOTES_CKt_initialized_to_localFixed_Async()
    {
        FileHelper.DeleteFolder( TestHelper.Monitor, TestHelper.CKliRemotesPath.AppendPart( "CKt(localFixed)" ) );
        await CKt_local_fix_Async();
        TestHelper.CKliCreateRemoteFolderFromCloned( "CKt_local_fix_Async", "CKt", "(localFixed)" );
    }


    [Test]
    [Explicit]
    public async Task on_CK_Async()
    {
        TestHelper.Monitor.Info( $"Initializing '{TestHelper.CKliDefaultWorldName}', test instance name: '{CKliRootEnv.InstanceName}'." );
        var context = CKliRootEnv.DefaultCKliEnv.ChangeDirectory( TestHelper.SolutionFolder.RemoveLastPart() );
        await CKliCommands.ExecAsync( TestHelper.Monitor, context, "build", "--branch", "stable" );
        var display = (StringScreen)context.Screen;

    }

    [Test]
    public async Task CKt_build_Async()
    {
        // Because we are NOT pushing here, we remove the secret: this ensures
        // that this test doesn't push anything.
        // "ckli build" is purely local, it has no impacts on the remotes.
        ProcessRunner.RunProcess( TestHelper.Monitor,
                                  "dotnet",
                                  """user-secrets remove FILESYSTEM_GIT --id CKli-CK""",
                                  Environment.CurrentDirectory )
                     .ShouldBe( 0 );

        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(initialized)" );
        var context = remotes.Clone( clonedFolder );

        // From CKt_init:
        var localNuGetFeed = context.CurrentStackPath.Combine( "$Local/CKt/NuGet" );
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

        // cd CK-Core.
        context = context.ChangeDirectory( "CKt-Core" );
        var display = (StringScreen)context.Screen;

        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "build" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            > Rank 0 (1)
            │   CKt-Core 
            > Rank 1 (1)
            │   CKt-ActivityMonitor 
            > Rank 2 (2)
            │   CKt-PerfectEvent 
            │   CKt-Monitoring 
            ❰✓❱

            """ );


    }
}
