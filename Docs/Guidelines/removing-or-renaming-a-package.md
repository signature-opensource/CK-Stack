# Removing or renaming a package

Rules for retiring a package of this stack, or publishing it under a new name. They apply to people and to coding
agents alike.

## Never just stop publishing a package: publish its tombstone

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

## Before publishing the tombstone

Every package of the stack must have stopped depending on the dead package. The World build checks it: a project of
the stack that still references it fails with the tombstone's error.

When the package is renamed, the new package exists (and is published) first: the tombstone's message names it.

## The tombstone project

The project keeps its name and its place in its repository: CKli builds and publishes it like any other project, as
the next version of the package. Its code is deleted and its `.csproj` becomes:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <!-- Tombstone: no assembly, no dependencies. -->
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <SuppressDependenciesWhenPacking>true</SuppressDependenciesWhenPacking>
    <!-- NU5128: the package has no lib/ folder, on purpose. -->
    <NoWarn>$(NoWarn);NU5128</NoWarn>
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

## Its documentation

Like any packable project (see [documentation.md](documentation.md)), the tombstone has a `README.md` and a
`docs/Package.md`. Both say only that the package is deprecated, what replaces it and where its documentation is:
`docs/Package.md` is what nuget.org shows. The repository's `README.md` lists the project as deprecated, in one line.
