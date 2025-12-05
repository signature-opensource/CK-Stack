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

public sealed partial class VersionTagPlugin : PrimaryRepoPlugin<VersionTagInfo>
{
    static readonly XName _xMinVersion = XNamespace.None.GetName( "MinVersion" );
    static readonly XName _xMaxVersion = XNamespace.None.GetName( "MaxVersion" );

    public VersionTagPlugin( PrimaryPluginContext primaryContext )
        : base( primaryContext )
    {
        World.Events.Issue += IssueRequested;
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

    (SVersion Min, SVersion? Max) ReadRepoConfiguration( IActivityMonitor monitor, Repo repo )
    {
        var config = PrimaryPluginContext.GetConfigurationFor( repo );
        // Non existing or invalid MinVersion fallbacks to v0.0.0.
        SVersion min = ReadVersionAttribute( monitor, config, _xMinVersion, CSVersion.FirstPossibleVersions[0] );

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
            // A version above MaxVersion is definitely not our concern.
            // But it's not the same for the MinMajor...
            if( maxVersion != null && v > maxVersion ) continue;

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
            if( v < minVersion )
            {
                // Tracking the best version below our MinVersion is required to provide a base
                // version in order to detect gaps and correct code base inclusion.
                // We deliberately accept a +Fake and +Deprecated here: a +Deprecated indicates
                // the existence of a version and +Fake exists exactly for this.
                //
                // There is a weakness here: if the best version found here has a +InvalidTag,
                // we use a version that has been invalidated...
                //
                // This is the price to pay to use the minMajor as an early exit and limit the number of handled tags
                // and this is assumed: MaxVersion is used for LTS, a +InvalidTag appearing after the
                // last regular release of a LTS is highly improbable - creating a LTS is done
                // on a clean, fully released World. Moreover the LTS world should only emit fix of already
                // produced stable.
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
        return new VersionTagInfo( repo, minVersion, maxVersion, lastStables, v2c, removableTags, invalidTags, tagConflicts );

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
