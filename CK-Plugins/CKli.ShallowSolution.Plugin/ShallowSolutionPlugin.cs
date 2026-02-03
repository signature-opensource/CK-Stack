using CKli.Core;
using LibGit2Sharp;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Internal;
using Microsoft.Extensions.FileProviders.Physical;
using System.Collections.Generic;
using System.IO;

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
    /// Gets the content of the commit as a <see cref="IDirectoryContents"/> that can be
    /// a <see cref="PhysicalDirectoryInfo"/> if the commit is the head of the repository.
    /// <para>
    /// There is no cache and no tracking: when <paramref name="useWorkingFolder"/> is true, if another commit
    /// is checked out, the content must not be used anymore or kittens will die.
    /// </para>
    /// </summary>
    /// <param name="commit">The commit for which content must be returned.</param>
    /// <param name="useWorkingFolder">
    /// False to always use the committed content and ignores the current file system (same as calling <see cref="GetCommitContent(Commit)"/>).
    /// </param>
    /// <returns>The commit content.</returns>
    public IDirectoryContents GetContent( Commit commit, bool useWorkingFolder = true )
    {
        var repo = ((IBelongToARepository)commit).Repository;
        return useWorkingFolder && commit.Sha == repo.Head.Tip.Sha
            ? new PhysicalDirectoryInfo( new DirectoryInfo( repo.Info.WorkingDirectory ) )
            : GetContent( commit.Tree );
    }

    /// <summary>
    /// Gets the content of the commit.
    /// </summary>
    /// <param name="commit">The commit.</param>
    /// <returns>The commit's content.</returns>
    public IDirectoryContents GetCommitContent( Commit commit ) => GetContent( commit.Tree );


    /// <summary>
    /// Gets the content of a <see cref="Tree"/>.
    /// </summary>
    /// <param name="tree">The Tree.</param>
    /// <returns>The Tree content.</returns>
    public IDirectoryContents GetContent( Tree tree )
    {
        if( !_gitContents.TryGetValue( tree.Sha, out var content ) )
        {
            content = new TreeFolder( tree );
            _gitContents.Add( tree.Sha, content );
        }
        return content;
    }

}



