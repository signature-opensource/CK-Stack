using CKli.BranchModel.Plugin;
using System;

namespace CKli.Build.Plugin;

public sealed partial class Roadmap
{
    /// <summary>
    /// Qualifies the reason to build a repository.
    /// </summary>
    [Flags]
    public enum MustBuildReason
    {
        /// <summary>
        /// No build required.
        /// </summary>
        None = 0,

        /// <summary>
        /// An upstream repository must be built.
        /// </summary>
        UpstreamBuild = 1,

        /// <summary>
        /// An upstream repository has been built, the solution must be built.
        /// </summary>
        UpstreamVersion = 2,

        /// <summary>
        /// The <see cref="HotGraph.SolutionVersionInfo.GetLastBuild(bool)"/> has a "+fake" version.
        /// </summary>
        FakeVersion = 4,

        /// <summary>
        /// The <see cref="HotGraph.SolutionVersionInfo.GetLastBuild(bool)"/> has a "+deprecated" version.
        /// </summary>
        DeprecatedVersion = 8,

        /// <summary>
        /// One or more package dependencies must be updated.
        /// </summary>
        DependencyUpdate = 16,

        /// <summary>
        /// Code changed.
        /// </summary>
        CodeChange = 32,
    }

}

