using CK.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Transactions;

namespace CKli.ShallowSolution.Plugin;

/// <summary>
/// Simple implementation of a <see cref="IPackageMapping"/> that can register mappings.
/// </summary>
[DebuggerDisplay( "{ToString(),nq}" )]
[SerializationVersion( 0 )]
public sealed class PackageMapper : IPackageMapping, ICKVersionedBinarySerializable
{
    sealed class EmptyMapper : IPackageMapping
    {
        public bool IsEmpty => true;

        public SVersion? GetMappedVersion( string packageId, SVersion from ) => null;

        public bool HasMapping( string packageId ) => false;
    }

    /// <summary>
    /// Gets a empty mapping singleton.
    /// </summary>
    public static readonly IPackageMapping Empty = new EmptyMapper();

    readonly Dictionary<string, object> _mapping;

    /// <summary>
    /// Initializes an empty mapper.
    /// </summary>
    public PackageMapper()
    {
        _mapping = new Dictionary<string, object>( StringComparer.OrdinalIgnoreCase );
    }


    /// <summary>
    /// Versioned deserializer.
    /// </summary>
    /// <param name="r">The reader.</param>
    /// <param name="version">The serialization version.</param>
    public PackageMapper( ICKBinaryReader r, int version )
    {
        int count = r.ReadNonNegativeSmallInt32();
        _mapping = new Dictionary<string, object>( count );
        for( int i = 0; i < count; i++ )
        {
            var k = r.ReadString();
            int l = r.ReadSmallInt32( 1 );
            if( l == 1 )
            {
                _mapping[k] = Tuple.Create( ReadVersion( r ), ReadVersion( r ) );
            }
            else
            {
                var list = new List<object>( l );
                for( int j = 0; j < l; j++ )
                {
                    list.Add( (ReadVersion( r ), ReadVersion( r )) );
                }
            }
        }

        static SVersion ReadVersion( ICKBinaryReader r ) => SVersion.Parse( r.ReadString() );
    }

    /// <summary>
    /// Versioned serialization.
    /// </summary>
    /// <param name="w">The target writer.</param>
    public void WriteData( ICKBinaryWriter w )
    {
        w.WriteNonNegativeSmallInt32( _mapping.Count );
        foreach( var kvp in _mapping )
        {
            w.Write( kvp.Key );
            if( kvp.Value is Tuple<SVersion, SVersion> one )
            {
                w.WriteSmallInt32( 1, 1 );
                Write( w, one.Item1, one.Item2 );
            }
            else
            {
                var list = (List<(SVersion From, SVersion To)>)kvp.Value;
                w.WriteSmallInt32( list.Count, 1 );
                foreach( var o in list ) Write( w, o.To, o.From );
            }
        }

        static void Write( ICKBinaryWriter w, SVersion from, SVersion to )
        {
            w.Write( from.ToString() );
            w.Write( to.ToString() );
        }
    }


    /// <inheritdoc />
    public bool IsEmpty => _mapping.Count == 0;

    /// <inheritdoc />
    public bool HasMapping( string packageId ) => _mapping.ContainsKey( packageId );

    /// <summary>
    /// Adds a (packageId,version) -&gt; version mapping. The (packageId,version) must not
    /// already be mapped to another version otherwise false is returned. This is idempotent:
    /// the mapped version can already be mapped.
    /// </summary>
    /// <param name="packageId">The package identifier.</param>
    /// <param name="from">The version to map.</param>
    /// <param name="to">The mapped version. Must not be <paramref name="from"/>.</param>
    /// <returns>True if the mapping has been added (or was already defined), false on conflict.</returns>
    public bool TryAdd( string packageId, SVersion from, SVersion to )
    {
        Throw.CheckNotNullOrWhiteSpaceArgument( packageId );
        Throw.CheckArgument( from != to );
        if( _mapping.TryGetValue( packageId, out var o ) )
        {
            if( o is Tuple<SVersion, SVersion> one )
            {
                if( one.Item1 == from )
                {
                    return one.Item2 == to;
                }
                _mapping[packageId] = new List<(SVersion, SVersion)> { (one.Item1, one.Item2), (from, to) };
            }
            else
            {
                var list = (List<(SVersion From, SVersion To)>)o;
                var exists = list.FirstOrDefault( m => m.From == from );
                if( exists.From != null )
                {
                    return exists.To == to;
                }
                list.Add( (from, to) );
            }
        }
        else
        {
            _mapping.Add( packageId, Tuple.Create( from, to ) );
        }
        return true;
    }

    /// <summary>
    /// Calls <see cref="TryAdd(string, SVersion, SVersion)"/> and throws on conflict.
    /// </summary>
    /// <param name="packageId">The package identifier.</param>
    /// <param name="from">The version to map.</param>
    /// <param name="to">The mapped version. Must not be <paramref name="from"/>.</param>
    public void Add( string packageId, SVersion from, SVersion to )
    {
        if( !TryAdd( packageId, from, to ) )
        {
            Throw.CKException( $"Package '{packageId}' for version '{from}' is already mapped." );
        }
    }

    /// <inheritdoc />
    public SVersion? GetMappedVersion( string packageId, SVersion from )
    {
        if( _mapping.TryGetValue( packageId, out var o ) )
        {
            if( o is Tuple<SVersion, SVersion> one )
            {
                if( one.Item1 == from ) return one.Item2;
            }
            else
            {
                var list = (List<(SVersion From, SVersion To)>)o;
                return list.FirstOrDefault( m => m.From == from ).To;
            }
        }
        return null;
    }

    /// <summary>
    /// Clears all registered mappings.
    /// </summary>
    public void Clear()
    {
        _mapping.Clear();
    }

    /// <summary>
    /// Writes the mappings.
    /// </summary>
    /// <param name="b">The target builder.</param>
    /// <returns>The builder.</returns>
    public StringBuilder Write( StringBuilder b )
    {
        foreach( var (name, o) in _mapping )
        {
            b.Append( name ).Append( ": " );
            if( o is Tuple<SVersion, SVersion> one )
            {
                b.Append( one.Item1 ).Append( " → " ).Append( one.Item2 ).AppendLine();
            }
            else
            {
                var list = (List<(SVersion From, SVersion To)>)o;
                foreach( var m in list )
                {
                    b.Append( m.From ).Append( " → " ).Append( m.To ).AppendLine();
                }
            }
        }
        return b;
    }

    /// <summary>
    /// Overridden to return all the mappings.
    /// </summary>
    /// <returns>The mappings.</returns>
    public override string ToString() => Write( new StringBuilder() ).ToString();

}
