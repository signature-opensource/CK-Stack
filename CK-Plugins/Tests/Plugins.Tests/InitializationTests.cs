using CK.Core;
using CKli;
using NUnit.Framework;
using Shouldly;
using System.Runtime.Loader;
using System.Text;
using System.Threading.Tasks;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

[TestFixture]
public class InitializationTests
{
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
        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "issue", "--fix" )).ShouldBeTrue();

    }
}
