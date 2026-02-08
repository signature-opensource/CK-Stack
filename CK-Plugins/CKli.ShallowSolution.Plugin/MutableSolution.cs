using CK.Core;
using CKli.Core;
using CSemVer;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace CKli.ShallowSolution.Plugin;

/// <summary>
/// Encapsulates a .slnx solution in file system directory (a checked out commit).
/// <para>
/// This handles migration from legacy .sln and change of solution name (if one and only
/// one .sln or .slnx exists at the root).
/// </para>
/// <para>
/// The only currently supported operation is <see cref="UpdatePackages"/>.
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
    /// Updating packages references is not that easy. "dotnet package update" is rather useless (it works only on a
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
            var projectFiles = new List<(NormalizedPath Path, XElement Project)>();
            if( !CommonSolution.LoadAllProjectFiles( monitor,
                                                     new CheckedOutFileProvider( _repo.WorkingFolder ),
                                                     _solution.Root!,
                                                     LoadOptions.PreserveWhitespace,
                                                     ( monitor, path, project ) =>
                                                     {
                                                         projectFiles.Add( (path, project) );
                                                         return true;
                                                     } ) )
            {
                return false;
            }
            using( monitor.OpenInfo( $"Updating package versions in {projectFiles.Count} files." ) )
            {
                foreach( var p in projectFiles )
                {
                    if( !UpdateVersions( monitor, p.Path, p.Project, mapping, updated ) )
                    {
                        return false;
                    }
                }
                foreach( var p in projectFiles )
                {
                    XmlHelper.SaveWithoutXmlDeclaration( p.Project.Document!, _repo.WorkingFolder.Combine( p.Path ) );
                }
            }
        }
        return true;

        static bool UpdateVersions( IActivityMonitor monitor,
                                    string path,
                                    XElement projectRoot,
                                    IPackageMapping mapping,
                                    PackageMapper updated )
        {
            if( Path.GetFileName( path.AsSpan() ).Equals( "Directory.Package.props", StringComparison.OrdinalIgnoreCase ) )
            {
                foreach( var e in projectRoot.Descendants( "PackageVersion" ) )
                {
                    var name = CommonSolution.GetIncludedName( monitor, path, e, LogLevel.Warn );
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
                foreach( var e in projectRoot.Descendants( "PackageReference" ) )
                {
                    var name = CommonSolution.GetIncludedName( monitor, path, e, LogLevel.Warn );
                    if( name != null && mapping.HasMapping( name ) )
                    {
                        if( !UpdateVersion( monitor, path, e, name, "VersionOverride", null, mapping, updated ) )
                        {
                            return false;
                        }
                        if( !UpdateVersion( monitor, path, e, name, "Version", null, mapping, updated ) )
                        {
                            return false;
                        }
                    }
                }
            }
            return true;

            static bool UpdateVersion( IActivityMonitor monitor,
                                       string path,
                                       XElement e,
                                       string packageId,
                                       XName attributeName,
                                       string? attributeRequiredMessage,
                                       IPackageMapping map,
                                       PackageMapper updated )
            {
               if( !CommonSolution.ReadVersionAttribute( monitor,
                                                         path,
                                                         e,
                                                         attributeName,
                                                         attributeRequiredMessage,
                                                         out XAttribute? a,
                                                         out SVersion? from ) )
                {
                    return false;
                }
                if( from != null )
                {
                    Throw.DebugAssert( a != null );
                    if( map.TryGetMappedVersion( packageId, from, out var to ) )
                    {
                        if( from != to )
                        {
                            a.SetValue( to.ToString() );
                            updated.TryAdd( packageId, from, to );
                        }
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
            var doc = XDocument.Load( slnOrSlnxPath, LoadOptions.PreserveWhitespace );
            Throw.CheckData( "A .slnx file must contain a <Solution> root element.", doc.Root?.Name.LocalName == "Solution" );
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
        if( isLegacySln || mustRenameFile )
        {
            var target = repo.WorkingFolder.AppendPart( repo.DisplayPath.LastPart + ".slnx" );
            Throw.DebugAssert( "Or we won't be here.", !File.Exists( target ) );
            if( isLegacySln )
            {
                using( monitor.OpenInfo( $"Migrating '{path.LastPart}'." ) )
                {
                    Throw.DebugAssert( mustRenameFile );
                    var args = $"""sln migrate "{path.LastPart}" """;
                    var e = ProcessRunner.RunProcess( monitor.ParallelLogger, "dotnet", args, repo.WorkingFolder );
                    if( e != 0 )
                    {
                        monitor.Error( $"Running 'dotnet {args}' failed with code '{e}'." );
                        return false;
                    }
                    // Successful migration:
                    // - The old .sln file can be deleted.
                    // - The path is now a .slnx file.
                    FileHelper.DeleteFile( monitor, path );
                    path = path.Path + 'x';
                    // This may be our target. Rather than testing path == target (case insensitive?), challenge the
                    // file system.
                    mustRenameFile = !File.Exists( target );
                }
            }
            if( mustRenameFile )
            {
                monitor.Info( $"Renaming '{path.LastPart}' to '{target.LastPart}'." );
                File.Move( path, target );
                path = target;
            }
        }
        return true;
    }

    static void WarnOnCaseIssue( IActivityMonitor monitor,
                                 NormalizedPath folder,
                                 string expectedName,
                                 string? displayFolder = null )
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
        // First: the exact .sln name (regular legacy case).
        if( foundExactSln != null )
        {
            monitor.Warn( $"Found legacy solution file '{Path.GetFileName( foundExactSln.AsSpan() )}'." );
            path = foundExactSln;
            isLegacySln = true;
            return true;
        }
        // Now we should have at least a .sln or a .slnx, otherwise we cannot do anything.
        if( foundSln1 == null && foundSlnx1 == null )
        {
            // No candidate.
            monitor.Error( $"Unable to find any .slnx or .sln file in '{repo.DisplayPath}'." );
            path = default;
            return false;
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
            monitor.Warn( $"Expecting '{expectedName}'. Considering single '{Path.GetFileName( foundSlnx1.AsSpan() )}' file." );
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
        monitor.Warn( $"Expecting '{expectedName}'. Considering single legacy '{Path.GetFileName( foundSln1.AsSpan() )}' file." );
        path = foundSln1;
        return true;
    }

}



