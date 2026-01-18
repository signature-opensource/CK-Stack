using CK.Core;
using CKli;
using CKli.Core;
using NUnit.Framework;
using Shouldly;
using System;
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
    public async Task CKtInit_migrate_Net8_fix_issues_and_create_a_fix_Async()
    {
        var context = TestEnv.EnsureCleanFolder();
        var remotes = TestEnv.OpenRemotes( "CKt(init)" );

        // ckli clone file:///.../CKt-Stack
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "clone", remotes.StackUri, "--ignore-parent-stack" )).ShouldBeTrue();
        // cd CKt
        context = context.ChangeDirectory( "CKt" );
        TestEnv.ConfigureStackPlugins( context );

        // ckli migrate net8
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "migrate", "net8" )).ShouldBeTrue();
        // ckli issue --fix
        // Note that this works only because the repository order is the dependency order of the new "stable" branch
        // because there is no "CKt.*" packages in real remote feeds.
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue", "--fix" )).ShouldBeTrue();

        // ckli release-database rebuild
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "release-database", "rebuild" )).ShouldBeTrue();

        // ckli branch push stable
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "branch", "push", "stable" )).ShouldBeTrue();

    }
}
