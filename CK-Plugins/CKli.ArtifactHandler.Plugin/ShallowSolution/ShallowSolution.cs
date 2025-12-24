using CK.Core;
using CKli.Core;
using LibGit2Sharp;
using Microsoft.VisualStudio.SolutionPersistence.Model;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;

namespace CKli.ArtifactHandler.Plugin;

public sealed class ShallowSolution
{
    readonly string _path;
    readonly Repo _repo;
    readonly Commit _commit;
    readonly Tree _tree;
    SolutionModel? _solution;
    BuildContentInfo? _contentInfo;

    public ShallowSolution( Repo repo, Commit commit )
    {
        Throw.CheckArgument( ((IBelongToARepository)commit).Repository == repo.GitRepository.Repository );
        _repo = repo;
        _commit = commit;
        _tree = commit.Tree;
        _path = Path.Combine( Path.GetTempPath(), Path.GetRandomFileName() );
        Directory.CreateDirectory( _path );
    }

    public SolutionModel? EnsureSolution( IActivityMonitor monitor )
    {
        if( _solution == null )
        {
            try
            {
                var slnPath = Checkout( monitor, _repo.DisplayPath.LastPart + ".slnx" );
                if( slnPath == null )
                {
                    return null;
                }
                var serializer = Microsoft.VisualStudio.SolutionPersistence.Serializer.SolutionSerializers.SlnXml;
                var sln = serializer.OpenAsync( slnPath, default ).Result;
                bool success = true;
                foreach( var project in sln.SolutionProjects )
                {
                    success &= Checkout( monitor, project.FilePath ) != null;
                }
                if( success )
                {
                    success &= OptionalCheckout( monitor, "Directory.Build.props", out _ )
                               && OptionalCheckout( monitor, "Directory.Packages.props", out _ );
                }
                if( success )
                {
                    _solution = sln;
                }
            }
            catch( Exception ex )
            {
                monitor.Error( $"Error while shallow checkout of '{_repo.DisplayPath}' on '{_commit}'.", ex );
            }
        }
        return _solution;
    }

    public BuildContentInfo? ShallowBuild( IActivityMonitor monitor )
    {
        if( _contentInfo == null )
        {
            if( EnsureSolution( monitor ) == null
                || !RunPack( monitor, _path, out var produced )
                || !RunPackageList( monitor, this, out var consumed ) )
            {
                return null;
            }
            _contentInfo = new BuildContentInfo( consumed, produced, [] );
        }
        return _contentInfo;

        static bool RunPack( IActivityMonitor monitor, string path, out ImmutableArray<string> packageIdentifiers )
        {
            var result = ImmutableArray.CreateBuilder<string>();
            using( monitor.OpenInfo( $"Executing 'dotnet pack' on core solution files." ) )
            {
                var e = ProcessRunner.RunProcess( monitor.ParallelLogger,
                                                  "dotnet",
                                                  """pack -tl:off --nologo -o "PackOut" """,
                                                  path );
                if( e != 0 )
                {
                    monitor.CloseGroup( $"Failed with code '{e}'." );
                    packageIdentifiers = [];
                    return false;
                }
                foreach( var a in Directory.EnumerateFiles( Path.Combine( path, "PackOut" ) ) )
                {
                    var fileName = Path.GetFileName( a.AsSpan() );
                    var ext = Path.GetExtension( fileName );
                    if( ext.Equals( ".nupkg", StringComparison.Ordinal ) )
                    {
                        result.Add( new string( fileName ) );
                    }
                    else
                    {
                        monitor.Warn( $"Unexpected file '{fileName}'. Ignored." );
                    }
                }
                result.Sort( StringComparer.Ordinal );
                packageIdentifiers = result.DrainToImmutable();
            }
            return true;
        }

        static bool RunPackageList( IActivityMonitor monitor, ShallowSolution folder, out ImmutableArray<NuGetPackageInstance> packages )
        {
            var stdOut = new StringBuilder();
            using( monitor.OpenInfo( $"Executing 'dotnet package list' on core solution files." ) )
            {
                var e = ProcessRunner.RunProcess( monitor.ParallelLogger,
                                                  "dotnet",
                                                  "package list --format json --no-restore",
                                                  folder._path );
                if( e != 0 )
                {
                    monitor.CloseGroup( $"Failed with code '{e}'." );
                    packages = [];
                    return false;
                }
            }
            return NuGetPackageInstance.ReadConsumedPackages( monitor, stdOut.ToString(), folder, out packages );
        }
    }

    string? Checkout( IActivityMonitor monitor, string path )
    {
        TreeEntry entry = _tree[path];
        if( entry == null )
        {
            int idx = path.IndexOf( '\\' );
            if( idx >= 0 ) path = path.Replace( '\\', '/');
            entry = _tree[path];
            if( entry == null )
            {
                monitor.Error( $"""
                    Unable to find '{path}' in '{_repo.DisplayPath}' on '{_commit}'. Tree contains {_tree.Count} files.
                    """ );
                return null;
            }
        }
        return Checkout( monitor, entry );
    }

    bool OptionalCheckout( IActivityMonitor monitor, string path, out string? tempPath )
    {
        var entry = _tree[path];
        if( entry == null )
        {
            tempPath = null;
            return true;
        }
        tempPath = Checkout( monitor, entry );
        return tempPath != null;
    }

    string? Checkout( IActivityMonitor monitor, TreeEntry entry )
    {
        if( entry.Target is not Blob b )
        {
            monitor.Error( $"Invalid target type '{entry.TargetType}' for '{entry.Path}' in '{_repo.DisplayPath}' on '{_commit}'." );
            return null;
        }
        var targetPath = Path.Combine( _path, entry.Path );
        var targetDirectory = Path.GetDirectoryName( targetPath );
        Directory.CreateDirectory( targetDirectory! );
        using( var content = b.GetContentStream() )
        using( var target = new FileStream( targetPath, FileMode.Create,
                                                        FileAccess.Write,
                                                        FileShare.None,
                                                        4096,
                                                        FileOptions.SequentialScan ) )
        {
            content.CopyTo( target );
        }
        return targetPath;
    }

    public void Destroy( IActivityMonitor monitor )
    {
        FileHelper.DeleteFolder( monitor, _path );
    }

    public override string ToString() => $"Shallow solution of '{_repo.DisplayPath}' on '{_commit}'";
}
