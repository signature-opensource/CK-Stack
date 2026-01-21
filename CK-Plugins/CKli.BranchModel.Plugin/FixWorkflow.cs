using CSemVer;
using CKli.Core;
using System.Collections.Immutable;
using CK.Core;
using System.IO;
using System;
using System.Linq;
using LibGit2Sharp;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// A FixWorkflow is initiated by "ckli fix start" command.
/// It contains the ordered list of <see cref="Repo"/> and versions to build and the
/// state of the last build.
/// <para>
/// This workflow works on a stable dependency graph: at the end of each build, the builder checks
/// that:
/// <list type="bullet">
///     <item>The produced package identifiers have not changed.</item>
///     <item>
///     For consumed packages, only packages external to the World (at the time of the initial release)
///     and packages produced by the fix workflow itself can have a new version.
///     </item>
/// </list>
/// <para>
/// the "fix/vMajor.Minor" branches are automatically created in downstream repositories if they don't exist yet.
/// </para>
/// </summary>
public sealed class FixWorkflow
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
        /// Gets the commit Sha to be fixed.
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
                monitor.Error( $"Unable to find RepoId = {id} in current world. Current Fix Workflow is invalid and will be cancelled." );
                return null;
            }
            var t = new TargetRepo( repo, branchName, toFixCommitSha, SVersion.Parse( targetVersion ), rank );
            t._index = index;
            return t;
        }

        internal void SetIndex( int i ) => _index = i;
    }

    readonly World _world;
    readonly ImmutableArray<TargetRepo> _targets;

    internal FixWorkflow( World world, ImmutableArray<TargetRepo> targets )
    {
        Throw.DebugAssert( targets.Length >= 1 );
        _world = world;
        _targets = targets;
    }

    /// <summary>
    /// Gets the origin repository for which the fix must be produced.
    /// </summary>
    public TargetRepo OriginRepo => _targets[0];

    /// <summary>
    /// Gets the targets including the first <see cref="OriginRepo"/>.
    /// </summary>
    public ImmutableArray<TargetRepo> Targets => _targets;

    /// <summary>
    /// Gets the world to which this workflow belongs.
    /// </summary>
    public World World => _world;

    /// <summary>
    /// Returns a renderable of this workflow.
    /// </summary>
    /// <param name="s">The screen type.</param>
    /// <returns>The renderable.</returns>
    public IRenderable ToRenderable( ScreenType s )
    {
        var o = OriginRepo;
        var header = s.Text( $"Fixing 'v{o.TargetVersion.Major}.{o.TargetVersion.Minor}.{o.TargetVersion.Patch - 1}' on" ).Box( marginRight: 1 )
                      .AddRight( s.Text( o.Repo.DisplayPath ).HyperLink( new Uri( o.Repo.WorkingFolder ) ) )
                      .AddRight( s.Text( ":" ) )
                      .Box();

        var arrow = s.Text( "->" ).Box( marginRight: 1 );
        var rows = s.Unit.AddBelow( _targets.Select( t =>
                     s.Text( $"{t.Index} -" ).Box( marginRight:1, align:ContentAlign.HRight)
                     .AddRight( s.Text( t.Repo.DisplayPath ).HyperLink( new Uri( t.Repo.WorkingFolder ) ).Box( marginRight: 1 ) )
                     .AddRight( arrow )
                     .AddRight( s.Text( t.TargetVersion.ToString() ).Box( foreColor: ConsoleColor.Green, marginRight: 1 ) )
                     .AddRight( s.Text( $"({t.BranchName})" ).Box( marginRight: 1, foreColor: ConsoleColor.DarkBlue ) ) ) );

        return header.AddBelow( rows.TableLayout() );
    }

    /// <summary>
    /// Loads the current world's fix workflow if it exists.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="world">The world.</param>
    /// <param name="workflow">The loaded workflow. Null if it doesn't exist or on error.</param>
    /// <returns>True on success, false on error.</returns>
    public static bool Load( IActivityMonitor monitor, World world, out FixWorkflow? workflow )
    {
        NormalizedPath path = GetFilePath( world );
        workflow = null;
        if( File.Exists( path ) )
        {
            try
            {
                using( var s = new FileStream( path, FileMode.Open, FileAccess.Read, FileShare.None ) )
                using( var r = new CKBinaryReader( s ) )
                {
                    workflow = DoRead( monitor, r, world );
                }
                if( workflow == null )
                {
                    CancelCurrent( monitor, world, path );
                    return false;
                }
            }
            catch( Exception ex )
            {
                monitor.Error( $"While loading '{path}'.", ex );
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Saves this workflow state.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <returns>True on success, false on error.</returns>
    public bool Save( IActivityMonitor monitor )
    {
        NormalizedPath path = GetFilePath( _world );
        try
        {
            using( var s = new FileStream( path, FileMode.Create, FileAccess.Write, FileShare.None ) )
            using( var w = new CKBinaryWriter( s ) )
            {
                w.Write( (byte)0 ); // Version.
                w.Write( _targets.Length );
                foreach( var t in _targets )
                {
                    t.Write( w );
                }
            }
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( $"While saving '{path}'.", ex );
            return false;
        }
    }

    /// <summary>
    /// Cancels the current fix workflow for the world if it exists.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="world">The world.</param>
    public static void CancelCurrent( IActivityMonitor monitor, World world )
    {
        CancelCurrent( monitor, world, GetFilePath( world ) );
    }

    /// <summary>
    /// Overridden to return the <see cref="OriginRepo"/> path and <see cref="TargetRepo.BranchName"/>.
    /// </summary>
    /// <returns>A readable string.</returns>
    public override string ToString() => $"{OriginRepo.Repo.DisplayPath}/{OriginRepo.BranchName}";


    static FixWorkflow? DoRead( IActivityMonitor monitor, CKBinaryReader r, World world )
    {
        int version = r.ReadByte();
        int count = r.ReadInt32();
        var targets = ImmutableArray.CreateBuilder<TargetRepo>( count );
        for( int i = 0; i < count; ++i )
        {
            TargetRepo? t = TargetRepo.Read( monitor, i, r, world, version );
            if( t == null )
            {
                return null;
            }
            targets.Add( t );
        }

        return new FixWorkflow( world, targets.MoveToImmutable() );
    }

    static void CancelCurrent( IActivityMonitor monitor, World world, NormalizedPath fileWorkflowPath )
    {
        if( File.Exists( fileWorkflowPath ) )
        {
            monitor.Info( ScreenType.CKliScreenTag, $"Cancelling current Fix Workflow for world '{world.Name}'." );
            FileHelper.DeleteFile( monitor, fileWorkflowPath );
        }
    }

    static NormalizedPath GetFilePath( World world )
    {
        return world.StackRepository.StackWorkingFolder.Combine( $"$Local/{world.Name.FullName}.FixWorkflow.bin" );
    }
}

