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
using System.Threading;
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
        //await CKt_init_Async();
        TestHelper.CKliCreateRemoteFolderFromCloned( "CKt_init_Async", "CKt", "(initialized)" );
    }

    /// <summary>
    /// <see cref="CKli.BranchModel.Plugin.BranchModelPlugin.FixStart"/>
    /// <see cref="CKli.BranchModel.Plugin.BranchModelPlugin.FixInfo"/>
    /// <see cref="CKli.Build.Plugin.BuildPlugin.FixBuildAsync"/>
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

        var localNuGetFeed = context.CurrentStackPath.Combine( "$Local/CKt/NuGet" );

        using( TestHelper.Monitor.OpenInfo( "First 'ckli fix build' => triggers the Net8 migration." ) )
        {
            // ckli fix build
            // This applies the Net8 Migration: there is a change in the code base, so we build the fixes. 
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "fix", "build" )).ShouldBeTrue();

            var files = Directory.EnumerateFiles( localNuGetFeed )
                                 .Select( p => Path.GetFileName( p ) )
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

        using( TestHelper.Monitor.OpenInfo( "Second 'ckli fix build' (CKt.ActivityMonitor has changed)." ) )
        {
            ModifyAndCreateCommit( context, "../CKt-ActivityMonitor/CKt.ActivityMonitor", "fix/v0.1" );

            using( TestHelper.Monitor.CollectTexts( out var logs ) )
            {
                (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "fix", "build" )).ShouldBeTrue();
                logs.ShouldContain( "Useless build for 'CKt-Core/1.0.1-local.fix.2' skipped." );
            }

            var files = Directory.EnumerateFiles( localNuGetFeed )
                                 .Select( p => Path.GetFileName( p ) )
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

        // ckli fix build
        // There is no change in the code base: there's nothing to fix.
        using( TestHelper.Monitor.CollectTexts( out var logs ) )
        {
            (await CKliCommands.ExecAsync( TestHelper.Monitor, context, "fix", "build" )).ShouldBeTrue();
            logs.ShouldContain( "Useless build for 'CKt-Core/1.0.1-local.fix.2' skipped." );
            logs.ShouldContain( "Useless build for 'CKt-ActivityMonitor/0.1.1-local.fix.3' skipped." );
            logs.ShouldContain( "Useless build for 'CKt-PerfectEvent/0.2.2-local.fix.4' skipped." );
            logs.ShouldContain( "Useless build for 'CKt-PerfectEvent/0.3.3-local.fix.4' skipped." );
            logs.ShouldContain( "Useless build for 'CKt-Monitoring/0.2.4-local.fix.3' skipped." );
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

        ModifyAndCreateCommit( context, "CKt.Core", "fix/v1.0" );


    }


    /// <summary>
    /// Create or modify a "CKliTestModification.cs" file in the <paramref name="projectFolder"/> and
    /// creates a new commit on a specified branch or on the currently checked out branch.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="projectFolder">The project  folder (eg. "CKt.Core") relative to <see cref="CKliEnv.CurrentDirectory"/> (can be absolute).</param>
    /// <param name="branchName">The branch name to update (or null to touch the working folder and commit on the current repository head).</param>
    /// <param name="commitMessage">Optional commit message.</param>
    static void ModifyAndCreateCommit( CKliEnv context, NormalizedPath projectFolder, string? branchName, string? commitMessage = null )
    {
        var projectPath = context.CurrentDirectory.Combine( projectFolder ).ResolveDots();

        if( string.IsNullOrEmpty( commitMessage ) )
        {
            commitMessage = $"// Touching {projectFolder.LastPart}.";
        }

        NormalizedPath gitPath = Repository.Discover( projectPath );
        if( gitPath.IsEmptyPath )
        {
            if( !Directory.Exists( projectPath ) )
            {
                Throw.ArgumentException( nameof( projectFolder ), $"""
                    Path '{projectPath}' doesn't exist. It has been combined from:
                    context.CurrentDirectory = '{context.CurrentDirectory}'
                    and:
                    projectFolder: '{projectFolder}'
                    """ );
            }
            Throw.ArgumentException( nameof( projectFolder ), $"Unable to find the .git folder from '{projectPath}'." );
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
                if( !b.IsCurrentRepositoryHead )
                {
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
                    return;
                }
            }
            // Either the branchName is null (the user wants to work in the head) or the
            // branch is the one currently checked out: use the working folder.
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
