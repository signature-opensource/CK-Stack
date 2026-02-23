using CK.Core;
using CKli.BranchModel.Plugin;
using CKli.Build.Plugin;
using CKli.Core;
using CKli.VersionTag.Plugin;
using CSemVer;
using LibGit2Sharp;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using LogLevel = CK.Core.LogLevel;

namespace CKli.Net8Migration.Plugin;

public sealed class Net8MigrationPlugin : PrimaryPluginBase
{
    readonly VersionTagPlugin _versionTag;
    readonly BranchModelPlugin _branchModel;
    readonly BuildPlugin _build;

    public Net8MigrationPlugin( PrimaryPluginContext primaryContext,
                                VersionTagPlugin versionTag,
                                BranchModelPlugin branchModel,
                                BuildPlugin build )
        : base( primaryContext )
    {
        _versionTag = versionTag;
        _branchModel = branchModel;
        branchModel.OnFixStart.Sync += OnFixStart;
        _build = build;
    }

    void OnFixStart( IActivityMonitor monitor, FixWorkflowStartEventArgs e )
    {
        // Skip restarts (done on the initial start).
        if( e.RestartingWorkflow )
        {
            return;
        } 
        using var _ = monitor.OpenInfo( "Applying Net8 migrations." );
        foreach( var target in e.Targets )
        {
            using( monitor.OpenInfo( $"On '{target}'." ) )
            {
                var repo = target.Repo;
                var b = repo.GitRepository.Repository.Branches[target.BranchName];
                Throw.CheckState( "The branch necessarily exists.", b != null );
                // If the branch has a CodeCakeBuilder or RepositoryInfo.xml entry, it needs
                // to be updated: this quick check avoids useless work.
                if( b.Tip["CodeCakeBuilder"] != null || b.Tip["RepositoryInfo.xml"] != null )
                {
                    if( repo.GitRepository.Checkout( monitor, b )
                        && RemoveRepositoryInfoAndCodeCakeBuilderAndSlnx( monitor, repo ) )
                    {
                        repo.GitRepository.Commit( monitor, "Net8 migration applied." );
                    }
                    else
                    {
                        monitor.Error( $"Error while normalizing files and folders for '{repo.DisplayPath}/{target.BranchName}'." );
                    }
                }
            }
        }
    }

    [Description( "Migrate Net8 stack." )]
    [CommandPath( "migrate net8" )]
    public bool Migrate( IActivityMonitor monitor,
                         bool resetAll = false,
                         bool checkDevelop = false )
    {
        if( resetAll )
        {
            using( monitor.OpenInfo( "Deleting all existing repo." ) )
            {
                foreach( var f in Directory.EnumerateDirectories( World.Name.WorldRoot ) )
                {
                    if( Path.GetFileName( f ) != StackRepository.PublicStackName )
                    {
                        if( !FileHelper.DeleteFolder( monitor, f ) )
                        {
                            return false;
                        }
                    }
                }
            }
        }
        // This will fix the layout by re-cloning all the repos.
        var repos = World.GetAllDefinedRepo( monitor );
        if( repos == null ) return false;

        // If we already run this once, the following check is not useless:
        // if we want to recompute the MinVersion, we need the RepositoryInfo.xml.
        if( !SetMasterAndCheckDevelopIsMerged( monitor, repos, checkDevelop ) ) return false;

        // We must computed the MinVersion before removing the RepositoryInfo.xml
        // (so before switching to stable if it has already been created).
        InitializeMinVersion( monitor, repos, _versionTag );

        // Ensure the stable branch and checkout: head is now stable.
        foreach( var repo in repos )
        {
            var stable = repo.GitRepository.EnsureBranch( monitor, "stable" );
            Commands.Checkout( repo.GitRepository.Repository, stable );
        }

        // Here we should also update the Directory.Build.props...
        if( !RemoveRepositoryInfoAndCodeCakeBuilderAndSlnx( monitor, repos ) ) return false;

        bool success = true;
        foreach( var repo in repos )
        {
            // This does nothing when no change (no new commit).
            success &= repo.GitRepository.Commit( monitor,
                                                  "Initialize stable branch with slnx and no RepositoryInfo.xml nor CodeCakeBuilder.",
                                                  CommitBehavior.CreateNewCommit ) is not CommitResult.Error;
        }

        // We are ready to work... Still in Net8 but on stable branch.

        return success;
    }

    static bool SetMasterAndCheckDevelopIsMerged( IActivityMonitor monitor, IReadOnlyList<Repo> repos, bool checkDevelop )
    {
        bool success = true;
        foreach( var repo in repos )
        {
            success &= repo.GitRepository.FullCheckout( monitor, "master", skipFetchMerge: true );
            if( checkDevelop )
            {
                var dev = repo.GitRepository.GetBranch( monitor, "develop", missingLocalAndRemote: LogLevel.Error );
                if( dev == null )
                {
                    success = false;
                }
                else
                {
                    var master = repo.GitRepository.Repository.Head;
                    var div = repo.GitRepository.Repository.ObjectDatabase.CalculateHistoryDivergence( master.Tip, dev.Tip );
                    if( div.CommonAncestor != dev.Tip && dev.Tip.Tree.Sha != master.Tip.Tree.Sha )
                    {
                        monitor.Error( $"The 'develop' branch is not merged in 'master'." );
                        success = false;
                    }
                }
            }
        }
        return success;
    }

    static bool RemoveRepositoryInfoAndCodeCakeBuilderAndSlnx( IActivityMonitor monitor, IReadOnlyList<Repo> repos )
    {
        bool success = true;
        foreach( var repo in repos )
        {
            success &= RemoveRepositoryInfoAndCodeCakeBuilderAndSlnx( monitor, repo );
        }
        return success;
    }

    static bool RemoveRepositoryInfoAndCodeCakeBuilderAndSlnx( IActivityMonitor monitor, Repo repo )
    {
        bool success = true;
        var repoXml = repo.WorkingFolder.AppendPart( "RepositoryInfo.xml" );
        success &= FileHelper.DeleteFile( monitor, repoXml );
        var slnPath = repo.WorkingFolder.AppendPart( repo.WorkingFolder.LastPart + ".sln" );
        if( File.Exists( slnPath ) )
        {
            if( ProcessRunner.RunProcess( monitor.ParallelLogger, "dotnet", "sln migrate", repo.WorkingFolder ) == 0 )
            {
                FileHelper.DeleteFile( monitor, slnPath );
            }
            else
            {
                success = false;
            }
        }
        // Idempotent.
        success &= ProcessRunner.RunProcess( monitor.ParallelLogger,
                                             "dotnet",
                                             "sln remove CodeCakeBuilder/CodeCakeBuilder.csproj",
                                             repo.WorkingFolder ) == 0;
        var ccbPath = repo.WorkingFolder.AppendPart( "CodeCakeBuilder" );
        success &= FileHelper.DeleteFolder( monitor, ccbPath );

        // Remove legacy Common/SharedKey.snk if it exists.
        var slnxPath = slnPath + 'x';
        var d = XDocument.Load( slnxPath );
        d.Root!.Descendants( "File" ).Where( e => e.Attribute( "Path" )?.Value == "RepositoryInfo.xml"
                                                 || e.Attribute( "Path" )?.Value == "Common/SharedKey.snk" ).Remove();
        XmlHelper.SaveWithoutXmlDeclaration( d, slnxPath );

        var nugetConfigPath = repo.WorkingFolder.AppendPart( "nuget.config" );
        if( File.Exists( nugetConfigPath ) )
        {
            var nuget = XDocument.Load( nugetConfigPath );
            // This doesn't remove the "local-feed" (that shouldn't exist) but initializes
            // the <packageSourceMapping> from the existing <packageSources>.
            NuGetHelper.SetOrRemoveNuGetSource( monitor, nuget, "local-feed", null );
            XmlHelper.SaveWithoutXmlDeclaration( nuget, nugetConfigPath );
        }

        // Loads and saves the .csproj, .props and .targets to "normalize" them (no Xml declaration, no BOM)
        // once for all.
        foreach( var f in Directory.EnumerateFiles( repo.WorkingFolder, "*", SearchOption.AllDirectories ) )
        {
            var ext = Path.GetExtension( f );
            if( ext == ".csproj" || ext == ".props" || ext == ".targets" )
            {
                try
                {
                    XmlHelper.SaveWithoutXmlDeclaration( XDocument.Load( f, LoadOptions.PreserveWhitespace ), f );
                }
                catch { }
            }
        }
        return success;
    }

    void InitializeMinVersion( IActivityMonitor monitor, IReadOnlyList<Repo> repos, VersionTagPlugin versionTag )
    {
        // Net8 specifics: we must compute the MinVersion because we are not coming from the current
        // model where the MinVersion is computed when a LTS is created.
        //
        // ==> See VersionTagPlugin.ComputeRepoLTSVersions
        //
        // Once the Net8 -> Net10 migration is done (and the LTS for Net8 exists), we won't need this.

        // To "infer" the MinVersion:
        // - The current "master/Repository.xml" file may contain a <SimpleGitVersion StartingVersion="">
        // - if master-Net6 exists, the first version tag on or below gives us the last Net6 version.
        // If both exists, we take the biggest one.
        //
        var details = new StringBuilder( "Computing MinVersion." );
        foreach( var repo in repos )
        {
            details.AppendLine( repo.DisplayPath );
            SVersion? vB = null;
            var bStartNet6 = repo.GitRepository.GetBranch( monitor, "master-Net6" );
            if( bStartNet6 != null )
            {
                var d = repo.GitRepository.Repository.Describe( bStartNet6.Tip, new DescribeOptions { Strategy = DescribeStrategy.Tags } );
                vB = SVersion.TryParse( d );
                details.AppendLine( $"[B] - {d} - {vB}" );
                if( vB.IsValid ) vB = SVersion.Create( vB.Major, vB.Minor, vB.Patch + 1 );
                else vB = null;
            }
            SVersion? vX = null;
            var file = repo.WorkingFolder.AppendPart( "RepositoryInfo.xml" );
            if( File.Exists( file ) )
            {
                var d = XDocument.Load( file ).Root?
                                     .Element( "SimpleGitVersion" )?
                                     .Attribute( "StartingVersion" )?
                                     .Value;
                vX = SVersion.TryParse( d );
                details.AppendLine( $"[X] - {d} - {vX}" );
                if( vX.IsValid ) vX = SVersion.Create( vX.Major, vX.Minor, vX.Patch );
                else vX = null;
            }
            SVersion? min = vB;
            if( vX > vB ) min = vX;
            if( min == null ) min = SVersion.Create( 0, 0, 0 );

            details.AppendLine( $"==> MinVersion = {min}" );

            versionTag.SetMinVersion( monitor, repo, min );
        }
        monitor.Info( details.ToString() );
    }
}
