# Documentation guidelines

Rules for every `README.md` and other Markdown document in the repositories of this stack.
They apply to people and to coding agents alike.

## Where documentation lives

What matters is the **project** (the future package), not the repository: a project can move to another
repository, and its documentation must move with it. So the details always live with the project.

- **The repository's `README.md` is terse.** It introduces the projects the repository contains, in a
  line or two each, and links to their `README.md`. When a project is deleted or moved to another
  repository, removing its entry is the only change the repository's `README.md` needs: nothing else in
  it is about that project.
- **The project's `README.md`** (next to its `.csproj`) is the reference documentation of the project.
- **For a complex project**, keep the project's `README.md` readable: put the detailed documentation
  of a feature in a `README.md` inside the implementation folder where that feature's source files
  live, and link to it from the project's `README.md`.
- **A packable project** (one that produces a NuGet package) must have both a `README.md` and a
  minimal `docs/Package.md`. `Package.md` is the package's readme on nuget.org (the shared
  `Directory.Build.props` sets it as `PackageReadmeFile` when it exists): a few lines saying what the
  package provides and its main types or entry points, not a copy of the `README.md`. It is read
  outside any repository, so any link in it is an absolute URL (see below).

## Content

- **Describe the current state only.** No dates, no "used to", no former names. History belongs in
  commit messages.
- **A change that makes a document wrong fixes it in the same change.** A README that lies is worse
  than no README.
- **Ground every claim in the source.** Type names, signatures, command names, parameters and
  configuration snippets are copied from the actual code or configuration file, never reconstructed
  from memory. Never document an API that does not exist.
- **Scale detail to what exists.** One honest README beats a forest of speculative documents.
  Don't write a README for a project nobody asked about just to fill a gap.
- **Link rather than duplicate.** If another document already explains something, point to it.
  Two copies of the same explanation will drift apart.

## Structure and style

- Headers over walls of prose; short paragraphs.
- A project README starts with **WHY** (its purpose, the problem it solves), then **HOW** (key types,
  commands, configuration, extension points).
- Tables for lists of things that share attributes: repositories, packages, commands, type roles.
- Code fences for real, verified signatures and configuration snippets.

## Linking between documents

### Direction: follow the dependency order

Links follow the topological order of the dependencies, as much as possible: a package `P` that depends
on a package `B` can always link to `B`'s documentation (its `README.md` or one of its implementation
folders' `README.md`), and often should, so that the basic concepts are read before the higher ones
built on them. Within a project, the same goes for its implementation folders.

A link the other way, from a basic package to one that depends on it, is the exception. It must say
explicitly why it is there, e.g. "for an example of this behavior in a concrete package, see ...". The
basic documentation must stand on its own without it.

### Where links point

**Cross-repository links use the repository's git origin URL, never a relative path.** A relative path
like `../Other-Repo/Some.Project/README.md` only resolves on a machine that has the sibling repository
cloned at that exact spot. It breaks on GitHub, in a packaged README, and in any differently laid-out
clone.

- **Within one repository**: ordinary relative links (`../Some.Project/README.md`).
- **Across repositories, and from a `docs/Package.md`**: `<origin>/blob/<branch>/<path>` for a file, `<origin>/tree/<branch>/<path>`
  for a folder; anchors (`#section`) are appended normally. Get the origin with
  `git -C <repo> remote get-url origin`.

**Which branch:** repositories whose name ends in `-Stack` use `main`; **every other repository uses
`stable`**. Don't read the branch off `origin/HEAD` (a remote's default may never have been
corrected) and never use the currently checked-out branch: day-to-day work happens on `dev/` branches,
which are deleted once integrated, so links pinned to them rot.

The boundary is between *repositories*, not folders. A folder inside the Stack repository links to the
Stack root with a relative path, but links into any repository the Stack manages need an origin URL.

Plain path descriptions are not links and stay as local paths: folder-layout diagrams, "cloned next to
`.PublicStack/`", a local-path column in a table.

## Reviewing existing documents

1. Treat a document as a set of falsifiable claims. Check each one against the current source or
   configuration, not against how the document reads.
2. Report each claim as **CONFIRMED**, **OUTDATED** (with the correct fact) or **NOT FOUND** (a dead
   reference; say what replaced it, if anything).
3. A review asked as "check" or "comment on" reports first; it doesn't rewrite the file.
4. When fixing, edit claim by claim instead of regenerating the whole file. This preserves what was
   correct and the author's prose.
5. Check links for three faults: a relative path that crosses a repository boundary, an origin URL
   pinned to the wrong branch, and a link against the dependency order that isn't explicitly
   introduced as an exception.
