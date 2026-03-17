using CK.Core;
using CSemVer;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace CKli.ShallowSolution.Plugin;

/// <summary>
/// A brutal mapper ignores the current version of a package: it systematically upgrades
/// a dependency to its associated target version if it exists.
/// </summary>
public static class BrutalPackageMapper
{
    /// <summary>
    /// Creates a brutal mapper from a simple package identifier to version dictionary.
    /// <para>
    /// The dictionary MUST use the <see cref="StringComparer.OrdinalIgnoreCase"/> comparer otherwise an <see cref="ArgumentException"/>
    /// is thrown.
    /// </para>
    /// </summary>
    /// <param name="mappings">The package to version dictionary. When null, <see cref="PackageMapper.Empty"/> is returned.</param>
    /// <returns>A basic mapping.</returns>
    public static IPackageMapping Create( IReadOnlyDictionary<string, SVersion>? mappings )
    {
        Throw.CheckArgument( mappings is not Dictionary<string, SVersion> d || d.Comparer == StringComparer.OrdinalIgnoreCase );
        Throw.CheckArgument( mappings is not ConcurrentDictionary<string, SVersion> c || c.Comparer == StringComparer.OrdinalIgnoreCase );
        return mappings != null ? new FromDictionary( mappings ) : PackageMapper.Empty;
    }

    sealed class FromDictionary : IPackageMapping
    {
        readonly IReadOnlyDictionary<string, SVersion> _mappings;

        public FromDictionary( IReadOnlyDictionary<string, SVersion> mappings ) => _mappings = mappings;

        public bool IsEmpty => _mappings.Count == 0;

        public bool HasMapping( string packageId ) => _mappings.ContainsKey( packageId );

        public SVersion? GetMappedVersion( string packageId, SVersion from ) => _mappings.GetValueOrDefault( packageId );

        public override string ToString()
        {
            var b = new StringBuilder();
            foreach( var (p, v) in _mappings )
            {
                b.Append( p ).Append( " → " ).Append( v.ToString() ).AppendLine();
            }
            return b.ToString();
        }
    }

}
