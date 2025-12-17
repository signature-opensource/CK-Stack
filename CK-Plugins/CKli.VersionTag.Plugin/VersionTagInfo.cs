using CK.Core;
using CKli.Core;
using CSemVer;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CKli.VersionTag.Plugin;

public sealed class VersionTagInfo : RepoInfo
{
    readonly List<TagCommit> _lastStables;
    readonly TagCommit? _lastStable;
    readonly Dictionary<SVersion, TagCommit> _v2C;
    readonly IReadOnlyList<Tag> _removableTags;
    readonly Dictionary<SVersion, (SVersion V, Tag T)>? _invalidTags;
    readonly List<((SVersion V, Tag T) T1, (SVersion V, Tag T) T2, TagConflict C)>? _tagConflicts;
    readonly World.Issue? _publishedReleaseContentIssue;
    readonly SVersion _minVersion;
    readonly SVersion? _maxVersion;
    Dictionary<string, TagCommit>? _sha2C;

    internal VersionTagInfo( Repo repo,
                             SVersion minVersion,
                             SVersion? maxVersion,
                             List<TagCommit> lastStables,
                             Dictionary<SVersion, TagCommit> v2c,
                             List<Tag>? removableTags,
                             Dictionary<SVersion, (SVersion V, Tag T)>? invalidTags,
                             List<((SVersion V, Tag T) T1, (SVersion V, Tag T) T2, TagConflict C)>? tagConflicts,
                             World.Issue? publishedReleaseContentIssue )
        : base( repo )
    {
        _lastStables = lastStables;
        if( lastStables.Count > 0 ) _lastStable = lastStables[0];
        _v2C = v2c;
        _minVersion = minVersion;
        _maxVersion = maxVersion;
        _removableTags = removableTags ?? [];
        _invalidTags = invalidTags;
        _tagConflicts = tagConflicts;
        _publishedReleaseContentIssue = publishedReleaseContentIssue;
    }

    /// <summary>
    /// Gets the smallest possible version configured for this Repo in the VersionTag plugin configuration.
    /// <para>
    /// This is necessarily a stable version (prerelease are automatically corrected).
    /// </para>
    /// </summary>
    public SVersion MinVersion => _minVersion;

    /// <summary>
    /// Gets the greatest possible version configured for this Repo in the VersionTag plugin configuration.
    /// <para>
    /// This is necessarily a stable version and this is always null in the default World and always non null
    /// in a LTS World.
    /// </para>
    /// </summary>
    public SVersion? MaxVersion => _maxVersion;

    /// <summary>
    /// Gets the last stable versions from the <see cref="LastStable"/> one to the oldest one.
    /// </summary>
    public IReadOnlyList<TagCommit> LastStables => _lastStables;

    /// <summary>
    /// Gets the last stable version: this is the common ancestor of the "hot zone" where branch model applies.
    /// </summary>
    public TagCommit? LastStable => _lastStable;

    /// <summary>
    /// Gets the versioned tag commits indexed by their version.
    /// </summary>
    public IReadOnlyDictionary<SVersion, TagCommit> TagCommits => _v2C;

    /// <summary>
    /// Gets the versioned tag commit indexed by their <see cref="TagCommit.Sha"/> and <see cref="TagCommit.ContentSha"/>.
    /// </summary>
    public IReadOnlyDictionary<string, TagCommit> TagCommitsBySha
    {
        get
        {
            if( _sha2C == null )
            {
                _sha2C = new Dictionary<string, TagCommit>( _v2C.Count * 2 );
                foreach( var tc in _v2C.Values )
                {
                    _sha2C.Add( tc.Sha, tc );
                    _sha2C.Add( tc.ContentSha, tc );
                }
            }
            return _sha2C;
        }
    }

    /// <summary>
    /// Gets the tags that can be removed (at least locally).
    /// </summary>
    public IReadOnlyList<Tag> RemovableTags => _removableTags;

    /// <summary>
    /// Gets whether tag conflicts have been found.
    /// </summary>
    public bool HasTagConflicts => _tagConflicts != null;

    /// <summary>
    /// Checks that <see cref="HasTagConflicts"/> is false or emits an error that invites
    /// the user to use 'ckli issue' and returns false. 
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <returns>True if no tag conflict exist, false otherwise.</returns>
    public bool CheckNoTagConflicts( IActivityMonitor monitor )
    {
        if( _tagConflicts != null )
        {
            monitor.Error( $"""
                    There are existing tag conflicts in '{Repo.DisplayPath}'. They must be (manually) fixed before.
                    Use 'ckli issue' to see them.
                    """ );
            return false;
        }
        return true;
    }

    /// <summary>
    /// Finds the first version tag in a commit list.
    /// </summary>
    /// <param name="commits">The list of commits to lookup.</param>
    /// <param name="foundContentSha">True if the commit has been found by its <see cref="Commit.Tree"/> content sha.</param>
    /// <returns>The first match or null.</returns>
    public TagCommit? FindFirst( IEnumerable<Commit> commits, out bool foundContentSha )
    {
        foundContentSha = false;
        // Build the index if not yet built.
        var index = TagCommitsBySha;
        foreach( Commit c in commits )
        {
            if( index.TryGetValue( c.Sha, out var tc ) )
            {
                return tc;
            }
            if( index.TryGetValue( c.Tree.Sha, out tc ) )
            {
                foundContentSha = true;
                return tc;
            }
        }
        return null;
    }

    /// <summary>
    /// Finds a versioned tag from a commit or from its content (its <see cref="Commit.Tree"/> sha).
    /// </summary>
    /// <param name="c">The commit for which a version must be found.</param>
    /// <param name="foundContentSha">True if the commit has been found by its <see cref="Commit.Tree"/> content sha.</param>
    /// <returns>The TagCommit if this commit has already been released, null otherwise.</returns>
    public TagCommit? Find( Commit c, out bool foundContentSha )
    {
        foundContentSha = false;
        // Build the index if not yet built.
        var index = TagCommitsBySha;
        if( index.TryGetValue( c.Sha, out var tc ) )
        {
            return tc;
        }
        if( index.TryGetValue( c.Tree.Sha, out tc ) )
        {
            foundContentSha = true;
            return tc;
        }
        return null;
    }

    /// <summary>
    /// Used by build: this checks that the <paramref name="buildCommit"/> can be built with <paramref name="version"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="buildCommit">The build commit selected by the build.</param>
    /// <param name="version">The target version.</param>
    /// <param name="allowRebuild">True if the user allows a rebuild of an already built commit.</param>
    /// <returns>The commit build info on success, null on error.</returns>
    public CommitBuildInfo? TryGetCommitBuildInfo( IActivityMonitor monitor, Commit buildCommit, SVersion version, bool allowRebuild )
    {
        // Preconditions for any commit.
        if( !CanBuildAnyCommit( monitor, buildCommit, version, allowRebuild, out bool isRebuild ) )
        {
            return null;
        }
        if( isRebuild )
        {
            return new CommitBuildInfo( this, version, buildCommit, isRebuild );
        }
        // We are not rebuilding (the version doesn't exist).
        // Considering the existing versions, whatever the build process is, there are some invariants
        // that must be respected.
        // - There must be no gaps between major.minor.patch increments.
        // - Whatever the version is (stable, pre or post release - the ones with the -- trick), the immediate
        //   previous stable release must exist and appear in the commit parents.
        //
        // To handle exceptions, this is where the "+fake" build meta data is considered: we strictly enforce the rules
        // but a "+fake" tag on any commit circumvents the rule and de facto documents the exception. 
        //
        if( _lastStable == null )
        {
            // There is no stable release at all in the (MinVersion,MaxVersion?) range.

            // We allow prerelease (not stable) to be initially produced.
            // What matters is the Major.Minor.Patch parts that must be based on our MinVersion that
            // ultimately defaults to 0.0.0.
            var minMajor = _minVersion.Major + 1;
            var minMinor = _minVersion.Minor + 1;
            var minPatch = _minVersion.Patch + 1;
            if( version.Major > minMajor
                || (version.Major == 0 && version.Minor > minMinor )
                || (version.Major == 0 && version.Minor == 0 && version.Patch > minPatch ) )
            {
                int fakeMajor = 0, fakeMinor = 0, fakePatch = 0;
                if( version.Major > 1 ) fakeMajor = version.Major - 1;
                else if( version.Minor > 1 )
                {
                    fakeMajor = version.Major;
                    fakeMinor = version.Minor - 1;
                }
                else
                {
                    fakeMajor = version.Major;
                    fakeMinor = version.Minor;
                    fakePatch = version.Patch - 1;
                }
                monitor.Error( $"""
                    Invalid first version 'v{version}' (there is no version yet in the configured version range in '{Repo.DisplayPath}').
                    The first version should be the configured VersionTag.MinVersion = "{_minVersion}".

                    {AllowFakeMessage( buildCommit, fakeMajor, fakeMinor, fakePatch, "non-standard first version" )}
                    """ );
                return null;
            }
            return new CommitBuildInfo( this, version, buildCommit, false );
        }
        TagCommit? baseCommit = FindBaseCommitByVersion( monitor, buildCommit, version );
        if( baseCommit == null )
        {
            return null;
        }
        var div = Repo.GitRepository.Repository.ObjectDatabase.CalculateHistoryDivergence( buildCommit, baseCommit.Commit );
        if( div.CommonAncestor.Sha != baseCommit.Commit.Sha )
        {
            monitor.Error( $"""
                    Invalid Commit/Version topology in '{Repo.DisplayPath}'.

                    To build the version 'v{version}', the commit '{buildCommit.Sha}' must be a parent of commit '{buildCommit.Sha}' with version 'v{baseCommit.Version}' built on {baseCommit.Commit.Committer.When}.
                    """ );
            return null;
        }
        return new CommitBuildInfo( this, version, buildCommit, false );
    }

    bool CanBuildAnyCommit( IActivityMonitor monitor, Commit buildCommit, SVersion version, bool allowRebuild, out bool isRebuild )
    {
        isRebuild = false;
        if( !CheckNoTagConflicts( monitor ) )
        {
            return false;
        }
        if( (_maxVersion != null && version > _maxVersion)
            || version < _minVersion )
        {
            monitor.Error( $"""
                    Version 'v{version}' is out of the configured MinVersion="{_minVersion}" MaxVersion="{_minVersion}") in '{Repo.DisplayPath}'.
                    """ );
            return false;
        }
        if( _v2C.TryGetValue( version, out var exists ) )
        {
            if( exists.IsFakeVersion )
            {
                monitor.Error( $"""
                    The version 'v{version}' in '{Repo.DisplayPath}' is defined by a fake version tag '{exists.Version.ParsedText}' on '{exists.Sha}'.

                    Fake version tags are here to allow explicit gaps in versions: this version should not be produced.
                    """ );
                return false;
            }
            // The version has already been produced. The buildCommit must be the same as the original version
            // and allowBuild must be true.
            if( exists.Commit.Sha != buildCommit.Sha )
            {
                monitor.Error( ActivityMonitor.Tags.ToBeInvestigated, $"""
                    Invalid build commit '{buildCommit.Sha}' for version 'v{version}' in '{Repo.DisplayPath}'.
                    This version has already been produced by commit '{exists.Sha}' on {exists.Commit.Committer.When}.

                    This is an error of the Build process itself: the build process must check that the version has not
                    already been released. If it's the case and a rebuild is allowed, it must consider the original build
                    commit rather than another commit (that may have the same code base).
                    """ );
                return false;
            }
            if( !allowRebuild )
            {
                // This should have been handled by the builder before calling TryGetCommitBuildInfo: this is a security.
                monitor.Error( $"""
                    The version 'v{version}' has already been produced by this commit but rebuilding it is not allowed.
                    """ );
                return false;
            }
            isRebuild = true;
            return true;
        }
        // This is a new version. The build process must have checked that the exact code base contained in the
        // build commit has not already been released from another commit with a different version (otherwise we would be
        // in the case above where the version exists).
        var already = Find( buildCommit, out var foundContentSha );
        if( already != null )
        {
            if( already.IsFakeVersion )
            {
                // The Commit is tagged with a +Fake version.
                // If the commit to build is exactly this one, this is more than weird and we reject this
                // (almost the same case as the above where a Fake version should be produced).
                // But if the commit has been found by its content Sha, then this MAY be a valid scenario.
                if( !foundContentSha )
                {
                    monitor.Error( $"""
                    Commit '{buildCommit.Sha}' is tagged with a fake version '{already.Version.ParsedText}' in '{Repo.DisplayPath}'.
                    Producing version 'v{version}' from it is forbidden.

                    Fake version tags are here to allow explicit gaps in versions: this version should not be produced.
                    """ );
                    return false;
                }
                return true;
            }
            // This is where the --ci build should be allowed (for 0000 version in Debug).
            monitor.Error( foundContentSha
                            ? $"""
                            Invalid build commit '{buildCommit.Sha}' for version 'v{version}' in '{Repo.DisplayPath}'.
                            This commit contains the exact same code as the version 'v{already.Version}' released on {already.Commit.Committer.When} by commit '{already.Sha}'.
                            
                            If publishing 2 different versions of the exact same code is really what is intended, please alter
                            any file with a minor modification. 
                            """
                            : $"""
                            Invalid build commit '{buildCommit.Sha}' for version 'v{version}' in '{Repo.DisplayPath}'.
                            This commit has already released the version 'v{already.Version}' on {already.Commit.Committer.When}.

                            The same commit cannot produce 2 different versions.
                            """ );

            return false;
        }
        return true;
    }

    TagCommit? FindBaseCommitByVersion( IActivityMonitor monitor, Commit buildCommit, SVersion version )
    {
        TagCommit? baseCommit = null;
        if( version.Patch == 0 )
        {
            if( version.Minor == 0 )
            {
                // New version is "Major.0.0".
                baseCommit = _lastStables.FirstOrDefault( tc => tc.Version.Major < version.Major );
                if( baseCommit == null )
                {
                    monitor.Error( $"""
                        Invalid version 'v{version}': there is no stable version 'v{version.Major - 1}.X.Y' in '{Repo.DisplayPath}'.

                        {AllowFakeMessage( buildCommit, version.Major - 1, 0, 0, "new \"retroactive\" major version" )}
                        """ );
                    return null;
                }
                if( baseCommit.Version.Major != version.Major - 1 )
                {
                    monitor.Error( $"""
                        Invalid version 'v{version}': the closest major is 'v{baseCommit.Version}' in '{Repo.DisplayPath}'.

                        {AllowFakeMessage( buildCommit, version.Major - 1, 0, 0, "gap between majors" )}
                        """ );
                    return null;
                }
            }
            else
            {
                // New version is "Major.Minor.0".
                baseCommit = _lastStables.FirstOrDefault( tc => tc.Version.Major == version.Major && tc.Version.Minor < version.Minor );
                if( baseCommit == null )
                {
                    monitor.Error( $"""
                        Invalid version 'v{version}': there is no stable version 'v{version.Major}.{version.Minor - 1}.X' in '{Repo.DisplayPath}'.

                        {AllowFakeMessage( buildCommit, version.Major, version.Minor - 1, 0, "new \"retroactive\" major.minor version (but this is really weird)" )}
                        """ );
                    return null;
                }
                if( baseCommit.Version.Minor != version.Minor - 1 )
                {
                    monitor.Error( $"""
                        Invalid version 'v{version}': the closest minor is 'v{baseCommit.Version}' in '{Repo.DisplayPath}'.

                        {AllowFakeMessage( buildCommit, version.Major, version.Minor - 1, 0, "gap between minors" )}                        
                        """ );
                    return null;
                }
            }
        }
        else
        {
            // New version is "Major.Minor.Patch".
            baseCommit = _lastStables.FirstOrDefault( tc => tc.Version.Major == version.Major
                                                            && tc.Version.Minor == version.Minor
                                                            && tc.Version.Patch < version.Patch );
            if( baseCommit == null )
            {
                monitor.Error( $"""
                        Invalid version 'v{version}': there is no stable version 'v{version.Major}.{version.Minor}.X' in '{Repo.DisplayPath}'.

                        {AllowFakeMessage( buildCommit, version.Major, version.Minor, version.Patch - 1, "new \"retroactive\" version (but this is really weird)" )}
                        """ );
                return null;
            }
            if( baseCommit.Version.Patch != version.Patch - 1 )
            {
                monitor.Error( $"""
                        Invalid version 'v{version}': the closest patch is 'v{baseCommit.Version}' in '{Repo.DisplayPath}'.

                        {AllowFakeMessage( buildCommit, version.Major, version.Minor, version.Patch - 1, "gap between patches" )}
                        """ );
                return null;
            }
        }
        return baseCommit;
    }

    static string AllowFakeMessage( Commit buildCommit,
                                    int fakeMajor,
                                    int fakeMinor,
                                    int fakePatch,
                                    string what )
    {
        return $"""
                If this is intended, you can tag one of the parent commit of '{buildCommit.Sha}' with a fake version tag:
                'v{fakeMajor}.{fakeMinor}.{fakePatch}+fake'

                This will (exceptionally!) allow this {what}.
                """;
    }

    internal void AddTag( SVersion version, Commit buildCommit, Tag t )
    {
        Throw.DebugAssert( !_v2C.ContainsKey( version ) );
        Throw.DebugAssert( _sha2C != null );
        Throw.DebugAssert( "This must have been checked by TryGetCommitBuildInfo.",
                             !_sha2C.ContainsKey( buildCommit.Sha )
                             && !_sha2C.ContainsKey( buildCommit.Tree.Sha ) );

        var newOne = new TagCommit( version, buildCommit, t );
        var idx = _lastStables.BinarySearch( newOne );
        Throw.DebugAssert( idx < 0 );
        _lastStables.Insert( ~idx, newOne );
        _v2C.Add( version, newOne );
        _sha2C.Add( newOne.Sha, newOne );
        _sha2C.Add( newOne.ContentSha, newOne );
    }

    internal void CollectIssues( IActivityMonitor monitor, ScreenType screenType, Action<World.Issue> collector )
    {
        if( _tagConflicts != null )
        {
            foreach( var conflict in _tagConflicts.GroupBy( c => c.C ) )
            {
                switch( conflict.Key )
                {
                    case TagConflict.DuplicateInvalidTag:
                        collector( World.Issue.CreateManual( $"Found {conflict.Count()} +invalid-tag with the same version.",
                            screenType.Text( $"""
                                        {conflict.Select( c => $" - {ToString( c.T1 )} / {ToString( c.T2 )}" ).Concatenate( Environment.NewLine )}
                                        This should be fixed manually.
                                        """ ),
                            Repo ) );
                        break;
                    case TagConflict.InvalidTagOnWrongCommit:
                        collector( World.Issue.CreateManual( $"Found {conflict.Count()} misplaced +invalid-tag.",
                            screenType.Text( $"""
                                        {conflict.Select( c => $" - Tag {ToString( c.T1 )} invalidates the version {ToString( c.T2 )}." ).Concatenate( Environment.NewLine )}
                                        This should be fixed manually.
                                        """ ),
                            Repo ) );
                        break;
                    case TagConflict.SameVersionOnDifferentCommit:
                        collector( World.Issue.CreateManual( $"Found {conflict.Count()} same version tag on different commits.",
                            screenType.Text( $"""
                                        {conflict.Select( c => $" - {ToString( c.T1 )} / {ToString( c.T2 )}" ).Concatenate( Environment.NewLine )}
                                        This should be fixed manually.
                                        """ ),
                            Repo ) );
                        break;
                    case TagConflict.DuplicatedVersionTag:
                        collector( World.Issue.CreateManual( $"Found {conflict.Count()} ambiguous version tags.",
                            screenType.Text( $"""
                                        {conflict.Select( c => $" - '{c.T1.V.ParsedText}' and '{c.T2.V.ParsedText}' on '{c.T1.T.Target.Sha}'." ).Concatenate( Environment.NewLine )}
                                        This should be fixed manually.
                                        """ ),
                            Repo ) );
                        break;
                }
            }
        }
        if( _publishedReleaseContentIssue != null )
        {
            collector( _publishedReleaseContentIssue );
        }
        if( _removableTags.Count > 0 )
        {
            collector( new RemovableVersionTagIssue(
                                $"Found {_removableTags.Count} removable version tags.",
                                screenType.Text( $"""
                                {_removableTags.Select( t => t.FriendlyName ).Concatenate()}

                                This will be fixed by deleting them locally: a fetch from te remote will make them reappear.
                                To really remove them, the tag should be deleted from the remote origin and local 
                                tags that replace them should be pushed. Use the command 'ckli tag push' to publish
                                version tags to the remote origin.
                                """ ),
                                Repo,
                                _removableTags ) );
        }

        static string ToString( (SVersion V, Tag T) t ) => $"'{t.V.ParsedText}' on '{t.T.Target.Sha}'";
    }

    sealed class RemovableVersionTagIssue : World.Issue
    {
        readonly IReadOnlyList<Tag> _tagsToDelete;

        public RemovableVersionTagIssue( string title,
                                         IRenderable body,
                                         Repo repo,
                                         IReadOnlyList<Tag> tagsToDelete )
            : base( title, body, repo )
        {
            _tagsToDelete = tagsToDelete;
        }

        protected override ValueTask<bool> ExecuteAsync( IActivityMonitor monitor, CKliEnv context, World world )
        {
            Throw.DebugAssert( Repo != null );
            using var gLog = monitor.OpenInfo( $"Deleting {_tagsToDelete.Count} version tags." );
            foreach( var t in _tagsToDelete )
            {
                Repo.GitRepository.Repository.Tags.Remove( t );
            }
            return ValueTask.FromResult( true );
        }
    }

}

