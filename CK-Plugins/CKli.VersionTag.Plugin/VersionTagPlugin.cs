using CK.Core;
using CKli.Core;
using CSemVer;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CKli.VersionTag.Plugin;

public sealed class VersionTagPlugin : PrimaryRepoPlugin<VersionTagInfo>
{
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

    protected override VersionTagInfo Create( IActivityMonitor monitor, Repo repo )
    {
        List<Tag>? removableTags = null;
        List<(SVersion, Tag)>? hidingTags = null;
        List<(TagCommit, TagCommit)>? versionConflicts = null;
        var v2c = new Dictionary<SVersion, TagCommit>();
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
            // Routes "+Invalid" or "+Deprecated" tag to the hidingTags list.
            if( IsHidingDisabledOrInvalidVersion( v ) )
            {
                hidingTags ??= new List<(SVersion, Tag)>();
                hidingTags.Add( (v, t) );
                continue;
            }
            else
            {
                var newOne = new TagCommit( v, c, t );
                if( v2c.TryGetValue( v, out var exists ) )
                {
                    // The version matches. Only 'v' prefix or BuildMetaData can differ (but we already handled
                    // +Invalid and +Deprecated meta).
                    // If these tags are on 2 different commits or we cannot elect a best, this is a conflict.
                    if( !ResolveConflict( v2c, exists, newOne, ref removableTags ) )
                    {
                        versionConflicts ??= new List<(TagCommit, TagCommit)>();
                        versionConflicts.Add( (newOne, exists) );
                    }
                }
                else
                {
                    v2c.Add( v, newOne );
                }
            }
        }
        // The idea here is to not spend too much time handling the "hiding tags":
        // - If a hiding tag hides nothing, forget it. It is not removable tag.
        // - If a hiding tag is not on the commit that it is supposed to hide: this should barely happen
        //   and we take no risk: we throw. This is an inconsistency that should be fixed before
        //   doing anything else. 
        // We are left with TagCommit that MUST be removed from v2c: there shouldn't be
        // much of them.
        // Once the hiding tag has been distributed, the tag that it hides can be safely deleted: the hiding
        // tag keeps the information for the user that "Here the version V was. RIP." but it then almost has
        // no impact on computation time.
        if( hidingTags != null )
        {
            foreach( var (v, t) in hidingTags )
            {
                if( v2c.TryGetValue( v, out var found ) )
                {
                    if( found.Sha != t.Target.Sha )
                    {
                        Throw.InvalidDataException( $"""
                            Invalid tag '{v.ParsedText}' in '{repo.DisplayPath}' on commit '{t.Target.Sha}'.
                            It pretends to hide the '{found.Version.ParsedText}' that is not on the same commit.
                            This must be fixed manually by removing one of them.
                            """ );
                    }
                    // Actual hide.
                    v2c.Remove( v );
                    removableTags ??= new List<Tag>();
                    removableTags.Add( found.Tag );
                }
            }
        }
        var lastStables = v2c.Values.Where( tc => tc.Version.IsStable ).Order().ToList();
        return new VersionTagInfo( repo, lastStables, v2c, removableTags, versionConflicts, hidingTags );

        static bool ResolveConflict( Dictionary<SVersion, TagCommit> v2c, TagCommit exists, TagCommit newOne, ref List<Tag>? removableTags )
        {
            bool resolved = true;
            if( newOne.Sha != exists.Sha )
            {
                resolved = false;
            }
            else
            {
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
            }
            return resolved;
        }
    }

    internal static bool IsHidingDisabledOrInvalidVersion( SVersion v )
    {
        return v.BuildMetaData.Contains( "Invalid", StringComparison.OrdinalIgnoreCase )
                        || v.BuildMetaData.Contains( "Deprecated", StringComparison.OrdinalIgnoreCase );
    }
}
