
# The simplest (?) possible workflow.
Every build starts with a branch in a Repo. The UX is minimalist:
- Checkout the branch to build: `ckli branch stable`.
- Create commits in the branch (or its "dev/" associated branch, see below).
- Call the build: `ckli build`.

# Database-less approach.
Commits that have been built are tagged with the produced version. These tags are annotated with the "build content":
- A list of package@version dependencies consumed by the built version. Can technically be empty but this almost never happen.
- A list of package identifiers produced. This is empty for a terminal solution that doesn't produce packages but only build assets.
- A list of build asset files that are the "deployment" files. Empty for solutions that only produce packages.

The state of the system is in the repositories. Databases, when they exist, can be rebuilt from scratch any time from the World's
repository. This is a key point of the architecture. In practice, even the content of the tags can be rebuilt: empty or
lightweight tags that are parsable as versions are automatically detected issues that can be automatically fixed by rebuilding
the their target commit and generating the tag message from the build result.

# Branch model & workflows.

Git is very (too) flexible. Years ago, the "git flow" was the commonly accepted way to structure the works in a repository. Recently,
the "GitHub workflow" has emerged as a simpler alternative and gained a lot of attraction. This article is a good description and
compares the two: https://www.geeksforgeeks.org/git/git-flow-vs-github-flow/.
They both focus on one repository and totally overlook the version numbering issue. We aim to integrate the SemVer numbering, to also
control and automate as much as possible this aspect of the developer job. Our approach can be seen as a mix between the Git and the
GitHub workflows that integrate the packaging and versioning step and tries to keep the best of both approaches.

A repository state can be divided in 2 "zones":
- The "hot zone" is the tip of the repositories with all the branches that contain the live, up-to-date, current code base.
- The "cold zone" contain the previous releases and their fixes.

There's two parts in the "CK workflow", the "hot zone" is based on the root, required, `stable` branch and a set of
8 well-known optional branches that map to "package qualities":
- `alpha`: is the branch with the most unstable code, it can contain code that will never be actually released. From the `alpha` branch,
  pre-release packages can be produced (1.2.0-a, 12.0.0-a-01, etc.).
- Then come the `beta`, `delta`, `epsilon`, `gamma`, `kappa` and `pre` branches that must contain increasingly "better", "more stable" code.
- The `rc` branch is the "best" pre release branch. It aims to be merged in `stable` sooner or later.

The "cold zone" is somehow optional: it is based on "fix/" branches that are created on **Major.Minor** released versions ("fix/v1.0", "fix/v2.4", etc.)
when a fix of an old release needs to be produced. To prepare the release of a fix, the command `ckli branch fix 1.2` creates the branch if needed
and checks out it on the appropriate commit (that is the last fix of the v1.2.x family).

An optional "dev/" branch can be associated to each of these branches. This introduces another "dimension" in the system: from "dev/" branches,
"post release" packages can be produced. Regular development should be done in the "dev/" branch but this is not required. When `ckli build` is
executed, one of its first task is to merge the "/dev" branch, if it exists, into the current branch.

__Important:__: From now on, we'll forget the 8 prerelease branches and focus on the "stable" one because:
- Structural: what applies to the "stable" and "fix" workflows apply to the pre-release branches (with some additional subtleties).
- Circumstantial: pre-release branches are not supported yet :-).

## The "dev/" branch
This branch is optional and can be created or suppressed any time. When it exists, its commits must contain the last "official" built commit in
its history, or more generally (if no built commit exists yet), the tip of its primary branch.
The following configuration should not happen.
```
+ [stable]
|\
| \
|  + [dev/stable]
|  |
```
Instead, the "dev/" branch must be based on its primary branch:
```
   + [dev/stable]
  /
 /
+ [stable]
|\
```
CKli is rather aggressive here: once merged (by `ckli build`), the "/dev" branch is deleted. To initiate a new "/dev" branch,
the command `ckli branch dev stable` can be used create it and this does a little bit more than you may expect:
- The "/dev" branch is created on the source commit.
- Its dependencies are read (the consumed packages of the build content) and if a post release build exists for a dependency in the World,
  the package is upgraded: a "/dev" branch must always rely on the very last available artifacts of it dependencies.

A "dev/" branch must contain code that is ready to be released, not-so-ready code should be in "feature" branches, independent branches
unknown to CKli that are manually managed.

## The post-release build (CI build)
CKLi relies on [CSemVer](https://csemver.org) and uses its "post-release" feature to support implicitly version numbered builds. Given any
valid CSemVer version a post release version can be computed that is guaranteed to be lower (according to SemVer rules) to any possible
next regular version. We often use the term "CI builds" for these post-release versions.
Such versions have a short life-time and have no "history": there is logically at most one CI Build to consider per primary branch of
a repository. 

A fundamental invariant of CKLi workflow is that "dev/" branches produces (this is easy) and always consumes (less easy) CI build
versions if they are available.

Preserving this invariant requires some work. When a build occurs for one of the dependencies, it must propagate across the World
("push" to upstream).
When a build is done on "dev/" branch CKli must ensure that for any dependency, the "/dev" branch tip is built and used as the dependency
("pull" from downstream).

The same invariant exists for the primary branch: a stable build has only stable dependencies. This pull+push mechanism means that the
build of a repository can always be triggered by the build of another repository in the World. This is the reason why "dev/" branches must
always contain "ready to release" code for itself (the CI Build that may be produced any time).

We did consider a possible behavior here: Could the "pull" be optional when building the "stable"?
This seems a good idea, especially for a fix: we may want to produce a fix of the current version without introducing any new code evolution
from its dependencies. This introduces an asymmetry, breaking the coherency of the primary/dev association. So we rejected this possibility.
And this is not an issue: the "fix" workflow is exactly here for this purpose! CKli will ensure that once built, the fixed code base will
be merged into the "hot zone". (Technically, the hot zone's branches should be git-rebased on the fix commit but we decided to avoid rebasing.)

Definitely, "dev/" branches must always contain "ready to release" code including its primary branch (when a regular build must be done).

When code is not that close to its "ready to release" state but we still want to deploy/test them, the 8 CKli pre-release branches comes into
play but this is another story.










