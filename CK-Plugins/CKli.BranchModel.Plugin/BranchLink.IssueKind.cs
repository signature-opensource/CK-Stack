namespace CKli.BranchModel.Plugin;

sealed partial class BranchLink
{
    /// <summary>
    /// Describes issues for <see cref="BranchLink"/>.
    /// </summary>
    public enum IssueKind
    {
        /// <summary>
        /// The <see cref="Ahead"/> doesn't exist or is ahead of <see cref="Branch"/>.
        /// </summary>
        None,

        /// <summary>
        /// The <see cref="Ahead"/> should be removed.
        /// </summary>
        Useless,

        /// <summary>
        /// <see cref="Ahead"/> exists but is not related to <see cref="Branch"/>.
        /// This is a strange issue that must be fixed manually.
        /// </summary>
        Unrelated,

        /// <summary>
        /// <see cref="Ahead"/> is behind <see cref="Branch"/> and should be rebased.
        /// </summary>
        Desynchronized
    }
}
