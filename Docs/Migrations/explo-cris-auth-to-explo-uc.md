# Migrating from explo/cris-auth to explo/uc

This guide lists what a repository (or a World) that uses the packages of the **`explo/cris-auth`** branch must change
to move to the packages of the **`explo/uc`** branch: the first steps of the Unified Configuration (see the Stack's
`.wip` epic when you have access to it). Apply [the cris-auth guide](v2026.282-to-explo-cris-auth.md) first.

Each item below has an identifier, a **Detect** step (what to search for: it is meant to be scripted), the **Do** step,
the **Behavior** and a **Check**.

## Contents

| Id | Area | Breaking? |
|---|---|---|
| [U1](#u1-the-apps-configuration-ckonfjson-the-hosts-appsettingsjson-are-projections) | The App's configuration: the `ckonf.json` of a builder (its BinPaths, the client applications declared with their `Type`); the hosts' `appsettings.json` are generated from it | Yes (the builders, the hosts' configuration) |

## U1. The App's configuration: `ckonf.json` (the hosts' `appsettings.json` are projections)

A builder ("XXX.Builder", run by `CKomposableAppBuilder` of CK.AppBuilder) can have a `ckonf.json`: the configuration of
the App, its BinPaths (its parties) keyed by their name, each one with the configuration of its host. The engine
configuration is deduced from it, and the `appsettings.json` of each host is generated from it. It is the first step of
the Unified Configuration. A builder without `ckonf.json` works as before (its `CKSetup.xml`), but the help that added the
client applications to the host's `appsettings.json` (M14 of the cris-auth guide) is gone: migrate.

**Detect:** a "XXX.Builder" project without `ckonf.json`; a `CKSetup.xml` with `<BinPaths>`, `<TypeScript>` or
`<Globalization>`; a builder `Program.cs` that configures the engine (`EnsureDefaultTypeScriptAspectConfiguration`,
`EnsureAspect<...>`).

**Do:**

1. **Create `ckonf.json` in the builder project.** Its BinPath is named after its projects: the BinPath "Front" is the
   "../Front.App" project (its bin folder is the builder's: `bin/{Configuration}/{TargetFramework}`) and the
   "../Front.Host" project. `"AppProject"` and `"HostProject"` (relative to the `ckonf.json` folder) depart from this
   convention; a path that leaves the solution's folder is an error. The content of the BinPath is the host's
   `appsettings.json` (move it, with its comments), plus the compile time keys below:

   ```jsonc
   // Front.Builder/ckonf.json
   {
     "App": {
       "BinPaths": {
         "Front": {
           "CK-AppIdentity": {
             "FullName": "Sample/Auth3/$Front/#Dev",
             "Local": {
               "AllowFeatures": "HttpEndpoint",
               "HttpEndpoint": { "Main": { "Urls": "http://localhost:5300" } },
               "PartyClient": {
                 "App": { "Type": "TypeScript", "Kind": "DefaultApp", "DevServerPort": 4200 },
                 "MiniApp": {
                   "Type": "TypeScript",
                   "Kind": "App",
                   "DevServerPort": 4201,
                   "Types": [ "CK.Ng.Cris.AspNet.CrisAspNetPackage, CK.Ng.Cris.AspNet", "CK.Sample.Front.IFrontInfoQCommand, Front.App" ]
                 }
               }
             }
           },
           "Logging": { "LogLevel": { "Default": "Warning" } }
         }
       }
     }
   }
   ```

2. **Move the client applications into their declaration.** A `<TypeScript Name="X" ...>` of the BinPath becomes the
   declared client `"Local:PartyClient:X"` with `"Type": "TypeScript"` and its attributes as keys: `Kind`,
   `DevServerPort`, `UseDist`, `TargetProjectPath`, `DefaultTypeScriptVersion`, `IntegrationMode`, `InstallYarn`,
   `GitIgnoreCKGenFolder`, `AutoInstallJest`, `EnableTSProjectReferences`, `DefaultCulture`, `ActiveCultures` (a string
   or an array), `TypeFilterName`; its lists `Types`, `ExcludedTypes` (a type is its assembly qualified name, or an
   object whose keys are its attributes: `{ "Name": "...", "Optional": true }`) and `Barrels`. The `"Type"` of a client is
   required: a client without `Type`, or whose `Type` no package handles, is a build error. The options of the client
   (`StaticFiles`) stay in its declaration: they are the run time's.

3. **Move the other BinPath settings:**
   - `<Globalization ActiveCultures="fr, en" />` becomes `"Globalization": { "ActiveCultures": "fr, en" }` in the
     BinPath;
   - the `Types` (with their `Kind`), `ExcludedTypes`, `Assemblies`, `DiscoverAssembliesFromPath`, `CompileOption`,
     `GenerateSourceFiles` of a `<BinPath>` become keys of the BinPath (`"Types": [ { "Name": "...", "Kind": "IsScoped" } ]`).

4. **Trim the `CKSetup.xml`:** it may keep only the aspects that are not deduced yet (the database: `Setupable`,
   `SqlSetup` with its `DefaultDatabaseConnectionString`, `Fluid`...) and the global options. A `<BinPaths>` in it is an
   error; the `TypeScript` and `Globalization` aspects are deduced (remove them). A `CKSetup.xml` left without aspect is
   deleted.

5. **Simplify the builder's `Program.cs`:** what it configured is in the `ckonf.json` (a configuration of the database
   aspect in code can stay):

   ```csharp
   return await CK.Setup.CKomposableAppBuilder.RunAsync();
   ```

6. **Run the builder** (build the builder project): it writes the host's `appsettings.json`. **Commit it**, with the
   `ckonf.json`.

**Behavior:**
- **The host's `appsettings.json` is generated:** its first line is `// Generated from Front.Builder/ckonf.json: do not
  edit.`, it is written when it changes, without the comments (they are in the `ckonf.json`) and without the compile
  time keys (the client's `Type` and TypeScript keys, `Globalization`, the BinPath keys). Edit the `ckonf.json`, never
  the projection. `"App": { "ProjectCompileKeys": true }` keeps the compile time keys of the clients in the projection
  (the run time accepts them).
- **The configuration is checked at build time:** the `CK-AppIdentity` of each BinPath is validated when the builder
  runs (an invalid configuration fails the build), the keys are case insensitive (`Back1` and `back1` is a duplicate:
  an error), an unknown key of `App` or of the root is an error.
- **The order of the BinPaths means nothing:** the engine receives them sorted by name.
- **The builder needs no new package:** the engines that deduce the configuration are the ones the builders already
  reference (`CK.AppIdentity.Engine`, M30 of the cris-auth guide; the TypeScript engine through `CK.TS.Angular.Engine` or
  `CK.TypeScript.Engine`; `CK.Globalization.Engine`), in their versions of the `explo/uc` branch.

**Check:** the builder's log has "Reading '.../ckonf.json'." and "written from 'ckonf.json'"; the host's
`appsettings.json` starts with the "Generated" line; the generated code (`$StObjGen`) and the `ck-gen` of the client
applications are unchanged by the migration. CK-Sample-AspNetAuth (five builders), CK-Sample-MultiBinPath (two
BinPaths in one builder), the samples of CK-Ng-Admin, CK-Ng-User-UserProfile, CK-Ng-Zorro-Contrib and
CK-Observable-Domain are migrated this way.

## The final checklist

1. Every builder has a `ckonf.json` (its BinPaths, the client applications declared with their `"Type"`), its
   `CKSetup.xml` has no `<BinPaths>` (or is gone), and the hosts' `appsettings.json` start with the "Generated" line,
   committed (U1).
