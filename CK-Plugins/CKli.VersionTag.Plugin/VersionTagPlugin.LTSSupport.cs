using CK.Core;
using CKli.Core;
using CSemVer;
using System.Collections.Generic;
using System.Text;

namespace CKli.VersionTag.Plugin;

public sealed partial class VersionTagPlugin
{
    /// <summary>
    /// Defines the configuration of a LTS World and the current default World. 
    /// </summary>
    /// <param name="Repo">The Repo.</param>
    /// <param name="LTSMinVersion">The MinVersion for this Repo in the LTS world.</param>
    /// <param name="LTSMaxVersion">The MaxVersion for this Repo in the LTS world.</param>
    /// <param name="NextMinVersion">The future <see cref="VersionTagInfo.MinVersion"/> for this Repo in the default World.</param>
    public sealed record RepoLTSVersion( Repo Repo, SVersion LTSMinVersion, SVersion LTSMaxVersion, SVersion NextMinVersion );

    /// <summary>
    /// Computes the <see cref="RepoLTSVersion"/> that must be used to configure a new LTS World (and the <see cref="VersionTagInfo.MinVersion"/>
    /// of the current World.
    /// <para>
    /// This can only be called on the default World.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <returns>The <see cref="RepoLTSVersion"/> indexed by Repo.</returns>
    public Dictionary<Repo, RepoLTSVersion>? ComputeRepoLTSVersions( IActivityMonitor monitor )
    {
        Throw.CheckState( World.Name.IsDefaultWorld );
        var repos = World.GetAllDefinedRepo( monitor );
        if( repos == null ) return null;

        var result = new Dictionary<Repo, RepoLTSVersion>( repos.Count );
        foreach( var repo in repos )
        {
            var versions = Get( monitor, repo );
            Throw.CheckState( versions.LastStables.Count > 0 );
            SVersion maxLTS;
            SVersion minNext;
            var max = versions.LastStables[0].Version;
            if( max.Major > 0 )
            {
                maxLTS = SVersion.Create( max.Major, CSVersion.MaxMinor, CSVersion.MaxPatch );
                minNext = SVersion.Create( max.Major + 1, 0, 0 );
            }
            else
            {
                maxLTS = SVersion.Create( max.Major, max.Minor, CSVersion.MaxPatch );
                minNext = SVersion.Create( max.Major, max.Minor + 1, 0 );
            }
            // We need a minLTS version.
            //  - If this Repo is a new one, the minLTS is the "v0.0.0" first possible release version
            //    that is the default MinVersion.
            //  - If this Repo has been created in a previous LTS, its minLTS is the current MinVersion.
            var minLTS = versions.MinVersion;

            result.Add( repo, new RepoLTSVersion( repo, minLTS, maxLTS, minNext ) );
        }

        return result;
    }

}
