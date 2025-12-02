using CK.Core;
using CKli.Core;
using CKli.VersionTag.Plugin;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace CKli.Build.Plugin;

public sealed partial class BuildPlugin
{
    void IssueRequested( IssueEvent e )
    {
        var monitor = e.Monitor;
        foreach( var r in e.Repos )
        {
            CollectVersionTagIssues( monitor, _versionTags.Get( monitor, r ), e.ScreenType, e.Add ); 
        }
    }

    void CollectVersionTagIssues( IActivityMonitor monitor,
                                  VersionTagInfo versionTagInfo,
                                  ScreenType screenType,
                                  Action<World.Issue> collector )
    {
        var lightWeightTags = versionTagInfo.LastStables.Where( tc => !tc.Tag.IsAnnotated ).ToArray();
        if( lightWeightTags.Length > 0 )
        {
            collector( new VersionTagIssue( this,
                                            versionTagInfo,
                                            $"{lightWeightTags.Length} lightweight tags must be transformed to annotated tags.",
                                            screenType.Text( lightWeightTags.Select( t => t.Version.ToString() ).Concatenate() ),
                                            lightWeightTags ) );
        }
        var unreadableMessages = versionTagInfo.LastStables.Where( tc => tc.Tag.IsAnnotated && tc.BuildContentInfo == null ).ToArray();
        if( unreadableMessages.Length > 0 )
        {
            monitor.Info( $"""
                The {unreadableMessages.Length} following tags in '{versionTagInfo.Repo.DisplayPath}' have unreadable messages:
                {unreadableMessages.Select( tc => $"- {tc.Version}:{Environment.NewLine}{tc.TagMessage}{Environment.NewLine}" ).Concatenate( Environment.NewLine )}
                """ );
            collector( new VersionTagIssue( this,
                                            versionTagInfo,
                                            $"{unreadableMessages.Length} tags have unreadable content info (see logs for details).",
                                            screenType.Text( unreadableMessages.Select( t => t.Version.ToString() ).Concatenate() ),
                                            unreadableMessages ) );
        }
    }

    sealed class VersionTagIssue : World.Issue
    {
        readonly BuildPlugin _buildPlugin;
        readonly VersionTagInfo _versionTagInfo;
        readonly TagCommit[] _tagsToRebuild;

        public VersionTagIssue( BuildPlugin buildPlugin,
                                VersionTagInfo versionTagInfo,
                                string title,
                                IRenderable body,
                                TagCommit[] tagsToRebuild )
            : base( title, body, versionTagInfo.Repo )
        {
            _buildPlugin = buildPlugin;
            _versionTagInfo = versionTagInfo;
            _tagsToRebuild = tagsToRebuild;
        }

        protected override ValueTask<bool> ExecuteAsync( IActivityMonitor monitor, CKliEnv context, World world )
        {
            Throw.DebugAssert( Repo != null );
            using var gLog = monitor.OpenInfo( $"Fixing {_tagsToRebuild.Length} tags content info in '{Repo.DisplayPath}'." );
            foreach( var tc in _tagsToRebuild )
            {
                if( !_buildPlugin.CoreBuild( monitor, context, _versionTagInfo, tc.Commit, tc.Version, runTest: false, rebuild: true ) )
                {
                    return ValueTask.FromResult( false );
                }
            }
            return ValueTask.FromResult( true );
        }
    }
}
