using CK.Core;
using CKli.Core;
using CSemVer;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace CKli.ShallowSolution.Plugin;

/// <summary>
/// <para>
/// Updating packages references is not that easy. "dotnet package update" is rather useless (it works only on a single project and
/// when packages to update are not found, it "Updating outdated packages in ..." and we absolutely don't want this).
/// </para>
/// <para>
/// More complex solutions exist but... https://github.com/dotnet-outdated use msbuild and lockfile (project.assets.json)
/// with NuGet.ProjectModel, https://github.com/RicoSuter/DNT use msbuild (and a bit of BuildAlyzer)
/// and https://github.com/Buildalyzer/Buildalyzer is not really needed here.
/// </para>
/// <para>
/// This implementation is brutal: we consider all the projects in the solution (that must be a .slnx: this automatically calls
/// "dotnet sln migrate" on a legacy sln) and any Directory.Package.props or Directory.Build.props files from their folder to
/// the solution root and updates the PackageVersion or PackageReference elements.
/// </para>
/// </summary>
public sealed class MutableSolution
{
    readonly Repo _repo;
    readonly XDocument _solution;

    MutableSolution( Repo repo, XDocument solution )
    {
        _repo = repo;
        _solution = solution;
    }

    /// <summary>
    /// Updating packages references is not that easy. dotnet package update" is rather useless (it works only on a
    /// single project and when packages to update are not found, it "Updating outdated packages in ..." and we absolutely
    /// don't want this).
    /// <para>
    /// More complex solutions exist but... https://github.com/dotnet-outdated use msbuild and lockfile (project.assets.json)
    /// with NuGet.ProjectModel, https://github.com/RicoSuter/DNT use msbuild (and a bit of BuildAlyzer)
    /// and https://github.com/Buildalyzer/Buildalyzer is not really needed here.
    /// </para>
    /// <para>
    /// This implementation is brutal: we consider all the projects in the solution (that must be a .slnx) 
    /// and any Directory.Package.props or Directory.Build.props files from their folder to the solution root and updates
    /// the PackageVersion or PackageReference elements.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="mapping">The packages mapping to apply.</param>
    /// <param name="updated">The package actually updated.</param>
    /// <returns>True on success, false on failure.</returns>
    public bool UpdatePackages( IActivityMonitor monitor, IPackageMapping mapping, PackageMapper updated )
    {
        if( !mapping.IsEmpty )
        {
            var projectFiles = LoadAllProjectFiles( monitor,
                                                    new CheckedOutFileProvider( _repo.WorkingFolder ),
                                                    _solution.Root!,
                                                    LoadOptions.PreserveWhitespace );
            if( projectFiles == null )
            {
                return false;
            }
            using( monitor.OpenInfo( $"Updating package versions in {projectFiles.Count} files." ) )
            {
                foreach( var p in projectFiles )
                {
                    if( !UpdateVersions( monitor, p.Path, p.Doc, mapping, updated ) )
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
                                    PackageMapper updated )
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
                        if( !UpdateVersion( monitor, path, e, name, "Version", "Version", mapping, updated ) )
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
                        if( !UpdateVersion( monitor, path, e, name, "VersionOverride", null, mapping, updated ) )
                        {
                            return false;
                        }
                        if( !UpdateVersion( monitor, path, e, name, "Version", "Version or VersionOverride", mapping, updated ) )
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
                                       PackageMapper updated )
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

    }

    /// <summary>
    /// Creates a <see cref="MutableSolution"/> from the currently checked out repository's working folder.
    /// <para>
    /// This automatically handles "previous or legacy solution name" by considering single ".slnx" file (or ".sln"
    /// file - "dotnet sln migrate" is automatically called) and rename it to the
    /// conventional solution file name (that is the last part of <see cref="Repo.DisplayPath"/>).
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="repo">The repository.</param>
    /// <returns>The solution or null on error.</returns>
    public static MutableSolution? Create( IActivityMonitor monitor, Repo repo )
    {
        NormalizedPath slnOrSlnxPath;
        if( !EnsureSlnxFile( monitor, repo, out slnOrSlnxPath ) )
        {
            return null;
        }
        try
        {
            var doc = XDocument.Load( slnOrSlnxPath );
            Throw.CheckData( "A .slnx file must contain a <Solution> root element.",
                              doc.Root?.Name.LocalName == "Solution" );
            return new MutableSolution( repo, doc );
        }
        catch( Exception ex )
        {
            monitor.Error( $"While loading '{repo.DisplayPath}' solution.", ex );
        }
        return null;
    }

    static bool EnsureSlnxFile( IActivityMonitor monitor, Repo repo, out NormalizedPath path )
    {
        if( !FindSolutionFile( monitor, repo, out path, out var isLegacySln, out var mustRenameFile ) )
        {
            return false;
        }
        if( isLegacySln )
        {
            Throw.DebugAssert( mustRenameFile );
            var args = $"""sln migrate "{path.LastPart}" """;
            var e = ProcessRunner.RunProcess( monitor.ParallelLogger, "dotnet", args, repo.WorkingFolder );
            if( e != 0 )
            {
                monitor.Error( $"Running 'dotnet {args}' failed with code '{e}'." );
                return false;
            }
        }
        if( mustRenameFile )
        {
            var target = repo.WorkingFolder.AppendPart( repo.DisplayPath.LastPart + ".slnx" );
            File.Move( path, target );
            path = target;
        }
        return true;
    }

    static void WarnOnCaseIssue( IActivityMonitor monitor, NormalizedPath folder, string expectedName, string? displayFolder = null )
    {
        // Lookup from the parent with the name as a pattern.
        var actualFileName = Path.GetFileName( Directory.EnumerateFileSystemEntries( folder, expectedName ).First().AsSpan() );
        if( !actualFileName.Equals( expectedName, StringComparison.Ordinal ) )
        {
            monitor.Warn( $"Case issue for '{displayFolder ?? folder}/{actualFileName}': it should be '{expectedName}'." );
        }
    }


    static bool FindSolutionFile( IActivityMonitor monitor,
                                  Repo repo,
                                  out NormalizedPath path,
                                  out bool isLegacySln,
                                  out bool mustRenameFile )
    {
        isLegacySln = false;
        var expectedName = repo.DisplayPath.LastPart + ".slnx";
        var expectedPath = repo.WorkingFolder.AppendPart( expectedName );
        if( File.Exists( expectedPath ) )
        {
            WarnOnCaseIssue( monitor, repo.WorkingFolder, expectedName, repo.DisplayPath );
            path = expectedPath;
            mustRenameFile = false;
            return true;
        }
        monitor.Warn( $"Missing '{repo.DisplayPath}/{expectedName}'." );
        mustRenameFile = true;
        string? foundExactSln = null;
        string? foundSln1 = null;
        StringBuilder? extraSln = null;
        string? foundSlnx1 = null;
        StringBuilder? extraSlnx = null;
        var files = Directory.EnumerateFiles( repo.WorkingFolder );
        foreach( var f in files )
        {
            var ext = Path.GetExtension( f.AsSpan() );
            if( ext.Equals( ".sln", StringComparison.OrdinalIgnoreCase ) )
            {
                var name = Path.GetFileNameWithoutExtension( f.AsSpan() );
                if( name.Equals( repo.DisplayPath.LastPart, StringComparison.OrdinalIgnoreCase ) )
                {
                    foundExactSln = f;
                    break;
                }
                else
                {
                    if( foundSln1 == null )
                    {
                        foundSln1 = f;
                    }
                    else
                    {
                        if( extraSln == null )
                        {
                            extraSln = new StringBuilder( f );
                        }
                        else
                        {
                            extraSln.AppendLine().Append( f );
                        }
                    }
                }
            }
            else if( ext.Equals( ".slnx", StringComparison.OrdinalIgnoreCase ) )
            {
                if( foundSlnx1 == null )
                {
                    foundSlnx1 = f;
                }
                else
                {
                    if( extraSlnx == null )
                    {
                        extraSlnx = new StringBuilder( f );
                    }
                    else
                    {
                        extraSlnx.AppendLine().Append( f );
                    }
                }
            }
        }
        if( foundExactSln != null && foundSlnx1 != null )
        {
            // Ambiguous.
            monitor.Error( $"Found both '{foundExactSln}' (right naming) and '{foundSlnx1}' (expected .slnx format) in '{repo.DisplayPath}'." );
            path = default;
            return false;
        }
        if( foundSln1 == null && foundSlnx1 == null )
        {
            // No candidate.
            monitor.Error( $"Unable to find any .slnx or .sln file in '{repo.DisplayPath}'." );
            path = default;
            return false;
        }
        // First: the exact .sln name.
        if( foundExactSln != null )
        {
            path = foundExactSln;
            isLegacySln = true;
            return true;
        }
        // Second: the .slnx file (unless there are more than one).
        if( foundSlnx1 != null )
        {
            if( extraSlnx != null )
            {
                monitor.Error( $"""
                    Ambiguous .slnx files in '{repo.DisplayPath}'. Cannot choose between:
                    {foundSlnx1}
                    {extraSlnx}
                    """ );
                path = default;
                return false;
            }
            path = foundSlnx1;
            return true;
        }
        // Third: the .sln file (unless there are more than one).
        Throw.DebugAssert( foundSln1 != null );
        if( extraSln != null )
        {
            monitor.Error( $"""
                Ambiguous .sln files in '{repo.DisplayPath}'. Cannot choose between:
                {foundSln1}
                {extraSln}
                """ );
            path = default;
            return false;
        }
        path = foundSln1;
        return true;
    }

    static List<(NormalizedPath Path, XDocument Doc)>? LoadAllProjectFiles( IActivityMonitor monitor,
                                                                            INormalizedFileProvider files,
                                                                            XElement solution,
                                                                            LoadOptions loadOptions )
    {
        var dedupFolders = new HashSet<string>();
        var projectFiles = new List<(NormalizedPath Path, XDocument Doc)>();
        foreach( var xmlProject in solution.Elements( "Project" ) )
        {
            NormalizedPath path = (string?)xmlProject.Attribute( "Path" );
            if( !path.IsEmptyPath )
            {
                var fInfo = files.GetFileInfo( path );
                if( fInfo == null )
                {
                    monitor.Warn( $"Missing file '{path}' declared by '{xmlProject}'." );
                }
                else
                {
                    if( !LoadProjectFile( monitor, files, projectFiles, fInfo, path, loadOptions )
                        || !LoadDirectoryFiles( monitor, files, projectFiles, path.RemoveLastPart(), dedupFolders, loadOptions ) )
                    {
                        return null;
                    }
                }
            }
        }
        return projectFiles;

        static bool LoadProjectFile( IActivityMonitor monitor,
                                     INormalizedFileProvider files,
                                     List<(NormalizedPath Path, XDocument Doc)> projectFiles,
                                     IFileInfo fInfo,
                                     NormalizedPath path,
                                     LoadOptions loadOptions )
        {
            try
            {
                using var stream = fInfo.CreateReadStream();
                var d = XDocument.Load( stream, loadOptions );
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
                                        INormalizedFileProvider files,
                                        List<(NormalizedPath Path, XDocument Doc)> projectFiles,
                                        NormalizedPath path,
                                        HashSet<string> dedupFolders,
                                        LoadOptions loadOptions )
        {
            if( path.IsEmptyPath || !dedupFolders.Add( path ) )
            {
                return true;
            }
            if( !LoadOptional( monitor, files, projectFiles, path, loadOptions, "Directory.Packages.props" ) )
            {
                return false;
            }
            if( !LoadOptional( monitor, files, projectFiles, path, loadOptions, "Directory.Build.props" ) )
            {
                return false;
            }
            return LoadDirectoryFiles( monitor, files, projectFiles, path.RemoveLastPart(), dedupFolders, loadOptions );

            static bool LoadOptional( IActivityMonitor monitor,
                                        INormalizedFileProvider files,
                                        List<(NormalizedPath Path, XDocument Doc)> projectFiles,
                                        NormalizedPath path,
                                        LoadOptions loadOptions,
                                        string fName )
            {
                var packageProps = path.AppendPart( fName );
                var fPackageProps = files.GetFileInfo( packageProps );
                if( fPackageProps != null && !LoadProjectFile( monitor, files, projectFiles, fPackageProps, packageProps, loadOptions ) )
                {
                    return false;
                }
                return true;
            }
        }

    }
}



