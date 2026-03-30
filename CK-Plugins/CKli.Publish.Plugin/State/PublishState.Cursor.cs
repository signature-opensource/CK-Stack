using CK.Core;
using CKli.ArtifactHandler.Plugin;
using System.Linq;
using System.Threading;

namespace CKli.Publish.Plugin;

sealed partial class PublishState
{
    /// <summary>
    /// Immutable logical cursor that identifies a position in a <see cref="PublishState"/> and can
    /// be forwarded. This drives the publishing process.
    /// </summary>
    public sealed class Cursor
    {
        readonly PublishState _state;
        readonly WorldReleaseInfo? _world;
        readonly RepoPublishInfo? _repo;
        readonly int _index;
        readonly LocType _location;

        /// <summary>
        /// Describes a current position in a <see cref="PublishState"/>.
        /// </summary>
        public enum LocType
        {
            /// <summary>
            /// The <see cref="Repo"/> must be published.
            /// </summary>
            BegOfRepo,

            /// <summary>
            /// The <see cref="Repo"/>'s package <see cref="Index"/> in <see cref="BuildContentInfo.Produced"/> must be published.
            /// </summary>
            InPackage,

            /// <summary>
            /// The <see cref="Repo"/>'s file <see cref="Index"/> in <see cref="BuildContentInfo.AssetFileNames"/> must be published.
            /// </summary>
            InFile,

            /// <summary>
            /// All the <see cref="Repo"/>'s packages and files have been published.
            /// </summary>
            EndOfRepo,

            /// <summary>
            /// All the <see cref="World"/>'s Repos have been published (<see cref="Repo"/> is null).
            /// </summary>
            EndOfWorld,

            /// <summary>
            /// End of state has been reached. There's nothing more to do, both <see cref="World"/> and <see cref="Repo"/> are null.
            /// </summary>
            EndOfState

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
        /// Gets whether this cursor is tah the end of the <see cref="State"/>.
        /// </summary>
        public bool IsEndOfState => _location == LocType.EndOfState;

        /// <summary>
        /// Gets the current world.
        /// Null only when <see cref="Location"/> is <see cref="LocType.EndOfState"/>.
        /// </summary>
        public WorldReleaseInfo? World => _world;

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

        /// <summary>
        /// Returns a cursor that is on the next position in the <see cref="State"/>.
        /// <para>
        /// If the current <see cref="World"/> doesn't appear anymore in the <see cref="PublishState.Releases"/>
        /// the returned cursor is on <see cref="LocType.EndOfState"/>.
        /// </para>
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
                if( _state._releases[^1] == _world )
                {
                    return new Cursor( _state );
                }
                int wIdx = _state._releases.IndexOf( _world );
                if( wIdx < 0 )
                {
                    return new Cursor( _state );
                }
                var world = _state._releases[wIdx + 1];
                return EnterWorld( _state, world );
            }
            Throw.DebugAssert( _repo != null );
            if( _location == LocType.BegOfRepo )
            {
                return EnterRepoBody( _state, _world, _repo );
            }
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
            nextIdx = _index + 1;
            return nextIdx < _repo.BuildContentInfo.AssetFileNames.Length
                ? new Cursor( _state, LocType.InFile, _world, _repo, nextIdx )
                : new Cursor( _state, LocType.EndOfRepo, _world, _repo, -1 );
        }

        /// <summary>
        /// Returns a cursor with a forwarded position in the <see cref="State"/>.
        /// </summary>
        /// <param name="offset">The offset to the current position. Must not be negative.</param>
        /// <returns>The cursor on the next position.</returns>
        public Cursor Forward( int offset )
        {
            Throw.CheckArgument( offset >= 0 );
            var c = this;
            while( --offset >= 0 && c.Location != LocType.EndOfState )
            {
                c = c.Forward();
            }
            return c;
        }

        /// <summary>
        /// Gets the position of this cursor in the <see cref="State"/>.
        /// <para>
        /// This returns -1 when <see cref="Location"/> is <see cref="LocType.EndOfState"/> or if the current <see cref="World"/>
        /// doesn't appear anymore in the <see cref="PublishState.Releases"/>.
        /// </para>
        /// </summary>
        /// <returns>This cursor position in the <see cref="State"/>. -1 when this cursor is no more in the state.</returns>
        public int GetPosition()
        {
            if( _location == LocType.EndOfState )
            {
                return -1;
            }
            Throw.DebugAssert( _world != null );
            int len = 0;
            bool foundWorld = false;
            foreach( var w in _state._releases )
            {
                if( w == _world )
                {
                    foundWorld = true;
                    break;
                }
                len += w.PublishedLength;
            }
            if( !foundWorld )
            {
                return -1;
            }
            if( _location == LocType.EndOfWorld )
            {
                return len + _world.PublishedLength;
            }
            Throw.DebugAssert( _repo != null );
            foreach( var r in _world.Repos )
            {
                if( r == _repo ) break;
                len += r.PublishedLength;
            }
            if( _location == LocType.EndOfRepo )
            {
                return len + _repo.PublishedLength;
            }
            ++len;
            if( _location == LocType.BegOfRepo )
            {
                return len;
            }
            if( _location == LocType.InPackage )
            {
                return len + _index;
            }
            Throw.DebugAssert( _location == LocType.InFile );
            return len + _repo.BuildContentInfo.Produced.Length + _index;
        }

        internal Cursor( PublishState state )
            : this( state, LocType.EndOfState, null, null, -1 )
        {
        }

        Cursor( PublishState state, LocType location, WorldReleaseInfo? world, RepoPublishInfo? repo, int index )
        {
            _state = state;
            _world = world;
            _repo = repo;
            _index = index;
            _location = location;
        }

        internal static Cursor Create( PublishState state )
        {
            var world = state.Releases.FirstOrDefault();
            return world != null
                        ? EnterWorld( state, world )
                        : new Cursor( state );
        }

        static Cursor EnterWorld( PublishState state, WorldReleaseInfo world )
        {
            // If there's a world, then there's a Repo: so if there's no packages
            // and no files, we end up directly to EndOfRepo.
            var repo = world.Repos[0];
            return EnterRepo( state, world, repo );
        }

        static Cursor EnterRepo( PublishState state, WorldReleaseInfo world, RepoPublishInfo repo )
        {
            // We come from EnterWorld() or _location == LocType.EndOfRepo => BegOfRepo, even
            // if the Repo is empty.
            return new Cursor( state, LocType.BegOfRepo, world, repo, -1 );
        }

        static Cursor EnterRepoBody( PublishState state, WorldReleaseInfo world, RepoPublishInfo repo )
        {
            // We come from LocType.BegOfRepo:
            // - Transitions to InPackage (if there's at least one package).
            // - InFile otherwise (if there's at least one file)
            // - or transitions directly to EndOfRepo.
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

    }
}

