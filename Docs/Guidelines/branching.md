# Branching guidelines

Rules for committing in the repositories of this stack. They apply to people and to coding agents
alike.

## Never commit to a bare branch

**No commit ever lands in a branch named `XXX`. It lands in `dev/XXX`.**

So work that belongs to `stable` is committed to `dev/stable`, work for `develop` to `dev/develop`,
and so on. The bare branches are integration targets, not places to commit.

Every branch whose name doesn't start with `dev/` is bare: `stable`, the pre-release branches
(`alpha`…`zulu`) and the exploratory ones alike. Work on `explo/xxx` is committed to `dev/explo/xxx`.

Commits that CKli makes itself on a bare branch ("Updated dependencies.", "Producing '…' from
unchanged head.") are not a precedent: what other repositories' histories show never overrides this
rule.

**Exception: Stack repositories (`*-Stack`, cloned as `.PublicStack`/`.PrivateStack`) commit on
`main`.**

CKli integrates the `dev/` branch when building or publishing, so this is not a style preference:
committing directly to `stable` or `develop` puts the work outside what the tooling picks up (this
creates an issue that must be fixed).

**In practice:** before the first commit of a session, check the current branch. If it is bare,
create (or switch to) its `dev/` counterpart *first* rather than committing and fixing it up after.

```
git rev-parse --abbrev-ref HEAD      # e.g. "stable"
git checkout -b dev/stable           # or: git checkout dev/stable, if it exists
```
