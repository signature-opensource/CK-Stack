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
    readonly IReadOnlyList<(TagCommit C1, TagCommit C2)> _versionConflicts;
    readonly IReadOnlyList<(SVersion, Tag)> _hidingTags;
    Dictionary<string, TagCommit>? _sha2C;

    internal VersionTagInfo( Repo repo,
                             List<TagCommit> lastStables,
                             Dictionary<SVersion, TagCommit> v2c,
                             List<Tag>? removableTags,
                             List<(TagCommit, TagCommit)>? versionConflicts,
                             List<(SVersion, Tag)>? hidingTags )
        : base( repo )
    {
        _lastStables = lastStables;
        if( lastStables.Count > 0 ) _lastStable = lastStables[0];
        _v2C = v2c;
        _removableTags = removableTags ?? [];
        _versionConflicts = versionConflicts ?? [];
        _hidingTags = hidingTags ?? [];
    }

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
    /// Gets the tags that can be removed.
    /// </summary>
    public IReadOnlyList<Tag> RemovableTags => _removableTags;

    /// <summary>
    /// Gets the "+Invalid" or "+Deprecated" tags.
    /// </summary>
    public IReadOnlyList<(SVersion Version, Tag Tag)> HidingTags => _hidingTags;

    /// <summary>
    /// Gets the duplicate version tag found.
    /// </summary>
    public IReadOnlyList<(TagCommit Duplicate, TagCommit FirstFound)> VersionConflicts => _versionConflicts;

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
        // To handle exceptions, this is where the "+Fake" build meta data is considered: we strictly enforce the rules
        // but a "+Fake" tag on any commit circumvents the rule and de facto documents the exception. 
        //
        if( _lastStable == null )
        {
            // There is no stable release at all. The very first version must be a stable.
            if( version.IsPrerelease )
            {
                monitor.Error( $"""
                    Invalid pre release version 'v{version}': there is no stable version yet in '{Repo.DisplayPath}'.
                    The first version must be a stable version (typically 'v0.1.0').
                    """ );
                return null;
            }
            if( version.Major > 1
                || (version.Major == 0 && version.Minor > 1)
                || (version.Major == 0 && version.Minor == 0 && version.Patch > 1) )
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
                    Invalid first version 'v{version}' (there is no stable version yet in '{Repo.DisplayPath}').
                    The first version must be 'v1.0.0', 'v0.1.0' or 'v0.0.1'.

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
        if( _v2C.TryGetValue( version, out var exists ) )
        {
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
                // This should have been handled bu the builder before calling TryGetCommitBuildInfo: this is a security.
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
                'v{fakeMajor}.{fakeMinor}.{fakePatch}+Fake'

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
        if( _versionConflicts.Count > 0 )
        {
            var invalidExample = _versionConflicts.SelectMany( c => new SVersion[] { c.C1.Version, c.C2.Version } )
                                                  .FirstOrDefault( v => v.BuildMetaData.Length == 0 )
                                 ?? SVersion.Create( 1, 2, 3 );
            invalidExample = invalidExample.WithBuildMetaData( "Invalid" );

            collector( World.Issue.CreateManual( $"Found {_versionConflicts.Count} duplicate version tags.",
                                        screenType.Text( $"""
                                        {_versionConflicts.Select( c => $" - {c.C1} / {c.C2}" ).Concatenate( Environment.NewLine )}
                                        This should be fixed manually.
                                        One of the commit may be tagged with a "Invalid" marker tag (example: 'v{invalidExample}') to
                                        distribute the information to other repositories (deleting a tag locally or on the remote origin will
                                        not delete it for other cloned repositories).
                                        """ ),
                                        Repo ) );
        }
        if( _removableTags.Count > 0 )
        {
            collector( new RemovableVersionTagIssue(
                                $"Found {_removableTags.Count} removable version tags.",
                                screenType.Text( $"""
                                {_removableTags.Select( t => t.FriendlyName ).Concatenate()}

                                This will be fixed by deleting them locally: a fetch will make them reappear.
                                To really remove them, the tag should be deleted from the remote origin and local 
                                tags that replace them should be pushed. Use the command 'ckli v-tag push' to publish
                                all version tags to the remote origin.
                                """ ),
                                Repo,
                                _removableTags ) );
        }
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

