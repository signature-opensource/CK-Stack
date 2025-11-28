using CK.Core;
using CKli.Core;
using CKli.VersionTag.Plugin;
using CSemVer;
using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace CKli.Build.Plugin;

/// <summary>
/// Concrete base class that can be specialized.
/// The <see cref="RepositoryBuilderPlugin.Create(IActivityMonitor, Repo)"/> acts as an abstract factory:
/// the actual RepoBuilder can differ for each Repo.
/// </summary>
public class RepoBuilder : RepoInfo
{
    readonly LocalStringCache _shaTestRunCache;

    public RepoBuilder( Repo repo, LocalStringCache shaTestRunCache )
        : base( repo )
    {
        _shaTestRunCache = shaTestRunCache;
    }

    public BuildResult Build( IActivityMonitor monitor,
                              CommitBuildInfo buildInfo,
                              bool release,
                              bool? runTest )
    {
        Throw.CheckState( "Repository must not be dirty when calling build.",
                          !Repo.GitRepository.GetSimpleStatusInfo().IsDirty );
        Throw.CheckArgument( buildInfo.Repo == Repo );

        var outputPath = FileUtil.CreateUniqueTimedFolder( Path.GetTempPath() + "CKliBuild", null, DateTime.UtcNow );
        try
        {
            if( DoBuild( monitor, buildInfo.Version, buildInfo.InformationalVersion, release, runTest, outputPath ) )
            {
                var consumedPackages = BuildResult.GetConsumedPackages( monitor, Repo );
                if( consumedPackages != null )
                {
                    var r = new BuildResult( Repo,
                                             buildInfo.Version,
                                             [.. Directory.EnumerateFiles( outputPath ).Select( s => new NormalizedPath( s ) )],
                                             consumedPackages,
                                             outputPath );
                    outputPath = null;
                    return r;
                }
            }
        }
        finally
        {
            if( outputPath != null ) FileHelper.DeleteFolder( monitor, outputPath );
        }
        return BuildResult.Failed;
    }

    /// <summary>
    /// Core Build method.
    /// By default, this calls the <see cref="DotNetBuildTestPack(IActivityMonitor, SVersion, string, bool, bool?, string)"/> helper.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="version">The version to build.</param>
    /// <param name="informationalVersion">The informatinal version to set (see <see cref="CSemVer.InformationalVersion"/>).</param>
    /// <param name="release">False to use Debug build configuration.</param>
    /// <param name="runTest">Whether tests should be run or not.</param>
    /// <param name="outputPath">Destination folder where the artifact files must be created.</param>
    /// <returns>True on success, false otherwise.</returns>
    protected virtual bool DoBuild( IActivityMonitor monitor,
                                    SVersion version,
                                    string informationalVersion,
                                    bool release,
                                    bool? runTest,
                                    NormalizedPath outputPath )
    {
        return DotNetBuildTestPack( monitor, version, informationalVersion, release, runTest, outputPath );
    }

    /// <summary>
    /// Helper that calls <see cref="DotNetBuild(IActivityMonitor, SVersion, string, bool)"/>,
    /// <see cref="DotNetTest(IActivityMonitor, bool?)"/> and <see cref="DotNetPack(IActivityMonitor, string)"/>.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="version">The version to build.</param>
    /// <param name="informationalVersion">The informatinal version to set (see <see cref="CSemVer.InformationalVersion"/>).</param>
    /// <param name="release">False to use Debug build configuration.</param>
    /// <param name="runTest">Whether tests should be run or not.</param>
    /// <param name="outputPath">Destination folder where the artifact files must be created.</param>
    /// <returns>True on success, false otherwise.</returns>
    protected virtual bool DotNetBuildTestPack( IActivityMonitor monitor,
                                                SVersion version,
                                                string informationalVersion,
                                                bool release,
                                                bool? runTest,
                                                string outputPath )
    {
        return DotNetBuild( monitor, version, informationalVersion, release )
               && DotNetTest( monitor, runTest )
               && DotNetPack( monitor, outputPath );
    }

    /// <summary>
    /// Helper that calls "dotnet build".
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="version">The version to build.</param>
    /// <param name="informationalVersion">The informatinal version to set (see <see cref="CSemVer.InformationalVersion"/>).</param>
    /// <param name="release">False to use Debug build configuration.</param>
    /// <returns>True on success, false otherwise.</returns>
    protected bool DotNetBuild( IActivityMonitor monitor, SVersion version, string informationalVersion, bool release )
    {
        return BuildPlugin.RunDotnet( monitor, Repo, $"""
            build -tl:off --nologo -c {(release ? "Release" : "Debug")} /p:Version={version};InformationalVersion="{informationalVersion}"
            """ );
    }

    /// <summary>
    /// Helper that calls "dotnet test" and handles the <paramref name="runTest"/> to skip the
    /// tests if they have aleady run.
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="runTest">Whether tests should be run or not.</param>
    /// <returns>True on success, false otherwise.</returns>
    protected bool DotNetTest( IActivityMonitor monitor, bool? runTest )
    {
        string testKey = Repo.GitRepository.Repository.Head.Tip.Tree.Sha;
        runTest ??= !_shaTestRunCache.Contains( monitor, testKey );
        if( runTest is true )
        {
            if( !BuildPlugin.RunDotnet( monitor, Repo, $"test -tl:off --nologo --no-build" ) )
            {
                return false;
            }
            _shaTestRunCache.Add( monitor, testKey );
        }
        return true;
    }

    /// <summary>
    /// Helper that calls "dotnet pack".
    /// </summary>
    /// <param name="monitor">The monitor to use.</param>
    /// <param name="outputPath">Destination folder where the artifact files must be created.</param>
    /// <returns>True on success, false otherwise.</returns>
    protected bool DotNetPack( IActivityMonitor monitor, string outputPath )
    {
        return BuildPlugin.RunDotnet( monitor, Repo, $"""pack -tl:off --nologo --no-build -o "{outputPath}" """ );
    }


}


