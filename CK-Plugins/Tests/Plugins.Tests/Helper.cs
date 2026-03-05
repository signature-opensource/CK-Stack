using CKli.Core;
using System;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

public static class Helper
{
    /// <summary>
    /// When a test DOESN'T push (it has no impact on the remotes), we remove
    /// the secret: this ensures that the test doesn't push anything.
    /// </summary>
    public static void RemoveFileSystemWritePAT()
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
    public static void SetFileSystemWritePAT()
    {
        ProcessRunner.RunProcess( TestHelper.Monitor,
                                  "dotnet",
                                  """user-secrets set FILESYSTEM_GIT "don't care" --id CKli-CK""",
                                  Environment.CurrentDirectory )
                     .ShouldBe( 0 );
    }

}
