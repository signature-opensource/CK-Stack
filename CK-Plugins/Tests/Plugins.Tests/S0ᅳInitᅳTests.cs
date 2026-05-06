using CKli;
using CKli.Core;
using NUnit.Framework;
using Shouldly;
using System.Threading.Tasks;
using static CK.Core.ActivityMonitorErrorCounter;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

[TestFixture]
public class S0ᅳInitᅳTests
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
            │ │ can try to build them and sets a "+invalid" tag on failure.
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
            │ │ can try to build them and sets a "+invalid" tag on failure.
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
            │ │ can try to build them and sets a "+invalid" tag on failure.
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
            │ │ can try to build them and sets a "+invalid" tag on failure.
            ❰✓❱

            """ );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue", "--fix" )).ShouldBeTrue();

        // ckli maintenance release-database rebuild
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "maintenance", "release-database", "rebuild" )).ShouldBeTrue();

        // ckli branch push stable
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "branch", "push", "stable" )).ShouldBeTrue();

        display.Clear();
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "ci", "build", "--dry-run" )).ShouldBeTrue();
        display.ToString().ShouldBe( """
            1 -  CKt-Core            v1.0.0 → v1.0.1--ci.3 🡡 (CodeChange)               
            2 -  CKt-ActivityMonitor v0.1.0 → v0.1.1--ci.4 🡡 (UpstreamBuild, CodeChange)
            3 ╓  CKt-PerfectEvent    v0.3.2 → v0.3.3--ci.4 🡡 (UpstreamBuild, CodeChange)
            4 ╙  CKt-Monitoring      v0.2.3 → v0.2.4--ci.4 🡡 (UpstreamBuild, CodeChange)
            Required build for 4 repositories across the 4 repositories.
            (No dependency updates other than the ones from the upstreams are needed.)
            🡡 4 repositories can be published.
            ❰✓❱

            """ );
    }

    [Explicit]
    [Test]
    public async Task REMOTES_CKt_init_to_initialized_Async()
    {
        FileHelper.DeleteFolder( TestHelper.Monitor, TestHelper.CKliRemotesPath.AppendPart( "CKt(initialized)" ) );
        await CKt_init_Async();
        TestHelper.CKliCreateRemoteFolderFromCloned( "CKt_init_Async", "CKt", "(initialized)" );
    }
}
