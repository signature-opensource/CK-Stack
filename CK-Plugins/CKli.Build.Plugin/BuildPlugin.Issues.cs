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
            When the commit cannot be successfully recompiled, the command 'ckli maintenance rebuild old'
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
        // Ultimate case: the regular tags with a content that appear in the Local database but miss artifacts.
        // => If it appears in the Published database, then we can remove it from the Local database (this may be done
        //    in the ReleaseDatabasePlugin.OnExistingVersionTags... whether it misses artifacts or not...).
        //    For the moment, if it appears in the Published database, we ignore it.
        // => Otherwise, we must rebuild it.
        var missingArtifacts = regulars.Where( tc => tc.BuildContentInfo != null
                                                     && _releaseDatabase.GetBuildContentInfo( monitor, versionTagInfo.Repo, tc.Version ) != null
                                                     && _releaseDatabase.GetBuildContentInfo( monitor, versionTagInfo.Repo, tc.Version, fromPublished: true ) == null
                                                     && !_artifactHandler.HasAllArtifacts( monitor, versionTagInfo.Repo, tc.Version, tc.BuildContentInfo, out _ ) ).ToArray();
        if( missingArtifacts.Length > 0 )
        {
            monitor.Info( $"""
                The {missingArtifacts.Length} following tags in '{versionTagInfo.Repo.DisplayPath}' must be rebuilt (expected artifacts are missing):
                {missingArtifacts.Select( tc => $"- {tc.Version}:{Environment.NewLine}{tc.TagMessage}{Environment.NewLine}" ).Concatenate( Environment.NewLine )}
                """ );
            collector( new TagsRebuildIssue( this,
                                             versionTagInfo,
                                             $"{missingArtifacts.Length} tags have missing artifacts (see logs for details).",
                                             screenType.Text( $"""
                                                {missingArtifacts.Select( t => t.Version.ToString() ).Concatenate()}

                                                These commits must be rebuilt to produce the packages and asset files.
                                                """ ),
                                             missingArtifacts ) );
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
            if( _root.HasIssue )
            {
                monitor.Warn( $"Issue on '{_root.BranchName.Name}' branch in '{Repo.DisplayPath}' must be fixed before building a initial 'v{_versionTagInfo.MinVersion}' version." );
                return true;
            }
            else using( monitor.OpenInfo( $"Fixing missing initial version in '{Repo.DisplayPath}' by creating 'v{_versionTagInfo.MinVersion}' from '{_root}'." ) )
            {
                // If there is a "dev/stable" branch: integrates it.
                if( _root.GitDevBranch != null && !_root.IntegrateDevBranch( monitor ) )
                {
                    return false;
                }
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
