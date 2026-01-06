using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CKli.ReleaseDatabase.Plugin;
using CKli.VersionTag.Plugin;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CKli.Build.Plugin;

/// <summary>
/// This graph builder propagates an initial (<see cref="RepoReleaseInfo"/>,<see cref="BuildResult"/>) to the
/// downstream repositories. It is a forward only process: each new build must produce the exact same packages as the
/// previous one and only the previously built packages are updated. The dependency graph cannot change during the build.
/// <para>
/// the "fix/vMajor.Minor" branches are automatically created in downstream repositories if they don't exist yet.
/// </para>
/// </summary>
sealed class FixPropagator
{
    readonly CKliEnv _context;
    readonly BuildPlugin _buildPlugin;
    readonly ReleaseDatabasePlugin _releaseDatabase;
    readonly VersionTagPlugin _versionTag;
    readonly Queue<(RepoReleaseInfo ReleaseInfo, TagCommit Commit)> _candidates;
    readonly List<NuGetPackageInstance> _updates;

    FixPropagator( RepoReleaseInfo origin,
                   BuildResult originResult,
                   CKliEnv context,
                   BuildPlugin buildPlugin,
                   ReleaseDatabasePlugin releaseDatabase,
                   VersionTagPlugin versionTag,
                   Queue<(RepoReleaseInfo ReleaseInfo, TagCommit Commit)> candidates,
                   List<NuGetPackageInstance> updates )
    {
        _context = context;
        _buildPlugin = buildPlugin;
        _releaseDatabase = releaseDatabase;
        _versionTag = versionTag;
        _candidates = candidates;
        _updates = updates;
    }

    internal static FixPropagator? Create( IActivityMonitor monitor,
                                           RepoReleaseInfo origin,
                                           BuildResult originResult,
                                           CKliEnv context,
                                           BuildPlugin buildPlugin,
                                           ReleaseDatabasePlugin releaseDatabase,
                                           VersionTagPlugin versionTags )
    {
        var candidates = new Queue<(RepoReleaseInfo ReleaseInfo, TagCommit Commit)>();
        var updates = new List<NuGetPackageInstance>();
        if( !OnBuild( monitor, versionTags, candidates, updates, origin, originResult ) )
        {
            return null;
        }
        return new FixPropagator( origin, originResult, context, buildPlugin, releaseDatabase, versionTags, candidates, updates );
    }

    static bool OnBuild( IActivityMonitor monitor,
                         VersionTagPlugin versionTags,
                         Queue<(RepoReleaseInfo ReleaseInfo, TagCommit Commit)> candidates,
                         List<NuGetPackageInstance> updates,
                         RepoReleaseInfo origin,
                         BuildResult originResult )
    {
        // We introduce a check here: we demand that the produced package identifiers are the same as the release
        // we are fixing: changing the produced packages that are structural/architectural artifacts is
        // everything but fixing.
        if( !originResult.Content.Produced.SequenceEqual( origin.Content.Produced ) )
        {
            monitor.Error( $"""
                Forbidden change in produced packages for a fix in '{origin.Repo.DisplayPath}':
                The version '{origin.Version.ParsedText}' produced packages: '{origin.Content.Produced.Concatenate( "', '" )}'.
                But the new fix 'v{originResult.Version}' produced: '{originResult.Content.Produced.Concatenate( "', '" )}'.
                """ );
            versionTags.DestroyLocalRelease( monitor, originResult.Repo, originResult.Version );
            return false;
        }
        // Adds the produced packages with their new version.
        updates.AddRange( originResult.Content.Produced.Select( id => new NuGetPackageInstance( id, originResult.Version ) ) );
        // Populates the build candidates list:
        // The impacts are all the Repo's LastMajorMinorStables commits that have one of our content's
        // produced package identifiers consumed with the lastFix.Version.
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
        // May be the solution is using the +deprecated: a regular version is "alive" (and must be fixed).
        // A +deprecated version is "dead" and should not be used anymore. If we deprecate aggressively, we won't have
        // these problems (and leads to less versions to manage) that globally optimize the system.
        //
        // To conclude: The impacts of a fix are all the Repo's LastMajorMinorStables commits that have one of
        //              our content's produced package identifiers consumed with the lastFix.Version.
        foreach( var next in origin.GetDirectConsumers( monitor ) )
        {
            var tags = versionTags.Get( monitor, next.Repo );
            var commit = tags.LastMajorMinorStables.FirstOrDefault( tc => tc.Version == next.Version );
            if( commit != null )
            {
                candidates.Enqueue( (next, commit) );
            }
        }
        return true;
    }

    public bool RunAll( IActivityMonitor monitor )
    {
        while( _candidates.TryDequeue( out var next ) )
        {
            //if( !SetupFixBranch( monitor, next.Commit ) )
            //{
            //    return false;
            //}
        }
        return true;
    }

}
