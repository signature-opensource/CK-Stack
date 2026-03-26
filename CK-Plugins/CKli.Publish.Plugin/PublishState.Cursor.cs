using CK.Core;
using CKli.ArtifactHandler.Plugin;
using System.Linq;

namespace CKli.Publish.Plugin;

sealed partial class PublishState
{
    /// <summary>
    /// Immutable logical cursor that identifies a position in a <see cref="PublishState"/> and can
    /// be forwarded.
    /// </summary>
    public sealed class Cursor
    {
        readonly PublishState _state;
        readonly WorldPublishInfo? _world;
        readonly RepoPublishInfo? _repo;
        readonly int _index;
        readonly LocType _location;

        /// <summary>
        /// Describes a current position in a <see cref="PublishState"/>.
        /// </summary>
        public enum LocType
        {
            /// <summary>
            /// End of state has been reached. There's nothing more to do, both <see cref="World"/> and <see cref="Repo"/> are null.
            /// </summary>
            EndOfState,

            /// <summary>
            /// All the <see cref="World"/>'s Repos have been published (<see cref="Repo"/> is null).
            /// </summary>
            EndOfWorld,

            /// <summary>
            /// All the <see cref="Repo"/>'s packages and files have been published.
            /// </summary>
            EndOfRepo,

            /// <summary>
            /// The <see cref="Repo"/>'s package <see cref="Index"/> in <see cref="BuildContentInfo.Produced"/> must be published.
            /// </summary>
            InPackage,

            /// <summary>
            /// The <see cref="Repo"/>'s file <see cref="Index"/> in <see cref="BuildContentInfo.AssetFileNames"/> must be published.
            /// </summary>
            InFile
        }

        /// <summary>
        /// Gets the state.
        /// </summary>
        public PublishState State => _state;

        /// <summary>
        /// Gets the current location.
        /// </summary>
        public LocType Location => _location;

        /// <summary>
        /// Gets the current world.
        /// Null only when <see cref="Location"/> is <see cref="LocType.EndOfState"/>.
        /// </summary>
        public WorldPublishInfo? World => _world;

        /// <summary>
        /// Gets the current repository.
        /// Null when <see cref="Location"/> is <see cref="LocType.EndOfWorld"/> or <see cref="LocType.EndOfState"/>.
        /// </summary>
        public RepoPublishInfo? Repo => _repo;

        /// <summary>
        /// Gets the index in the <see cref="RepoPublishInfo.BuildContentInfo"/>'s <see cref="BuildContentInfo.Produced"/> packages or <see cref="BuildContentInfo.AssetFileNames"/>.
        /// When <see cref="Location"/> is not <see cref="LocType.InFile"/> or <see cref="LocType.InPackage"/>, this is -1.
        /// </summary>
        public int Index => _index;

        internal Cursor( PublishState state )
            : this( state, LocType.EndOfState, null, null, -1 )
        {
        }

        Cursor( PublishState state, LocType location, WorldPublishInfo? world, RepoPublishInfo? repo, int index )
        {
            _state = state;
            _world = world;
            _repo = repo;
            _index = index;
            _location = location;
        }

        internal static Cursor Create( PublishState state, int offset )
        {
            var world = state.Publications.FirstOrDefault();
            var c = world != null
                        ? EnterWorld( state, world )
                        : new Cursor( state );
            while( --offset >= 0 && c.Location != LocType.EndOfState )
            {
                c = c.Forward();
            }
            return c;
        }

        static Cursor EnterWorld( PublishState state, WorldPublishInfo world )
        {
            // If there's a world, then there's a Repo: so if there's no packages
            // and no files, we end up directly to EndOfRepo.
            var repo = world.Repos[0];
            return EnterRepo( state, world, repo );
        }

        static Cursor EnterRepo( PublishState state, WorldPublishInfo world, RepoPublishInfo repo )
        {
            var loc = LocType.EndOfRepo;
            int index = -1;
            if( repo.BuildContentInfo.Produced.Length > 0 )
            {
                loc = LocType.InPackage;
                index = 0;
            }
            else if( repo.BuildContentInfo.AssetFileNames.Length > 0 )
            {
                loc = LocType.InFile;
                index = 0;
            }
            return new Cursor( state, loc, world, repo, index );
        }

        /// <summary>
        /// Returns a cursor that is on the next position in the <see cref="State"/>.
        /// </summary>
        /// <returns>The cursor on the next position.</returns>
        public Cursor Forward()
        {
            if( _location == LocType.EndOfState )
            {
                return this;
            }
            Throw.DebugAssert( _world != null );
            if( _location == LocType.EndOfWorld )
            {
                if( _state._publications[^1] == _world )
                {
                    return new Cursor( _state );
                }
                var world = _state._publications[ _state._publications.IndexOf( _world ) + 1 ];
                return EnterWorld( _state, world );
            }
            Throw.DebugAssert( _repo != null );
            int nextIdx;
            if( _location == LocType.EndOfRepo )
            {
                nextIdx = _repo.Index + 1;
                return nextIdx == _world.Repos.Length
                    ? new Cursor( _state, LocType.EndOfWorld, _world, null, -1 )
                    : EnterRepo( _state, _world, _world.Repos[nextIdx] );
            }
            if( _location == LocType.InPackage )
            {
                nextIdx = _index + 1;
                if( nextIdx < _repo.BuildContentInfo.Produced.Length )
                {
                    return new Cursor( _state, LocType.InPackage, _world, _repo, nextIdx );
                }
                if( _repo.BuildContentInfo.AssetFileNames.Length > 0 )
                {
                    return new Cursor( _state, LocType.InFile, _world, _repo, 0 );
                }
                return new Cursor( _state, LocType.EndOfRepo, _world, _repo, -1 );
            }
            Throw.DebugAssert( _location == LocType.InFile );
            {
                nextIdx = _index + 1;
                return nextIdx < _repo.BuildContentInfo.AssetFileNames.Length
                    ? new Cursor( _state, LocType.InFile, _world, _repo, nextIdx )
                    : new Cursor( _state, LocType.EndOfRepo, _world, _repo, -1 );
            }
        }
    }
}

