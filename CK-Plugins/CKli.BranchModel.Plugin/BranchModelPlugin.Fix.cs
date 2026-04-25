using CK.Core;
using CK.PerfectEvent;
using CKli.Core;
using CKli.ReleaseDatabase.Plugin;
using CKli.VersionTag.Plugin;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;

namespace CKli.BranchModel.Plugin;

public sealed partial class BranchModelPlugin
{

    /// <summary>
    /// Event raised when a <see cref="FixWorkflow"/> is started (or restarted).
    /// <para>
    /// This enables other plugins to check prepare the commits of the targets' <see cref="FixWorkflow.TargetRepo.BranchName"/>
    /// if needed.
    /// </para>
    /// </summary>
    public PerfectEvent<FixWorkflowStartEventArgs> OnFixStart => _onFixStart.PerfectEvent;


    [Description( "Starts a Fix Workflow. This Repo 'fix/vMajor.Minor' branch is checked out." )]
    [CommandPath( "fix start" )]
    public async Task<bool> FixStartAsync( IActivityMonitor monitor,
                                           CKliEnv context,
                                           [Description( "The Major or Major.Minor version to fix." )]
                                           string version,
                                           [Description( "Allow 'fix/' branches to be moved on the commit to fix if they are not already on it." )]
                                           bool moveBranch )
    {
        // Parses the Major.Minor. Minor is -1 if only Major is specified (we'll consider the max Minor).
        if( !ParseMajorMinor( version, out int major, out int minor ) )
        {
            monitor.Error( $"Invalid version '{version}'. Must be a Major number, Major.Minor numbers optionally prefixed with 'v'." );
            return false;
        }
        var repo = World.GetDefinedRepo( monitor, context.CurrentDirectory );
        if( repo == null )
        {
            return false;
        }
        // It's not that easy here:
        // - We must know the LastStables of this Repo to resolve the base version to fix.
        // - Once we Get the VersionTagInfo for this Repo it is immutable: so we should have all the required tags before.
        // - But we don't want to blindly replace all the local tags with the remote ones: starting a fix should have as
        //   few side-effects as possible.
        // 
        // So we fetch only the "fix/vMajor.*" branches with tags that limits the pulled tags to the minimum and
        // enables us to handle the floating minor.
        //
        // We consider only the "origin" remote here but we COULD allow all remotes.
        //
        var branchSpec = World.Name.LTSName != null
                            ? $"{World.Name.LTSName}/fix/v{major}.*"
                            : $"fix/v{major}.*";
        if( !repo.GitRepository.FetchRemoteBranches( monitor, withTags: true, branchSpec ) )
        {
            return false;
        }
        // Now that the tags of the vMajor have been fetched, we can obtain the VersionTagInfo to find the commit that
        // must be fixed.
        var versionInfo = _versionTags.GetWithoutIssue( monitor, repo, "starting a fix" );
        if( versionInfo == null )
        {
            return false;
        }
        Throw.DebugAssert( !versionInfo.HasIssue );
        var toFix = versionInfo.LastStables.Where( c => c.Version.Major == major && (minor == -1 || c.Version.Minor == minor) ).Max();
        if( toFix == null )
        {
            monitor.Error( minor != -1
                            ? $"Unable to find any version to fix for 'v{major}.{minor}'."
                            : $"Unable to find any version to fix for 'v{major}'." );
            return false;
        }
        if( toFix == versionInfo.HotZone.LastStable )
        {
            monitor.Error( $"""
                The version to fix '{toFix.Version.ParsedText}' is the current last stable version.
                Use the regular 'ckli build/publish' or 'ckli ci build/publish' workflows to produce a fix.
                """ );
            return false;
        }
        // We now know the target version.
        var targetVersion = SVersion.Create( toFix.Version.Major, toFix.Version.Minor, toFix.Version.Patch + 1 );
        // We are on the origin of the fix, we have fetched all the fix branches for the major: we try to merge
        // any remote work in this scope into our local repository. It is a kind of "scoped pull" that guaranties
        // that we start a fix with an up-to-date local code of the fix origin.
        if( !repo.GitRepository.MergeRemoteBranches( monitor, continueOnError: true, fromAllRemotes: true, branchSpec ) )
        {
            monitor.Error( "The merge conflicts must be resolved before starting the fix." );
            return false;
        }
        // In the recursive part of CreateTargets that handles the impacts (below), we fetch all the "fix/v*" branches
        // and merge only the impacted branches.
        // If any impacted branch has any merge conflict, this is also an error that must be fixed.

        // If load fails, the workflow has been deleted. We could allow this (as there's no more workflow)
        // but this would be weird. It's cleaner to fail this call: the fix start command can be repeated.
        if( !FixWorkflow.Load( monitor, World, out var exists ) )
        {
            return false;
        }
        bool restartingWorkflow = false;
        if( exists != null )
        {
            var o = exists.OriginRepo;
            if( o.Repo == repo && o.TargetVersion == targetVersion )
            {
                // Restarting.
                // This must resynchronizes the workflow with the remote branches.
                // It is exactly the same as a "cancel+start".
                // This "resync" may also be done at the start of 'ckli publish' to detect fix already existing
                // (and may be conflicting) on the remotes...
                restartingWorkflow = true;
            }
            else
            {
                monitor.Error( $"A fix workflow already exists for world '{World.Name}'. It must be canceled first." );
                context.Screen.Display( exists.ToRenderable );
                return false;
            }
        }
        // Defensive programming here: the RepoReleaseInfo must exist.
        // This is the origin of the FixWorkflow.
        var originReleaseInfo = _releaseDatabase.GetReleaseInfo( monitor, repo, toFix.Version, LogLevel.Error );
        if( originReleaseInfo == null )
        {
            monitor.Error( ActivityMonitor.Tags.ToBeInvestigated,
                $"Release '{repo.DisplayPath}/{toFix.Version}' cannot be found in the Release database and there's no VersionTag issue. This should not happen." );
            return false;
        }
        // First, creates the origin (originReleaseInfo,targetVersion) as a FixWorkflow.TargetRepo.
        // If it fails, it is useless to create the downstream repositories targets.
        var origin = CreateTarget( monitor, context, _versionTags, moveBranch, originReleaseInfo.Repo, toFix, targetVersion, 0 );
        if( origin == null )
        {
            return false;
        }
        if( !CreateTargets( monitor, context, _versionTags, origin, moveBranch, originReleaseInfo, out var targets ) )
        {
            return false;
        }
        if( _onFixStart.HasHandlers )
        {
            using( monitor.OpenInfo( "Raising OnFixStart event." ) )
            {
                bool eventError = false;
                using( monitor.OnError( () => eventError = true ) )
                {
                    var e = new FixWorkflowStartEventArgs( monitor, targets, restartingWorkflow );
                    if( !await _onFixStart.SafeRaiseAsync( monitor, e ).ConfigureAwait( false )
                        || eventError )
                    {
                        monitor.CloseGroup( $"OnFixStart event handling failed." );
                        return false;
                    }
                }
            }
        }
        var workflow = new FixWorkflow( World, targets );
        if( !workflow.Save( monitor ) )
        {
            return false;
        }
        // If it's a new workflow, let's keep the house clean by deleting any trace
        // of a previous workflow.
        if( !restartingWorkflow )
        {
            _releaseDatabase.DestroyAllLocalFixRelease( monitor );
            _artifactHandler.DestroyAllLocalFixRelease( monitor );
        }

        // Display (avoid a subsequent 'ckli fix info').
        context.Screen.Display( workflow.ToRenderable );
        return true;

        static bool ParseMajorMinor( ReadOnlySpan<char> s, out int major, out int minor )
        {
            minor = -1;
            s.TryMatch( 'v' );
            if( s.TryMatchInteger( out major ) && major >= 0 )
            {
                if( s.TryMatch( '.' ) && (!s.TryMatchInteger( out minor ) || minor < 0) )
                {
                    return false;
                }
                return true;
            }
            return false;
        }

        static bool CreateTargets( IActivityMonitor monitor,
                                   CKliEnv context,
                                   VersionTagPlugin versionTags,
                                   FixWorkflow.TargetRepo first,
                                   bool moveBranch,
                                   RepoReleaseInfo origin,
                                   out ImmutableArray<FixWorkflow.TargetRepo> targets )
        {
            #region Comments
            // The direct impacts are filtered. This limits the number of release to process at the source: only
            // repo/version that need to be built will appear in the direct impacts.
            //
            // The impacts are all the Repo's LastMajorMinorStables commits that have one of our content's
            // produced package identifiers consumed with the lastFix.Version.
            //
            // A lighter impact can be to consider only the Repo's LastStable... But here we are on a "fix/"
            // branch of a somehow "past" release, we are not releasing in the "hot zone" (as a new minor stable version):
            // it seems coherent to propagate "widely in the past"... But this may produce a lot of useless releases...
            //
            // Is there a way to "opt in" or "opt out" the propagation? It must be:
            //   - easy to initiate and to stop.
            //   - easy to grasp (by looking at the repository).
            // Idea! Can it be the "fix/" branch that does the job?
            // Given a fix of "My-Core/v1.0.1", a repo consumed this package in its v1.0.0 for its own versions
            // - v4.0.0 (LastStable, LastMajorMinorStable)
            // - v3.1.2 (LastMajorMinorStable)
            // - v3.1.1 (out of this scope: not in the LastMajorMinorStable)
            // - v3.1.1 (same as above)
            // - v3.0.1 (LastMajorMinorStable)
            // - v3.0.0 (out of this scope: not in the LastMajorMinorStable)
            // - v2.0.1 (LastMajorMinorStable)
            // - v2.0.0 (out of this scope: not in the LastMajorMinorStable)
            //
            // Using the LastMajorMinorStable, this triggers 4 new versions (4.0.1, 3.1.3, 3.0.2 and 2.0.2) which, in turn, produce
            // more versions of downstream repos.
            // Is the 2.0.2 useful? required?
            // If we really don't care of v2 anymore, then a +deprecated tag can/should be created (this solves the problem:
            // deprecated versions are no more "regular" versions and are ignored).
            // But if we consider +deprecated as a "strong signal" that shouldn't be overused, then we need another mechanism
            // that may be the existence of the "fix/" branch (here, does a "fix/v2.0" exist or not?).
            //  - Is this branch "automatically" created (opt-out mode)?
            //    Pros: safe. Cons: when will they be deleted? by who? (answer: almost never...)
            //  - Opt-in mode? It's so easy to forget to create it...
            //  => None is good.
            // Changing the point of view: a --critical-fix flag makes us use the LastMajorMinorStable otherwise
            // only the LastStable is used.
            // This is far better (especially regarding the fact that in practice CK has a "upgrade asap" philosophy).
            // But... wait... We started this discussion with
            //      "But here we are on a "fix/" branch of a somehow "past" release, we are not releasing in the "hot zone"
            //      (as a new minor stable version): it seems coherent to propagate "widely in the past"... "
            // I initially thought that the "build fix" was easier than working in the "hot zone". But this is not that obvious.
            // Is it because we are "pushing downstream" rather that thinking "pulling upstream"?
            // What does pulling means here. Something like:
            // "Hey, World, please upgrade ALL your dependencies to available patch versions and gives me updated packages.".
            // The question is eventually the same: which packages for which (potentially outdated) versions?
            // No... We definitely want to push fixes downstream.
            //
            // The solution is to use the +deprecated: a regular version is "alive" (and must be fixed).
            // A +deprecated version is "dead" and should not be used anymore. If we deprecate aggressively, we won't have
            // these problems (and leads to less versions to manage) that globally optimize the system.
            //
            // To conclude: The impacts of a fix are all the Repo's LastMajorMinorStables commits that have one of
            //              our content's produced package identifiers consumed with the lastFix.Version.
            //
            // This is the filter:
            //     foreach( var next in origin.GetDirectConsumers( monitor ) )
            //     {
            //         var tags = versionTags.Get( monitor, next.Repo );
            //         var commit = tags.LastMajorMinorStables.FirstOrDefault( tc => tc.Version == next.Version );
            //         if( commit != null )
            //         {
            //             yield return (next, commit);
            //         }
            //     }
            //
            // But we must ensure the global build order. Here A has {B,C} as its direct consumers
            // and both of them has D in their direct consumers.
            //
            //   A -> B -> D
            //     -> C -> D
            //
            // While traversing the consumers we must ensure that D will appear after B and C. There is no
            // order between 2 consumers - here B and C - but the depth (the rank) is crucial.
            //
            // Note: we can use the reference equality of the RepoReleaseInfo to identify the instances.
            //
            // The simplest algorithm here that relies on the filter above is to have a list and before appending
            // a new consumer, find if if it exists and removes it. This is O(n²) only to add a consumer.
            // To better scale, an ever growing list of nullables can be used and a dictionary Consumer -> Index
            // can be maintained to set to null the previous entry. At the end only the non null entries are
            // returned. These 2 approaches have the same drawback: the whole set of consumers must be reprocessed
            // for each occurrence of a shared consumer.
            //
            // We use another approach that updates the depth (rank) while traversing the consumers. If we avoid a repetitive
            // lookup in the tags.LastMajorMinorStables, we unfortunately reprocess shared consumers to increase the
            // ranks (but only when needed).
            // 
            #endregion
            // This tracks the repo for which "fix/v*" branches have been fetched.
            var fetched = new HashSet<Repo>();
            var all = new Dictionary<RepoReleaseInfo, (TagCommit? Commit, int Rank)>();
            Throw.DebugAssert( first.Index == 0 && first.Rank == 0 );
            int? count = Collect( monitor, all, versionTags, origin, 1, fetched );
            if( !count.HasValue )
            {
                targets = default;
                return false;
            }
            targets = default;
            var b = ImmutableArray.CreateBuilder<FixWorkflow.TargetRepo>( 1 + count.Value );
            b.Add( first );
            foreach( var (info, (commit, rank)) in all )
            {
                // Skips null marker.
                if( commit == null ) continue;
                var targetVersion = SVersion.Create( commit.Version.Major, commit.Version.Minor, commit.Version.Patch + 1 );
                if( !info.Repo.GitRepository.MergeRemoteBranches( monitor,
                                                                  continueOnError: false,
                                                                  fromAllRemotes: false,
                                                                  $"{info.Repo.World.Name.LTSName}/fix/v{targetVersion.Major}.{targetVersion.Minor}" ) )
                {
                    return false;
                }
                var t = CreateTarget( monitor, context, versionTags, moveBranch, info.Repo, commit, targetVersion, rank );
                if( t == null ) return false;
                b.Add( t );
            }
            Throw.DebugAssert( b.Count == 1 + count );
            b.Sort( static ( t1, t2 ) =>
            {
                int cmp = t1.Rank.CompareTo( t2.Rank );
                if( cmp != 0 ) return cmp;
                cmp = t1.Repo.Index.CompareTo( t2.Repo.Index );
                if( cmp != 0 ) return cmp;
                return t1.TargetVersion.CompareTo( t2.TargetVersion );
            } );
            targets = b.MoveToImmutable();
            for( int i = 1; i < targets.Length; i++ )
            {
                targets[i].SetIndex( i );
            }
            return true;

            static int? Collect( IActivityMonitor monitor,
                                 Dictionary<RepoReleaseInfo, (TagCommit? Commit, int Rank)> all,
                                 VersionTagPlugin versionTags,
                                 RepoReleaseInfo origin,
                                 int rank,
                                 HashSet<Repo> fetched )
            {
                int count = 0;
                foreach( RepoReleaseInfo next in origin.GetDirectConsumers( monitor ) )
                {
                    if( all.TryGetValue( next, out var exists ) )
                    {
                        if( exists.Commit != null )
                        {
                            if( exists.Rank < rank )
                            {
                                all[next] = (exists.Commit, rank);
                                Collect( monitor, all, versionTags, next, rank + 1, fetched );
                            }
                        }
                    }
                    else
                    {
                        // We fetch (with the tags) all the "fix/v*" branches here (before computing the version tags).
                        // There can be multiple impacts in the same repository (with different minors but
                        // also majors!).
                        // And we do this only once per impacted Repo (hence the HashSet<Repo> fetched).
                        if( fetched.Add( next.Repo ) )
                        {
                            var ltsName = next.Repo.World.Name.LTSName;
                            var branchSpec = ltsName != null ? $"{ltsName}/fix/v*" : "fix/v*";
                            if( !next.Repo.GitRepository.FetchRemoteBranches( monitor, withTags: true, branchSpec ) )
                            {
                                return null;
                            }
                        }
                        var tags = versionTags.GetWithoutIssue( monitor, next.Repo, "starting the fix" );
                        if( tags == null )
                        {
                            return null;
                        }
                        // This may seem counter intuitive here but the impact here often doesn't exist:
                        // this is because we consider here the last patch of the major.minor for the repo
                        // and a origin can have been consumed by the v1.2.3 of a consumer but if a v1.2.4
                        // has been built, the v1.2.3 must be skipped.
                        // Note that if the v1.2.4 doesn't consume origin anymore, this is a violation of the
                        // "fix policy" but is not an error: we just ignore this.
                        var commit = tags.LastMajorMinorStables.FirstOrDefault( tc => tc.IsRegularVersion && tc.Version == next.Version );
                        if( commit != null )
                        {
                            all.Add( next, (commit, rank) );
                            ++count;
                            int? belowCount = Collect( monitor, all, versionTags, next, rank + 1, fetched );
                            if( !belowCount.HasValue )
                            {
                                return null;
                            }
                            count += belowCount.Value;
                        }
                        else
                        {
                            // Adds a null marker to avoid subsequent lookups.
                            all.Add( next, default );
                        }
                    }
                }
                return count;
            }
        }


        static FixWorkflow.TargetRepo? CreateTarget( IActivityMonitor monitor,
                                                     CKliEnv context,
                                                     VersionTagPlugin versionTags,
                                                     bool moveBranch,
                                                     Repo repo,
                                                     TagCommit toFix,
                                                     SVersion targetVersion,
                                                     int rank )
        {
            var ltsName = repo.World.Name.LTSName;
            var branchName = ltsName != null
                                ? $"{ltsName}/fix/v{toFix.Version.Major}.{toFix.Version.Minor}"
                                : $"fix/v{toFix.Version.Major}.{toFix.Version.Minor}";
            // Find an existing branch.
            var bFix = repo.GitRepository.GetBranch( monitor, branchName, LogLevel.Info );
            // Provide an empty commit to the developer so that the branch is not on the existing versioned commit.
            if( bFix != null )
            {
                // When bFix.Tip.Tree.Sha == toFix.ContentSha, we are in the initial nominal case:
                // the commit referenced by the /fix branch contains the code to fix.
                if( bFix.Tip.Tree.Sha != toFix.Commit.Tree.Sha )
                {
                    // The /fix branch must contain the commit to fix.
                    var versionedParent = versionTags.Get( monitor, repo ).FindFirst( bFix.Commits );
                    if( versionedParent != toFix )
                    {
                        if( !moveBranch )
                        {
                            monitor.Error( $"""
                                Branch '{branchName}' in '{repo.DisplayPath}' doesn't contain the commit '{toFix.Sha}' for the version '{toFix.Version.ParsedText}' to fix.
                                Use --move-branch flag to move the branch on the commit to fix.
                                """ );
                            return null;
                        }
                        monitor.Info( $"Moving branch '{branchName}' in '{repo.DisplayPath}' to commit '{toFix.Sha}'." );
                        bFix = null;
                    }
                }
            }
            // Provide an empty commit to the developer so that the branch is not on the existing versioned commit.
            if( bFix == null || bFix.Tip.Sha == toFix.Commit.Sha )
            {
                var r = repo.GitRepository.Repository;
                var c = r.ObjectDatabase.CreateCommit( toFix.Commit.Author,
                                                       context.Committer,
                                                       $"Starting '{branchName}' (this commit can be amended).",
                                                       toFix.Commit.Tree,
                                                       [toFix.Commit],
                                                       prettifyMessage: false );
                // Create or update the /fix branch.
                bFix = r.Branches.Add( branchName, c, allowOverwrite: true );
            }

            return new FixWorkflow.TargetRepo( repo,
                                               branchName,
                                               toFix.Commit.Sha,
                                               targetVersion,
                                               rank );
        }

    }

    [Description( "Dumps the current Fix Workflow." )]
    [CommandPath( "fix info" )]
    public bool FixInfo( IActivityMonitor monitor, CKliEnv context )
    {
        if( !FixWorkflow.Load( monitor, World, out var workflow) )
        {
            return false; 
        }
        if( workflow == null )
        {
            monitor.Info( ScreenType.CKliScreenTag, "There is no current Fix Workflow." );
        }
        else
        {
            context.Screen.Display( workflow.ToRenderable );
        }
        return true;
    }

    [Description( "Cancels the current Fix Workflow." )]
    [CommandPath( "fix cancel" )]
    public bool FixCancel( IActivityMonitor monitor, CKliEnv context )
    {
        if( !FixWorkflow.Load( monitor, World, out var workflow ) )
        {
            return false;
        }
        if( workflow == null )
        {
            monitor.Info( ScreenType.CKliScreenTag, "No current workflow exist." );
            return true;
        }
        foreach( var target in workflow.Targets )
        {
            _versionTags.DestroyLocalRelease( monitor, target.Repo, target.TargetVersion );
        }
        FixWorkflow.DeleteCurrent( monitor, World );
        return true;
    }
}

