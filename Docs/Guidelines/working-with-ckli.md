# Working with ckli

Rules for building and testing the repositories of this stack with ckli. They apply to people and to coding agents alike.

## Builds

- **`ckli build --focus` from the most downstream repository of a change**: it builds the current repository and the
  upstream ones that need a build, and stops (the consumers only with `--continue`). `--dry-run` first: the numbered lines
  before "'--focus' stops before the N other builds" are what will build.
- **ckli refuses to build when any repository of the World is dirty**, not only the ones it builds: commit (or stash) first.
- **Never edit a repository while it builds:** its "Updated dependencies" commit stages everything in the working tree.
- **ckli restores the working trees after the tests:** the files that the tests regenerate are not kept by a ckli build.
- **Push with `ckli push`**, never a `git push` per repository.

## Package versions

- **A repository references one version per package identifier** (ckli refuses two). A version set by hand (a new
  reference, a newer version) is set in every project of the repository that references the package. For a new
  reference, the simplest is the version that the repository already references for the packages of the same version
  line: `ckli build` updates them all.
- **Between two ckli builds**, a repository restores the freshly built packages with a temporary nuget config that adds
  the local feed (`$Local/NuGet` of the Stack): `dotnet restore --configfile <it>`. The repository's own `nuget.config`
  is never modified.

## Tests

- **A stale `$StObjGen/G0.cs`** (the generated code of a test project, rewritten by each engine run) breaks the build after
  a model change (CS0535, CS0534, CS1729): delete it, build, run the tests (they regenerate it), build again.
- **The TypeScript test projects regenerate files** (`angular.json`, `jest.config.js`, `yarn.lock`...): commit only a
  correct regeneration (the engine's normalization), restore the rest.
- **A flaky test is investigated, not retried blindly:** retry once; if it fails again (even another test of the same
  kind), find the cause (a 5 seconds Jest timeout on a loaded machine was one: the generated `jest.config.js` now sets 30
  seconds).
