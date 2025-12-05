using CK.Core;
using CKli.Build.Plugin;
using CKli.Core;
using CKli.VersionTag.Plugin;
using CSemVer;
using LibGit2Sharp;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using LogLevel = CK.Core.LogLevel;

namespace CKli.Net8Migration.Plugin;

sealed class CreateLTSHelper
{
}

public sealed class Net8MigrationPlugin : PrimaryPluginBase
{
    readonly VersionTagPlugin _versionTag;
    readonly BuildPlugin _build;

    public Net8MigrationPlugin( PrimaryPluginContext primaryContext,
                                VersionTagPlugin versionTag,
                                BuildPlugin build )
        : base( primaryContext )
    {
        _versionTag = versionTag;
        _build = build;
    }

    [Description( "Migrate Net8 stack." )]
    [CommandPath( "migrate net8" )]
    public bool Migrate( IActivityMonitor monitor, CKliEnv context )
    {
        var repos = World.GetAllDefinedRepo( monitor );
        if( repos == null ) return false;

        //if( !Pull( monitor, repos ) ) return false;

        // If we already run this once, the following check is not useless
        // if we want to recompute the MinVersion: we need the RepositoryInfo.xml.
        if( !SetMasterAndCheckDevelopIsMerged( monitor, repos ) ) return false;

        // We must computed the MinVersion before removing the RepositoryInfo.xml
        // (so before switching to stable if it has already been created).
        InitializeMinVersion( monitor, repos, _versionTag );

        // Ensure the stable branch and checkout: head is now stable.
        foreach( var repo in repos )
        {
            var stable = repo.GitRepository.EnsureBranch( monitor, "stable" );
            Commands.Checkout( repo.GitRepository.Repository, stable );
        }
        
        var snapshotStables = repos.ToDictionary( r => r, r =>
        {
            Throw.CheckState( !r.GitRepository.GetSimpleStatusInfo().IsDirty );
            Throw.CheckState( r.GitRepository.Repository.Head.FriendlyName == "stable" );
            return r.GitRepository.Repository.Head.Tip;
        } );

        // Here we should also update the DirectoryInfo.props...
        if( !RemoveRepositoryInfoAndCodeCakeBuilder( monitor, repos ) ) return false;

        bool success = true;
        foreach( var repo in repos )
        {
            // This does nothing when no change.
            success &= repo.GitRepository.Commit( monitor, "Initialize stable branch without RepositoryInfo.xml and CodeCakeBuilder." ) is not CommitResult.Error;
            // Fix the tags.
            success &= _build.FixVersionTagIssues( monitor, context, _versionTag.Get( monitor, repo ) );
        }

        // We are ready to work... Still in Net8 but on stable branch.

        // Now we can fix the tags after having computed the MinVersion.
        //===> Fix issues... But currently we cannot run 'ckli issue --fix' from the inside.

        // Restore stable.
        //foreach( var (repo, commit) in snapshotStables )
        //{
        //    Throw.CheckState( repo.GitRepository.Repository.Head.FriendlyName == "stable" );
        //    repo.GitRepository.Repository.Reset( ResetMode.Hard, commit, new CheckoutOptions { CheckoutModifiers = CheckoutModifiers.Force } );
        //}

        return success;
    }

    private static bool Pull( IActivityMonitor monitor, IReadOnlyList<Repo> repos )
    {
        bool success = true;
        foreach( var repo in repos )
        {
            success &= repo.Pull( monitor ).IsSuccess();
        }
        return success;
    }


    private static bool SetMasterAndCheckDevelopIsMerged( IActivityMonitor monitor, IReadOnlyList<Repo> repos )
    {
        bool success = true;
        foreach( var repo in repos )
        {
            success &= repo.SetCurrentBranch( monitor, "master" );
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
        return success;
    }

    static bool RemoveRepositoryInfoAndCodeCakeBuilder( IActivityMonitor monitor, IReadOnlyList<Repo> repos )
    {
        bool success = true;
        foreach( var repo in repos )
        {
            var repoXml = repo.WorkingFolder.AppendPart( "RepositoryInfo.xml" );
            success &= FileHelper.DeleteFile( monitor, repoXml );
            var ccbPath = repo.WorkingFolder.AppendPart( "CodeCakeBuilder" );
            success &= FileHelper.DeleteFolder( monitor, ccbPath );
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
                if( !vB.IsValid ) vB = null;
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
                if( !vX.IsValid ) vX = null;
            }
            SVersion? min = vB;
            if( vX > vB ) min = vX;
            min ??= CSVersion.FirstPossibleVersions[0];
            min = SVersion.Create( min.Major, min.Minor, min.Patch );

            details.AppendLine( $"==> MinVersion = {min}" );

            versionTag.SetMinVersion( monitor, repo, min );
        }
        monitor.Info( details.ToString() );
    }
}
