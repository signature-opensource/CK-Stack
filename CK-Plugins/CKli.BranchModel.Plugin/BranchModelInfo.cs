using CK.Core;
using CKli.Core;
using CKli.VersionTag.Plugin;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using LogLevel = CK.Core.LogLevel;

namespace CKli.BranchModel.Plugin;

public sealed partial class BranchModelInfo : RepoInfo
{
    readonly Dictionary<string, BranchInfo> _branches;
    readonly BranchTree _model;
    [AllowNull] BranchInfo _root;

    internal BranchModelInfo( Repo repo, BranchTree model )
        : base( repo )
    {
        _model = model;
        _branches = new Dictionary<string, BranchInfo>( model.Branches.Count );
    }

    /// <summary>
    /// Gets all the <see cref="BranchInfo"/> indexed by their name.
    /// Their git <see cref="BranchInfo.Branch"/> may be null.
    /// </summary>
    public IReadOnlyDictionary<string, BranchInfo> Branches => _branches;

    /// <summary>
    /// Gets the root branch.
    /// Its <see cref="BranchInfo.Branch"/> can be null (this has to be fixed).  
    /// </summary>
    public BranchInfo Root => _root;

    internal void Initialize( IActivityMonitor monitor )
    {
        foreach( var b in _model.Branches.Values )
        {
            Initialize( monitor, b );
        }
        _root = _branches[_model.Root.Name];
    }

    BranchInfo Initialize( IActivityMonitor monitor, BranchNode expected )
    {
        var git = Repo.GitRepository.Repository;
        if( !_branches.TryGetValue( expected.Name, out var info ) )
        {
            var theoreticalBaseBranch = expected.Base != null ? Initialize( monitor, expected.Base ) : null;
            BranchInfo? existingBaseBranch = theoreticalBaseBranch;
            while( existingBaseBranch != null && existingBaseBranch.Branch == null )
            {
                existingBaseBranch = existingBaseBranch.TheoreticalBaseBranch;
            }

            var b = Repo.GitRepository.GetBranch( monitor, expected.Name, missingLocalAndRemote: LogLevel.None );

            HistoryDivergence? div = b != null && existingBaseBranch?.Branch != null
                                        ? git.ObjectDatabase.CalculateHistoryDivergence( b.Tip, existingBaseBranch.Branch.Tip )
                                        : null;
            info = new BranchInfo( expected, theoreticalBaseBranch, existingBaseBranch, b, div );
            _branches.Add( expected.Name, info );
        }
        return info;
    }

    internal void CollectIssues( IActivityMonitor monitor,
                                 VersionTagInfo tags,
                                 ScreenType screenType,
                                 Action<World.Issue> collector )
    {
        // If the "stable" branch doesn't exist, no need to continue.
        var root = _branches[_model.Root.Name];
        if( root.Branch == null )
        {
            Throw.DebugAssert( _model.Root.DevBranch != null );
            Branch? startingD = Repo.GitRepository.GetBranch( monitor, _model.Root.DevBranch.Name, LogLevel.Info );
            Branch? startingM = null;
            if( startingD == null )
            {
                startingM = Repo.GitRepository.GetBranch( monitor, "master", LogLevel.Info )
                            ?? Repo.GitRepository.GetBranch( monitor, "main", LogLevel.Info );
            }
            collector( MissingRootBranchIssue.Create( monitor, root, tags, startingD, startingM, screenType, Repo ) );
            return;
        }
        // Consider all the branches that exist.
        foreach( var branch in _branches.Values )
        {
            if( branch != root && branch.Branch != null )
            {
                Throw.DebugAssert( "Because the root exists.", branch.ExistingBaseBranch?.Branch != null );
                Throw.DebugAssert( "Because the root and the Branch exist.", branch.Divergence != null );
                var div = branch.Divergence;
                Throw.DebugAssert( div.One == branch.Branch.Tip && div.Another == branch.ExistingBaseBranch.Branch.Tip );
                if( div.CommonAncestor == null )
                {
                    var issue = World.Issue.CreateManual( $"",
                                                          screenType.Text( $"""
                                                              Branch '{branch.Branch.FriendlyName}' is independent of its base '{branch.ExistingBaseBranch.Branch.FriendlyName}' (no common ancestor).
                                                              This is an unexpected situation that must be fixed manually by making '{branch.Branch.FriendlyName}' reference the same commit as its base or deleting the branch.
                                                              """ ), Repo );
                    collector( issue );
                }
                else
                {
                    Throw.DebugAssert( div.AheadBy != null && div.BehindBy != null );
                    if( div.AheadBy.Value == 0 )
                    {
                        // Branch can be suppressed.
                    }
                    else if( div.BehindBy.Value > 0 )
                    {
                        // Branch must be rebased.

                    }
                }
            }
        }
    }


}
