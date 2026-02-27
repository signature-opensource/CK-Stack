using CK.Core;
using LibGit2Sharp;
using System.Collections.Generic;

namespace CKli.BranchModel.Plugin;

sealed class BranchLinkIssueBuilder
{
    List<(Branch Branch, object BaseOrName)>? _removables;
    List<(Branch Ahead, Branch Base, int BehindBy)>? _desynchronized;
    List<(Branch Ahead, Branch Base)>? _unrelated;

    public void AddMissingBase( Branch branch, string baseBranchName )
    {
        _removables ??= [];
        _removables.Add( (branch, baseBranchName) );
    }

    public void AddUselessBranch( Branch b, Branch baseBranch )
    {
        _removables ??= [];
        _removables.Add( (b, baseBranch) );
    }

    public void AddDesynchronized( Branch ahead, Branch b, int behindBy )
    {
        _desynchronized ??= [];
        _desynchronized.Add( (ahead, b, behindBy) );
    }

    public void AddUnrelated( Branch ahead, Branch b )
    {
        _unrelated ??= [];
        _unrelated.Add( (ahead, b) );
    }


}
