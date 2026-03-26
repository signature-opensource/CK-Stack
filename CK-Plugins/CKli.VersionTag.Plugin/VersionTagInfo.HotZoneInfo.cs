using CK.Core;
using CKli.Core;
using CSemVer;

namespace CKli.VersionTag.Plugin;

public sealed partial class VersionTagInfo
{
    /// <summary>
    /// Captures the "hot zone" version tags.
    /// </summary>
    public sealed class HotZoneInfo
    {
        readonly TagCommit _topHot;
        readonly TagCommit _lastStable;
        readonly World.Issue? _hotZoneIssue;
        readonly TagCommit? _lastAvailableStable;

        HotZoneInfo( TagCommit lastStable, TagCommit topHot, World.Issue? hotZoneIssue, TagCommit? lastAvailableStable )
        {
            _lastStable = lastStable;
            _topHot = topHot;
            _hotZoneIssue = hotZoneIssue;
            _lastAvailableStable = lastAvailableStable;
        }

        internal static HotZoneInfo Create( IActivityMonitor monitor, World world, Repo repo, TagCommit lastStable, TagCommit topHot, TagCommit? lastAvailableStable )
        {
            World.Issue? hotZoneIssue = null;
            var hotSupremum = SVersion.Create( topHot.Version.Major + 1, 0, 0 );
            if( topHot.Version >= hotSupremum )
            {
                var message = $"""
                              The greatest version tag '{topHot.Version.ParsedText}' cannot be greater or equal to 'v{lastStable.Version.Major + 1
                              }.0.0' because the last stable version is '{lastStable.Version.ParsedText}'.
                              This should be fixed manually.
                              """;

                monitor.Warn( message );
                hotZoneIssue = World.Issue.CreateManual( "Hot zone issue detected.", world.ScreenType.Text( message ), repo );
            }
            return new HotZoneInfo( lastStable, topHot, hotZoneIssue, lastAvailableStable );
        }

        /// <summary>
        /// Gets a manual issue if <see cref="TopHot"/> is greater or equal to the next major of the last stable version.
        /// </summary>
        public World.Issue? HotZoneIssue => _hotZoneIssue;

        /// <summary>
        /// Gets whether this hot zone is empty (the top hot version is the same as the last stable version).
        /// <para>
        /// This is not an issue: the repository has no pending pre-release version nor CI build.
        /// </para>
        /// </summary>
        public bool IsEmpty => _lastStable == _topHot;

        /// <summary>
        /// Gets the top tag commit. This is greater or equal to <see cref="LastStable"/>.
        /// </summary>
        public TagCommit TopHot => _topHot;

        /// <summary>
        /// Gets the last stable version: this is the common ancestor of the "hot zone" where branch model applies.
        /// <para>
        /// This can be a "+fake" or a "+deprecated" version (<see cref="TagCommit.IsRegularVersion"/> can be false) and
        /// <see cref="TagCommit.BuildContentInfo"/> may be null. When this happens, the  
        /// </para>
        /// </summary>
        public TagCommit LastStable => _lastStable;

        /// <summary>
        /// Gets the last stable commit that has a non null, known, <see cref="TagCommit.BuildContentInfo"/>.
        /// <para>
        /// This is most often <see cref="LastStable"/> but if we are in the corner case
        /// where <see cref="LastStable"/> is "+fake" or "+deprecated", then this is a fallback to use when
        /// a current/previous content needs to be obtained.
        /// </para>
        /// <para>
        /// This can be null: it means that all the released tags up to the <see cref="VersionTagInfo.MinVersion"/> (that necessarily
        /// exists if this HotZone is available) have been deprecated. Highly improbable though...
        /// </para>
        /// </summary>
        public TagCommit? LastAvailableStable => _lastAvailableStable;
    }

}

