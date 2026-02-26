using CK.Core;
using CKli.Core;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using static System.Net.Mime.MediaTypeNames;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// "alpha", "beta", "delta", "epsilon", "gamma", "kappa", "pre", "rc" and "stable".
/// </summary>
public sealed partial class BranchNamespace
{
    readonly BranchName _root;
    readonly ImmutableArray<BranchName> _branches;
    Dictionary<string, BranchName>? _byName;

    internal BranchNamespace( string? ltsName, string? configuration )
    {
        IEnumerable<(string BranchName, bool IsDisconnected)> b = configuration != null
                                                                    ? Parse( configuration )
                                                                    : [ ("stable", false),
                                                                        ("rc", false),
                                                                        ("pre", false),
                                                                        ("kappa", false),
                                                                        ("gamma", false ),
                                                                        ("epsilon", false),
                                                                        ("delta", false),
                                                                        ("beta", false),
                                                                        ("alpha", false) ];
        _branches = Create( ltsName, b );
        _root = _branches[0];
    }

    static ImmutableArray<BranchName> Create( string? ltsName,
                                              IEnumerable<(string BranchName, bool IsDisconnected)> configuration )
    {
        var result = ImmutableArray.CreateBuilder<BranchName>();
        var e = configuration.GetEnumerator();
        if( !e.MoveNext() )
        {
            throw new CKException( "Empty configuration." );
        }
        // root branch.
        var b = new BranchName( null, ltsName, e.Current.BranchName, 0 );
        Add( result, b );
        while( e.MoveNext() )
        {
            b = Create( b, ltsName, e.Current.BranchName, !e.Current.IsDisconnected );
            Add( result, b );
        }
        return result.DrainToImmutable();

        static BranchName Create( BranchName b, string? ltsName, string name, bool connected )
        {
            return new BranchName( connected ? b : null, ltsName, name, b.InstabilityRank + 2 );
        }

        static void Add( ImmutableArray<BranchName>.Builder result, BranchName b )
        {
            result.Add( b );
            Throw.DebugAssert( b.DevBranch != null );
            result.Add( b.DevBranch );
        }
    }

    static List<(string BranchName, bool IsDisconnected)> Parse( string configuration )
    {
        var result = new List<(string BranchName, bool IsDisconnected)>();
        int count = 0;
        char initialChar = '\0';
        ReadOnlySpan<char> config = configuration;
        while( config.SkipWhiteSpaces() && config.Length > 0 )
        {
            bool isDisconnected = config.TryMatch( '*' );
            if( isDisconnected ) config.SkipWhiteSpaces();
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
            result.Add( (branchName, isDisconnected) );
        }
        return result;
    }

    [GeneratedRegex( "^[a-z][0-9a-z_-]+", RegexOptions.CultureInvariant )]
    private static partial Regex ValidBranchName();

    /// <summary>
    /// Gets the "stable" root branch name.
    /// </summary>
    public BranchName Root => _root;

    /// <summary>
    /// Gets the branches in increasing <see cref="BranchName.InstabilityRank"/>.
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
