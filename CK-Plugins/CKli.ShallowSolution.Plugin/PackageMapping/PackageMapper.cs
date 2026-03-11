using CK.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace CKli.ShallowSolution.Plugin;

/// <summary>
/// Simple implementation of a <see cref="IPackageMapping"/> that can register mappings.
/// </summary>
[DebuggerDisplay( "{ToString(),nq}" )]
public sealed class PackageMapper : IPackageMapping
{
    readonly Dictionary<string, object> _mapping;

    /// <summary>
    /// Initializes an empty mapper.
    /// </summary>
    public PackageMapper()
    {
        _mapping = new Dictionary<string, object>();
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
            b.Append( name ).Append( ": " ).AppendLine();
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
