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
using System.Text;
using System.Xml.Linq;

namespace CKli.VersionTag.Plugin;

public sealed partial class VersionTagPlugin : PrimaryRepoPlugin<VersionTagInfo>
{
    readonly ReleaseDatabasePlugin _releaseDatabase;
    readonly ArtifactHandlerPlugin _artifactHandler;
    Dictionary<string, SVersion>? _externalPackages;

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
    /// Sets <see cref="XMinVersion"/> for a Repo.
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
                                   .Edit( monitor, ( monitor, e ) => e.SetAttributeValue( XNames.MinVersion, min.ToString() ) );
    }

    /// <summary>
    /// Gets The World's configured packages versions from this
    /// <code>
    /// &lt;Packages&gt;
    ///     &lt;Package Name = "..." Version="..." /&gt;
    ///  &lt;/Packages&gt;
    /// </code>
    /// VersionTag plugin configuration content.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>The World's configured packages versions.</returns>
    public IReadOnlyDictionary<string, SVersion>? GetPackagesConfiguration( IActivityMonitor monitor )
    {
        try
        {
            return _externalPackages ??= PrimaryPluginContext.Configuration.XElement
                                                    .Elements( "Packages" )
                                                    .Elements( "Package" )
                                                    .ToDictionary( e => (string)e.Attribute( XNames.Name )!,
                                                                   e => SVersion.Parse( (string)e.Attribute( XNames.Version )! ),
                                                                   StringComparer.OrdinalIgnoreCase );
        }
        catch( Exception ex )
        {
            monitor.Error( $"""
                Unable to read <Packages> element from <VersionTag> configuration.
                Expecting:
                <Packages>
                    <Package Name="..." Version="..." />
                </Packages>
                Configuration is:
                {PrimaryPluginContext.Configuration.XElement}
                """, ex );
            return null;
        }
    }

    /// <summary>
    /// Destroys a released version. The version tag is deleted, the release database is updated
    /// and any artifacts are removed: this centralizes the calls to <see cref="ReleaseDatabasePlugin.DestroyLocalRelease(IActivityMonitor, Repo, SVersion)"/>
    /// and <see cref="ArtifactHandlerPlugin.DestroyLocalRelease(IActivityMonitor, Repo, SVersion, BuildContentInfo)"/>
    /// that should not be called directly.
    /// <para>
    /// This is idempotent and doesn't trigger the initialization of the <see cref="VersionTagInfo"/> for the Repo, but if it
    /// <see cref="RepoPluginBase{T}.HasRepoInfoBeenCreated(Repo)">has been created</see> it is updated.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="repo">The source repository.</param>
    /// <param name="version">The release to destroy.</param>
    /// <param name="removeFromNuGetGlobalCache">
    /// False to let the package in the NuGet global cache (if it exists).
    /// The global cache is "%userprofile%\.nuget\packages" on windows and "~/.nuget/packages" on Mac/Linux.
    /// </param>
    /// <returns>True on success, false on error (only errors are that assets cannot be properly deleted).</returns>
    public bool DestroyLocalRelease( IActivityMonitor monitor, Repo repo, SVersion version, bool removeFromNuGetGlobalCache = true )
    {
        TagCommit? tagCommit = HasRepoInfoBeenCreated( repo ) ? Get( monitor, repo ).RemoveTagCommit( version ) : null;

        Tag? tag = tagCommit?.Tag;
        BuildContentInfo? tagContent = tagCommit?.BuildContentInfo;
        if( tag == null )
        {
            tag = repo.GitRepository.Repository.Tags[$"v{version.ToString()}"] ?? repo.GitRepository.Repository.Tags[version.ToString()];
            if( tag != null )
            {
                _ = BuildContentInfo.TryParse( tag.Annotation?.Message, out tagContent );
            }
        }
        if( tag != null )
        {
            repo.GitRepository.DeleteLocalTags( monitor, [tag.CanonicalName] );
        }
        return CleanupLocalRelease( monitor, repo, version, tagContent, removeFromNuGetGlobalCache );
    }

    /// <summary>
    /// Centralized deletion of the artifacts of a release. This tries to delete every possible traces but doesn't remove the
    /// versioned tag: use <see cref="DestroyLocalRelease(IActivityMonitor, Repo, SVersion, bool)"/> to fully destroy a release.
    /// <para>
    /// This centralizes the calls to <see cref="ReleaseDatabasePlugin.DestroyLocalRelease(IActivityMonitor, Repo, SVersion)"/>
    /// and <see cref="ArtifactHandlerPlugin.DestroyLocalRelease(IActivityMonitor, Repo, SVersion, BuildContentInfo)"/>
    /// that should not be called directly.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="repo">The source repository.</param>
    /// <param name="version">The version to cleanup.</param>
    /// <param name="removeFromNuGetGlobalCache">
    /// False to let the package in the NuGet global cache (if it exists).
    /// The global cache is "%userprofile%\.nuget\packages" on windows and "~/.nuget/packages" on Mac/Linux.
    /// </param>
    /// <returns>True on success, false on error (only errors are that assets cannot be properly deleted).</returns>
    public bool CleanupLocalRelease( IActivityMonitor monitor,
                                     Repo repo,
                                     SVersion version,
                                     BuildContentInfo? knownContent,
                                     bool removeFromNuGetGlobalCache = true )
    {
        return DoCleanupLocalRelease( monitor, repo, version, knownContent, removeFromPublishedDatabase: false, removeFromNuGetGlobalCache );
    }

    bool DoCleanupLocalRelease( IActivityMonitor monitor,
                                Repo repo,
                                SVersion version,
                                BuildContentInfo? knownContent,
                                bool removeFromPublishedDatabase,
                                bool removeFromNuGetGlobalCache = true )
    {
        using( monitor.OpenInfo( $"Deleting local release '{repo.DisplayPath}/{version}'." ) )
        {
            // Take no risk: delete every possible traces (but avoids calling twice the same destroy).
            //
            // If we must suppress the Published release, call DestroyPublishedRelease, otherwise we must
            // only lookup for the content.
            var dbPubContent = removeFromPublishedDatabase
                                ? _releaseDatabase.DestroyPublishedRelease( monitor, repo, version )
                                : _releaseDatabase.GetBuildContentInfo( monitor, repo, version, fromPublished: true );

            var dbLocContent = _releaseDatabase.DestroyLocalRelease( monitor, repo, version );

            bool success = true;
            if( knownContent != null )
            {
                if( knownContent == dbPubContent ) dbPubContent = null;
                if( knownContent == dbLocContent ) dbLocContent = null;
                success = _artifactHandler.DestroyLocalRelease( monitor, repo, version, knownContent, removeFromNuGetGlobalCache );
            }
            if( dbPubContent != null )
            {
                if( dbPubContent == dbLocContent ) dbLocContent = null;
                success &= _artifactHandler.DestroyLocalRelease( monitor, repo, version, dbPubContent, removeFromNuGetGlobalCache );
            }
            if( dbLocContent != null )
            {
                success &= _artifactHandler.DestroyLocalRelease( monitor, repo, version, dbLocContent, removeFromNuGetGlobalCache );
            }
            return success;
        }
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
        Remote tags drives the update of the published database: a local only version tag will remain local.
        """ )]
    [CommandPath( "maintenance release-database rebuild" )]
    public bool RebuildReleaseDatabases( IActivityMonitor monitor,
                                         CKliEnv context,
                                         [Description("Pushes the local tag to update an existing remote tag if its content differ.")]
                                         bool updateRemoteTags = false )
    {
        var repos = World.GetAllDefinedRepo( monitor );
        if( repos == null ) return false;

        // Before destroying the databases, we require that the tags (GitTagInfo.Diff) are "clean":
        // there must not be "fetch required" (tags with unknown commit target) nor tag conflicts
        // (tag that exists on local and remote and targets 2 different commits).
        //
        // Obtaining these tags info will allow us to consider that a version tag that is on the
        // remote side is de facto published: we'll publish the local release that transfers its
        // information from the local to the published database.
        //
        // Moreover (below), if the version tag differ, we push the local one (that updates the remote one):
        // this supports a move from obsolete (or legacy lightweight) tags to an up-to-date version of the tags content.
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
        if( !TryGetAllWithoutIssue( monitor, out var allInfo, before: "retrying" ) )
        {
            return false;
        }
        // Consider remote version tags: move the release from local to remote database and update the remote tag if it differs.
        bool pushTagFailed = false;
        var pushTagBuffer = new List<Tag>();
        var updateRemoteTagsWarning = updateRemoteTags ? null : new StringBuilder();
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
                            pushTagBuffer.Add( tc.Tag );
                        }
                    }
                }
                if( pushTagBuffer.Count > 0 )
                {
                    if( updateRemoteTagsWarning == null )
                    {
                        monitor.Info( "Updating remote tags that differ from locally updated ones." );
                        if( !repo.GitRepository.PushTags( monitor, pushTagBuffer.Select( t => t.CanonicalName ) ) )
                        {
                            // When push failed, odds are that we miss the key.
                            // it seems better to stop immediately.
                            pushTagFailed = true;
                            break;
                        }
                    }
                    else
                    {
                        updateRemoteTagsWarning.Append( $"""
                            - {repo.DisplayPath}:
                              '{pushTagBuffer.Select( t => t.FriendlyName ).Concatenate("', '")}'


                            """ );
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
            Throw.DebugAssert( updateRemoteTagsWarning == null );
            monitor.Warn( """
                Some tag pushes have failed. Use 'ckli tag list' to analyze tag differences.
                These differences should be fixed manually.
                """ );
        }
        else if( updateRemoteTagsWarning != null && updateRemoteTagsWarning.Length > 0 )
        {
            updateRemoteTagsWarning.AppendLine().Append( """
                Above repositories have remote tags that differ from their local counterparts.
                They should be updated by using the --update-remote-tags flag or differences can be analyzed with 'ckli tag list'.
                """ );
            monitor.Warn( updateRemoteTagsWarning.ToString() );
        }
        monitor.Info( ScreenType.CKliScreenTag, "Databases of 'Published' and 'Local' releases been successfully rebuilt:" );
        return true;
    }

    static bool GetAllDiffTags( IActivityMonitor monitor,
                                CKliEnv context,
                                IReadOnlyList<Repo> repos,
                                out ImmutableArray<GitTagInfo.Diff> allDiffTags )
    {
        using( monitor.OpenInfo( "Analyzing Tags on the repositories and their 'origin' remotes. Checking that no blocking issue exist for them." ) )
        {
            bool success = true;
            var b = ImmutableArray.CreateBuilder<GitTagInfo.Diff>( repos.Count );
            List<int>? issues = null;
            foreach( var repo in repos )
            {
                using( monitor.OpenInfo( $"Collecting local & remote tags of '{repo.DisplayPath}'." ) )
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
        SVersion min = ReadVersionAttribute( monitor, config, XNames.MinVersion, SVersion.Create( 0, 0, 0 ) );

        SVersion? max = null;
        var maxAttr = config.XElement.Attribute( XNames.MaxVersion );
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
            min = ReadVersionAttribute( monitor, config, XNames.MaxVersion, min );
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
        //              During this pass, we also compute the topHot (that is the greatest regular version tag).
        var v2c = new Dictionary<SVersion, TagCommit>();
        TagCommit? topHot = null;
        foreach( var newOne in validTags )
        {
            // This filters out any version tags (regular, +fake or +deprecated): +invalid always wins.
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

        // LastStables are used by ckli fix. They must be sorted (in reverse version order, TagCommit.CompareTo does that).
        // We use an explicit for each loop so we also compute the lowestCI tag to support auto-deletion of obsolete CI builds.
        var lastStables = new List<TagCommit>();
        TagCommit? lowestCI = null;
        foreach( var tc in v2c.Values )
        {
            if( tc.Version.IsStable )
            {
                lastStables.Add( tc );
            }
            else if( tc.Version.IsCI() && (lowestCI == null || lowestCI.Version > tc.Version) )
            {
                lowestCI = tc;
            }
        }
        // Uses TagCommit.CompareTo that reverts the Version.
        lastStables.Sort();
        TagCommit? lastStable = null;
        TagCommit? lastAvailableStable = null;
        if( lastStables.Count > 0 )
        {
            lastStable = lastStables[0];
            // Handle obsolete CI builds.
            if( lowestCI != null && lowestCI.Version < lastStable.Version )
            {
                AutoDeleteObsoleteCIReleases( monitor, repo, removableTags, v2c, lastStable, lowestCI );
            }
            if( lastStable.BuildContentInfo != null )
            {
                lastAvailableStable = lastStable;
            }
            else
            {
                lastAvailableStable = lastStables.FirstOrDefault( tc => tc.BuildContentInfo != null );
            }
        }

        Throw.DebugAssert( topHot == lastStable || (topHot != null && lastStable != null && topHot.Version > lastStable.Version) );
        // Two HotZone issues: no version tags (Build plugin can auto fix that) and a top hot that is "too much higher" than the last
        // stable (this is a strong signal of a bad tag that should be deleted).
        VersionTagInfo.HotZoneInfo? hotZone = null;    
        if( topHot != null )
        {
            Throw.DebugAssert( lastStable != null );
            // The HotZoneInfo will create the required manual fix if topHot.Version >= (lastStable.Major + 1, 0, 0).
            hotZone = VersionTagInfo.HotZoneInfo.Create( monitor, World, repo, lastStable, topHot, lastAvailableStable );
        }
        else
        {
            Throw.DebugAssert( lastStable == null );
            // The build plugin will handle this.
            monitor.Warn( $"No initial version found in '{repo.DisplayPath}'." );
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

    void AutoDeleteObsoleteCIReleases( IActivityMonitor monitor,
                                       Repo repo,
                                       List<Tag>? removableTags,
                                       Dictionary<SVersion, TagCommit> v2c,
                                       TagCommit lastStable,
                                       TagCommit lowestCI )
    {
        Throw.DebugAssert( lowestCI.Version < lastStable.Version );
        // There's at least one CI version that should be suppressed.
        var toRemove = v2c.Values.Where( tc => tc.Version.IsCI() && tc.Version < lastStable.Version ).ToList();
        Throw.DebugAssert( toRemove.Contains( lowestCI ) );
        bool success = true;
        using( monitor.OpenInfo( $"Deleting obsolete CI versions: '{toRemove.Select( tc => tc.Version.ParsedText ).Concatenate( "', '" )}'." ) )
        {
            foreach( var tc in toRemove )
            {
                // This TagCommit is doomed.
                v2c.Remove( tc.Version );
                // If we have collected a "+deprecated", removes the non-deprecated one from the removable tags.
                if( tc.IsDeprecatedVersion && removableTags != null )
                {
                    var vClean = tc.Version.WithBuildMetaData( null );
                    removableTags.RemoveAll( t => SVersion.TryParse( t.FriendlyName, out var v ) && v == vClean );
                }
                success &= DoCleanupLocalRelease( monitor,
                                                  repo,
                                                  tc.Version,
                                                  tc.BuildContentInfo,
                                                  removeFromPublishedDatabase: true,
                                                  removeFromNuGetGlobalCache: true );
            }
            if( success )
            {
                success = repo.GitRepository.DeleteLocalTags( monitor, toRemove.Select( tc => tc.Version.ParsedText! ) );
            }
            monitor.CloseGroup( success ? "Success." : "Failed." );
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
