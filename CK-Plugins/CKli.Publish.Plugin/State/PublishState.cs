using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using static CK.Core.ActivityMonitorErrorCounter;
using static CK.Core.CheckedWriteStream;

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
    readonly NormalizedPath _path;
    Cursor _primaryCursor;

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
        if( cleanupReleases && c.World != null && c.World != _primaryCursor.World )
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
        return Persist( monitor, fullUpdate )
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
        return Persist( monitor, fullWrite: true );
    }

    /// <summary>
    /// Creates a new <see cref="Cursor"/> positioned at the start of this state.
    /// </summary>
    /// <returns>The cursor to use to traverse this state.</returns>
    public Cursor CreateCursor( int position = 0 ) => Cursor.Create( this );

    PublishState( NormalizedPath path )
    {
        _path = path;
        _releases = new List<WorldReleaseInfo>();
        _primaryCursor = new Cursor( this );
    }

    PublishState( NormalizedPath path, List<WorldReleaseInfo> releases, int primaryPosition )
    {
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
                    int position = r.ReadInt32();
                    int version = r.ReadByte();
                    int count = r.ReadNonNegativeSmallInt32();
                    while( --count >= 0 )
                    {
                        var p = WorldReleaseInfo.Read( monitor, world, r, version );
                        if( p == null ) return null;
                        releases.Add( p );
                    }
                    return new PublishState( path, releases, position );
                }
            }
            catch( Exception ex )
            {
                monitor.Error( $"While loading '{path}'.", ex );
                return null;
            }
        }
        return new PublishState( path );
    }

    internal bool Persist( IActivityMonitor monitor, bool fullWrite )
    {
        if( _releases.Count == 0 )
        {
            return FileHelper.DeleteFile( monitor, _path );
        }
        try
        {
            int primaryPosition = _primaryCursor.GetPosition();
            if( fullWrite || !File.Exists( _path ) )
            {
                using( var stream = File.OpenWrite( _path ) )
                using( var w = new CKBinaryWriter( stream ) )
                {
                    w.Write( (byte)0 );
                    w.Write( primaryPosition );
                    w.WriteNonNegativeSmallInt32( _releases.Count );
                    foreach( var release in _releases )
                    {
                        release.Write( w );
                    }
                }
            }
            else
            {
                using SafeFileHandle handle = File.OpenHandle( _path, FileMode.Open, FileAccess.Write );
                var sPos = MemoryMarshal.AsBytes( MemoryMarshal.CreateReadOnlySpan( ref primaryPosition, 1 ) );
                RandomAccess.Write( handle, sPos, 1 );
                RandomAccess.FlushToDisk( handle );
            }
            return true;
        }
        catch( Exception ex )
        {
            monitor.Error( $"While persisting '{_path}'.", ex );
            return false;
        }
    }

}

