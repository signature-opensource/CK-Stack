using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.Core;
using CKli.ReleaseDatabase.Plugin;
using CSemVer;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Xml.Linq;

namespace CKli.VersionTag.Plugin;

public sealed partial class VersionTagPlugin : PrimaryRepoPlugin<VersionTagInfo>
{
    static readonly XName _xMinVersion = XNamespace.None.GetName( "MinVersion" );
    static readonly XName _xMaxVersion = XNamespace.None.GetName( "MaxVersion" );
    readonly ReleaseDatabasePlugin _releaseDatabase;
    readonly ArtifactHandlerPlugin _artifactHandler;

    public VersionTagPlugin( PrimaryPluginContext primaryContext,
                             ReleaseDatabasePlugin releaseDatabase,
                             ArtifactHandlerPlugin artifactHandler )
        : base( primaryContext )
    {
        World.Events.Issue += IssueRequested;
        _releaseDatabase = releaseDatabase;
        _artifactHandler = artifactHandler;
    }

    void IssueRequested( IssueEvent e )
    {
        var monitor = e.Monitor;
        foreach( var r in e.Repos )
        {
            Get( monitor, r ).CollectIssues( monitor, e.ScreenType, e.Add );
        }
    }

    /// <summary>
    /// Gets the non null version tag info for the repository only if <see cref="VersionTagInfo.HasIssues"/> is false.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="repo">The repository.</param>
    /// <param name="before">Expected following operation description. When null, no error is emitted (it must be emitted by the caller).</param>
    /// <returns>The non null info or null if there are issues.</returns>
    public VersionTagInfo? GetWithoutIssue( IActivityMonitor monitor, Repo repo, string? before = "continuing" )
    {
        var versionInfo = Get( monitor, repo );
        if( versionInfo.HasIssues )
        {
            if( before != null )
            {
                monitor.Error( $"Please fix any issue before {before}." );
            }
            return null;
        }
        return versionInfo;
    }

    /// <summary>
    /// Sets <see cref="MinVersion"/> for a Repo.
    /// This must be called before the <see cref="VersionTagInfo"/> for the Repo is obtained.
    /// This is required for .Net 8 migration. This can be removed one day. 
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="repo">The repository.</param>
    /// <param name="min">The new MinVersion.</param>
    /// <returns>True on success, false on error.</returns>
    public bool SetMinVersion( IActivityMonitor monitor, Repo repo, SVersion min )
    {
        Throw.CheckArgument( min != null && min.IsValid && !min.IsPrerelease );
        Throw.CheckState( !HasRepoInfoBeenCreated( repo ) );
        return PrimaryPluginContext.GetConfigurationFor( repo )
                                   .Edit( monitor, ( monitor, e ) => e.SetAttributeValue( _xMinVersion, min.ToString() ) );
    }

    /// <summary>
    /// Destroys a released version. The version tag is deleted, the release database is updated
    /// and any artifacts are removed: this centralizes the calls to <see cref="ReleaseDatabasePlugin.DestroyLocalRelease(IActivityMonitor, Repo, SVersion)"/>
    /// and <see cref="ArtifactHandlerPlugin.DestroyLocalRelease(IActivityMonitor, Repo, SVersion, BuildContentInfo)"/>
    /// that should not be called directly.
    /// <para>
    /// This is idempotent.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="repo">The source repository.</param>
    /// <param name="version">The release to destroy.</param>
    /// <returns>True on success, false on error (only errors are that assets cannot be properly deleted).</returns>
    public bool DestroyLocalRelease( IActivityMonitor monitor, Repo repo, SVersion version )
    {
        var info = Get( monitor, repo );
        if( info == null ) return false;
        using( monitor.OpenInfo( $"Deleting local release '{repo.DisplayPath}/{version}'." ) )
        {
            // Take no risk: delete every possible traces (but avoids calling twice the same destroy).
            var tag = info.RemoveTag( version )?.BuildContentInfo;
            var db = _releaseDatabase.DestroyLocalRelease( monitor, repo, version );
            if( tag != null && db != null && db != tag )
            {
                // Use single | (no short circuit).
                return _artifactHandler.DestroyLocalRelease( monitor, repo, version, tag )
                        | _artifactHandler.DestroyLocalRelease( monitor, repo, version, db );
            }
            var any = db ?? tag;
            if( any != null )
            {
                return _artifactHandler.DestroyLocalRelease( monitor, repo, version, any );
            }
        }
        return true;
    }


    /// <summary>
    /// Rebuilds the published and local databases.
    /// Remote tags drives the update of the published database and are updated on the remote: a local only version
    /// tag will remain local.
    /// <para>
    /// Artefacts are not pushed.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="context">The CKli context.</param>
    /// <returns>True on success, false on error.</returns>
    [Description( """
        Suppress the published and local databases and rebuild them from the version tags content.
        Remote tags drives the update of the published database and are updated on the remote:
        a local only version tag will remain local.
        """ )]
    [CommandPath( "maintenance release-database rebuild" )]
    public bool RebuildReleaseDatabases( IActivityMonitor monitor, CKliEnv context )
    {
        var repos = World.GetAllDefinedRepo( monitor );
        if( repos == null ) return false;

        // Before destroying the databases, we require that the tags (GitTagInfo.Diff) are "clean":
        // there must not be "fetch required" (tags with unknown commit target) nor tag conflicts
        // (tag that exists on local and remote and targets 2 different commits).
        //
        // Obtaining these tags info will allow us to consider that a version tag that is on the
        // remote side is de facto published: we'll publish the local release that transfers its
        // information from the local to the published database. Moreover, if the version tag
        // differ, we push the local one (that updates the remote one): this supports a move from
        // obsolete (or legacy lightweight) tags to an up-to-date version of the tags content.
        //
        if( !GetAllDiffTags( monitor, context, repos, out var allDiffTags ) )
        {
            return false;
        }

        // Deleting the databases.
        _releaseDatabase.DestroyDatabases( monitor );

        // Resolving the VersionTagInfo repopulates (and saves) the local database.
        // If there is any version tag issue (rebuild is needed to compute the tag content),
        // we demand to execute a "ckli issue --fix".
        if( !TryGetAll( monitor, out var allInfo ) )
        {
            return false;
        }
        if( allInfo.Any( i => i.HasIssues ) )
        {
            monitor.Error( "There must be no version tag issues. Use 'ckli issue' before retrying." );
            return false;
        }
        // Consider remote version tags: move the release from local to remote database and update the remote tag if it differs.
        bool pushTagFailed = false;
        var pushTagBuffer = new List<string>();
        int publishedReleaseCount = 0;
        foreach( var repo in repos )
        {
            using( monitor.OpenInfo( $"Analyzing releases of '{repo.DisplayPath}'." ) )
            {
                var diffTags = allDiffTags[repo.Index];
                var versionTags = Get( monitor, repo );
                foreach( var tc in versionTags.TagCommits.Values.Where( tc => tc.IsRegularVersion ) )
                {
                    GitTagInfo.DiffEntry e = diffTags.Entries.FirstOrDefault( e => e.Commit.Sha == tc.Sha );
                    GitTagInfo.LocalRemoteTag? t = e.Tags?.FirstOrDefault( t => t.CanonicalName == tc.Tag.CanonicalName );
                    if( t == null )
                    {
                        // This should not happen: stop early.
                        monitor.Error( ActivityMonitor.Tags.ToBeInvestigated,
                                       $"""
                                       Version tag '{tc.Version.ParsedText}' on commit '{tc.Sha}' with content:
                                       {tc.BuildContentInfo}

                                       Cannot be found in any GitTagInfo.DiffEntry of '{repo.DisplayPath}'. 
                                       """ );
                        return false;
                    }
                    Throw.DebugAssert( "Otherwise we wouldn't have found it.", t.Local != null );
                    // Ignores local only.
                    if( t.Remote != null )
                    {
                        // This release has a remote tag: publish it.
                        monitor.Info( $"Moving '{tc.Version.ParsedText}' to published database." );
                        // This has no reason to fail: stop early.
                        if( !_releaseDatabase.PublishRelease( monitor, repo, tc.Version ) )
                        {
                            return false;
                        }
                        ++publishedReleaseCount;
                        if( (t.Diff & GitTagInfo.TagDiff.DifferMask) != 0 )
                        {
                            // The remote tag must be updated.
                            pushTagBuffer.Add( tc.Tag.CanonicalName );
                        }
                    }
                }
                if( pushTagBuffer.Count > 0 )
                {
                    monitor.Info( "Updating remote tags that differ from locally updated ones." );
                    if( !repo.GitRepository.PushTags( monitor, pushTagBuffer ) )
                    {
                        pushTagFailed = true;
                    }
                    pushTagBuffer.Clear();
                }
            }
        }
        if( publishedReleaseCount > 0 )
        {
            if( !World.StackRepository.Commit( monitor, $"Updated published database with {publishedReleaseCount} releases." ) )
            {
                return false;
            }
        }
        if( pushTagFailed )
        {
            monitor.Warn( """
                Some tag pushes have failed. Use 'ckli tag list' to analyze tag differences.
                These differences should be fixed manually.
                """ );
        }
        return true;
    }

    static bool GetAllDiffTags( IActivityMonitor monitor,
                                CKliEnv context,
                                IReadOnlyList<Repo> repos,
                                out ImmutableArray<GitTagInfo.Diff> allDiffTags )
    {
        using( monitor.OpenInfo( "Analyzing Tags on the repositories and their 'origin' remotes." ) )
        {
            bool success = true;
            var b = ImmutableArray.CreateBuilder<GitTagInfo.Diff>( repos.Count );
            List<int>? issues = null;
            foreach( var repo in repos )
            {
                if( repo.GitRepository.GetDiffTags( monitor, out var diffTags ) )
                {
                    b.Add( diffTags );
                    if( diffTags.FetchRequired || diffTags.ConflictCount > 0 )
                    {
                        issues ??= new List<int>();
                        issues.Add( repo.Index );
                    }
                }
                else
                {
                    success = false;
                }
            }
            if( !success )
            {
                allDiffTags = default;
                return false;
            }
            if( issues != null )
            {
                allDiffTags = default;
                monitor.Error( $"{issues.Count} repositories have tag issues that must be fixed. Please fix them before retrying." );
                // We want to display only the "fetch required" and the conflicts. Existing tags, local/remote and even differences
                // are not really relevant here.
                context.Screen.Display( s => s.Unit.AddBelow(
                    issues.Select( idx => (Repo: repos[idx], Diff: b[idx]) )
                          .Select( d => new Collapsable( s.Text( d.Repo.DisplayPath ).HyperLink( new Uri( d.Repo.WorkingFolder ) )
                                                          .AddBelow( d.Diff.ToRenderable( s,
                                                                                          withLocalInvalidTags: false,
                                                                                          withRemoteInvalidTags: false,
                                                                                          withRegularTags: false,
                                                                                          withLocalOnlyTags: false,
                                                                                          withRemoteOnlyTags: false,
                                                                                          withDifferences: false ) ) ) ) ) );
                return false;
            }
            allDiffTags = b.MoveToImmutable();
            return true;
        }
    }

    (SVersion Min, SVersion? Max) ReadRepoConfiguration( IActivityMonitor monitor, Repo repo )
    {
        var config = PrimaryPluginContext.GetConfigurationFor( repo );
        // Non existing or invalid MinVersion fallbacks to v0.0.0.
        SVersion min = ReadVersionAttribute( monitor, config, _xMinVersion, SVersion.Create( 0, 0, 0 ) );

        SVersion? max = null;
        var maxAttr = config.XElement.Attribute( _xMaxVersion );
        if( World.Name.IsDefaultWorld )
        {
            if( maxAttr != null )
            {
                monitor.Warn( $"""
                    In a default World (not a LTS one), there must be no MaxVersion.
                    Removing VersionTagPlugin.MaxVersion = "{maxAttr.Value}" for '{repo}'.
                    """ );
                config.Edit( monitor, ( monitor, e ) => maxAttr.Remove() );
            }
        }
        else
        {
            // LTS world: the max version must exist.
            // We read it and use the min version as the fallback: this gives an invalid range
            // that should be fixed by the user.
            min = ReadVersionAttribute( monitor, config, _xMaxVersion, min );
            if( min >= max )
            {
                monitor.Warn( $"Invalid Min/MaxVersion range in '{repo}'. This must be fixed." );
            }
        }
        return (min, max);

        static SVersion ReadVersionAttribute( IActivityMonitor monitor,
                                              PluginConfiguration config,
                                              XName name,
                                              SVersion defaultValue )
        {
            Throw.DebugAssert( config.Repo != null );
            var text = config.XElement.Attribute( name )?.Value;
            SVersion parsedV = SVersion.TryParse( text );
            SVersion v;
            if( !parsedV.IsValid )
            {
                v = defaultValue;
                if( text == null )
                {
                    monitor.Trace( $"Initializing '{config.Repo.DisplayPath}' VersionTagPlugin.{name.LocalName} to '{v}'." );
                }
                else
                {
                    monitor.Warn( $"""
                        Invalid '{config.Repo.DisplayPath}' VersionTagPlugin.{name.LocalName}: '{text}'.
                        Reinitializing to '{v}'.
                        """ );
                }
            }
            else
            {
                v = parsedV;
                if( v.IsPrerelease )
                {
                    v = SVersion.Create( v.Major, v.Minor, v.Patch );
                    monitor.Warn( $"""
                        Invalid '{config.Repo.DisplayPath}' VersionTagPlugin.{name.LocalName}: '{text}' must be a stable version.
                        Reinitializing to '{v}'.
                        """ );
                }
            }
            if( v != parsedV )
            {
                config.Edit( monitor, ( monitor, e ) => e.SetAttributeValue( name, v ) );
            }
            return v;
        }

    }

    protected override VersionTagInfo Create( IActivityMonitor monitor, Repo repo )
    {
        var (minVersion, maxVersion) = ReadRepoConfiguration( monitor, repo );

        var isExecutingIssue = PrimaryPluginContext.Command is CKliIssue;

        List<Tag>? removableTags = null;
        // Collects conflicting tags.
        List<((SVersion V, Tag T) T1, (SVersion V, Tag T) T2, TagConflict C)>? tagConflicts = null;

        // First pass. Enumerates all the tags to keep all +invalid and
        // tags in the MajorRange.
        // This list is temporary (first pass) to build the v2c index.
        List<TagCommit> validTags = new List<TagCommit>();
        Dictionary<SVersion, (SVersion V, Tag T)>? invalidTags = null;
        bool hasBadTagNames = false;
        var r = repo.GitRepository.Repository;
        foreach( var t in r.Tags )
        {
            var tagName = t.FriendlyName;
            if( !GitRepository.IsCKliValidTagName( tagName ) )
            {
                hasBadTagNames = true;
                continue;
            }
            var v = SVersion.TryParse( tagName );
            // Consider only SVersion tag and target that is a commit (safe cast).
            if( !v.IsValid || t.Target is not Commit c )
            {
                continue;
            }
            // Above MaxVersion or below MinVersion: ignore.
            if( (maxVersion != null && v > maxVersion) || v < minVersion ) continue;

            // A +invalid tag totally cancels an existing version tag. We collect them
            // and apply them once all the valid tags have been collected.
            //
            // The +invalid tags are temporary artifacts that are used to distribute the information
            // across the repositories. Once the bad tag doesn't appear anywhere, a +invalid tag 
            // must be removed.
            //
            if( v.BuildMetaData.Contains( "invalid", StringComparison.Ordinal ) )
            {
                invalidTags ??= new Dictionary<SVersion, (SVersion V, Tag T)>();
                if( invalidTags.TryGetValue( v, out var exists ) )
                {
                    tagConflicts ??= new();
                    tagConflicts.Add( (exists, (v, t), TagConflict.DuplicateInvalidTag) );
                }
                else
                {
                    invalidTags.Add( v, (v, t) );
                }
                continue;
            }
            // A +deprecated was an actual version. They appear in the VersionTagInfo.TagCommits (like a +fake).
            // This is required, for instance, to be able to produce a 4.0.1 fix after the deprecated 4.0.0 version.
            //
            // As opposed to +invalid tags, +deprecated tags must never be deleted. They memorize the
            // existence of a version.
            //
            var tc = new TagCommit( v, c, t );
            validTags.Add( tc );
        }
        // Second pass: filters out the invalid tags and produces the v2C index
        //              along with potential tag conflicts.
        var v2c = new Dictionary<SVersion, TagCommit>();
        TagCommit? topHot = null;
        foreach( var newOne in validTags )
        {
            // This filters out any version tags (regular, +fake or +deprecated).
            if( invalidTags != null && invalidTags.TryGetValue( newOne.Version, out var invalid ) )
            {
                if( newOne.Commit.Sha != invalid.T.Target.Sha )
                {
                    tagConflicts ??= new();
                    tagConflicts.Add( (invalid, (newOne.Version,newOne.Tag), TagConflict.InvalidTagOnWrongCommit) );
                }
                // Invalidated tag. Forget it.
                continue;
            }
            if( v2c.TryGetValue( newOne.Version, out var exists ) )
            {
                Throw.DebugAssert( topHot != null );
                // If the version is on different commit, this is a tag conflict.
                if( newOne.Commit.Sha != exists.Sha )
                {
                    tagConflicts ??= new();
                    tagConflicts.Add( ((exists.Version, exists.Tag), (newOne.Version, newOne.Tag), TagConflict.SameVersionOnDifferentCommit) );
                    continue;
                }
                // But this is not the only conflict...
                // Actually, the only "valid" (expected) conflict is between a Deprecated and regular version.
                if( exists.IsDeprecatedVersion && newOne.IsRegularVersion )
                {
                    // The collected tag is the deprecated one. We have nothing to do except that the regular
                    // version can be deleted.
                    removableTags ??= new List<Tag>();
                    removableTags.Add( newOne.Tag );
                    Throw.DebugAssert( "The topHot cannot be the newOne (it may be exists).", topHot != newOne );
                    continue;
                }
                if( newOne.IsDeprecatedVersion && exists.IsRegularVersion )
                {
                    // The collected tag is replaced with the deprecated one.
                    // The regular tag can be removed.
                    v2c[newOne.Version] = newOne;
                    removableTags ??= new List<Tag>();
                    removableTags.Add( exists.Tag );
                    if( topHot == exists ) topHot = newOne;
                    continue;
                }
                // 2 regular tags: we must be able to chose a best one or this is
                // a DuplicatedVersionTag tag conflict.
                if( exists.IsRegularVersion && newOne.IsRegularVersion )
                {
                    // If both versions are regular, we try to resolve the conflict by choosing
                    // - an annotated tag with a parsable content info
                    // - over an annotated tag with invalid content info
                    // - over a lightweight tag.
                    // - On "equality", a tag that starts with 'v' over a tag without 'v' prefix.
                    var best = ResolveConflict( v2c, exists, newOne, ref removableTags );
                    if( best != null )
                    {
                        if( topHot == exists && best == newOne ) topHot = newOne;
                        continue;
                    }
                }
                tagConflicts ??= new();
                tagConflicts.Add( ((exists.Version, exists.Tag), (newOne.Version, newOne.Tag), TagConflict.DuplicatedVersionTag ) );
            }
            else
            {
                if( topHot == null || topHot.Version < newOne.Version )
                {
                    topHot = newOne;
                }
                v2c.Add( newOne.Version, newOne );
            }
        }


        if( hasBadTagNames )
        {
            monitor.Warn( $"One or more tags have been ignored in '{repo.DisplayPath}'. Use 'ckli tag list' to identify them." );
        }

        // We capture the invalidTags: may be one day we can create a World.Issue that could
        // remove them (we must ensure that the hidden version tags are removed in other repositories:
        // the origin remote may be enough).
        //
        // We capture tagConflicts: these MUST be fixed. Most of the branch/build commands will require
        // that there is no more tagConflicts before running.
        //
        // We feed the release database with the existing tags: this costs but it guaranties that release
        // databases are pure "index" that can be rebuilt any time.
        // We handle only regular versions:
        //  - Fake versions are, by design, skipped.
        //  - Deprecated versions are also filtered out: whether the version is deprecated is not the concern of
        //    the release database. Purging deprecated versions will be done later and through dedicated mechanisms.
        //
        // This step can also produce an important issue: the fact that a release tag is NOT the same as the Published
        // release database contains. This is odd and should almost never happen but this is a checkpoint that doesn't
        // cost much.
        //
        var lastStables = v2c.Values.Where( tc => tc.Version.IsStable ).Order().ToList();
        var lastStable = lastStables.Count > 0 ? lastStables[0] : null;

        Throw.DebugAssert( topHot == lastStable || (topHot != null && lastStable != null && topHot.Version > lastStable.Version) );
        // Two HotZone issues: no version tags (Build plugin can auto fix that) and a top hot that is too much higher than the last
        // stable (this is a strong signal of a bad tag that should be deleted).
        VersionTagInfo.HotZoneInfo? hotZone = null;    
        if( topHot != null )
        {
            Throw.DebugAssert( lastStable != null );
            // The HotZoneInfo will create the required manual fix if topHot.Version >= (lastStable.Major + 1, 0, 0).
            hotZone = VersionTagInfo.HotZoneInfo.Create( monitor, World, repo, lastStable, topHot );
        }
        else
        {
            Throw.DebugAssert( lastStable == null );
            // The build plugin will handle this.
            monitor.Warn( $"No initial version found in '{repo.DisplayPath}'." );
        }
        bool hasMissingContentInfo = false;
        World.Issue? publishedReleaseContentIssue = null;
        if( tagConflicts == null )
        {
            // This iterator provides all the versions per repository to the release database and detects
            // version tags with missing or bad content info that will be handled by the Build plugin (the issue
            // is implemented in the build plugin because its fix requires builds to be run).
            var it = new RegularVersionTagIterator( v2c );
            publishedReleaseContentIssue = _releaseDatabase.OnExistingVersionTags( monitor, repo, it.GetVersions() );
            hasMissingContentInfo = it.HasMissingContentInfo;
            if( !isExecutingIssue && (publishedReleaseContentIssue != null || hasMissingContentInfo) )
            {
                monitor.Warn( $"At least one version tag issue in '{repo.DisplayPath}'. Use 'ckli issue' for details." );
            }
        }
        else if( !isExecutingIssue )
        {
            monitor.Warn( $"{tagConflicts.Count} tag conflicts in repository '{repo.DisplayPath}'. Use 'ckli issue' for details." );
        }

        return new VersionTagInfo( repo,
                                   minVersion,
                                   maxVersion,
                                   lastStables,
                                   hotZone,
                                   v2c,
                                   removableTags,
                                   invalidTags,
                                   tagConflicts,
                                   publishedReleaseContentIssue,
                                   hasMissingContentInfo );

        static TagCommit? ResolveConflict( Dictionary<SVersion, TagCommit> v2c, TagCommit exists, TagCommit newOne, ref List<Tag>? removableTags )
        {
            Throw.DebugAssert( newOne.Sha == exists.Sha );
            // Annotated is better than lightweight, if both are annotated,
            // a parsable content info is better.
            var bestOnAnnotation = newOne.Tag.IsAnnotated
                                    ? (exists.Tag.IsAnnotated
                                        ? (newOne.BuildContentInfo != null
                                            ? (exists.BuildContentInfo != null
                                                ? null
                                                : newOne)
                                            : (exists.BuildContentInfo == null
                                                ? null
                                                : exists))
                                        : newOne)
                                    : (exists.Tag.IsAnnotated
                                        ? exists
                                        : null);
            // No better one: use the 'v' prefix.
            var best = bestOnAnnotation ?? (newOne.Version.ParsedText![0] == 'v'
                                            ? (exists.Version.ParsedText![0] == 'v'
                                                ? null
                                                : newOne)
                                            : (exists.Version.ParsedText![0] == 'v'
                                                ? exists
                                                : null));
            // No one is better: BuildMetaData difference.
            // Gives up.
            if( best != null )
            {
                removableTags ??= new List<Tag>();
                if( best != exists )
                {
                    v2c[best.Version] = best;
                    removableTags.Add( exists.Tag );
                }
                else
                {
                    removableTags.Add( newOne.Tag );
                }
            }
            return best;
        }
    }

    sealed class RegularVersionTagIterator
    {
        readonly Dictionary<SVersion, TagCommit> _v2c;
        public bool HasMissingContentInfo;

        public RegularVersionTagIterator( Dictionary<SVersion, TagCommit> v2c )
        {
            _v2c = v2c;
        }

        internal IEnumerable<(SVersion,BuildContentInfo)> GetVersions()
        {
            foreach( var tc in _v2c.Values )
            {
                if( tc.IsRegularVersion )
                {
                    var info = tc.BuildContentInfo;
                    if( info != null )
                    {
                        yield return (tc.Version, info);
                    }
                    else
                    {
                        HasMissingContentInfo = true;
                    }
                }
            }
        }
    }
}
