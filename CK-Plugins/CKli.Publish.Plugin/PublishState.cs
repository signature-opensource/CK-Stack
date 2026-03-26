using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using static CK.Core.ActivityMonitorErrorCounter;

namespace CKli.Publish.Plugin;

/// <summary>
/// Models a list of waiting world release to be published.
/// </summary>
sealed partial class PublishState
{
    readonly List<WorldPublishInfo> _publications;
    Cursor _current;

    /// <summary>
    /// Gets the list of world releases that wait to be published.
    /// </summary>
    public IReadOnlyList<WorldPublishInfo> Publications => _publications;

    PublishState()
    {
        _publications = new List<WorldPublishInfo>();
        _current = new Cursor( this );
    }

    /// <summary>
    /// Adds a new world release.
    /// </summary>
    /// <param name="newOne">The new world release.</param>
    public void Add( WorldPublishInfo newOne )
    {
        Throw.CheckArgument( _publications.Count == 0 || newOne.BuildDate > _publications[^1].BuildDate  );
        _publications.Add( newOne );
        if( _current.Location == Cursor.LocType.EndOfState )
        {

        }
    }

    /// <summary>
    /// Creates a <see cref="Cursor"/> at a given position.
    /// </summary>
    /// <param name="position">The initial cursor position.</param>
    /// <returns>The cursor to use to traverse this state.</returns>
    public Cursor CreateCursor( int position = 0 ) => Cursor.Create( this, position );

    public static PublishState? Load( IActivityMonitor monitor, World world )
    {
        var path = world.Name.LocalDataFolder.AppendPart( "PublishState.bin" );
        var result = new PublishState();
        if( File.Exists( path ) )
        {
            try
            {
                using( var stream = File.OpenRead( path ) )
                using( var r = new CKBinaryReader( stream ) )
                {
                    int version = r.ReadByte();
                    int count = r.ReadNonNegativeSmallInt32();
                    var p = WorldPublishInfo.Read( monitor, world, r, version );
                    if( p == null ) return null;
                    result.Add( p );
                }
            }
            catch( Exception ex )
            {
                monitor.Error( $"While loading '{path}'.", ex );
                return null;
            }
        }
        return result;
    }

    internal async Task<bool> RunAsync( IActivityMonitor monitor )
    {
    }
}

