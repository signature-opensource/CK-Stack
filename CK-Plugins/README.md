# Workflows

## Fix

1. `cd <repo>`
2. `ckli fix start vMajor[.Minor]`
3. develop, test, commit (in the `fix/vMajor.Minor` branch).
4. `ckli fix build`
5. `ckli fix publish`

If the build fails, the downstream repositories are restored. No
version tags appear and no build artifacts are left in the `$Local/`
folder.

To be able to work in downstream repositories (to investigate issues or
fixing code impacted by the fix), then the `--local` flag should be used.

4. `ckli fix build --local`

This produces short-lived prerelease versions of the fix that are kept
alive locally and replaced by subsequent prerelease (and the final fix).
Theses local prerelease versions follow the pattern "Major.Minor.Patch-local.CommitDepth"
and cannot be published.


## Complex fix







   1. 1. Go to the repository that produces the package.
2. Creates the `fix/` branch for the version to fix (`ckli branch fix v3.1` or `ckli branch fix v3`).
3. The branch `fix/v3.1` is created or moved to a commit that is based
on the last patch for the Major.Minor and the branch is checked out.
4. Develop and test the fix in the branch.
1. 
