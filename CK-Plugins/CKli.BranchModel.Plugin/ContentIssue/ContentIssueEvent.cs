using CK.Core;
using CKli.Core;
using CKli.ShallowSolution.Plugin;
using LibGit2Sharp;

namespace CKli.BranchModel.Plugin;

/// <summary>
/// Raised by <see cref="BranchModelPlugin.ContentIssue"/> on "ckli issue" command when no branch related issues
/// exist: this is used to check the content of the repositories (more precisely, the content of the
/// <see cref="HotBranch"/> that are active (the ones with a <see cref="HotBranch.GitBranch"/>).
/// </summary>
public sealed partial class ContentIssueEvent : EventMonitoredArgs
{
    readonly ShallowSolutionPlugin _shallowSolution;
    readonly Collector _collector;
    INormalizedFileProvider? _content;
    GitSolution? _gitSolution;
    bool? _gitSolutionResolved;

    internal ContentIssueEvent( IActivityMonitor monitor,
                                HotBranch branch,
                                ShallowSolutionPlugin shallowSolution )
        : base( monitor )
    {
        _collector = new Collector( branch );
        _shallowSolution = shallowSolution;
    }

    /// <summary>
    /// Gets the repository.
    /// </summary>
    public Repo Repo => _collector.Branch.Repo;

    /// <summary>
    /// Gets the hot branch that must be analyzed (<see cref="HotBranch.IsActive"/> is true).
    /// </summary>
    public HotBranch Branch => _collector.Branch;

    /// <summary>
    /// Gets the non null <see cref="HotBranch.GitBranch"/> (because the branch is active).
    /// </summary>
    public Branch GitBranch => _collector.Branch.GitBranch!;

    /// <summary>
    /// Gets the content branch: if it exists, it's the <see cref="HotBranch.GitDevBranch"/> otherwise
    /// the regular <see cref="GitBranch"/> is used.
    /// </summary>
    public Branch GitContentBranch => _collector.GitContentBranch;

    /// <summary>
    /// Gets the content of the <see cref="Branch"/> from <see cref="GitContentBranch"/>.
    /// </summary>
    public INormalizedFileProvider Content => _content ??= _shallowSolution.GetFiles( GitContentBranch.Tip );

    /// <summary>
    /// Gets the <see cref="GitSolution"/> from the <see cref="GitContentBranch"/> if the ".slnx" exists
    /// and can be read without errors (see <see cref="ShallowSolutionPlugin.TryGetShallowSolution"/>).
    /// <para>
    /// A missing ".slnx" is not an error: <paramref name="solution"/> is null and a manual issue is
    /// emitted.
    /// </para>
    /// </summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="solution">Outputs the loaded solution if the ".slnx" file exists and no error occurred.</param>
    /// <returns>True on success (the <paramref name="solution"/> may be null), false on error.</returns>
    public bool TryGetSolution( IActivityMonitor monitor, out GitSolution? solution )
    {
        if( _gitSolutionResolved is null )
        {
            // If an error occurs while 
            if( _shallowSolution.TryGetShallowSolution( monitor, Repo, GitContentBranch, out _gitSolution ) )
            {
                _gitSolutionResolved = true;
                if( _gitSolution == null )
                {
                    _collector.ManualFix( $"Missing '{Repo.DisplayPath.LastPart}.slnx' solution file." );
                }
            }
            else
            {
                _gitSolutionResolved = false;
            }
        }
        solution = _gitSolution;
        return _gitSolutionResolved.Value;
    }

    /// <summary>
    /// Gets the issues collector where content issues must be signaled.
    /// </summary>
    public Collector Issues => _collector;
}
