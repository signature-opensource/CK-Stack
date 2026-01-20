using CK.Core;
using CKli;
using CKli.Core;
using Microsoft.Extensions.Primitives;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Loader;
using System.Text;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

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
                                  """user-secrets set FILESYSTEM_GIT_WRITE_PAT "don't care" --id CKli-CK""",
                                  Environment.CurrentDirectory )
                     .ShouldBe( 0 );
    }

    /// <summary>
    /// <see cref="CKli.Net8Migration.Plugin.Net8MigrationPlugin.Migrate"/>
    /// <see cref="CKli.Build.Plugin.BuildPlugin.FixVersionTagIssues"/>
    /// <see cref="CKli.VersionTag.Plugin.VersionTagPlugin.RebuildReleaseDatabases"/>
    /// </summary>
    /// <returns></returns>
    [Test]
    public async Task CKt_init_Async()
    {
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
    public async Task CKt_init_to_initialized_Async()
    {
        await CKt_init_Async();
        TestHelper.CKliCreateRemoteFolderFromCloned( "CKt_init_Async", "CKt", "(initialized)" );
    }

    /// <summary>
    /// <see cref="CKli.BranchModel.Plugin.BranchModelPlugin.FixStart"/>
    /// <see cref="CKli.BranchModel.Plugin.BranchModelPlugin.FixInfo"/>
    /// </summary>
    /// <returns></returns>
    [Test]
    public async Task CKt_create_fix_Async()
    {
        var clonedFolder = TestHelper.InitializeClonedFolder();
        var remotes = TestHelper.OpenRemotes( "CKt(initialized)" );
        var context = remotes.Clone( clonedFolder );

        // cd CK-Core.
        context = context.ChangeDirectory( "CKt-Core" );
        var display = (StringScreen)context.Screen;

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

        // ckli fix build
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "fix", "build" )).ShouldBeTrue();

        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "fix", "start", "v2" )).ShouldBeFalse();
            logs.ShouldContain( "Unable to find any version to fix for 'v2'." );
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
}
