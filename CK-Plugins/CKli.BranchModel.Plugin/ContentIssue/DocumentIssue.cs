using CK.Core;
using CKli.Core;
using System.Threading.Tasks;

namespace CKli.BranchModel.Plugin;

public abstract class DocumentIssue
{
    readonly NormalizedPath _path;
    readonly HotBranch _branch;

    protected DocumentIssue( NormalizedPath path, HotBranch branch )
    {
        _path = path;
        _branch = branch;
    }

    public NormalizedPath Path => _path;

    public HotBranch Branch => _branch;

    protected abstract ValueTask<bool> ExecuteAsync( IActivityMonitor monitor, CKliEnv context, World world );
}

