using CK.Core;

namespace CKli.BranchModel.Plugin;

sealed class BranchLinkIssueBuilder
{

    public void Add( BranchLink link )
    {
        Throw.DebugAssert( link.Issue != BranchLink.IssueKind.None );
        switch( link.Issue )
        {
            case BranchLink.IssueKind.Useless: 
        }
    }
}
