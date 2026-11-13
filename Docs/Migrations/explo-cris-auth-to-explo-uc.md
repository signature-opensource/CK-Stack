# Migrating from explo/cris-auth to explo/uc

This guide lists what a repository (or a World) that uses the packages of the **`explo/cris-auth`** branch must change
to move to the packages of the **`explo/uc`** branch: the first steps of the Unified Configuration (see the Stack's
`.wip` epic when you have access to it). Apply [the cris-auth guide](v2026.282-to-explo-cris-auth.md) first.

Each item below has an identifier, a **Detect** step (what to search for: it is meant to be scripted), the **Do** step,
the **Behavior** and a **Check**.

## Contents

| Id | Area | Breaking? |
|---|---|---|
| [U1](#u1-the-builders-configuration-ckonfjson-the-hosts-appsettingsjson-are-projections) | The builder's configuration: the `ckonf.json` of a builder (its Apps, their party, the client applications declared with their `Type`, the aspects); the hosts' `appsettings.json` are generated from it | Yes (the builders, the hosts' configuration) |

## U1. The builder's configuration: `ckonf.json` (the hosts' `appsettings.json` are projections)

A builder ("XXX.Builder", run by `CKomposableAppBuilder` of CK.AppBuilder) can have a `ckonf.json`: the configuration of
what it builds in one engine run, its **Apps** keyed by their name (an App is an "X.App"/"X.Host" pair: one process, its
root party and its tenants), each one with the configuration of its host, and the **aspects**. The engine
configuration is deduced from it (an App is a BinPath of the engine), and the `appsettings.json` of each host is
generated from it. It is the first step of the Unified Configuration. A builder without `ckonf.json` works as before (its
`CKSetup.xml`), but the help that added the client applications to the host's `appsettings.json` (M14 of the cris-auth
guide) is gone: migrate.

**Detect:** a "XXX.Builder" project without `ckonf.json`; a `CKSetup.xml` with `<BinPaths>`, `<TypeScript>` or
`<Globalization>`; a builder `Program.cs` that configures the engine (`EnsureDefaultTypeScriptAspectConfiguration`,
`EnsureAspect<...>`).

**Do:**

1. **Create `ckonf.json` in the builder project.** An App is named after its projects: the App "Front" is the
   "../Front.App" project (its bin folder is the builder's: `bin/{Configuration}/{TargetFramework}`) and the
   "../Front.Host" project. `"AppProject"` and `"HostProject"` (relative to the `ckonf.json` folder) depart from this
   convention; a path that leaves the solution's folder is an error. The content of the App is the host's
   `appsettings.json` (move it, with its comments) **without its `"CK-AppIdentity"` level**: the keys of the party
   (`FullName`, `Local`, `Parties`, `Defaults`, `DomainName`, `PartyName`, `EnvironmentName`, `StoreRootPath`,
   `TrashBinRetention`, `StrictConfigurationMode`) are directly in the App, next to the other sections of the host
   (`Logging`, `CK-Monitoring`...). Plus the compile time keys below:

   ```jsonc
   // Front.Builder/ckonf.json
   {
     "Apps": {
       "Front": {
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
         },
         "Parties": [ { "PartyName": "$Auth3", "AllowFeatures": "AuthCenterRemote", "Address": "tcp:127.0.0.1:37300" } ],
         "Logging": { "LogLevel": { "Default": "Warning" } }
       }
     }
   }
   ```

   The root of `ckonf.json` has `"Apps"`, `"Aspects"` (see step 3) and `"ProjectCompileKeys"` (see the Behavior); any
   other key is an error.

2. **Move the client applications into their declaration.** A `<TypeScript Name="X" ...>` of the BinPath becomes the
   declared client `"Local:PartyClient:X"` with `"Type": "TypeScript"` and its attributes as keys: `Kind`,
   `DevServerPort`, `UseDist`, `TargetProjectPath`, `DefaultTypeScriptVersion`, `IntegrationMode`, `InstallYarn`,
   `GitIgnoreCKGenFolder`, `AutoInstallJest`, `EnableTSProjectReferences`, `DefaultCulture`, `ActiveCultures` (a string
   or an array), `TypeFilterName`; its lists `Types`, `ExcludedTypes` (a type is its assembly qualified name, or an
   object whose keys are its attributes: `{ "Name": "...", "Optional": true }`) and `Barrels`. The `"Type"` of a client is
   required: a client without `Type`, or whose `Type` no package handles, is a build error. The options of the client
   (`StaticFiles`) stay in its declaration: they are the run time's.

3. **Move the other BinPath settings:**
   - `<Globalization ActiveCultures="fr, en" />` becomes an aspect of the App:
     `"Aspects": { "Globalization": { "ActiveCultures": "fr, en" } }` (or an array of culture names). The aspects are
     compile time, keyed by their name, in the App (its aspects) or at the root of `ckonf.json` (the aspects of the whole
     builder); an aspect that no package handles is a build error ("No package handles the aspect ...");
   - the `Types` (with their `Kind`), `ExcludedTypes`, `Assemblies`, `DiscoverAssembliesFromPath`, `CompileOption`,
     `GenerateSourceFiles` of a `<BinPath>` become keys of the App (`"Types": [ { "Name": "...", "Kind": "IsScoped" } ]`).

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
  time keys (the client's `Type` and TypeScript keys, the `Aspects`, the App keys). **The keys of the party are in its
  `"CK-AppIdentity"` section** (first), the other keys of the App follow as they are: the run time configuration is the
  one of the cris-auth guide (the user-secrets and the environment variables keep their `CK-AppIdentity:...` keys). Edit
  the `ckonf.json`, never the projection. `"ProjectCompileKeys": true` (at the root) keeps the compile time keys of the
  clients in the projection (the run time accepts them).
- **The configuration is checked at build time:** the party of each App is validated when the builder runs (an
  invalid configuration fails the build), the keys are case insensitive (`Back1` and `back1` is a duplicate: an error),
  an unknown key of the root is an error, and a key named `CK-AppIdentity` in an App that has a party is an error (it is
  the section of the projection: the old `"CK-AppIdentity": { ... }` level must be removed).
- **The order of the Apps means nothing:** the engine receives them sorted by name.
- **The builder needs no new package:** the engines that deduce the configuration are the ones the builders already
  reference (`CK.AppIdentity.Engine`, M30 of the cris-auth guide; the TypeScript engine through `CK.TS.Angular.Engine` or
  `CK.TypeScript.Engine`; `CK.Globalization.Engine`), in their versions of the `explo/uc` branch.

**Check:** the builder's log has "Reading '.../ckonf.json'." and "written from 'ckonf.json'"; the host's
`appsettings.json` starts with the "Generated" line; the generated code (`$StObjGen`) and the `ck-gen` of the client
applications are unchanged by the migration. CK-Sample-AspNetAuth (five builders), CK-Sample-MultiBinPath (two
Apps in one builder), the samples of CK-Ng-Admin, CK-Ng-User-UserProfile, CK-Ng-Zorro-Contrib and
CK-Observable-Domain are migrated this way.

## The final checklist

1. Every builder has a `ckonf.json` (its `"Apps"` with the keys of their party directly in them, the client
   applications declared with their `"Type"`, the `"Aspects"`), its `CKSetup.xml` has no `<BinPaths>` (or is gone), and
   the hosts' `appsettings.json` start with the "Generated" line, committed (U1).
