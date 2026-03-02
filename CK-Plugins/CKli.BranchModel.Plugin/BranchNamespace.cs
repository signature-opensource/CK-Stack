using CK.Core;
using CKli.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using static System.Net.Mime.MediaTypeNames;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// Captures the branches that makes the hot zone.
/// </summary>
public sealed partial class BranchNamespace
{
    readonly BranchName _root;
    readonly ImmutableArray<BranchName> _branches;
    Dictionary<string, BranchName>? _byName;

    internal BranchNamespace( string? ltsName, string? configuration )
    {
        IEnumerable<(string BranchName, BranchLinkType Link)> b = configuration != null
                                                                    ? Parse( configuration )
                                                                    : [ ("stable", BranchLinkType.None),
                                                                        ("rc", BranchLinkType.PreRelease),
                                                                        ("pre", BranchLinkType.PreRelease),
                                                                        ("kappa", BranchLinkType.PreRelease),
                                                                        ("gamma", BranchLinkType.PreRelease ),
                                                                        ("epsilon", BranchLinkType.PreRelease),
                                                                        ("delta", BranchLinkType.PreRelease),
                                                                        ("beta", BranchLinkType.PreRelease),
                                                                        ("alpha", BranchLinkType.PreRelease) ];
        _branches = Create( ltsName, b );
        _root = _branches[0];
    }

    static ImmutableArray<BranchName> Create( string? ltsName,
                                              IEnumerable<(string BranchName, BranchLinkType Link)> configuration )
    {
        var result = ImmutableArray.CreateBuilder<BranchName>();
        var e = configuration.GetEnumerator();
        if( !e.MoveNext() )
        {
            throw new CKException( "Empty configuration." );
        }
        // root branch.
        var b = new BranchName( BranchLinkType.None, ltsName, e.Current.BranchName, 0 );
        result.Add( b );
        while( e.MoveNext() )
        {
            b = new BranchName( e.Current.Link, ltsName, e.Current.BranchName, b.Index + 1 );
            result.Add( b );
        }
        return result.DrainToImmutable();
    }

    static List<(string BranchName, BranchLinkType Link)> Parse( string configuration )
    {
        var result = new List<(string BranchName, BranchLinkType Link)>();
        int count = 0;
        char initialChar = '\0';
        ReadOnlySpan<char> config = configuration;
        while( config.SkipWhiteSpaces() && config.Length > 0 )
        {
            BranchLinkType linkType = BranchLinkType.None;
            if( count > 0 && !Match( ref config, out linkType ) )
            {
                throw new CKException( $"""
                    Unable to parse link type in:
                    {configuration}
                    Expected '||' (None), '|' (Stable), '->' (Prerelease) or '=>' (CI), got:
                    {config}
                    """ );
            }
            var e = ValidBranchName().EnumerateMatches( config );
            if( !e.MoveNext() )
            {
                throw new CKException( $"""
                    Unable to parse a branch name in:
                    {configuration}
                    Expected a lowercase ASCII identifier (which may contain dash '-' or underscore '_'), got:
                    {config}
                    """ );
            }
            Throw.DebugAssert( e.Current.Index == 0 );
            var branchName = new string( config.Slice( 0, e.Current.Length ) );
            config = config.Slice( e.Current.Length );
            if( count != 0 )
            {
                var currentInitial = branchName[0];
                if( initialChar >= currentInitial )
                {
                    throw new CKException( $"""
                        Invalid branch name '{branchName}' in:
                        {configuration}
                        The branch name must start with a letter greater than '{initialChar}'.
                        """ );
                }
                initialChar = currentInitial;
            }
            ++count;
            result.Add( (branchName, linkType) );
        }
        return result;

        static bool Match( ref ReadOnlySpan<char> h, out BranchLinkType t )
        {
            t = BranchLinkType.PreRelease;
            if( h.TryMatch( '|' ) )
            {
                t = h.TryMatch( '|' )
                        ? BranchLinkType.None
                        : BranchLinkType.Stable;
            }
            else
            {
                bool full = h.TryMatch( '=' );
                if( !full && !h.TryMatch( '-' )
                    || !h.TryMatch( '>' ) )
                {
                    return false;
                }
                t = full ? BranchLinkType.Full : BranchLinkType.PreRelease;
            }
            h.SkipWhiteSpaces();
            return true;
        }
    }

    [GeneratedRegex( "^[a-z][0-9a-z_-]+", RegexOptions.CultureInvariant )]
    private static partial Regex ValidBranchName();

    /// <summary>
    /// Gets the "stable" root branch name.
    /// </summary>
    public BranchName Root => _root;

    /// <summary>
    /// Gets the branches in increasing <see cref="BranchName.Index"/>.
    /// </summary>
    public ImmutableArray<BranchName> Branches => _branches;

    /// <summary>
    /// Gets an index of the branch names by their <see cref="BranchName.Name"/>.
    /// </summary>
    public IReadOnlyDictionary<string, BranchName> ByName => _byName ??= Branches.ToDictionary( b => b.Name );

    /// <summary>
    /// Finds a branch name.
    /// </summary>
    /// <param name="name">The branch name.</param>
    /// <returns>The branch or null.</returns>
    public BranchName? Find( string name ) => ByName.GetValueOrDefault( name ); 

}
