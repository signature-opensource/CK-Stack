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
        /// This can only be combined with <see cref="CodeChange"/>.
        /// </summary>
        Upstream = 1,

        /// <summary>
        /// The <see cref="HotGraph.SolutionVersionInfo.VersionMustBuild"/> is true.
        /// This can only be combined with <see cref="CodeChange"/>.
        /// </summary>
        Version = 2,

        /// <summary>
        /// One or more package dependencies must be updated.
        /// This can only be combined with <see cref="CodeChange"/>.
        /// </summary>
        DependencyUpdate = 4,

        /// <summary>
        /// Code changed.
        /// This can be combined with any other reasons.
        /// </summary>
        CodeChange = 8
    }

}

