# Workflows

## Fix

1. `cd <repo>`
2. `ckli fix start vMajor[.Minor]`
3. develop, test, commit (in the `fix/vMajor.Minor` branch).
4. `ckli fix publish`
5. Or `ckli fix cancel`

Starting a fix creates a "fix context" for the World that contains the impacts of the
fix on the downstream repositories: one "fix/vMajor.Minor" branch is created in the
origin `<repo>` and one or more "fix/vMajor.Minor" are created in every repository (recursively)
that have non deprecated released versions that must be updated.

This fix context is unique: the fix must be published or canceled before another fix is started.
Canceling a fix preserves the work that may have been done in the different repositories, the branches
with their commits if any are left as-is and the same fix can be restarted anytime.

Publishing the fix compiles, tests and propagates the fixed packages
recursively to the fix branches of the downstream repositories.

If the build fails, the downstream repositories are restored. No
version tags appear and no build artifacts are left in the `$Local/`
folder.

To be able to work in downstream repositories (to investigate issues or
fixing code impacted by the fix), then the `ckli fix build` command should be used.

This produces short-lived prerelease versions of the fix that are kept
alive locally and replaced by subsequent prerelease (and the final fix).
Theses local prerelease versions follow the pattern "Major.Minor.Patch-local.fix.CommitDepth"
and cannot be published.








   1. 1. Go to the repository that produces the package.
2. Creates the `fix/` branch for the version to fix (`ckli branch fix v3.1` or `ckli branch fix v3`).
3. The branch `fix/v3.1` is created or moved to a commit that is based
on the last patch for the Major.Minor and the branch is checked out.
4. Develop and test the fix in the branch.
1. 
