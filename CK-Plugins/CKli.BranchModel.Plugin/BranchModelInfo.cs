using CK.Core;
using CKli.Core;
using CKli.VersionTag.Plugin;
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
    readonly List<HotBranch>? _removable;
    readonly List<HotBranch>? _desynchronized;
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
                              List<HotBranch>? removable,
                              List<HotBranch>? desynchronized,
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
    /// Gets all the <see cref="HotBranch"/> indexed by their <see cref="BranchName.InstabilityRank"/>.
    /// Their git <see cref="HotBranch.GitBranch"/> may be null.
    /// </summary>
    public ImmutableArray<HotBranch> Branches => _branches;

    /// <summary>
    /// Gets the "closest" active branch in this Repo for the branch.
    /// <para>
    /// <list type="bullet">
    ///     <item>
    ///     For a regular branch it is its "dev/" branch if it exists. Otherwise, it
    ///     is the closest instable branch above.
    ///     </item>
    ///     <item>
    ///     For a "dev/" branch this considers the interleaved base "dev/" branches.
    ///     </item>
    /// </list>
    /// </para>
    /// This is almost the same as using the <see cref="BranchName.Fallbacks"/> except that this ignores
    /// disconnected branches: this can return null only if <see cref="HasIssue"/> is true because the ultimate branch is the
    /// "stable" that is necessarily active when there's no branch issue.
    /// </summary>
    /// <param name="name">The branch name.</param>
    /// <returns>The active hot branch or null if the "stable" git branch doesn't exist.</returns>
    public HotBranch? GetActiveBranch( BranchName name )
    {
        if( name.IsDevBranch )
        {
            for( int i = name.InstabilityRank; i >= 0; --i )
            {
                var b = _branches[ i ];
                if( b.GitBranch != null ) 
            }
        }
        return name.Fallbacks.Select( n => _branches[n.InstabilityRank] )
                             .FirstOrDefault( b => b?.GitBranch != null );
    }

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
            Throw.DebugAssert( _model.Root.DevBranch != null );
            Branch? mainOrMaster = Repo.GitRepository.GetBranch( monitor, "master", LogLevel.Info )
                                ?? Repo.GitRepository.GetBranch( monitor, "main", LogLevel.Info );
            collector( MissingRootBranchIssue.Create( monitor, Root, mainOrMaster, screenType ) );
            return;
        }
        if( _unrelated != null )
        {
            foreach( var branch in _unrelated )
            {
                Throw.DebugAssert( branch.ActiveBase != null );
                var issue = World.Issue.CreateManual( $"Unrelated branch.",
                                                      screenType.Text( $"""
                                                              Branch '{branch.BranchName}' is independent of its base '{branch.ActiveBase.BranchName}' (no common ancestor).
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
                                   && b.ActiveBase.GitBranch != null );
                if( git.ObjectDatabase.CanMergeWithoutConflict( b.GitBranch.Tip, b.ActiveBase.GitBranch.Tip ) )
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
                text.Append( "- Base branch '" ).Append( b.ActiveBase!.BranchName.Name ).Append( "' has " ).Append( b.Divergence!.BehindBy!.Value )
                    .Append( " commits that must be in '" ).Append( b.BranchName.Name ).Append( "'." )
                    .AppendLine();
            }
        }
    }

}
