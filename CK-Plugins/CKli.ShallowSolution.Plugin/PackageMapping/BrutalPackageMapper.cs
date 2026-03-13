using CK.Core;
using CSemVer;
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
    /// </summary>
    /// <param name="mappings">The package to version dictionary. When null, <see cref="PackageMapper.Empty"/> is returned.</param>
    /// <returns>A basic mapping.</returns>
    public static IPackageMapping Create( Dictionary<string, SVersion>? mappings )
    {
        return mappings != null ? new FromDictionary( mappings ) : PackageMapper.Empty;
    }

    sealed class FromDictionary : IPackageMapping
    {
        readonly Dictionary<string, SVersion> _mappings;

        public FromDictionary( Dictionary<string, SVersion> mappings ) => _mappings = mappings;

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
