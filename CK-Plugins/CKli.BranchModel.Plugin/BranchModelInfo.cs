using CK.Core;
using CKli.Core;
using CKli.VersionTag.Plugin;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using LogLevel = CK.Core.LogLevel;

namespace CKli.BranchModel.Plugin;

public sealed partial class BranchModelInfo : RepoInfo
{
    readonly HotBranch _root;
    readonly BranchNamespace _model;
    readonly Dictionary<string, HotBranch> _branches;
    readonly List<HotBranch>? _removable;
    readonly List<HotBranch>? _desynchronized;
    readonly List<HotBranch>? _unrelated;
    readonly bool _hasIssues;

    // Missing "stable" case.
    internal BranchModelInfo( Repo repo, BranchNamespace model, HotBranch root )
        : base( repo )
    {
        _model = model;
        _root = root;
        _branches = new Dictionary<string, HotBranch>( 1 ) { { root.BranchName.Name, root } };
        _hasIssues = true;
    }

    internal BranchModelInfo( Repo repo,
                              BranchNamespace model,
                              HotBranch root,
                              Dictionary<string, HotBranch> branches,
                              List<HotBranch>? removable,
                              List<HotBranch>? desynchronized,
                              List<HotBranch>? unrelated )
        : base( repo )
    {
        _model = model;
        _root = root;
        _branches = branches;
        _removable = removable;
        _desynchronized = desynchronized;
        _unrelated = unrelated;
        _hasIssues = unrelated != null || desynchronized != null;
    }

    /// <summary>
    /// Gets all the <see cref="HotBranch"/> indexed by their name.
    /// Their git <see cref="HotBranch.GitBranch"/> may be null.
    /// <para>
    /// When <see cref="HasIssues"/> is true there may be only the <see cref="Root"/> in this dictionary:
    /// the "stable" root branch is missing.
    /// </para>
    /// </summary>
    public IReadOnlyDictionary<string, HotBranch> Branches => _branches;

    /// <summary>
    /// Gets the root branch.
    /// Its <see cref="HotBranch.GitBranch"/> can be null (this has to be fixed, <see cref="HasIssues"/> is true).  
    /// </summary>
    public HotBranch Root => _root;

    /// <summary>
    /// Gets whether one or more issues must be resolved before anything serious can be done with this repository.
    /// </summary>
    public bool HasIssues => _hasIssues;

    internal void CollectIssues( IActivityMonitor monitor,
                                 VersionTagInfo tags,
                                 ScreenType screenType,
                                 Action<World.Issue> collector )
    {
        // If the "stable" branch doesn't exist, no need to continue.
        if( _root.GitBranch == null )
        {
            Throw.DebugAssert( _model.Root.DevBranch != null );
            Branch? startingD = Repo.GitRepository.GetBranch( monitor, _model.Root.DevBranch.Name, LogLevel.Info );
            Branch? startingM = null;
            if( startingD == null )
            {
                startingM = Repo.GitRepository.GetBranch( monitor, "master", LogLevel.Info )
                            ?? Repo.GitRepository.GetBranch( monitor, "main", LogLevel.Info );
            }
            collector( MissingRootBranchIssue.Create( monitor, _root, tags, startingD, startingM, screenType, Repo ) );
            return;
        }
        if( _unrelated != null )
        {
            foreach( var branch in _unrelated )
            {
                Throw.DebugAssert( branch.ExistingBaseBranch != null );
                var issue = World.Issue.CreateManual( $"Unrelated branch.",
                                                      screenType.Text( $"""
                                                              Branch '{branch.BranchName}' is independent of its base '{branch.ExistingBaseBranch.BranchName}' (no common ancestor).
                                                              This is an unexpected situation that must be fixed manually.
                                                              """ ), Repo );
                collector( issue );
            }
        }

        if( _removable != null )
        {
            collector( RemovableBranchesIssue.Create( screenType, Repo, _removable ) );
        }
        if( _desynchronized != null )
        {
            var git = Repo.GitRepository.Repository;
            var bManual = new StringBuilder();
            var autos = new List<HotBranch>();
            var bAuto = new StringBuilder();
            foreach( var b in _desynchronized )
            {
                Throw.DebugAssert( b.IsDesynchronizedBranch
                                   && b.Divergence.BehindBy != null
                                   && b.ExistingBaseBranch.GitBranch != null );
                if( git.ObjectDatabase.CanMergeWithoutConflict( b.GitBranch.Tip, b.ExistingBaseBranch.GitBranch.Tip ) )
                {
                    autos.Add( b );
                    AppendDetails( bAuto, b );
                }
                else
                {
                    AppendDetails( bManual, b );
                }
            }
            if( autos.Count > 0 )
            {
                bAuto.Append( "Base branches can be merged without conflict into the desynchronized branches." );
                var body = screenType.Text( bAuto.ToString() );
                collector( new DesynchronizedBranchesIssue( body, autos, Repo ) );
            }

            if( bManual.Length > 0 )
            {
                bManual.Append( "Base branches cannot be merged without conflict into the desynchronized branches. This must be done manually." );
                var body = screenType.Text( bManual.ToString() );
                collector( World.Issue.CreateManual( "Desynchronized branches.", body, Repo ) );
            }

            static void AppendDetails( StringBuilder text, HotBranch b )
            {
                text.Append( "- Base branch '" ).Append( b.ExistingBaseBranch!.BranchName.Name ).Append( "' has " ).Append( b.Divergence!.BehindBy!.Value )
                    .Append( " commits that must be in '" ).Append( b.BranchName.Name ).Append( "'." )
                    .AppendLine();
            }
        }
    }

}
