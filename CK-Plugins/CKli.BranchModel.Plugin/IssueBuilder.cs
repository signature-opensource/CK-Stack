using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Text;

namespace CKli.BranchModel.Plugin;

sealed partial class IssueBuilder
{
    List<(Branch Branch, object BaseOrName)>? _removables;
    List<(Branch Ahead, Branch Base, int BehindBy)>? _desynchronized;
    List<(Branch Ahead, Branch Base)>? _unrelated;

    public void OnMissingBaseBranch( Branch branch, string baseBranchName )
    {
        _removables ??= [];
        _removables.Add( (branch, baseBranchName) );
    }

    public void OnUselessBranch( Branch branch, Branch baseBranch )
    {
        _removables ??= [];
        _removables.Add( (branch, baseBranch) );
    }

    public void OnDesynchronized( Branch ahead, Branch branch, int behindBy )
    {
        _desynchronized ??= [];
        _desynchronized.Add( (ahead, branch, behindBy) );
    }

    public void OnUnrelated( Branch ahead, Branch branch )
    {
        _unrelated ??= [];
        _unrelated.Add( (ahead, branch) );
    }

    internal void CollectIssues( IActivityMonitor monitor,
                                 Repo repo,
                                 ScreenType screenType,
                                 Action<World.Issue> collector )
    {
        if( _unrelated != null )
        {
            foreach( var (a,b) in _unrelated )
            {
                var issue = World.Issue.CreateManual( $"Unrelated branch.",
                                                      screenType.Text( $"""
                                                              Branch '{a.FriendlyName}' is independent of its base '{b.FriendlyName}' (no common ancestor).
                                                              This is an unexpected situation that must be fixed manually.
                                                              """ ), repo );
                collector( issue );
            }
        }
        if( _removables != null )
        {
            collector( RemovableBranchesIssue.Create( screenType, repo, _removables ) );
        }
        if( _desynchronized != null )
        {
            var git = repo.GitRepository.Repository;
            var bManual = new StringBuilder();
            var autos = new List<(Branch Branch, Branch Base)>();
            var bAuto = new StringBuilder();
            foreach( var b in _desynchronized )
            {
                if( git.ObjectDatabase.CanMergeWithoutConflict( b.Base.Tip, b.Ahead.Tip ) )
                {
                    autos.Add( (b.Ahead, b.Base) );
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
                collector( new DesynchronizedBranchesIssue( body, autos, repo ) );
            }

            if( bManual.Length > 0 )
            {
                bManual.Append( "Base branches cannot be merged without conflict into the desynchronized branches. This must be done manually." );
                var body = screenType.Text( bManual.ToString() );
                collector( World.Issue.CreateManual( "Desynchronized branches.", body, repo ) );
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
