using CK.Core;
using CKli.Core;
using System;
using System.Collections.Generic;
using System.IO;

namespace CKli.Publish.Plugin;

/// <summary>
/// Models a list of world releases to be published.
/// <para>
/// This acts as a FIFO (a queue): by forwarding the <see cref="PrimaryCursor"/>, the <see cref="Releases"/> are removed.
/// </para>
/// </summary>
sealed partial class PublishState
{
    readonly List<WorldReleaseInfo> _releases;
    readonly World _world;
    readonly NormalizedPath _path;
    Cursor _primaryCursor;

    /// <summary>
    /// Gets the world.
    /// </summary>
    public World World => _world;

    /// <summary>
    /// Gets the list of world releases that wait to be published.
    /// </summary>
    public IReadOnlyList<WorldReleaseInfo> Releases => _releases;

    /// <summary>
    /// Gets the cursor associated to this state.
    /// </summary>
    public Cursor PrimaryCursor => _primaryCursor;

    /// <summary>
    /// Updates the <see cref="PrimaryCursor"/> by forwarding it and removes from <see cref="Releases"/>
    /// the world releases that are before the new cursor.
    /// <para>
    /// This immediately persists the change: if this fails, null is returned.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="offset">The number of positions. Must be positive.</param>
    /// <param name="cleanupReleases">False to keep world releases even if the resulting cursor is after them.</param>
    /// <returns>The updated <see cref="PrimaryCursor"/> on success, false if persisting the state failed.</returns>
    public Cursor? ForwardPrimaryCursor( IActivityMonitor monitor, int offset, bool cleanupReleases = true )
    {
        Throw.CheckArgument( offset > 0 );
        bool fullUpdate = false;
        var c = _primaryCursor.Forward( offset );
        // Quick: consider only a change of the World.
        if( cleanupReleases && c.World != _primaryCursor.World )
        {
            Throw.DebugAssert( c.World is null == c.Location is Cursor.LocType.EndOfState );
            if( c.World != null )
            {
                while( _releases.Count > 0 )
                {
                    if( _releases[0] != c.World )
                    {
                        _releases.RemoveAt( 0 );
                        fullUpdate = true;
                    }
                }
            }
            else
            {
                // World is null <=> EndOfState.
                _releases.Clear();
            }
        }
        return Persist( monitor, fullUpdate, c )
                ? _primaryCursor = c
                : null;
    }

    /// <summary>
    /// Adds a new world release and persists the new state.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="newOne">The new world release.</param>
    /// <returns>True on success, false if persisting the state failed.</returns>
    public bool Add( IActivityMonitor monitor, WorldReleaseInfo newOne )
    {
        Throw.CheckArgument( _releases.Count == 0 || newOne.BuildDate > _releases[^1].BuildDate );
        _releases.Add( newOne );
        if( _primaryCursor.Location == Cursor.LocType.EndOfState )
        {
            _primaryCursor = CreateCursor();
        }
        return Persist( monitor, fullWrite: true, _primaryCursor );
    }

    /// <summary>
    /// Creates a new <see cref="Cursor"/> positioned at the start of this state.
    /// </summary>
    /// <returns>The cursor to use to traverse this state.</returns>
    public Cursor CreateCursor( int position = 0 ) => Cursor.Create( this );

    PublishState( World world, NormalizedPath path )
    {
        _world = world;
        _path = path;
        _releases = new List<WorldReleaseInfo>();
        _primaryCursor = new Cursor( this );
    }

    PublishState( World world, NormalizedPath path, List<WorldReleaseInfo> releases, int primaryPosition )
    {
        _world = world;
        _path = path;
        _releases = releases;
        _primaryCursor = new Cursor( this ).Forward( primaryPosition );
    }

    public static PublishState? Load( IActivityMonitor monitor, World world )
    {
        var path = world.Name.LocalDataFolder.AppendPart( "PublishState.bin" );
        if( File.Exists( path ) )
        {
            try
            {
                using( var stream = File.OpenRead( path ) )
                using( var r = new CKBinaryReader( stream ) )
                {
                    var releases = new List<WorldReleaseInfo>();
                    int version = r.ReadByte();
                    int position = r.ReadInt32();
                    int count = r.ReadNonNegativeSmallInt32();
                    while( --count >= 0 )
                    {
                        var p = WorldReleaseInfo.Read( monitor, world, r, version );
                        if( p == null ) return null;
                        releases.Add( p );
                    }
                    return new PublishState( world, path, releases, position );
                }
            }
            catch( Exception ex )
            {
                monitor.Error( $"While loading '{path}'.", ex );
                return null;
            }
        }
        return new PublishState( world, path );
    }

    /// <summary>
    /// </summary>
    /// <param name="monitor"></param>
    /// <param name="fullWrite"></param>
    /// <param name="primaryCursor"></param>
    /// <returns></returns>
    internal bool Persist( IActivityMonitor monitor, bool fullWrite, Cursor primaryCursor )
    {
        return true;
        //if( _releases.Count == 0 )
        //{
        //    return FileHelper.DeleteFile( monitor, _path );
        //}
        //try
        //{
        //    int primaryPosition = primaryCursor.GetPosition();
        //    if( fullWrite || !File.Exists( _path ) )
        //    {
        //        using( var stream = File.OpenWrite( _path ) )
        //        using( var w = new CKBinaryWriter( stream ) )
        //        {
        //            w.Write( (byte)0 );
        //            w.Write( primaryPosition );
        //            w.WriteNonNegativeSmallInt32( _releases.Count );
        //            foreach( var release in _releases )
        //            {
        //                release.Write( w );
        //            }
        //        }
        //    }
        //    else
        //    {
        //        using SafeFileHandle handle = File.OpenHandle( _path, FileMode.Open, FileAccess.Write );
        //        var sPos = MemoryMarshal.AsBytes( MemoryMarshal.CreateReadOnlySpan( ref primaryPosition, 1 ) );
        //        RandomAccess.Write( handle, sPos, 1 );
        //        RandomAccess.FlushToDisk( handle );
        //    }
        //    return true;
        //}
        //catch( Exception ex )
        //{
        //    monitor.Error( $"While persisting '{_path}'.", ex );
        //    return false;
        //}
    }

}

