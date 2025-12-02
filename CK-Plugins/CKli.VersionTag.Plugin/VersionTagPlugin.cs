using CK.Core;
using CKli.Core;
using CSemVer;
using LibGit2Sharp;
using System;
using System.Collections.Generic;

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
        List<Tag>? ignoredVersionTags = null;
        List<(TagCommit, TagCommit)>? versionConflicts = null;
        var lastStables = new List<TagCommit>();
        var v2c = new Dictionary<SVersion, TagCommit>();
        var r = repo.GitRepository.Repository;
        foreach( var t in r.Tags )
        {
            var tagName = t.FriendlyName;
            var v = SVersion.TryParse( tagName, handleCSVersion: false );
            if( !v.IsValid ) continue;
            // Consider only target that is a commit (safe cast)
            // and filter out "+invalid" or "+deprecated" tag.
            if( v.BuildMetaData.Contains( "invalid", StringComparison.OrdinalIgnoreCase )
                || v.BuildMetaData.Contains( "deprecated", StringComparison.OrdinalIgnoreCase )
                || t.Target is not Commit c )
            {
                ignoredVersionTags ??= new List<Tag>();
                ignoredVersionTags.Add( t );
                continue;
            }
            var tc = new TagCommit( v, c, t );
            if( v2c.TryGetValue( v, out var exists ) )
            {
                versionConflicts ??= new List<(TagCommit, TagCommit)>();
                versionConflicts.Add( (tc, exists) );
            }
            else
            {
                if( v.IsStable )
                {
                    lastStables.Add( tc );
                }
                v2c.Add( v, tc );
            }
        }
        lastStables.Sort();
        return new VersionTagInfo( repo, lastStables, v2c, ignoredVersionTags, versionConflicts );
    }

}
