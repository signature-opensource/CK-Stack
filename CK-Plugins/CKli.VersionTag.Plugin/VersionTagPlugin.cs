using CK.Core;
using CKli.Core;
using CSemVer;
using LibGit2Sharp;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace CKli.VersionTag.Plugin;

public sealed class VersionTagPlugin : PrimaryRepoPlugin<VersionTagInfo>
{
    static readonly XName _xMajorRange = XNamespace.None.GetName( "MajorRange" );

    public VersionTagPlugin( PrimaryPluginContext primaryContext )
        : base( primaryContext )
    {
        World.Events.Issue += IssueRequested;
    }

    (int MinMajor, int MaxMajor) ReadConfiguration( IActivityMonitor monitor, Repo repo )
    {
        var config = PrimaryPluginContext.GetConfigurationFor( repo );
        if( config.HasValue )
        {
            var a = config.Value.XElement.Attribute( _xMajorRange );
            if( a != null )
            {
                var text = a.Value.AsSpan();
                if( text.TryMatchInteger<int>( out var min )
                    && text.SkipWhiteSpaces()
                    && text.TryMatch( ',' )
                    && text.TryMatchInteger<int>( out var max ) )
                {
                    var sMin = int.Clamp( min, 0, CSVersion.MaxMajor - 1 );
                    var sMax = int.Clamp( max, sMin + 1, CSVersion.MaxMajor );
                    if( sMin != min || sMax != max )
                    {
                        var fix = $"{sMin},{sMax}";
                        monitor.Warn( $"""
                            Incorrect "MajorRange" attribute: '{a.Value}' in VersionTagPlugin configuration of '{repo.DisplayPath}'.
                            Auto corrected to "{fix}".
                            """ );
                        PrimaryPluginContext.ConfigurationEditor.EditConfigurationFor( monitor,
                                                                                       repo,
                                                                                       ( monitor, c ) => c.XElement.SetAttributeValue( _xMajorRange, fix ) );
                    }
                    return (sMin, sMax);
                }
                monitor.Warn( $"""
                    Unable to parse "MajorRange" attribute: '{a.Value}' in VersionTagPlugin configuration of '{repo.DisplayPath}'.
                    Using full range "0,{CSVersion.MaxMajor}".
                    """ );
            }
        }
        return (0, CSVersion.MaxMajor);
    }

    void IssueRequested( IssueEvent e )
    {
        var monitor = e.Monitor;
        foreach( var r in e.Repos )
        {
            Get( monitor, r ).CollectIssues( monitor, e.ScreenType, e.Add );
        }
    }

    protected override VersionTagInfo Create( IActivityMonitor monitor, Repo repo )
    {
        var (minMajor, maxMajor) = ReadConfiguration( monitor, repo );

        List<Tag>? removableTags = null;
        // Collects conflicting tags.
        List<((SVersion V, Tag T) T1, (SVersion V, Tag T) T2, TagConflict C)>? tagConflicts = null;

        // First pass. Enumerates all the tags to keep all +InvalidTag and
        // tags in the MajorRange.
        // This list is temporary (first pass) to build the v2c index.
        List<TagCommit> validTags = new List<TagCommit>();
        Dictionary<SVersion, (SVersion V, Tag T)>? invalidTags = null;
        (SVersion? Version,Tag? Tag) lastStableBelowMinMajor = default;
        var r = repo.GitRepository.Repository;
        foreach( var t in r.Tags )
        {
            var tagName = t.FriendlyName;
            var v = SVersion.TryParse( tagName );
            // Consider only SVersion tag and target that is a commit (safe cast).
            if( !v.IsValid || t.Target is not Commit c )
            {
                continue;
            }
            // A version above MaxMajor is definitely not our concern.
            // But it's not the same for the MinMajor...
            if( v.Major > maxMajor ) continue;

            // A +InvalidTag totally cancels an existing version tag. We collect them
            // and apply them once all the valid tags have been collected.
            //
            // The +InvalidTag tags are temporary artifacts that are used to distribute the information
            // across the repositories. Once the bad tag doesn't appear anywhere, a +InvalidTag tag
            // must be removed.
            //
            if( v.BuildMetaData.Contains( "InvalidTag", StringComparison.OrdinalIgnoreCase )
                || v.BuildMetaData.Contains( "Invalid", StringComparison.OrdinalIgnoreCase ) )
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
            if( v.Major < minMajor )
            {
                // Tracking the best version below our MinMajor is required to provide a base
                // version in order to detect gaps and correct code base inclusion.
                // We deliberately accept a +Fake and +Deprecated here: a +Deprecated indicates
                // the existence of a version and +Fake exists exactly for this.
                //
                // There is a weakness here: if the best version found here has a +InvalidTag,
                // we cannot see it. This is the price to pay to use the minMajor as an early exit
                // and limit the number of handled tags.
                // This is assumed: MajorRange is used for LTS, a +InvalidTag appearing after the
                // last regular release of a LTS is highly improbable - creating a LTS is done
                // on a clean, fully released World. Moreover the LTS world should only emit fix of already
                // produced stable: the major is okay and as we increase the major, this is fine.
                // 
                if( v.IsStable
                    && (lastStableBelowMinMajor.Version == null || lastStableBelowMinMajor.Version < v) )
                {
                    lastStableBelowMinMajor = (v, t);
                }
                continue;
            }
            // A +Deprecated was an actual version. They appear in the VersionTagInfo.TagCommits and
            // VersionTagInfo.Stables collections (like a +Fake).
            // This is required, for instance, to be able to produce a 4.0.1 fix after the Deprecated 4.0.0 version.
            //
            // As opposed to +Invalid tags, +Deprecated tags must never be deleted. They memorize the
            // existence of a version.
            //
            validTags.Add( new TagCommit( v, c, t ) );
        }
        // Second pass: filters out the invalid tags and produces the v2C index
        //              along with potential tag conflicts.
        var v2c = new Dictionary<SVersion, TagCommit>();
        foreach( var newOne in validTags )
        {
            // This filters out any version (regular, +Fake or +Deprecated).
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
                // If the version is on different commit, this is a tag conflict.
                if( newOne.Commit.Sha != exists.Sha )
                {
                    tagConflicts ??= new();
                    tagConflicts.Add( ((exists.Version, exists.Tag), (newOne.Version, newOne.Tag), TagConflict.SameVersionOnDifferentCommit) );
                    continue;
                }
                // But this is not the only conflict...
                // Actually, the only "valid" conflict is between a Deprecated and regular version.
                if( exists.IsDeprecatedVersion && newOne.IsRegularVersion )
                {
                    // The collected tag is the deprecated one. We have nothing to do except that the regular
                    // version can be deleted.
                    removableTags ??= new List<Tag>();
                    removableTags.Add( newOne.Tag );
                }
                else if( newOne.IsDeprecatedVersion && exists.IsRegularVersion )
                {
                    // The collected tag is replaced with the deprecated one.
                    // The regular tag can be removed.
                    v2c[newOne.Version] = newOne;
                    removableTags ??= new List<Tag>();
                    removableTags.Add( exists.Tag );
                }
                else if( exists.IsRegularVersion && newOne.IsRegularVersion )
                {
                    // If both versions are regular, we try to resolve the conflict by choosing
                    // - an annotated tag with a parsable content info
                    // - over an annotated tag with invalid content info
                    // - over a lightweight tag.
                    // - On "equality", a tag that starts with 'v' over a tag without 'v' prefix.
                    if( ResolveConflict( v2c, exists, newOne, ref removableTags ) )
                    {
                        continue;
                    }
                }
                tagConflicts ??= new();
                tagConflicts.Add( ((exists.Version, exists.Tag), (newOne.Version, newOne.Tag), TagConflict.DuplicatedVersionTag ) );
            }
            else
            {
                v2c.Add( newOne.Version, newOne );
            }
        }

        var lastStables = v2c.Values.Where( tc => tc.Version.IsStable ).Order().ToList();

        // We capture the invalidTags: may be one day we can create a World.Issue that could
        // remove them (we must ensure that the hidden version tags are removed in other repositories:
        // the origin remote may be enough).
        //
        // We capture tagConflicts: these MUST be fixed. Most of the branch/build commands will require
        // that there is no more tagConflicts before running.
        //
        return new VersionTagInfo( repo, minMajor, maxMajor, lastStables, v2c, removableTags, invalidTags, tagConflicts );

        static bool ResolveConflict( Dictionary<SVersion, TagCommit> v2c, TagCommit exists, TagCommit newOne, ref List<Tag>? removableTags )
        {
            Throw.DebugAssert( newOne.Sha == exists.Sha );
            bool resolved = true;
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
            if( best == null )
            {
                resolved = false;
            }
            else
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
            return resolved;
        }
    }

    internal static bool IsDeprecatedOrInvalidVersion( SVersion v, out bool isDeprecated )
    {
        return (isDeprecated = v.BuildMetaData.Contains( "Deprecated", StringComparison.OrdinalIgnoreCase ))
                        || v.BuildMetaData.Contains( "Invalid", StringComparison.OrdinalIgnoreCase );
    }
}
