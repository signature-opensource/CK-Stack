using CK.Core;
using CKli.BranchModel.Plugin;
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
        // No version tag case.
        if( versionTagInfo.LastStables.Count == 0 )
        {
            Throw.DebugAssert( versionTagInfo.HotZone == null );
            var branchModel = _branchModel.Get( monitor, versionTagInfo.Repo );
            if( branchModel.Root.GitBranch != null )
            {
                // We have a root branch: let's fix this by building it with the MinVersion.
                collector( new NoVersionTagIssue( this,
                                                  versionTagInfo,
                                                  "Missing initial version.",
                                                  screenType.Text( $"""
                                                      This can be fixed by building the 'v{versionTagInfo.MinVersion}' version from '{branchModel.Root.BranchName}' branch.
                                                      """ ),
                                                  branchModel.Root ) );
            }
        }
        // Tags rebuild case.
        var regulars = versionTagInfo.TagCommits.Values.Where( tc => tc.IsRegularVersion );
        var lightWeightTags = regulars.Where( tc => !tc.Tag.IsAnnotated ).ToArray();
        const string rebuildMessage = """
            Fixing these tags recompiles the commit to obtain the consumed/produced packages and asset files.
            On success, the tag content is updated.
            When the commit cannot be successfully recompiled, the command 'ckli rebuild old'
            can retry and sets a "+deprecated" version on the old commits on failure.
            """;
        if( lightWeightTags.Length > 0 )
        {
            collector( new TagsRebuildIssue( this,
                                             versionTagInfo,
                                             $"{lightWeightTags.Length} lightweight tags must be transformed to annotated tags.",
                                             screenType.Text( $"""
                                                {lightWeightTags.Select( t => t.Version.ToString() ).Concatenate()}

                                                {rebuildMessage}
                                                """ ),
                                             lightWeightTags ) );
        }
        var unreadableMessages = regulars.Where( tc => tc.Tag.IsAnnotated && tc.BuildContentInfo == null ).ToArray();
        if( unreadableMessages.Length > 0 )
        {
            monitor.Info( $"""
                The {unreadableMessages.Length} following tags in '{versionTagInfo.Repo.DisplayPath}' have unreadable messages:
                {unreadableMessages.Select( tc => $"- {tc.Version}:{Environment.NewLine}{tc.TagMessage}{Environment.NewLine}" ).Concatenate( Environment.NewLine )}
                """ );
            collector( new TagsRebuildIssue( this,
                                             versionTagInfo,
                                             $"{unreadableMessages.Length} tags have unreadable content info (see logs for details).",
                                             screenType.Text( $"""
                                                {unreadableMessages.Select( t => t.Version.ToString() ).Concatenate()}

                                                {rebuildMessage}
                                                """ ),
                                             unreadableMessages ) );
        }
    }

    sealed class TagsRebuildIssue : World.Issue
    {
        readonly BuildPlugin _buildPlugin;
        readonly VersionTagInfo _versionTagInfo;
        readonly TagCommit[] _tagsToRebuild;

        public TagsRebuildIssue( BuildPlugin buildPlugin,
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

        protected override async  ValueTask<bool> ExecuteAsync( IActivityMonitor monitor, CKliEnv context, World world )
        {
            Throw.DebugAssert( Repo != null );
            using var gLog = monitor.OpenInfo( $"Fixing {_tagsToRebuild.Length} tags content info in '{Repo.DisplayPath}'." );
            foreach( var tc in _tagsToRebuild )
            {
                if( await _buildPlugin.CoreBuildAsync( monitor,
                                                       context,
                                                       _versionTagInfo,
                                                       tc.Commit,
                                                       tc.Version,
                                                       runTest: false,
                                                       forceRebuild: true ) == null )
                {
                    return false;
                }
            }
            return true;
        }
    }

    sealed class NoVersionTagIssue : World.Issue
    {
        readonly BuildPlugin _buildPlugin;
        readonly VersionTagInfo _versionTagInfo;
        readonly HotBranch _root;

        public NoVersionTagIssue( BuildPlugin buildPlugin, VersionTagInfo versionTagInfo, string title, IRenderable body, HotBranch root )
            : base( title, body, versionTagInfo.Repo )
        {
            _buildPlugin = buildPlugin;
            _versionTagInfo = versionTagInfo;
            _root = root;
        }

        protected override async ValueTask<bool> ExecuteAsync( IActivityMonitor monitor, CKliEnv context, World world )
        {
            Throw.DebugAssert( Repo != null && _root.GitBranch != null );
            using( monitor.OpenInfo( $"Fixing missing initial version in '{Repo.DisplayPath}' by creating 'v{_versionTagInfo.MinVersion}' from '{_root}'." ) )
            {
                return await _buildPlugin.CoreBuildAsync( monitor,
                                                           context,
                                                           _versionTagInfo,
                                                           _root.GitBranch.Tip,
                                                           _versionTagInfo.MinVersion,
                                                           runTest: false,
                                                           forceRebuild: true ) != null;
            }
        }
    }
}
