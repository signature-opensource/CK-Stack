using CSemVer;
using CKli.Core;
using CK.Core;

namespace CKli.BranchModel.Plugin;

public sealed partial class FixWorkflow
{
    /// <summary>
    /// Repository build target. 
    /// </summary>
    public sealed class TargetRepo
    {
        readonly Repo _repo;
        readonly string _branchName;
        readonly string _toFixCommitSha;
        readonly SVersion _targetVersion;
        readonly int _rank;
        SVersion? _toFixVersion;
        int _index;

        /// <summary>
        /// Gets the index of this target in the <see cref="Targets"/>.
        /// </summary>
        public int Index => _index;

        /// <summary>
        /// Gets the repository to publish.
        /// </summary>
        public Repo Repo => _repo;

        /// <summary>
        /// Gets the fix branch name.
        /// </summary>
        public string BranchName => _branchName;

        /// <summary>
        /// Gets the commit Sha to be fixed.
        /// </summary>
        public string ToFixCommitSha => _toFixCommitSha;

        /// <summary>
        /// Gets the version to be fixed.
        /// </summary>
        public SVersion ToFixVersion => _toFixVersion ??= SVersion.Create( _targetVersion.Major, _targetVersion.Minor, _targetVersion.Patch - 1 );

        /// <summary>
        /// Gets the target version to produce.
        /// <see cref="SVersion.IsStable"/> is true, the <see cref="SVersion.Patch"/> is positive.
        /// </summary>
        public SVersion TargetVersion => _targetVersion;

        /// <summary>
        /// Gets the rank of this target among the <see cref="Targets"/>.
        /// The targets that share the same rank can be built in parallel.
        /// </summary>
        public int Rank => _rank;

        /// <summary>
        /// Overridden to return the <see cref="Repo"/> display path and <see cref="BranchName"/>.
        /// </summary>
        /// <returns>A readable string.</returns>
        public override string ToString() => $"{_repo.DisplayPath} ({_branchName})";

        internal TargetRepo( Repo repo, string branchName, string toFixCommitSha, SVersion targetVersion, int rank )
        {
            _repo = repo;
            _branchName = branchName;
            _toFixCommitSha = toFixCommitSha;
            _targetVersion = targetVersion;
            _rank = rank;
        }

        internal void Write( CKBinaryWriter w )
        {
            w.Write( _repo.CKliRepoId.Value );
            w.Write( _branchName );
            w.Write( _toFixCommitSha );
            w.Write( _targetVersion.ToString() );
            w.WriteNonNegativeSmallInt32( _rank );
        }

        internal static TargetRepo? Read( IActivityMonitor monitor, int index, CKBinaryReader r, World world, int version )
        {
            Throw.DebugAssert( version == 0 );
            var id = new RandomId( r.ReadUInt64() );
            var branchName = r.ReadString();
            var toFixCommitSha = r.ReadString();
            var targetVersion = r.ReadString();
            var rank = r.ReadNonNegativeSmallInt32();

            var repo = world.FindByCKliRepoId( monitor, id );
            if( repo == null )
            {
                monitor.Error( $"Unable to find RepoId = {id} in current world. Current Fix Workflow is invalid and will be deleted." );
                return null;
            }
            var t = new TargetRepo( repo, branchName, toFixCommitSha, SVersion.Parse( targetVersion ), rank );
            t._index = index;
            return t;
        }

        internal void SetIndex( int i ) => _index = i;
    }
}

