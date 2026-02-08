using CK.Core;
using CKli;
using CKli.Core;
using LibGit2Sharp;
using NUnit.Framework;
using Shouldly;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;
using static System.Net.WebRequestMethods;

namespace Plugins.Tests;

[TestFixture]
public class InitializationTests
{
    [SetUp]
    public void Setup()
    {
        // Because we are pushing here, we need the Write PAT for the "FILESYSTEM"
        // That is useless (credentials are not used on local file system) but it's
        // good to not make an exception for this case.
        ProcessRunner.RunProcess( TestHelper.Monitor,
                                  "dotnet",
                                  """user-secrets remove FILESYSTEM_GIT_WRITE_PAT --id CKli-CK""",
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
        // Because we are pushing here, we need the Write PAT for the "FILESYSTEM"
        // That is useless (credentials are not used on local file system) but it's
        // good to not make an exception for this case.
        ProcessRunner.RunProcess( TestHelper.Monitor,
                                  "dotnet",
                                  """user-secrets set FILESYSTEM_GIT_WRITE_PAT "don't care" --id CKli-CK""",
                                  Environment.CurrentDirectory )
                     .ShouldBe( 0 );

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

    /// <summary>
    /// <see cref="CKli.BranchModel.Plugin.BranchModelPlugin.FixStart"/>
    /// <see cref="CKli.BranchModel.Plugin.BranchModelPlugin.FixInfo"/>
    /// <see cref="CKli.Build.Plugin.BuildPlugin.FixBuildAsync"/>
    /// </summary>
    /// <returns></returns>
    [Test]
    public async Task CKt_local_fix_Async()
    {
        // Because we are NOT pushing here, we remove the secret: this ensures
        // that this test doesn't push anything.
        // "ckli fix build" is purely local, it has no impacts on the remotes.
        ProcessRunner.RunProcess( TestHelper.Monitor,
                                  "dotnet",
                                  """user-secrets remove FILESYSTEM_GIT_WRITE_PAT --id CKli-CK""",
                                  Environment.CurrentDirectory )
                     .ShouldBe( 0 );

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

}
