using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using Shouldly;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using System.Xml.Linq;
using static CK.Testing.MonitorTestHelper;

namespace CKli;

static partial class TestEnv
{
    readonly static NormalizedPath _barePath;
    readonly static NormalizedPath _remotesPath;
    readonly static NormalizedPath _clonedPath;
    readonly static WorldName _worldName;
    readonly static Dictionary<string, RemotesCollection> _remoteRepositories;
    readonly static XElement _hostPluginsConfiguration;

    static TestEnv()
    {
        _remotesPath = TestHelper.TestProjectFolder.AppendPart( "Remotes" );
        _barePath = _remotesPath.AppendPart( "bare" );
        _clonedPath = TestHelper.TestProjectFolder.AppendPart( "Cloned" );

        var pluginFolderName = TestHelper.TestProjectFolder.Parts[^3];
        Throw.CheckState( pluginFolderName.Contains( "-Plugins" ) );
        int idx = pluginFolderName.IndexOf( "-Plugins" );
        var stackName = pluginFolderName.Substring( 0, idx );
        var ltsName = pluginFolderName.Substring( idx + 8 );
        _worldName = new WorldName( stackName, ltsName );

        //var nunitLoadContext = AssemblyLoadContext.All.FirstOrDefault( c => c.GetType().Name == "TestAssemblyLoadContext" );
        //if( nunitLoadContext == null )
        //{
        //    Throw.InvalidOperationException( """
        //        NUnit test adapter doesn't use AssemblyLoadContext named 'TestAssemblyLoadContext'.
        //        This must be investigated.
        //        """ );
        //}
        // We must ensure that the CKli.Plugins.CompiledPlugins is available before loading this assembly
        // in the load context because once loaded, we won't be able to "update" it if the compiled plugins
        // weren't available...
        // We may use a ReflectionOnly context or use the Meta API but this costs. We simply consider that
        // if the CKli.CompiledPlugins.cs file is present, then it's fine: we are in a test project that
        // depends on the CKli.Plugins project, so when it is compiled, the CKli.Plugins is also compiled.
        //
        // We check that CompileMode is not None and that no plugins are disabled before.
        // If the user deleted the CKli.CompiledPlugins.cs, he must run "ckli plugin info" to restore the
        // compiled plugins.
        //
        _hostPluginsConfiguration = ReadStackPluginConfiguration( TestHelper.Monitor, _worldName );

        var ckliPluginsCompiledFile = TestHelper.SolutionFolder.AppendPart( pluginFolderName ).AppendPart( "CKli.Plugins" ).AppendPart( "CKli.CompiledPlugins.cs" );
        if( !File.Exists( ckliPluginsCompiledFile ) )
        {
            Throw.InvalidOperationException( $"The compiled plugins source code generated file is missing: '{ckliPluginsCompiledFile}'." );
        }
        var runFolder = TestHelper.SolutionFolder.Combine( PluginMachinery.GetLocalRunFolder( pluginFolderName ) );
        var ckliPluginFilePath = runFolder.AppendPart( "CKli.Plugins.dll" );
        if( !File.Exists( ckliPluginFilePath ) )
        {
            Throw.InvalidOperationException( $"The compiled plugins file is missing: '{ckliPluginFilePath}'." );
        }
        var f = GetPluginFactory( ckliPluginFilePath );
        if( f == null )
        {
            Throw.InvalidOperationException( "Unable to get the plugin factory from the compiled plugins." );
        }
        CKliRootEnv.Initialize( _worldName.FullName, screen: new StringScreen(), findCurrentStackPath: false );
        World.DirectPluginFactory = f;

        _remoteRepositories = InitializeRemotes();

        static XElement ReadStackPluginConfiguration( IActivityMonitor monitor, WorldName worldHostName )
        {
            // Reads the host's default world definition definition file <Plugins> element.
            XElement? stackPlugins = null;
            try
            {
                var stackDefinitionFile = TestHelper.SolutionFolder.AppendPart( $"{worldHostName}.xml" );
                XDocument stackDefinitionDoc = XDocument.Load( stackDefinitionFile, LoadOptions.PreserveWhitespace );
                stackPlugins = stackDefinitionDoc.Root?.Element( "Plugins" );
                if( stackPlugins == null )
                {
                    monitor.Error( "The Stack's World '{worldHostName}' has no <Plugins> element." );
                }
                else if( stackPlugins.Elements().Any( e => (bool?)e.Attribute( "Disabled" ) is true ) )
                {
                    monitor.Error( $"""
                                    The Stack's World '{worldHostName}' <Plugins> element must no have ANY disabled plugins:
                                    {stackPlugins}
                                    """ );
                    stackPlugins = null;
                }
            }
            catch( Exception ex )
            {
                monitor.Error( $"While reading the Stack's World '{worldHostName}' <Plugins> element.", ex );
                stackPlugins = null;
            }
            if( stackPlugins == null )
            {
                Throw.InvalidOperationException( $"Unable to read the Stack's World '{worldHostName}' <Plugins> element." );
            }
            return stackPlugins;
        }

        static IPluginFactory? GetPluginFactory( NormalizedPath ckliPluginFilePath )
        {
            var ckliPlugins = Assembly.LoadFrom( ckliPluginFilePath );
            var compiled = ckliPlugins.GetType( "CKli.Plugins.CompiledPlugins" );
            return (IPluginFactory?)compiled?
                                    .GetMethod( "UncheckedGet" )?
                                    .Invoke( null, [] );
        }
    }

    static Dictionary<string, RemotesCollection> InitializeRemotes()
    {
        var remoteIndexPath = _barePath.AppendPart( "Remotes.txt" );

        var zipPath = _remotesPath.AppendPart( "Remotes.zip" );
        var zipTime = File.GetLastWriteTimeUtc( zipPath );
        if( !File.Exists( remoteIndexPath )
            || File.GetLastWriteTimeUtc( remoteIndexPath ) != zipTime )
        {
            using( TestHelper.Monitor.OpenInfo( $"Last write time of 'Remotes/' differ from 'Remotes/Remotes.zip'. Restoring remotes from zip." ) )
            {
                RestoreRemotesZipAndCreateBareRepositories( remoteIndexPath, zipPath, zipTime );
            }
        }
        return File.ReadAllLines( remoteIndexPath )
                    .Select( l => l.Split( '/' ) )
                    .GroupBy( names => names[0], names => names[1] )
                    .Select( g => new RemotesCollection( g.Key, g.ToArray() ) )
                    .ToDictionary( r => r.FullName );

        static void RestoreRemotesZipAndCreateBareRepositories( NormalizedPath remoteIndexPath, NormalizedPath zipPath, DateTime zipTime )
        {
            // Cleanup "bare/" content if it exists and delete any existing unzipped repositories.
            foreach( var stack in Directory.EnumerateDirectories( _remotesPath ) )
            {
                var stackName = Path.GetFileName( stack.AsSpan() );
                if( stackName.Equals( "bare", StringComparison.OrdinalIgnoreCase ) )
                {
                    foreach( var openedBare in Directory.EnumerateDirectories( stack ) )
                    {
                        DeleteFolder( openedBare );
                    }
                    foreach( var zippedBareOrRemotesIndex in Directory.EnumerateFiles( stack ) )
                    {
                        Throw.Assert( Path.GetFileName( zippedBareOrRemotesIndex ) == "Remotes.txt"
                                      || zippedBareOrRemotesIndex.EndsWith( ".zip" ) );
                        DeleteFile( zippedBareOrRemotesIndex );
                    }
                }
                else
                {
                    foreach( var repository in Directory.EnumerateDirectories( stack ) )
                    {
                        if( !FileHelper.DeleteClonedFolderOnly( TestHelper.Monitor, repository, out var _ ) )
                        {
                            TestHelper.Monitor.Warn( $"Folder '{repository}' didn't contain a .git folder. All folders in Remotes/<stack> should be git working folders." );
                        }
                    }
                }
            }

            // Extracts Remotes.zip content.
            // Disallow overwriting: .gitignore file and README.md must not be in the Zip archive.
            ZipFile.ExtractToDirectory( zipPath, _remotesPath, overwriteFiles: false );
            // Fills the bare/ with the .zip of the bare repositories and creates the Remotes.txt
            // index file.
            var remotesIndex = new StringBuilder();
            Directory.CreateDirectory( _barePath );
            foreach( var stack in Directory.EnumerateDirectories( _remotesPath ) )
            {
                var stackName = Path.GetFileName( stack.AsSpan() );
                if( !stackName.Equals( "bare", StringComparison.OrdinalIgnoreCase ) )
                {
                    var bareStack = Path.Combine( _barePath, new string( stackName ) );
                    foreach( var repository in Directory.EnumerateDirectories( stack ) )
                    {
                        var src = new DirectoryInfo( Path.Combine( repository, ".git" ) );
                        var dst = Path.Combine( bareStack, Path.GetFileName( repository ), ".git" );
                        var target = new DirectoryInfo( dst );
                        FileUtil.CopyDirectory( src, target );
                        using var r = new Repository( dst );
                        r.Config.Set( "core.bare", true );
                        remotesIndex.AppendLine( $"{stackName}/{Path.GetFileName( repository )}" );
                    }
                    ZipFile.CreateFromDirectory( bareStack, bareStack + ".zip" );
                }
            }
            File.WriteAllText( remoteIndexPath, remotesIndex.ToString() );
            File.SetLastWriteTimeUtc( remoteIndexPath, zipTime );
        }
    }

    /// <summary>
    /// Obtains a clean (unmodified) <see cref="RemotesCollection"/> that must exist.
    /// </summary>
    /// <param name="name">The <see cref="IRemotesCollection.Name"/> to use.</param>
    /// <returns>The active remotes collection.</returns>
    public static RemotesCollection OpenRemotes( string name )
    {
        Throw.DebugAssert( _remoteRepositories != null );
        var r = _remoteRepositories[name];
        // Deletes the current repository that may have been modified
        // and extracts a brand new bare git repository.
        var path = _barePath.AppendPart( r.FullName );
        DeleteFolder( path );
        ZipFile.ExtractToDirectory( path + ".zip", path, overwriteFiles: false );
        return r;
    }

    /// <summary>
    /// Called by tests to cleanup their respective "Cloned/&lt;test-name&gt;" where they can clone
    /// the stack they want from the "Remotes".
    /// </summary>
    /// <param name="name">The test name.</param>
    /// <param name="clearStackRegistryFile">True to clear the stack registry (<see cref="StackRepository.ClearRegistry"/>).</param>
    /// <returns>A <see cref="CKliEnv"/> where <see cref="CKliEnv.CurrentDirectory"/> is the dedicated test repository.</returns>
    public static CKliEnv EnsureCleanFolder( [CallerMemberName] string? name = null, bool clearStackRegistryFile = true )
    {
        var path = _clonedPath.AppendPart( name );
        if( Directory.Exists( path ) )
        {
            RemoveAllReadOnlyAttribute( path );
            TestHelper.CleanupFolder( path, ensureFolderAvailable: true );
        }
        else
        {
            Directory.CreateDirectory( path );
        }
        if( clearStackRegistryFile )
        {
            Throw.CheckState( StackRepository.ClearRegistry( TestHelper.Monitor ) );
        }
        return new CKliEnv( path, screen: new StringScreen(), findCurrentStackPath: false );

        static void RemoveAllReadOnlyAttribute( string folder )
        {
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = false,
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.System
            };
            foreach( var f in Directory.EnumerateFiles( folder, "*", options ) )
            {
                File.SetAttributes( f, FileAttributes.Normal );
            }
        }
    }

    public static void ConfigureStackPlugins( CKliEnv context, Action<IActivityMonitor, XElement>? editor = null )
    {
        Throw.CheckArgument( !context.CurrentStackPath.IsEmptyPath );
        Throw.CheckArgument( context.CurrentStackPath.StartsWith( _clonedPath ) );

        // Opens the text world without plugins: there's no <Plugins> yet: this necessarily fails.
        if( !StackRepository.OpenWorldFromPath( TestHelper.Monitor,
                                                context,
                                                out var stack,
                                                out var world,
                                                skipPullStack: true,
                                                withPlugins: false ) )
        {
            Throw.CKException( $"Unable to open test Stack from '{context.CurrentDirectory}'." );
        }

        try
        {
            // Injects Plugins configuration of the host world into the world definition of the world to test.
            world.DefinitionFile.RawEditPlugins( TestHelper.Monitor, ( monitor, plugins ) =>
            {
                plugins.RemoveAll();
                plugins.Add( _hostPluginsConfiguration.Attributes() );
                plugins.Add( _hostPluginsConfiguration.Nodes() );
                editor?.Invoke( monitor, plugins );
            } );
            world.DefinitionFile.SaveFile( TestHelper.Monitor );
            stack.Commit( TestHelper.Monitor, "Updated <Plugins> configuration." );
        }
        finally
        {
            stack.Dispose();
        }
    }


    /// <summary>
    /// Ensures that <see cref="FileHelper.TryMoveFolder(IActivityMonitor, NormalizedPath, NormalizedPath, HashSet{NormalizedPath}?)"/>
    /// succeeds.
    /// </summary>
    /// <param name="from">The folder path to move.</param>
    /// <param name="to">The renamed or moved destination folder path.</param>
    public static void MoveFolder( NormalizedPath from, NormalizedPath to )
    {
        FileHelper.TryMoveFolder( TestHelper.Monitor, from, to ).ShouldBeTrue();
    }

    /// <summary>
    /// Ensures that <see cref="FileHelper.DeleteFolder(IActivityMonitor, string)"/> succeeds.
    /// </summary>
    /// <param name="path">The folder path to delete.</param>
    public static void DeleteFolder( string path )
    {
        FileHelper.DeleteFolder( TestHelper.Monitor, path ).ShouldBeTrue();
    }

    /// <summary>
    /// Ensures that <see cref="FileHelper.DeleteFile(IActivityMonitor, string)"/> succeeds.
    /// </summary>
    /// <param name="path">The file path to delete.</param>
    public static void DeleteFile( string path )
    {
        FileHelper.DeleteFile( TestHelper.Monitor, path ).ShouldBeTrue();
    }
}
