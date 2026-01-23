using CK.Core;
using CKli.ArtifactHandler.Plugin;
using CKli.BranchModel.Plugin;
using CKli.Core;
using CKli.VersionTag.Plugin;
using CSemVer;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Xml.Linq;

namespace CKli.Build.Plugin;

public sealed partial class BuildPlugin
{

    [Description( """
        Local build the current Fix Workflow.
        """ )]
    [CommandPath( "fix build" )]
    public bool FixBuild( IActivityMonitor monitor,
                          CKliEnv context,
                          [Description( "Don't run tests even if they have never locally run on a commit." )]
                          bool skipTests = false,
                          [Description( "Run tests even if they have already run successfully on a commit." )]
                          bool forceTests = false )
    {
        if( !HandleForceSkipTests( monitor, skipTests, forceTests, out bool? runTest )
            || !FixWorkflow.Load( monitor, World, out var workflow ) )
        {
            return false;
        }
        bool publishing = false;


        if( workflow == null )
        {
            monitor.Error( $"No current Fix Workflow exist for world '{World.Name}'." );
            return false;
        }
        if( !_branchModel.CheckBasicPreconditions( monitor, $"building '{workflow}'", out var allRepos ) )
        {
            return false;
        }
        var results = new BuildResult[workflow.Targets.Length];
        var packageMapper = new PackageMapper();
        var packageMapping = new FixPackageMapping( packageMapper );
        var reusableUpdated = new PackageMapper();
        foreach( var target in workflow.Targets )
        {
            using( monitor.OpenInfo( $"Building {target.Index} - {target.Repo.DisplayPath}" ) )
            {
                var versionInfo = _versionTags.Get( monitor, target.Repo );

                if( !CheckoutFixTargetBranch( monitor, target, versionInfo, out var toFix, out int commitDepth )
                    || !UpdatePackages( monitor, target.Repo, packageMapping, ref reusableUpdated )
                    || !CommitUpdatedPackages( monitor, reusableUpdated, target, out bool hasNewCommit ) )
                {
                    return false;
                }
                Throw.DebugAssert( toFix.BuildContentInfo != null );

                if( hasNewCommit )
                {
                    commitDepth++;
                }
                var targetVersion = target.TargetVersion;
                if( !publishing )
                {
                    targetVersion = SVersion.Create( targetVersion.Major, targetVersion.Minor, targetVersion.Patch, $"local.fix.{commitDepth}" );
                }

                var result = CoreBuild( monitor,
                                        context,
                                        versionInfo,
                                        target.Repo.GitRepository.Repository.Head.Tip,
                                        targetVersion,
                                        runTest );
                if( result == null )
                {
                    return false;
                }
                // We introduce a check here: we demand that the produced package identifiers are the same as the release
                // we are fixing: changing the produced packages that are structural/architectural artifacts is
                // everything but fixing.
                if( !result.SkippedBuild && !result.Content.Produced.SequenceEqual( toFix.BuildContentInfo.Produced ) )
                {
                    monitor.Error( $"""
                            Forbidden change in produced packages for a fix in '{target.Repo.DisplayPath}':
                            The version 'v{target.ToFixVersion}' produced packages: '{toFix.BuildContentInfo.Produced.Concatenate( "', '" )}'.
                            But the new fix 'v{targetVersion}' produced: '{result.Content.Produced.Concatenate( "', '" )}'.
                            """ );
                    _versionTags.DestroyLocalRelease( monitor, result.Repo, targetVersion );
                    return false;
                }
                // Adds the new produced packages to the updates map.
                foreach( var p in result.Content.Produced )
                {
                    packageMapper.Add( p, target.ToFixVersion, targetVersion );
                }
                results[target.Index] = result;
            }
        }
        return true;



        static bool CommitUpdatedPackages( IActivityMonitor monitor,
                                           PackageMapper? reusableUpdated,
                                           FixWorkflow.TargetRepo target,
                                           out bool hasNewCommit )
        {
            Throw.DebugAssert( reusableUpdated != null );
            hasNewCommit = false;
            if( !reusableUpdated.IsEmpty )
            {
                var b = new StringBuilder( "Updates: " );
                reusableUpdated.Write( b.AppendLine() );
                var commitResult = target.Repo.GitRepository.Commit( monitor, b.ToString() );
                if( commitResult is CommitResult.Error )
                {
                    return false;
                }
                hasNewCommit = commitResult is not CommitResult.NoChanges;
                reusableUpdated.Clear();
            }
            return true;
        }
    }

    bool CheckoutFixTargetBranch( IActivityMonitor monitor,
                                  FixWorkflow.TargetRepo target,
                                  VersionTag.Plugin.VersionTagInfo versionInfo,
                                  [NotNullWhen(true)]out TagCommit? toFix,
                                  out int commitDepth )
    {
        commitDepth = 0;
        // We must be able to retrieve the TagCommit to fix.
        if( !versionInfo.TagCommits.TryGetValue( target.ToFixVersion, out toFix ) )
        {
            monitor.Error( $"Unable to find the commit '{target.ToFixCommitSha}' version 'v{target.ToFixVersion}' to be fixed in '{target.Repo.DisplayPath}'." );
            return false;
        }
        if( toFix.BuildContentInfo == null )
        {
            monitor.Error( $"The version 'v{target.ToFixVersion}' of the commit '{target.ToFixCommitSha}' to be fixed in '{target.Repo.DisplayPath}' has no more a valid build content." );
            return false;
        }

        GitRepository gitRepository = target.Repo.GitRepository;

        var branch = gitRepository.GetBranch( monitor, target.BranchName, CK.Core.LogLevel.Error );
        if( branch == null )
        {
            return false;
        }

        var divergence = gitRepository.Repository.ObjectDatabase.CalculateHistoryDivergence( toFix.Commit, branch.Tip );
        if( divergence.BehindBy == null )
        {
            monitor.Error( $"Branch '{target.BranchName}' in '{target.Repo.DisplayPath}' is not related to the commit '{target.ToFixCommitSha}' version 'v{target.ToFixVersion}' to be fixed." );
            return false;
        }
        commitDepth = divergence.BehindBy.Value;

        return gitRepository.Checkout( monitor, branch );
    }

    /// <summary>
    /// Not that easy. "dotnet package update" is rather useless (it works only on a single project and when packages to update
    /// are not found, it "Updating outdated packages in ..." and we absolutely don't want this).
    /// <para>
    /// More complex solutions exist but... https://github.com/dotnet-outdated use msbuild and lockfile (project.assets.json)
    /// with NuGet.ProjectModel, https://github.com/RicoSuter/DNT use msbuild (and a bit of BuildAlyzer)
    /// and https://github.com/Buildalyzer/Buildalyzer is not really needed here.
    /// </para>
    /// <para>
    /// This implementation is brutal: we consider all the projects in the solution (<see cref="VSSolution.Plugin.VSSolutionInfo"/>)
    /// and any Directory.Package.props or Directory.Build.props files from their folder to the solution root and updates
    /// the PackageVersion or PackageReference elements.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="repo">The repository (must be checked out).</param>
    /// <param name="mapping">The packages mapping to apply.</param>
    /// <param name="updated">The package actually updated.</param>
    /// <returns>True on success, false on failure.</returns>
    bool UpdatePackages( IActivityMonitor monitor, Repo repo, IPackageMapping mapping, ref PackageMapper? updated )
    {
        var solution = _solutionPlugin.Get( monitor, repo );
        if( solution.Issue != VSSolution.Plugin.VSSolutionIssue.None )
        {
            monitor.Error( $"Solution '{repo.DisplayPath}' must be fixed first ({solution.Issue}). Use 'ckli issue' for details." );
            return false;
        }
        if( !mapping.IsEmpty )
        {
            var projectFiles = LoadAllProjectFiles( monitor, repo, solution );
            if( projectFiles == null )
            {
                return false;
            }
            using( monitor.OpenInfo( $"Updating package versions in {projectFiles.Count} files." ) )
            {
                foreach( var p in projectFiles )
                {
                    if( !UpdateVersions( monitor, p.Path, p.Doc, mapping, ref updated ) )
                    {
                        return false;
                    }
                }
                foreach( var p in projectFiles )
                {
                    XmlHelper.SaveWithoutXmlDeclaration( p.Doc, p.Path );
                }
            }
        }
        return true;

        static bool UpdateVersions( IActivityMonitor monitor,
                                    string path,
                                    XDocument doc,
                                    IPackageMapping mapping,
                                    ref PackageMapper? updated )
        {
            if( doc.Root == null )
            {
                monitor.Error( $"Invalid Xml document in '{path}'." );
                return false;
            }
            if( Path.GetFileName( path.AsSpan() ).Equals( "Directory.Package.props", StringComparison.OrdinalIgnoreCase ) )
            {
                foreach( var e in doc.Root.Descendants( "PackageVersion" ) )
                {
                    var name = GetIncludedName( monitor, path, e );
                    if( name != null && mapping.HasMapping( name ) ) 
                    {
                        if( !UpdateVersion( monitor, path, e, name, "Version", "Version", mapping, ref updated ) )
                        {
                            return false;
                        }
                    }
                }
            }
            else
            {
                foreach( var e in doc.Root.Descendants( "PackageReference" ) )
                {
                    var name = GetIncludedName( monitor, path, e );
                    if( name != null && mapping.HasMapping( name ) )
                    {
                        if( !UpdateVersion( monitor, path, e, name, "VersionOverride", null, mapping, ref updated ) )
                        {
                            return false;
                        }
                        if( !UpdateVersion( monitor, path, e, name, "Version", "Version or VersionOverride", mapping, ref updated ) )
                        {
                            return false;
                        }
                    }
                }
            }
            return true;

            static string? GetIncludedName( IActivityMonitor monitor, string path, XElement e )
            {
                var name = e.Attribute( "Include" )?.Value;
                if( string.IsNullOrEmpty( name ) )
                {
                    monitor.Warn( $"""
                            Missing Include attribute in:
                            {e}
                            in '{path}'.
                            """ );
                    return null;
                }
                return name;
            }

            static bool UpdateVersion( IActivityMonitor monitor,
                                       string path,
                                       XElement e,
                                       string packageId,
                                       XName attributeName,
                                       string? attributeRequiredMessage,
                                       IPackageMapping map,
                                       ref PackageMapper? updated )
            {
                var a = e.Attribute( attributeName );
                if( a == null )
                {
                    if( attributeRequiredMessage != null )
                    {
                        monitor.Error( $"""
                        Expected {attributeRequiredMessage} in:
                        {e}
                        In file '{path}'.
                        """ );
                        return false;
                    }
                    return true;
                }
                var from = SVersion.TryParse( a.Value );
                if( !from.IsValid )
                {
                    monitor.Error( $"""
                        Unable to parse {attributeName.LocalName} in:
                        {e}
                        Error: {from.ErrorMessage}
                        In file '{path}'.
                        """ );
                    return false;
                }
                if( map.TryGetMappedVersion( packageId, from, out var to ) )
                {
                    a.SetValue( to.ToString() );
                    updated ??= new PackageMapper();
                    updated.TryAdd( packageId, from, to );
                }
                else
                {
                    monitor.Warn( $"""
                        Unhandled version in:
                        {e}
                        The package '{packageId}'' version map is: {map.ToString()}.
                        In file '{path}'.
                        """ );
                }
                return true;
            }
        }

        static List<(string Path, XDocument Doc)>? LoadAllProjectFiles( IActivityMonitor monitor,
                                                                        Repo repo,
                                                                        VSSolution.Plugin.VSSolutionInfo solution )
        {
            var dedupFolders = new HashSet<string>();
            var projectFiles = new List<(string Path, XDocument Doc)>();
            foreach( var p in solution.Projects.Values )
            {
                var path = Path.Combine( repo.WorkingFolder, p.FilePath );

                if( !LoadProjectFile( monitor, projectFiles, path )
                    || !LoadDirectoryFiles( monitor, projectFiles, path, repo.WorkingFolder.Path.Length, dedupFolders ) )
                {
                    return null;
                }
            }
            return projectFiles;

            static bool LoadProjectFile( IActivityMonitor monitor, List<(string Path, XDocument Doc)> projectFiles, string path )
            {
                Throw.DebugAssert( File.Exists( path ) );
                try
                {
                    var d = XDocument.Load( path, LoadOptions.PreserveWhitespace );
                    projectFiles.Add( (path, d) );
                }
                catch( Exception ex )
                {
                    monitor.Error( $"Unable to load file '{path}'.", ex );
                    return false;
                }
                return true;
            }

            static bool LoadDirectoryFiles( IActivityMonitor monitor,
                                            List<(string Path, XDocument Doc)> projectFiles,
                                            string path,
                                            int rootPathLength,
                                            HashSet<string> dedupFolders )
            {
                var parentPath = Path.GetDirectoryName( path );
                if( parentPath == null
                    || parentPath.Length < rootPathLength
                    || !dedupFolders.Add( parentPath ) )
                {
                    return true;
                }
                var packageProps = Path.Combine( parentPath, "Directory.Packages.props" );
                if( File.Exists( packageProps ) && !LoadProjectFile( monitor, projectFiles, packageProps ) )
                {
                    return false;
                }
                var buildProps = Path.Combine( parentPath, "Directory.Build.props" );
                if( File.Exists( buildProps ) && !LoadProjectFile( monitor, projectFiles, buildProps ) )
                {
                    return false;
                }
                return LoadDirectoryFiles( monitor, projectFiles, parentPath, rootPathLength, dedupFolders );
            }

        }
    }

    sealed record class ProjectFile( NormalizedPath FilePath, XDocument Doc );
}
