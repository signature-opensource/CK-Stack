using CK.Core;
using CKli;
using CKli.BranchModel.Plugin;
using CKli.Core;
using LibGit2Sharp;
using NUnit.Framework;
using Shouldly;
using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using static CK.Testing.MonitorTestHelper;

namespace Plugins.Tests;

[TestFixture]
public class BranchLinkTests
{
    static GitRepository GetGitRepository( [CallerMemberName] string? methodTestName = null )
    {
        var folder = TestHelper.InitializeClonedFolder( methodTestName );
        var gitPath = folder.AppendPart( "SomeRepo" );
        var url = new Uri( folder.AppendPart( $"Fake-Remote" ) );
        var committer = CKliRootEnv.DefaultCKliEnv.Committer;
        // Initialize the git repository with the origin URL
        var git = GitRepository.Init( TestHelper.Monitor,
                                      CKliRootEnv.SecretsStore,
                                      committer,
                                      gitPath,
                                      gitPath.LastPart,
                                      isPublic: true,
                                      url,
                                      "main" )
                               .ShouldNotBeNull();
        // Initial empty commit.
        git.Repository.Commit( "Initial commit automatically created.", committer, committer, new CommitOptions { AllowEmptyCommit = true } );
        return git;
    }

    static void TouchFile( GitRepository repo, string fileNameSuffix = "", string? content = null )
    {
        var p = repo.WorkingFolder.AppendPart( $"_touch_file_{fileNameSuffix}.txt" );
        if( content != null )
        {
            File.WriteAllText( p, content );
        }
        else
        {
            content = Util.GetRandomBase64UrlString( 10 ) + Environment.NewLine;
            if( !File.Exists( p ) )
            {
                File.WriteAllText( p, content );
            }
            else
            {
                File.WriteAllText( p, File.ReadAllText( p ) + content );
            }
        }
    }

    [Test]
    public void playing_with_ahead()
    {
        var monitor = TestHelper.Monitor;
        using var repo = GetGitRepository();

        // Useless Ahead vanishes.
        {
            var mainBranch = repo.GetBranch( monitor, "main" ).ShouldNotBeNull();
            var mainLink = BranchLink.Create( mainBranch, "dev/main" );
            mainLink.Ahead.ShouldBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.None );

            mainLink = mainLink.EnsureAhead( repo ).ShouldNotBeNull();
            mainLink.Ahead.ShouldNotBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.Useless );

            mainLink = mainLink.IntegrateAhead( monitor, repo ).ShouldNotBeNull();
            mainLink.Branch.Tip.Sha.ShouldBe( mainBranch.Tip.Sha, "Empty commit is silently skipped." );
            mainLink.Ahead.ShouldBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.None );
        }

        // Integrating ahead (ahead is checked out).
        {
            var mainBranch = repo.GetBranch( monitor, "main" ).ShouldNotBeNull();
            var mainLink = BranchLink.Create( mainBranch, "dev/main" );
            mainLink.Ahead.ShouldBeNull();

            mainLink = mainLink.EnsureAhead( repo );
            mainLink.Ahead.ShouldNotBeNull();

            repo.Checkout( monitor, mainLink.Ahead ).ShouldBeTrue();
            repo.CurrentBranchName.ShouldBe( "dev/main" );
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.Useless );
            TouchFile( repo, "1" );
            mainLink = mainLink.CommitAhead( monitor, repo, "Some message. (1)" ).ShouldNotBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.None );

            mainLink = mainLink.IntegrateAhead( monitor, repo ).ShouldNotBeNull();
            mainLink.Branch.Tip.Sha.ShouldNotBe( mainBranch.Tip.Sha );
            // When removed, the fact that is was the checked out branch is handled: the base branch
            // is checked out.
            repo.CurrentBranchName.ShouldBe( "main" );
            mainLink.Ahead.ShouldBeNull();

            // main -> +
            //         |\
            //         | + "Some message."
            //         | |
            //         | + "Initializing 'dev/main'.
            //         |/
            //         +
            mainLink.Branch.Tip.Parents.Count().ShouldBe( 2 );
            var inDev = mainLink.Branch.Tip.Parents.Single( c => c.Message.StartsWith( "Some message. (1)" ) );
            inDev = inDev.Parents.Single( c => c.Message.StartsWith( "Initializing 'dev/main'." ) );
            inDev.Parents.Single().Sha.ShouldBe( mainBranch.Tip.Sha );
        }

        // Integrating ahead (while base branch is checked out).
        // => Same as previous.
        {
            var mainBranch = repo.GetBranch( monitor, "main" ).ShouldNotBeNull();
            var mainLink = BranchLink.Create( mainBranch, "dev/main" );
            mainLink.Ahead.ShouldBeNull();

            mainLink = mainLink.EnsureAhead( repo );
            mainLink.Ahead.ShouldNotBeNull();

            repo.Checkout( monitor, mainLink.Ahead ).ShouldBeTrue();
            repo.CurrentBranchName.ShouldBe( "dev/main" );
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.Useless );
            TouchFile( repo, "2" );
            mainLink = mainLink.CommitAhead( monitor, repo, "Some message. (2)" ).ShouldNotBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.None );

            repo.Checkout( monitor, mainLink.Branch ).ShouldBeTrue();

            mainLink = mainLink.IntegrateAhead( monitor, repo ).ShouldNotBeNull();
            mainLink.Branch.Tip.Sha.ShouldNotBe( mainBranch.Tip.Sha );
            // When removed, the fact that is was the checked out branch is handled: the base branch
            // is checked out.
            repo.CurrentBranchName.ShouldBe( "main" );
            mainLink.Ahead.ShouldBeNull();

            // main -> +
            //         |\
            //         | + "Some message."
            //         | |
            //         | + "Initializing 'dev/main'.
            //         |/
            //         +
            mainLink.Branch.Tip.Parents.Count().ShouldBe( 2 );
            var inDev = mainLink.Branch.Tip.Parents.Single( c => c.Message.StartsWith( "Some message. (2)" ) );
            inDev = inDev.Parents.Single( c => c.Message.StartsWith( "Initializing 'dev/main'." ) );
            inDev.Parents.Single().Sha.ShouldBe( mainBranch.Tip.Sha );
        }

        // Integrating ahead (amending the "empty ahead commit").
        {
            var mainBranch = repo.GetBranch( monitor, "main" ).ShouldNotBeNull();
            var mainLink = BranchLink.Create( mainBranch, "dev/main" );
            mainLink.Ahead.ShouldBeNull();

            mainLink = mainLink.EnsureAhead( repo );
            mainLink.Ahead.ShouldNotBeNull();
            repo.Checkout( monitor, mainLink.Ahead );
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.Useless );
            TouchFile( repo, "3" );
            repo.Commit( monitor, "Some message. (3)", CommitBehavior.AmendIfPossibleAndOverwritePreviousMessage ).ShouldBe( CommitResult.Amended );
            mainLink = mainLink.CommitAhead( monitor, repo, "Unused message because there's nothing to commit." ).ShouldNotBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.None );

            mainLink = mainLink.IntegrateAhead( monitor, repo ).ShouldNotBeNull();
            mainLink.Branch.Tip.Sha.ShouldNotBe( mainBranch.Tip.Sha );
            mainLink.Ahead.ShouldBeNull();

            // main -> +
            //         |\
            //         | + "Some message."
            //         |/
            //         +
            mainLink.Branch.Tip.Parents.Count().ShouldBe( 2 );
            var inDev = mainLink.Branch.Tip.Parents.Single( c => c.Message.StartsWith( "Some message. (3)" ) );
            inDev.Parents.Single().Sha.ShouldBe( mainBranch.Tip.Sha );
        }
    }

    [Test]
    public void link_desynchronization()
    {
        var monitor = TestHelper.Monitor;
        using var repo = GetGitRepository();

        // Required to restore a neutral checked out branch (to be able to destroy "main" at the end of each block of tests).
        var root = repo.EnsureBranch( monitor, "root" ).ShouldNotBeNull();
        
        // Regular desynchronization.
        {
            // Ensure ahead (creates the "empty ahead commit").
            var mainLink = BranchLink.Create( repo.EnsureBranch( monitor, "main" ).ShouldNotBeNull(), "dev/main" )
                                     .EnsureAhead( repo )
                                     .ShouldNotBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.Useless );

            // Commit in the base branch: we never commit in the base branch, there's no API for this.
            repo.Checkout( monitor, mainLink.Branch ).ShouldBeTrue();
            TouchFile( repo );
            repo.Commit( monitor, "On Base! (1)", CommitBehavior.CreateNewCommit ).ShouldBe( CommitResult.Commited );

            // Refresh.
            mainLink = mainLink.Refresh( repo ).ShouldNotBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.Desynchronized );

            mainLink = mainLink.SynchronizeAhead( monitor, repo ).ShouldNotBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.Useless );

            mainLink = mainLink.IntegrateAhead( monitor, repo ).ShouldNotBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.None );

            // Cleanup "main" for subsequent tests.
            repo.Checkout( monitor, root ).ShouldBeTrue();
            repo.Repository.Branches.Remove( mainLink.Branch );
            mainLink.Refresh( repo ).ShouldBeNull();
        }

        // Desynchronized and commits in ahead without conflicts: SynchronizeAhead then IntegrateAhead.
        {
            var mainLink = BranchLink.Create( repo.EnsureBranch( monitor, "main" ).ShouldNotBeNull(), "dev/main" )
                                     .EnsureAhead( repo )
                                     .ShouldNotBeNull();
            // Commit in Ahead.
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.Useless );
            repo.Checkout( monitor, mainLink.Ahead.ShouldNotBeNull() ).ShouldBeTrue();
            TouchFile( repo, "One" );
            mainLink = mainLink.CommitAhead( monitor, repo, "Some message. (2)" ).ShouldNotBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.None );

            // Commit in the base branch: we never commit in the base branch, there's no API for this.
            repo.Checkout( monitor, mainLink.Branch ).ShouldBeTrue();
            TouchFile( repo, "Two" );
            repo.Commit( monitor, "On Base! (2)", CommitBehavior.CreateNewCommit ).ShouldBe( CommitResult.Commited );

            // Refresh: Desynchronized!
            mainLink = mainLink.Refresh( repo ).ShouldNotBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.Desynchronized );

            mainLink = mainLink.SynchronizeAhead( monitor, repo ).ShouldNotBeNull();
            mainLink.Ahead.ShouldNotBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.None );

            mainLink = mainLink.IntegrateAhead( monitor, repo ).ShouldNotBeNull();
            mainLink.Ahead.ShouldBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.None );

            // Cleanup "main" for subsequent tests.
            repo.Checkout( monitor, root ).ShouldBeTrue();
            repo.Repository.Branches.Remove( mainLink.Branch );
            mainLink.Refresh( repo ).ShouldBeNull();
        }

        // Desynchronized and commits in ahead without conflicts: IntegrateAhead then SynchronizeAhead.
        {
            var mainLink = BranchLink.Create( repo.EnsureBranch( monitor, "main" ).ShouldNotBeNull(), "dev/main" )
                                     .EnsureAhead( repo )
                                     .ShouldNotBeNull();
            // Commit in Ahead.
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.Useless );
            repo.Checkout( monitor, mainLink.Ahead.ShouldNotBeNull() ).ShouldBeTrue();
            TouchFile( repo, "One" );
            mainLink = mainLink.CommitAhead( monitor, repo, "Some message. (3)" ).ShouldNotBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.None );

            // Commit in the base branch: we never commit in the base branch, there's no API for this.
            repo.Checkout( monitor, mainLink.Branch ).ShouldBeTrue();
            TouchFile( repo, "Two" );
            repo.Commit( monitor, "On Base! (3)", CommitBehavior.CreateNewCommit ).ShouldBe( CommitResult.Commited );

            // Refresh: Desynchronized!
            mainLink = mainLink.Refresh( repo ).ShouldNotBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.Desynchronized );

            mainLink = mainLink.IntegrateAhead( monitor, repo ).ShouldNotBeNull();
            mainLink.Ahead.ShouldBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.None );

            // The head integration solved everything, Synchronize is a no-op.
            var newMainLink = mainLink.SynchronizeAhead( monitor, repo );
            newMainLink.ShouldBeSameAs( mainLink );

            // Cleanup "main" for subsequent tests.
            repo.Checkout( monitor, root ).ShouldBeTrue();
            repo.Repository.Branches.Remove( mainLink.Branch );
            mainLink.Refresh( repo ).ShouldBeNull();
        }

        // When same content is eventually in base and ahead: the link is Useless.
        {
            var mainLink = BranchLink.Create( repo.EnsureBranch( monitor, "main" ).ShouldNotBeNull(), "dev/main" )
                                     .EnsureAhead( repo )
                                     .ShouldNotBeNull();
            // Commit in Ahead.
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.Useless );
            repo.Checkout( monitor, mainLink.Ahead.ShouldNotBeNull() ).ShouldBeTrue();
            TouchFile( repo, content: "Kif Kif" );
            mainLink = mainLink.CommitAhead( monitor, repo, "Some message. (4)" ).ShouldNotBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.None );

            // Commit in the base branch: we never commit in the base branch, there's no API for this.
            repo.Checkout( monitor, mainLink.Branch ).ShouldBeTrue();
            TouchFile( repo, content: "Kif Kif" );
            repo.Commit( monitor, "On Base! (4)", CommitBehavior.CreateNewCommit ).ShouldBe( CommitResult.Commited );

            // Refresh: the contents are the same. Ahead is useless.
            mainLink = mainLink.Refresh( repo ).ShouldNotBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.Useless );

            // No error.
            mainLink = mainLink.SynchronizeAhead( monitor, repo ).ShouldNotBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.Useless );

            mainLink = mainLink.IntegrateAhead( monitor, repo ).ShouldNotBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.None );

            // Cleanup "main" for subsequent tests.
            repo.Checkout( monitor, root ).ShouldBeTrue();
            repo.Repository.Branches.Remove( mainLink.Branch );
            mainLink.Refresh( repo ).ShouldBeNull();
        }

        // Synchronization conflict (manual resolution required).
        {
            var mainLink = BranchLink.Create( repo.EnsureBranch( monitor, "main" ).ShouldNotBeNull(), "dev/main" )
                                     .EnsureAhead( repo )
                                     .ShouldNotBeNull();
            // Commit in Ahead.
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.Useless );
            repo.Checkout( monitor, mainLink.Ahead.ShouldNotBeNull() ).ShouldBeTrue();
            TouchFile( repo );
            mainLink = mainLink.CommitAhead( monitor, repo, "Some message. (5)" ).ShouldNotBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.None );

            // Commit in the base branch: we never commit in the base branch, there's no API for this.
            repo.Checkout( monitor, mainLink.Branch ).ShouldBeTrue();
            TouchFile( repo );
            repo.Commit( monitor, "On Base! (5)", CommitBehavior.CreateNewCommit ).ShouldBe( CommitResult.Commited );

            // Refresh: Desynchronized!
            mainLink = mainLink.Refresh( repo ).ShouldNotBeNull();
            mainLink.Issue.ShouldBe( BranchLink.IssueKind.Desynchronized );

            // But there's a merge conflict that must be manually resolved.
            mainLink.SynchronizeAhead( monitor, repo ).ShouldBeNull();

            // Cleanup "dev/main" and "main" for subsequent tests.
            repo.Checkout( monitor, root ).ShouldBeTrue();
            repo.Repository.Branches.Remove( mainLink.Ahead );
            repo.Repository.Branches.Remove( mainLink.Branch );
            mainLink.Refresh( repo ).ShouldBeNull();
        }

    }

}
