# Managing projects and packages across repositories

Rules for moving a project to another place or repository, retiring a package, or publishing it under a new name.
They apply to people and to coding agents alike.

A project, and the package it produces, is not tied to its repository: it can move to another one, and its
documentation moves with it (see [documentation.md](documentation.md)). A package is never just dropped: its last
version says what to do instead. CKli implements these rules with two commands of its HotZone plugin,
[`ckli project move`](https://github.com/CK-Build/CKli/blob/stable/StandardPlugins/CKli.HotZone.Plugin/README.md#how-project-move)
and
[`ckli project retire`](https://github.com/CK-Build/CKli/blob/stable/StandardPlugins/CKli.HotZone.Plugin/README.md#how-project-retire):
their documentation details their options and checks, this guideline says what they are for and why.

## Moving a project

`ckli project move <projects>`, run from the destination folder (or with `--target`), moves projects inside a
repository or from other repositories of the World. `--with-tests` moves the `X.Tests` project of each moved `X` with
it; `--dry-run` displays the plan.

- **The package keeps its identity.** Its name doesn't change: its consumers keep their `<PackageReference>`, only its
  producer changes.
- **A package is never produced again in a version it already had.** A repository that receives packages from another
  one starts above every version of these sources (a `+fake` version tag).
- **The references follow.** Inside a repository, the `<ProjectReference>` paths are rewritten; a reference that now
  crosses a repository boundary becomes a `<PackageReference>` (and the other way round). Two repositories never
  depend on each other: a move that would create a cycle is refused (moving the downstream projects first avoids it).
- **Only what git tracks moves**, and the move lands on the `dev/` branches, uncommitted: review it, build from the
  World root (the source and the target must both build), update the `README.md` of both repositories (the project's
  line moves from one to the other) and commit.

## Retiring a package: publish its tombstone

A package that is simply no longer published stays silently usable: its last version keeps being restored, and
nothing tells its consumers that it is dead or what replaces it.

**The last version of a removed (or renamed) package is a tombstone:** a package without code and without
dependencies, whose only content is a transitive MSBuild `.targets` file that **fails the build** of any project that
depends on it, directly or through other packages, with an error that names the replacement.

- **No code:** a consumer gets one explicit error instead of a cascade of compilation errors about missing types.
- **No dependencies:** the dead package no longer drags its own dependency graph into its consumers.
- **No dependency on the replacement either:** moving to the replacement is a deliberate migration (its API differs,
  or its name does), not something that happens behind the consumer's back.
- **Transitive:** the `.targets` is packed in `buildTransitive/`, so the error reaches a project that only depends on
  a library that depends on the dead package. That library must be upgraded too, and the error says so.

The previous versions stay on the feeds: a consumer that doesn't upgrade is not affected. The tombstone reaches only
the consumers that move to it, directly or because one of their dependencies requires it.

`ckli project retire <projects> [--replacement <package or README url>] [--reason <text>]` writes the tombstones
described below: a tombstone always says what to do, so one of `--replacement` and `--reason` is required.
`--with-tests` deletes the `X.Tests` project of each retired `X`, `--dry-run` displays the plan.

### Before retiring

Every project of the stack must have stopped depending on the dead package: `ckli project retire` refuses otherwise,
and lists the consumers.

When the package is renamed or replaced, the new package exists (and is published) first: the tombstone's message
names it.

### The tombstone project

The project keeps its name and its place in its repository: CKli builds and publishes it like any other project. Its
code is deleted and its `.csproj` becomes:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <!-- Tombstone: no assembly, no dependencies. -->
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <SuppressDependenciesWhenPacking>true</SuppressDependenciesWhenPacking>
    <!-- NU5128: the package has no lib/ folder, on purpose. -->
    <NoWarn>$(NoWarn);NU5128</NoWarn>
    <Description>Deprecated: replaced by New.Package.</Description>
  </PropertyGroup>
  <ItemGroup>
    <Content Include="MSBuild/Old.Package.targets">
      <Pack>true</Pack>
      <PackagePath>buildTransitive\Old.Package.targets</PackagePath>
    </Content>
  </ItemGroup>
</Project>
```

`MSBuild/Old.Package.targets` (the file is named after the package id: it is how NuGet imports it):

```xml
<Project>
  <Target Name="Old_Package_IsDeprecated" BeforeTargets="ResolveAssemblyReferences">
    <Error Code="CKDEPRECATED"
           Text="Package 'Old.Package' is deprecated and must be replaced by 'New.Package': see https://github.com/signature-opensource/New-Repo/blob/stable/New.Package/README.md." />
  </Target>
</Project>
```

- **The message** names the dead package, its replacement (or "no replacement" and what to do instead) and links to
  the documentation of the replacement (an absolute URL, see [documentation.md](documentation.md)).
- **The error code** is always `CKDEPRECATED`, so that the build logs of a migration are easy to search.
- **The target name** is unique (derived from the package id): several tombstones can be imported by the same project.

Like any packable project (see [documentation.md](documentation.md)), the tombstone has a `README.md` and a
`docs/Package.md`. Both say only that the package is deprecated, what replaces it and where its documentation is:
`docs/Package.md` is what nuget.org shows. The repository's `README.md` lists the project as deprecated, in one line.

### Its version: a new Major

A tombstone breaks every consumer that moves to it: its repository starts a new Major (`ckli project retire` sets
its `+fake` version tag), whatever else the repository publishes with it.

### Published once

**Once its stable version is published, the tombstone is removed from the root branch (`stable`):** its folder is
deleted and it leaves the solution. Nothing is left to build, and the package is never published again: its
tombstone stays its last version. `ckli issue --fix` does it (on `dev/stable`), and a build of `stable` is refused
until then.

The other branches keep the tombstone until they are merged: they publish it again as prereleases, which only the
consumers of their prereleases see. Removing it on such a branch would remove it from `stable` at the merge, before its
stable version is published.

## Renaming a package

A rename is a new package and a retired one: the old project stays where it is and becomes the tombstone.

1. Create the new project under its new name (a copy of the old one), in its repository, and publish it.
2. Move every consumer of the stack to it.
3. Retire the old one: `ckli project retire Old.Package --replacement New.Package`.

To rename a package while changing its repository, create the new project directly in its new repository.
