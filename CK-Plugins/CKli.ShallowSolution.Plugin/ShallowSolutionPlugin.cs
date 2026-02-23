using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace CKli.ShallowSolution.Plugin;

/// <summary>
/// Provides shallow analysis of the content regarding projects and dependencies in any commit.
/// <para>
/// This is simple (rather "brutal") but covers our current needs.
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
    /// Creates a <see cref="GitSolution"/> from a root ".slnx" file that must be conventionally named
    /// with the current repository name: this is used in the "Hot Zone", we don't handles renaming or
    /// legacy .sln format here as the "Hot Zone" is up-to-date by design.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="repo">The repository.</param>
    /// <param name="branch">The branch from which the solution must be read.</param>
    /// <returns>The solution or null on error.</returns>
    public GitSolution? GetShallowSolution( IActivityMonitor monitor, Repo repo, Branch branch )
    {
        Throw.CheckArgument( !branch.IsRemote );
        var (files,doc) = GetSolutionXDocument( monitor, repo, branch );
        if( doc == null )
        {
            return null;
        }
        var s = new GitSolution( repo, branch );
        if( !CommonSolution.LoadAllProjectFiles( monitor,
                                                 files,
                                                 doc.Root!,
                                                 LoadOptions.PreserveWhitespace,
                                                 s.AddProjectFile ) )
        {
            return null;
        }
        return s;
    }

    (INormalizedFileProvider, XDocument?) GetSolutionXDocument( IActivityMonitor monitor, Repo repo, Branch branch )
    {
        var gitFromBranch = ((IBelongToARepository)branch).Repository;
        Throw.CheckArgument( repo.GitRepository.Repository == gitFromBranch );
        var root = GetFiles( branch.Tip );
        var expectedName = repo.DisplayPath.LastPart + ".slnx";

        var solutionInfo = root.GetFileInfo( expectedName );
        if( solutionInfo == null )
        {
            monitor.Error( $"Expecting file '{expectedName}' in '{repo.DisplayPath}', branch '{branch.FriendlyName}'." );
            return (root, null);
        }
        try
        {
            using var stream = solutionInfo.CreateReadStream();
            var doc = XDocument.Load( stream, LoadOptions.PreserveWhitespace );
            Throw.CheckData( "A .slnx file must contain a <Solution> root element.", doc.Root?.Name.LocalName == "Solution" );
            return (root, doc);
        }
        catch( Exception ex )
        {
            monitor.Error( $"While loading '{repo.DisplayPath}/{expectedName}' solution in branch '{branch.FriendlyName}'.", ex );
        }
        return (root, null);
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

