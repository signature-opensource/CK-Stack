using CK.Core;
using CKli;
using CKli.Core;
using NUnit.Framework;
using Shouldly;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static CK.Core.ActivityMonitorErrorCounter;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

[TestFixture]
public class InitializationTests
{
    /// <summary>
    /// <see cref="CKli.Net8Migration.Plugin.Net8MigrationPlugin.MigrateNet8"/>
    /// <see cref="CKli.VersionTag.Plugin.VersionTagPlugin.RebuildReleaseDatabases"/>
    /// </summary>
    /// <returns></returns>
    [Test]
    public async Task CKt_init_Async()
    {
        Helper.SetFileSystemWritePAT();

        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(init)" );
        var context = remotes.Clone( clonedFolder );
        var display = (StringScreen)context.Screen;

        // ckli migrate net8
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "maintenance", "migrate", "net8" )).ShouldBeTrue();

        // The "migrate net8" leaves a possibly useless "dev/stable" branch in the repositories and
        // this is intended (for the real life). This is a BranchModel issue:
        //
        // │ │ Removable branches.
        // │ │ - 'dev/stable' is merged into 'stable'.
        // │ │ It can be deleted.
        //
        // And this would prevent the BranchModel ContentIssue to be raised and the NuGet.config => nuget.config to be discovered.
        // Unfortunately, on Linux, the "NuGet.config" casing fails the builds...
        // To make this test pass on Linux, we must remove the "Removable branches" issue.
        // We may have handled this specifically here but instead, this mad us considered that the "useless branch" issue is a
        // "weak issue": they don't prevent the ContentIssue to be raised and this is a real improvement because this really is
        // a "weak issue".
        //
        // ckli issue --fix
        //
        // This fix the lightweight to annotated tags issues by recompiling the tagged commits.
        // Note that this works only because the repository order is the dependency order of the new "stable" branch
        // because there is no "CKt.*" packages in real remote feeds.
        //
        // This renames all "NuGet.config" files to "nuget.config": this is  the work of the CommonFiles plugin and
        // the BranchModel/HotBranch/ContentIssue.
        //
        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            > CKt-Core (3)
            │ > Removable branches.
            │ │ - 'dev/stable' is merged into 'stable'.
            │ │ It can be deleted.
            │ > Content issues.
            │ │ > Branch: stable (1 content issue)
            │ │ │ > File must be moved: NuGet.config → nuget.config (case differ)
            │ > 1 lightweight tags must be transformed to annotated tags.
            │ │ 1.0.0
            │ │ 
            │ │ Fixing these tags recompiles the commit to obtain the consumed/produced packages and asset files.
            │ │ On success, the tag content is updated.
            │ │ When the commit cannot be successfully recompiled, the command 'ckli maintenance rebuild old'
            │ │ can retry and sets a "+deprecated" version on the old commits on failure.
            > CKt-ActivityMonitor (3)
            │ > Removable branches.
            │ │ - 'dev/stable' is merged into 'stable'.
            │ │ It can be deleted.
            │ > Content issues.
            │ │ > Branch: stable (1 content issue)
            │ │ │ > File must be moved: NuGet.config → nuget.config (case differ)
            │ > 1 lightweight tags must be transformed to annotated tags.
            │ │ 0.1.0
            │ │ 
            │ │ Fixing these tags recompiles the commit to obtain the consumed/produced packages and asset files.
            │ │ On success, the tag content is updated.
            │ │ When the commit cannot be successfully recompiled, the command 'ckli maintenance rebuild old'
            │ │ can retry and sets a "+deprecated" version on the old commits on failure.
            > CKt-PerfectEvent (3)
            │ > Removable branches.
            │ │ - 'dev/stable' is merged into 'stable'.
            │ │ It can be deleted.
            │ > Content issues.
            │ │ > Branch: stable (1 content issue)
            │ │ │ > File must be moved: NuGet.config → nuget.config (case differ)
            │ > 4 lightweight tags must be transformed to annotated tags.
            │ │ 0.2.0, 0.2.1, 0.3.0, 0.3.2
            │ │ 
            │ │ Fixing these tags recompiles the commit to obtain the consumed/produced packages and asset files.
            │ │ On success, the tag content is updated.
            │ │ When the commit cannot be successfully recompiled, the command 'ckli maintenance rebuild old'
            │ │ can retry and sets a "+deprecated" version on the old commits on failure.
            > CKt-Monitoring (3)
            │ > Removable branches.
            │ │ - 'dev/stable' is merged into 'stable'.
            │ │ It can be deleted.
            │ > Content issues.
            │ │ > Branch: stable (1 content issue)
            │ │ │ > File must be moved: NuGet.config → nuget.config (case differ)
            │ > 1 lightweight tags must be transformed to annotated tags.
            │ │ 0.2.3
            │ │ 
            │ │ Fixing these tags recompiles the commit to obtain the consumed/produced packages and asset files.
            │ │ On success, the tag content is updated.
            │ │ When the commit cannot be successfully recompiled, the command 'ckli maintenance rebuild old'
            │ │ can retry and sets a "+deprecated" version on the old commits on failure.
            ❰✓❱

            """ );

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

        // cd CK-Core.
        var cktCoreContext = context.ChangeDirectory( "CKt-Core" );
        var display = (StringScreen)cktCoreContext.Screen;

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
        TestHelper.ModifyAndCreateCommit( cktCoreContext, "CKt.Core", "dev/stable", "feat: some feature." );
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

        TestHelper.ModifyAndCreateCommit( cktCoreContext, "../CKt-ActivityMonitor/CKt.ActivityMonitor", "fix/v0.1" );

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

    [Explicit]
    [Test]
    public async Task REMOTES_CKt_initialized_to_localFixed_Async()
    {
        FileHelper.DeleteFolder( TestHelper.Monitor, TestHelper.CKliRemotesPath.AppendPart( "CKt(localFixed)" ) );
        await CKt_local_fix_Async();
        TestHelper.CKliCreateRemoteFolderFromCloned( "CKt_local_fix_Async", "CKt", "(localFixed)" );
    }


    //[Test]
    //[Explicit]
    //public async Task on_CK_Async()
    //{
    //    TestHelper.Monitor.Info( $"Initializing '{TestHelper.CKliDefaultWorldName}', test instance name: '{CKliRootEnv.InstanceName}'." );
    //    var context = CKliRootEnv.DefaultCKliEnv.ChangeDirectory( TestHelper.SolutionFolder.RemoveLastPart() );
    //    await CKliCommands.ExecAsync( TestHelper.Monitor, context, "build", "--branch", "stable" );
    //    var display = (StringScreen)context.Screen;

    //}
}
