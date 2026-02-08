using CK.Core;
using CKli.Core;
using CSemVer;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace CKli.ShallowSolution.Plugin;

/// <summary>
/// Defines share helpers that are used by the different "solution" flavors. 
/// </summary>
static class CommonSolution
{
    /// <summary>
    /// Loads all projects defined in a .slnx file (&lt;Project Path="..." .../&gt;) as well
    /// as all "Directory.Package.props" and "Directory.Build.props" (case insensitive) from
    /// each project folder up to the root.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="root">The root that contains the <paramref name="solution"/>.</param>
    /// <param name="solution">The &lt;Solution&gt; element.</param>
    /// <param name="loadOptions"><see cref="LoadOptions"/> for project documents.</param>
    /// <param name="collector">Project collector.</param>
    /// <returns>True on success, false on error.</returns>
    internal static bool LoadAllProjectFiles( IActivityMonitor monitor,
                                              INormalizedFileProvider root,
                                              XElement solution,
                                              LoadOptions loadOptions,
                                              Func<IActivityMonitor, NormalizedPath, XElement,bool> collector )
    {
        var dedupFolders = new HashSet<string>();
        foreach( var xmlProject in solution.Elements( "Project" ) )
        {
            NormalizedPath path = (string?)xmlProject.Attribute( "Path" );
            if( !path.IsEmptyPath )
            {
                var fInfo = root.GetFileInfo( path );
                if( fInfo == null )
                {
                    monitor.Warn( $"Missing file '{path}' declared by '{xmlProject}'." );
                }
                else
                {
                    if( !LoadProjectFile( monitor, root, collector, fInfo, path, loadOptions )
                        || !LoadDirectoryFiles( monitor, root, collector, path.RemoveLastPart(), dedupFolders, loadOptions ) )
                    {
                        return false;
                    }
                }
            }
        }
        return true;

        static bool LoadProjectFile( IActivityMonitor monitor,
                                     INormalizedFileProvider files,
                                     Func<IActivityMonitor, NormalizedPath, XElement,bool> collector,
                                     IFileInfo fInfo,
                                     NormalizedPath path,
                                     LoadOptions loadOptions )
        {
            try
            {
                using var stream = fInfo.CreateReadStream();
                var d = XDocument.Load( stream, loadOptions );
                return collector( monitor, path, d.Root! );
            }
            catch( Exception ex )
            {
                monitor.Error( $"Unable to load file '{path}'.", ex );
                return false;
            }
        }

        static bool LoadDirectoryFiles( IActivityMonitor monitor,
                                        INormalizedFileProvider files,
                                        Func<IActivityMonitor, NormalizedPath, XElement,bool> collector,
                                        NormalizedPath path,
                                        HashSet<string> dedupFolders,
                                        LoadOptions loadOptions )
        {
            if( path.IsEmptyPath || !dedupFolders.Add( path ) )
            {
                return true;
            }
            if( !LoadOptional( monitor, files, collector, path, loadOptions, "Directory.Packages.props" ) )
            {
                return false;
            }
            if( !LoadOptional( monitor, files, collector, path, loadOptions, "Directory.Build.props" ) )
            {
                return false;
            }
            return LoadDirectoryFiles( monitor, files, collector, path.RemoveLastPart(), dedupFolders, loadOptions );

            static bool LoadOptional( IActivityMonitor monitor,
                                      INormalizedFileProvider files,
                                      Func<IActivityMonitor, NormalizedPath, XElement,bool> collector,
                                      NormalizedPath path,
                                      LoadOptions loadOptions,
                                      string fName )
            {
                var packageProps = path.AppendPart( fName );
                var fPackageProps = files.GetFileInfo( packageProps );
                if( fPackageProps != null
                    && !LoadProjectFile( monitor, files, collector, fPackageProps, packageProps, loadOptions ) )
                {
                    return false;
                }
                return true;
            }
        }

    }


    /// <summary>
    /// Reads a required Include="..." attribute of any XElement.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="sourceFilePath">The path to log on error.</param>
    /// <param name="e">The element to read from.</param>
    /// <param name="logLevel">Log level to use to emit the "Missing Include ..." message.</param>
    /// <returns>The included name or null is not found.</returns>
    internal static string? GetIncludedName( IActivityMonitor monitor, string sourceFilePath, XElement e, LogLevel logLevel )
    {
        var name = e.Attribute( "Include" )?.Value;
        if( string.IsNullOrEmpty( name ) )
        {
            monitor.Log( logLevel, $"""
                                    Missing Include attribute in:
                                    {e}
                                    in '{sourceFilePath}'.
                                    """ );
            return null;
        }
        return name;
    }

    /// <summary>
    /// Centralized handling of a Version="..." attribute.
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="sourceFilePath">The path to log on error.</param>
    /// <param name="e">The element to read from.</param>
    /// <param name="attributeName">The attribute name.</param>
    /// <param name="attributeRequiredMessage">The "Expected ..." message part if the attribute is required.</param>
    /// <param name="a">Outputs the attribute if found.</param>
    /// <param name="version">Outputs the version if the attribute has been found and the version is successfully parsed.</param>
    /// <returns>True on sucess, false on error.</returns>
    internal static bool ReadVersionAttribute( IActivityMonitor monitor,
                                              string sourceFilePath,
                                              XElement e,
                                              XName attributeName,
                                              string? attributeRequiredMessage,
                                              out XAttribute? a,
                                              out SVersion? version )
    {
        a = e.Attribute( attributeName );
        if( a == null )
        {
            version = null;
            if( attributeRequiredMessage != null )
            {
                monitor.Error( $"""
                                Expected {attributeRequiredMessage} in:
                                {e}
                                In file '{sourceFilePath}'.
                                """ );
                return false;
            }
            return true;
        }
        version = SVersion.TryParse( a.Value );
        if( !version.IsValid )
        {
            monitor.Error( $"""
                            Unable to parse {attributeName.LocalName} in:
                            {e}
                            Error: {version.ErrorMessage}
                            In file '{sourceFilePath}'.
                            """ );
            return false;
        }
        return true;
    }

}



