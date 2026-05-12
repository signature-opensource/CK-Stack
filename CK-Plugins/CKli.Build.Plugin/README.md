
# The simplest (?) possible workflow.
Every build starts with a branch in a Repo. The UX is minimalist:
- Checkout the branch to build: `ckli checkout stable`.
- Create commits in the "dev/" associated branch, see below.
- Use:
  - `ckli ci build` to produce CI builds from the "dev/" branches.
  - `ckli build` to merge "dev/" into their associated branch and produce non-CI builds.
  - `ckli ci publish` to produce CI builds from the "dev/" branches and publish them.
  - `ckli publish` to merge "dev/" into their associated branch, produce non-CI builds and publish them.

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

The "cold zone" is somehow optional: it is based on "fix/" branches that are created on **Major.Minor** released versions ("fix/v1.0",
"fix/v2.4", etc.). This is handled by the Fix Workflow.

An optional "dev/" branch can be associated to each of these branches. This introduces another "dimension" in the system: from "dev/" branches,
"post release" packages can be produced. Regular development should be done in the "dev/" branch but this is not required. When `ckli publish` is
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
Instead, the "dev/" branch must be "visually" based on its primary branch:
```
+ [stable] [dev/stable]
|\
```
or
```
   + [dev/stable]
  /
 /
+ [stable]
|\
```
CKli is rather aggressive here: once merged (by `ckli publish`), the "/dev" branch is deleted. To initiate a new "/dev" branch,
the command `ckli checkout dev/stable` can be used to create it.

A "dev/" branch must contain code that is ready to be released, not-so-ready code should be in "feature" branches, independent branches
unknown to CKli that are manually managed.

## The post-release build (CI build)
CKLi relies on [CSemVer](https://csemver.org) and uses its "post-release" feature to support implicitly version numbered builds. Given any
valid CSemVer version a post release version can be computed that is guaranteed to be lower (according to SemVer rules) to any possible
next regular version. We often use the term "CI builds" for these post-release versions.
Such versions have a short life-time and have no "history": there is logically at most one CI Build to consider per primary branch of
a repository. 

A fundamental invariant of CKLi workflow is that "dev/" branches produces and always consumes CI build
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
And this is not an issue: the "fix" workflow is exactly here for this purpose! CKli ensures that a fix cannot be started from the "hot zone".

When code is not that close to its "ready to release" state but we still want to deploy/test them, the 8 CKli pre-release branches comes into
play but this is another story.










