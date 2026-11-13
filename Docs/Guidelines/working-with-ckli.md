# Working with ckli

Rules for building and testing the repositories of this stack with ckli. They apply to people and to coding agents alike.

## Builds

- **`ckli build --focus` from the most downstream repository of a change**: it builds the current repository and the
  upstream ones that need a build, and stops (the consumers only with `--continue`). `--dry-run` first: among the
  numbered lines, `⊙` marks the current repository and `→·` the upstream ones that will build with it; `·→` marks
  the consumers, built only with `--continue`. The last line says how many builds `--focus` leaves out.
- **ckli refuses to build when any repository of the World is dirty**, not only the ones it builds: commit (or stash) first.
- **Never edit a repository while it builds:** its "Updated dependencies" commit stages everything in the working tree.
- **A build that changes a tracked file fails** and lists the files that it created, modified or deleted (a test that
  regenerates a fixture, an engine that adds a `.gitignore` line). `--build-impacts commit` amends them to the built
  commit and builds it again, `--build-impacts discard` erases them. **When a change is known to have build impacts**
  (an engine change that regenerates the test applications), pass `--build-impacts commit` from the first run: it
  saves the failed run and the manual commit. Then read what it committed: only a correct regeneration (the engine's
  normalization) belongs to the repository. Unexpected impacts: build without the option and look at the list.
- **Push with `ckli push`**, never a `git push` per repository. It pushes the Stack repository too; `--stack-only`
  pushes only the Stack repository.
- **Move, retire or rename a project with `ckli project move` and `ckli project retire`**, never by hand: see
  [Managing projects across repositories](managing-projects-across-repositories.md).

## Repositories

- **A new repository: `ckli repo create <url>`**, run from the folder of the World where it belongs (`Core/CKomposable`
  for instance): that folder is also its place in the World definition. It creates the remote repository (public for a
  public Stack, private for a private one), clones it there, and commits the World definition in the Stack. The
  BranchModel plugin makes the root branch of the branch model (`stable`) the default branch, pushed at once, and
  creates `dev/stable`, checked out. If the command fails, the remote repository is deleted.
- **An existing repository: `ckli repo add <url>`**, the same way, without creating the remote.
- **Removing a repository from the World: `ckli repo remove <name>`** deletes its folder, without checking for unpushed
  work: push its branches first (with `ckli push` while it is still in the World). If the folder can't be deleted (a
  process holds it), the World definition is changed but the Stack is not committed: free the folder, delete it, and
  commit `CK.xml`.
- **The new repository is empty:** its solution and build files are written by hand, on its `dev/` branch;
  `ckli issue` shows what is still expected.
- **The Stack is committed, not pushed:** `ckli push` (or `ckli push --stack-only`) publishes the change.

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
  correct regeneration (the engine's normalization), restore the rest. In a ckli build they are build impacts (see
  above).
- **A flaky test is investigated, not retried blindly:** retry once; if it fails again (even another test of the same
  kind), find the cause (a 5 seconds Jest timeout on a loaded machine was one: the generated `jest.config.js` now sets 30
  seconds).
