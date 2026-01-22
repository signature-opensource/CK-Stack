using CK.Core;
using CKli;
using CKli.Core;
using LibGit2Sharp;
using Microsoft.Extensions.Primitives;
using NUnit.Framework;
using Shouldly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Loader;
using System.Text;
using System.Text.RegularExpressions;
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
    /// <see cref="CKli.Build.Plugin.BuildPlugin.FixBuild"/>
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

        //// ckli fix build
        //// There is no change in the code base: this is an error as there's nothing to fix.
        //using( TestHelper.Monitor.CollectTexts( out var logs ) )
        //{
        //    (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "fix", "build" )).ShouldBeFalse();
        //    logs.Any( l => Regex.Match( l.ReplaceLineEndings(), """
        //        Invalid build commit '.*' for version 'v1.0.1-local.fix.1' in 'CKt-Core'.
        //        This commit contains the exact same code as the version 'v1.0.0' released on .* by commit '.*'.

        //        If publishing 2 different versions of the exact same code is really what is intended, please alter
        //        any file with a minor modification.
                
        //        """.ReplaceLineEndings() ).Success );
        //}

        //// ckli fix cancel
        //(await CKliCommands.ExecAsync( TestHelper.Monitor, context, "fix", "cancel" )).ShouldBeTrue();

        //// ckli fix start v1.0
        //(await CKliCommands.ExecAsync( TestHelper.Monitor, context, "fix", "start", "v1.0" )).ShouldBeTrue();

        //display.Clear();
        //(await CKliCommands.ExecAsync( TestHelper.Monitor, context, "fix", "info" )).ShouldBeTrue();
        //display.ToString().ShouldBe( """
        //    Fixing 'v1.0.0' on CKt-Core:
        //    0 - CKt-Core            -> 1.0.1 (fix/v1.0) 
        //    1 - CKt-ActivityMonitor -> 0.1.1 (fix/v0.1) 
        //    2 - CKt-PerfectEvent    -> 0.2.2 (fix/v0.2) 
        //    3 - CKt-PerfectEvent    -> 0.3.3 (fix/v0.3) 
        //    4 - CKt-Monitoring      -> 0.2.4 (fix/v0.2) 
        //    ❰✓❱

        //    """ );

        ModifyAndCreateCommit( context, "CKt.Core", "fix/v1.0" );

        (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "fix", "build" )).ShouldBeTrue();
    }


    /// <summary>
    /// Create or modify a "CKliTestModification.cs" file in the <paramref name="projectFolder"/> and
    /// creates a new commit on a specified branch or on the currently checked out branch.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="projectFolder">The project (eg. "CKt.Core") folder relative to <see cref="CKliEnv.CurrentDirectory"/> (can be absolute).</param>
    /// <param name="branchName">The branch name or null to touch the working folder and commit on the current repository head.</param>
    /// <param name="commitMessage">Optional commit message.</param>
    static void ModifyAndCreateCommit( CKliEnv context, NormalizedPath projectFolder, string? branchName, string? commitMessage = null )
    {
        var projectPath = context.CurrentDirectory.Combine( projectFolder );

        if( string.IsNullOrEmpty( commitMessage ) )
        {
            commitMessage = $"// Touching {projectFolder}.";
        }

        NormalizedPath gitPath = Repository.Discover( projectPath );
        if( gitPath.IsEmptyPath )
        {
            Throw.ArgumentException( $"Unable to find the .git folder from '{projectPath}'." );
        }
        gitPath = gitPath.RemoveLastPart();
        using( var git = new Repository( gitPath ) )
        {
            if( branchName != null )
            {
                var b = git.Branches[branchName];
                if( b == null )
                {
                    Throw.ArgumentException( $"Unable to find branch '{branchName}'." );
                }
                TreeDefinition tDef = TreeDefinition.From( b.Tip.Tree );
                gitPath.TryGetRelativePathTo( projectPath, out var relativeProjectPath ).ShouldBeTrue();
                if( tDef[relativeProjectPath] == null )
                {
                    Throw.ArgumentException( $"Unable to find '{relativeProjectPath}' in branch '{branchName}'." );
                }
                var filePath = relativeProjectPath.AppendPart( "CKliTestModification.cs" );

                string text = "// Created";
                TreeEntryDefinition? fileDef = tDef[filePath];
                if( fileDef != null )
                {
                    if( fileDef.TargetType != TreeEntryTargetType.Blob || fileDef.Mode != Mode.NonExecutableFile )
                    {
                        Throw.InvalidOperationException( $"Entry '{filePath}' in branch '{branchName}' is not a non executable Blob." );
                    }
                    var blob = git.Lookup<Blob>( fileDef.TargetId );
                    text = blob.GetContentText() + $"{Environment.NewLine}{DateTime.UtcNow}";
                }
                ObjectId textId = git.ObjectDatabase.Write<Blob>( Encoding.UTF8.GetBytes( text ) );
                tDef.Add( filePath, textId, Mode.NonExecutableFile );
                var newTree = git.ObjectDatabase.CreateTree( tDef );
                var newCommit = git.ObjectDatabase.CreateCommit( context.Committer, context.Committer, commitMessage, newTree, [b.Tip], prettifyMessage: true );
                git.Refs.UpdateTarget( b.Reference, newCommit.Id, null );
            }
            else
            {
                var sourceFilePath = projectPath.AppendPart( "CKliTestModification.cs" );
                if( !File.Exists( sourceFilePath ) )
                {
                    File.WriteAllText( sourceFilePath, "// Created" );
                }
                else
                {
                    File.WriteAllText( sourceFilePath, File.ReadAllText( sourceFilePath ) + $"{Environment.NewLine}{DateTime.UtcNow}" );
                }
                Commands.Stage( git, "*" );
                git.Commit( commitMessage, context.Committer, context.Committer );
            }
        }
    }
}
