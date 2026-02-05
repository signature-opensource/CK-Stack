using CK.Core;
using CKli.Core;
using CSemVer;
using LibGit2Sharp;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Internal;
using Microsoft.Extensions.FileProviders.Physical;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Xml.Linq;

namespace CKli.ShallowSolution.Plugin;

/// <summary>
/// Provides a shallow analysis of the content regarding projects and dependencies in any commit.
/// <para>
/// This starts from a root .slnx file (.sln is not supported) with the same name as the repository (the last part
/// of the <see cref="Repo.DisplayPath"/>). Only .csproj referenced by the root .slnx are considered as well as
/// all "Directory.Packages.props" and "Directory.Build.props" that can be found from the .csproj folders up to
/// the repository root.
/// </para>
/// <para>
/// This is simple, rather "brutal" but covers our current needs.
/// </para>
/// </summary>
public sealed class ShallowSolutionPlugin : PrimaryPluginBase
{
    readonly Dictionary<string, TreeFolder> _gitContents;

    public ShallowSolutionPlugin( PrimaryPluginContext primaryContext )
        : base( primaryContext )
    {
        _gitContents = new Dictionary<string, TreeFolder>();
    }

    /// <summary>
    /// Gets the content of the commit as a <see cref="INormalizedFileProvider"/> that can be
    /// the physical file system if the commit is the head of the repository.
    /// <para>
    /// There is no cache and no tracking: when <paramref name="useWorkingFolder"/> is true (the default),
    /// if another commit is checked out, the content must not be used anymore or kittens will die.
    /// </para>
    /// </summary>
    /// <param name="commit">The commit for which content must be returned.</param>
    /// <param name="useWorkingFolder">
    /// False to always use the committed content and ignores the current file system (same as calling <see cref="GetFiles(Commit)"/>).
    /// </param>
    /// <returns>The commit content.</returns>
    public INormalizedFileProvider GetFiles( Commit commit, bool useWorkingFolder = true )
    {
        var repo = ((IBelongToARepository)commit).Repository;
        return useWorkingFolder && commit.Sha == repo.Head.Tip.Sha
            ? new CheckedOutFileProvider( repo.Info.WorkingDirectory )
            : GetFiles( commit.Tree );
    }

    /// <summary>
    /// Gets the content of the commit.
    /// </summary>
    /// <param name="commit">The commit.</param>
    /// <returns>The commit's content.</returns>
    public INormalizedFileProvider GetFiles( Commit commit ) => GetFiles( commit.Tree );


    /// <summary>
    /// Gets the content of a <see cref="Tree"/>.
    /// </summary>
    /// <param name="tree">The Tree.</param>
    /// <returns>The Tree content.</returns>
    public INormalizedFileProvider GetFiles( Tree tree )
    {
        if( !_gitContents.TryGetValue( tree.Sha, out var content ) )
        {
            content = new TreeFolder( tree );
            _gitContents.Add( tree.Sha, content );
        }
        return content;
    }

    /// <summary>
    /// Creates a <see cref="MutableSolution"/> and calls <see cref="MutableSolution.UpdatePackages(IActivityMonitor, IPackageMapping, PackageMapper)"/>.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="repo">The repository (must be checked out).</param>
    /// <param name="mapping">The packages mapping to apply.</param>
    /// <param name="updated">The package actually updated.</param>
    /// <returns>True on success, false on failure.</returns>
    public bool UpdatePackages( IActivityMonitor monitor, Repo repo, IPackageMapping mapping, PackageMapper updated )
    {
        var solution = MutableSolution.Create( monitor, repo );
        if( solution == null )
        {
            return false;
        }
        return solution.UpdatePackages( monitor, mapping, updated );
    }

}

