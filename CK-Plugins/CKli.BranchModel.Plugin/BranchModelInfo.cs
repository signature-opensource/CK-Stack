using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using LogLevel = CK.Core.LogLevel;

namespace CKli.BranchModel.Plugin;

public sealed partial class BranchModelInfo : RepoInfo
{
    readonly BranchNamespace _model;
    readonly ImmutableArray<HotBranch> _branches;
    readonly List<RemovableGitBranch>? _removable;
    readonly List<(Branch Branch, Branch Base, int BehindBy)>? _desynchronized;
    readonly List<HotBranch>? _unrelated;
    readonly bool _hasIssue;

    // Missing "stable" case (this is the worst case that prevents almost any operation except fixing this
    // by creating the root branch).
    internal BranchModelInfo( Repo repo, BranchNamespace model, HotBranch root )
        : base( repo )
    {
        root._info = this;
        _model = model;
        _branches = [root];
        _hasIssue = true;
    }

    internal BranchModelInfo( Repo repo,
                              BranchNamespace ns,
                              ImmutableArray<HotBranch> branches,
                              List<RemovableGitBranch>? removable,
                              List<(Branch Branch, Branch Base, int BehindBy)>? desynchronized,
                              List<HotBranch>? unrelated )
        : base( repo )
    {
        _model = ns;
        _branches = branches;
        _removable = removable;
        _desynchronized = desynchronized;
        _unrelated = unrelated;
        _hasIssue = unrelated != null || desynchronized != null || removable != null;
    }

    /// <summary>
    /// Gets all the <see cref="HotBranch"/> indexed by their <see cref="BranchName.Index"/>.
    /// Their git <see cref="HotBranch.GitBranch"/> may be null.
    /// </summary>
    public ImmutableArray<HotBranch> Branches => _branches;

    /// <summary>
    /// Gets the root branch.
    /// Its <see cref="HotBranch.GitBranch"/> can be null (this has to be fixed, <see cref="HasIssue"/> is true).  
    /// </summary>
    public HotBranch Root => _branches[0];

    /// <inheritdoc />
    public override bool HasIssue => _hasIssue;

    internal void CollectIssues( IActivityMonitor monitor,
                                 ScreenType screenType,
                                 Action<World.Issue> collector )
    {
        // If the "stable" branch doesn't exist, no need to continue.
        if( Root.GitBranch == null )
        {
            // Use "dev/stable" if it exists.
            Branch? mainOrMaster = Root.GitDevBranch
                                    ?? Repo.GitRepository.GetBranch( monitor, "master", LogLevel.Info )
                                    ?? Repo.GitRepository.GetBranch( monitor, "main", LogLevel.Info );
            collector( MissingRootBranchIssue.Create( monitor, Root, mainOrMaster, screenType ) );
            return;
        }
        if( _unrelated != null )
        {
            foreach( var branch in _unrelated )
            {
                Throw.DebugAssert( branch.GitBranch != null && branch.GitDevBranch != null );
                var issue = World.Issue.CreateManual( $"Unrelated branch.",
                                                      screenType.Text( $"""
                                                              Branch '{branch.BranchName.DevName}' is independent of its base '{branch.BranchName.Name}' (no common ancestor).
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
            var autos = new List<(Branch Branch, Branch Base)>();
            var bAuto = new StringBuilder();
            foreach( var b in _desynchronized )
            {
                if( git.ObjectDatabase.CanMergeWithoutConflict( b.Base.Tip, b.Branch.Tip ) )
                {
                    autos.Add( (b.Branch, b.Base) );
                    AppendDetails( bAuto, in b );
                }
                else
                {
                    AppendDetails( bManual, in b );
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

            static void AppendDetails( StringBuilder text, in (Branch Branch, Branch Base, int BehindBy) b )
            {
                text.Append( "- Branch '" ).Append( b.Base.FriendlyName ).Append( "' has " ).Append( b.BehindBy )
                    .Append( " commits that must be in '" ).Append( b.Branch.FriendlyName ).Append( "'." )
                    .AppendLine();
            }
        }
    }

}
