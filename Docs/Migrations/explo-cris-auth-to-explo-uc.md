# Migrating from explo/cris-auth to explo/uc

This guide lists what a repository (or a World) that uses the packages of the **`explo/cris-auth`** branch must change
to move to the packages of the **`explo/uc`** branch: the first steps of the Unified Configuration (see the Stack's
`.wip` epic when you have access to it). Apply [the cris-auth guide](v2026.282-to-explo-cris-auth.md) first.

Each item below has an identifier, a **Detect** step (what to search for: it is meant to be scripted), the **Do** step,
the **Behavior** and a **Check**.

## Contents

| Id | Area | Breaking? |
|---|---|---|
| [U1](#u1-the-builders-configuration-ckonfjson-the-hosts-appsettingsjson-are-projections) | The builder's configuration: the `ckonf.json` of a builder (its Apps, their party, the client applications declared with their features and their type set, the aspects); the hosts' `appsettings.json` are generated from it | Yes (the builders, the hosts' configuration) |
| [U3](#u3-the-client-features) | The client features: a `PartyClient` is a bag of `ClientFeature` ("NgApp", "Hosting"), `IPartyClientFactory` and `TypeScriptClient` are gone | Yes (the code that builds or reads clients, the running status) |
| [U2](#u2-one-httpendpoint-per-local-party) | One HttpEndpoint per local party: `Local:HttpEndpoint` has its `Urls` (or `Kestrel`) directly, `ConfigureHttpEndpoint`, `HttpEndpointFeatureDriver.Endpoint` | Yes (the configuration of the parties, the application's configuration of the endpoint) |
| [U4](#u4-the-features-are-declared-by-their-key-allowfeatures-and-disallowfeatures-are-gone) | The features are declared by their key (`true`, the section, `false`): `AllowFeatures` and `DisallowFeatures` are gone | Yes (configuration) |
| [U5](#u5-the-feature-drivers-declare-their-feature-partyfeature) | The feature drivers declare their feature with `[PartyFeature( "Name", TypeActivation.X )]` (the base constructor takes the service only), the client features with `[PartyClientFeature]`, the abstractions their cardinality, the maps of the tests list the generated directories | Yes (the feature drivers, the client features, the tests) |
| [U6](#u6-the-description-of-a-party-is-intrinsic) | The description of a party is intrinsic: `PartyDescriptionFeatureDriver` is gone | Yes (the tests) |
| [U7](#u7-the-keys-of-a-party-are-a-closed-set) | The keys of a party are a closed set: the settings move into their feature's section (KeyManagement, Transport, `AuthCenterRemote:Party`, the CngKeyStore and PasswordLost options) | Yes (configuration) |
| [U8](#u8-an-option-declares-its-data) | An option declares its name and its activation (`[PartyFeatureOption]`, `[PartyClientFeatureOption]`), reads its `IConfiguration` in its constructor, its owner holds it (`GetOption<T>`): `Initialize` and `InitializeOptions` are gone | Yes (the options, their owners) |
| [U9](#u9-the-renamed-packages-cktestingengine-cktypecache-cktypeset) | The renamed packages: the testing packages that run the engine are `CK.Testing.Engine.*`, CK.Engine.TypeCollector is CK.TypeCache (and CK.TypeSet), with their namespaces | Yes (the package references, two namespaces) |

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
   (`Logging`, `CK-Monitoring`...); the features are declared by their key (U4). Plus the compile time keys below:

   ```jsonc
   // Front.Builder/ckonf.json
   {
     "Apps": {
       "Front": {
         "FullName": "Sample/Auth3/$Front/#Dev",
         "Local": {
           "HttpEndpoint": { "Urls": "http://localhost:5300" },
           "PartyClient": {
             "App": { "NgApp": true, "Hosting": { "RequestPath": "/", "DevServerPort": 4200 } },
             "MiniApp": {
               "Types": [
                 "CK.Ng.Cris.AspNet.CrisAspNetPackage, CK.Ng.Cris.AspNet",
                 "CK.Sample.Front.IFrontInfoQCommand, Front.App",
                 "CK.TS.Angular.NgAppClientFeature, CK.TS.Angular",
                 "CK.TypeScript.HostingClientFeature, CK.TS.AspNet.Clients"
               ],
               "NgApp": true,
               "Hosting": { "DevServerPort": 4201 }
             }
           }
         },
         "Parties": [ { "PartyName": "$Auth3", "AuthCenterRemote": true, "Address": "tcp:127.0.0.1:37300" } ],
         "Logging": { "LogLevel": { "Default": "Warning" } }
       }
     }
   }
   ```

   The root of `ckonf.json` has `"Apps"`, `"Aspects"` (see step 3) and `"ProjectCompileKeys"` (see the Behavior); any
   other key is an error.

2. **Move the client applications into their declaration.** A client is a bag of client features. A
   `<TypeScript Name="X" ...>` of the BinPath (a client application served by its host) becomes the declared client
   `"Local:PartyClient:X"` with:
   - **its type set:** the `Types` and `ExcludedTypes` of the `<TypeScript>` become the `"Types"` and `"ExcludedTypes"` of
     the client (a type is its assembly qualified name, or an object whose keys are its attributes:
     `{ "Name": "...", "Optional": true }`). **The types drive:** a client with `"Types"` is restricted to them and to what
     they require, and a key doesn't bring its type in: such a client also lists its client features,
     `"CK.TS.Angular.NgAppClientFeature, CK.TS.Angular"` for `"NgApp"` and
     `"CK.TypeScript.HostingClientFeature, CK.TS.AspNet.Clients"` for `"Hosting"` (otherwise the build fails: the key names a
     type that is not a member of the set). The TypeScript engine reads the computed set: its TypeScript packages (the
     listed ones and what they require) and the Pocos it lists; the other listed types (the client features) are not
     TypeScript types. A client without `"Types"` has all the types of its App;
   - **its framework, `"NgApp"`** (an Angular application, CK.TS.Angular): the other attributes of the `<TypeScript>`
     become its keys: `TargetProjectPath`, `DefaultTypeScriptVersion`, `IntegrationMode`, `InstallYarn`,
     `GitIgnoreCKGenFolder`, `AutoInstallJest`, `EnableTSProjectReferences`, `DefaultCulture`, `ActiveCultures` (a string
     or an array), `TypeFilterName` and the list `Barrels`. Without any of them: `"NgApp": true` (an empty object `{}` is
     no declaration);
   - **its hosting, `"Hosting"`** (CK.TS.AspNet.Clients): `Kind="DefaultApp"` becomes `"RequestPath": "/"` (an `App`
     needs nothing: its request path is `/{kebab-name}`, `"RequestPath"` sets another one), `DevServerPort` its
     `"DevServerPort"`, `UseDist="true"` its `"UseDist": true` (a run time key); the options of the client move into it:
     `"Hosting": { "StaticFiles": { ... } }`.

   Any other key of a client is an error. A client that no build feature handles (no `"NgApp"`) is a build error, and a
   `"NgApp"` client without `"Hosting"` is not supported yet. Two clients at the same request path are an error.

   A local party can have a type set too, in its `Local` (`Parties[i]:Local` for a tenant domain): `"Types"` and
   `"ExcludedTypes"`, compile time keys. Its exclusions apply to its clients (a client that adds a type that its party
   excludes is an error); its additions are added to the App.

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
  time keys (the type sets, the keys of "NgApp", the `RequestPath` and `DevServerPort` of "Hosting", the `Aspects`, the App
  keys; a feature whose keys are all compile time is written `true`). **The keys of the party are in its
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
  reference (`CK.AppIdentity.Engine`, M30 of the cris-auth guide; `CK.TS.Angular.Engine` for the "NgApp" clients;
  `CK.Globalization.Engine`), in their versions of the `explo/uc` branch.

**Check:** the builder's log has "Reading '.../ckonf.json'." and "written from 'ckonf.json'"; the host's
`appsettings.json` starts with the "Generated" line; the generated code (`$StObjGen`) and the `ck-gen` of the client
applications are unchanged by the migration. CK-Sample-AspNetAuth (five builders), CK-Sample-MultiBinPath (two
Apps in one builder), the samples of CK-Ng-Admin, CK-Ng-User-UserProfile, CK-Ng-Zorro-Contrib and
CK-Observable-Domain are migrated this way.

## U2. One HttpEndpoint per local party

A local party has one HTTP endpoint: one origin (its public URL), one pipeline. The named endpoints of
`Local:HttpEndpoint` are gone: its `Urls` (or its `Kestrel` section) and its options are directly in it. Several listening
addresses don't need several endpoints: `Urls` takes several (separated by `;`, or an array), a `Kestrel` section
several Kestrel endpoints.

**Detect:** a `"HttpEndpoint": { "<Name>": { ... } }` in a `ckonf.json`, an `appsettings.json` or a test
configuration (a key `Local:HttpEndpoint:<Name>:Urls`, `Local:HttpEndpoint:<Name>:Kestrel`,
`Local:HttpEndpoint:<Name>:ForwardedHeaders`...); a call to `AddHttpEndpoint`; a use of `HttpEndpointFeatureDriver.Endpoints`,
`RunningHttpEndpoint.Name` or `AspNetServerTestHelperExtensions.EndpointName` (CK.Testing.AspNetServer); a script that
reads the `Http` section of a `DevRunningStatus.json` as an array.

**Do:**

1. **Remove the endpoint level** of `Local:HttpEndpoint` (in the `ckonf.json` of a builder: the `appsettings.json` of the
   host is regenerated, see U1):

   ```jsonc
   // Before.
   "HttpEndpoint": { "Main": { "Urls": "http://localhost:5300", "ForwardedHeaders": { "KnownProxies": "10.0.0.12" } } }
   // After.
   "HttpEndpoint": { "Urls": "http://localhost:5300", "ForwardedHeaders": { "KnownProxies": "10.0.0.12" } }
   ```

   The keys of a test configuration follow: `Local:HttpEndpoint:Main:Urls` becomes `Local:HttpEndpoint:Urls`.
2. **Replace `AddHttpEndpoint( name, configure )`** by `HttpEndpointHostApplicationBuilderExtensions.ConfigureHttpEndpoint( configure )`
   (namespace `Microsoft.Extensions.Hosting`, package CK.AppIdentity.HttpEndpoint):

   ```csharp
   builder.ConfigureHttpEndpoint( web => web.AppendApplicationBuilder( app => app.UseMiddleware<MyMiddleware>() ) );
   ```

   It can be called more than once: the configurations apply in their order, after the contributions of the packages.
   A call without configuration is removed.
3. **Replace `HttpEndpointFeatureDriver.Endpoints`** (a list) by `HttpEndpointFeatureDriver.Endpoint`: the
   `RunningHttpEndpoint` (its `Application`, `Urls` and `DefaultUrl`; it has no `Name` any more), null when it is not
   running. `AspNetServerTestHelperExtensions.EndpointName` is removed: `CreateRunningAspNetServerAsync` sets
   `Local:HttpEndpoint:Urls` to `http://127.0.0.1:0` unless `Urls` or `Kestrel` is configured.
4. **A script that reads the running status** (`#Dev/Store/.../DevRunningStatus.json`) reads `Http` as an object:
   `{ "Urls": [...], "DefaultUrl": "..." }` (null when the endpoint is not running). The `#Dev/Start.ps1` of
   CK.AppIdentity.Hosting does.

**Behavior:**
- A `Local:HttpEndpoint` without `Urls` nor `Kestrel` (or with both) fails the start of the party; a child section that
  has `Urls` or `Kestrel` is reported as a named endpoint ("the endpoint level is removed: move its content into
  Local:HttpEndpoint").
- The options of the endpoint (`ForwardedHeaders`) are the party's: their messages name `Local:HttpEndpoint:<Option>`.
- An `HttpEndpointOption` (a package's option) reads its own section in its constructor (U8).
- Only the root party has an endpoint, as before.

**Check:** no `HttpEndpoint` section has a named child, no `AddHttpEndpoint` nor `Endpoints` is left, and the party
answers on its `Urls`. CK-Sample-AspNetAuth, CK-Sample-MultiBinPath and the samples of CK-Ng-Admin, CK-Ng-User-UserProfile,
CK-Ng-Zorro-Contrib and CK-Observable-Domain are migrated this way.

## U3. The client features

A client of a party is a bag of client features, as a party is a bag of features: what it is comes from its features,
not from its type. The configuration side is in U1 (step 2); this item is about the code.

**Detect:** a use of `IPartyClientFactory`, of a class that specializes `PartyClient`, of `PartyClient.Options` or
`PartyClient.InitializeOptions`, of `TypeScriptClient`, `TypeScriptClientFactory` or `TypeScriptClientOption`, of
`ClientInfo.UseDist` or `TypeScriptBinPathAspectConfiguration.UseDist`, or of the `Kind` of the `Clients` section of a
`DevRunningStatus.json`.

**Do:**

1. **A kind of client becomes a client feature:** a class that specializes `ClientFeature` (CK.AppIdentity.PartyClient)
   and declares its name (the key of its section in the client) and its activation with
   `[PartyClientFeature( "XXX", TypeActivation.Optional )]` (see U5), and its driver, a
   `ClientFeatureDriver<XXXClientFeature>` that creates it (`Create( monitor, client, configuration )`, the
   `configuration` being the section of the feature) instead of an `IPartyClientFactory`. Its requirements are
   `[Requires<T>]` on the class: a feature of the party (`[Requires<HttpEndpointFeatureDriver>]`, instead of
   `RequiredFeatures`) or an abstraction implemented by another feature of the client (an interface that extends
   `IPartyComponentAbstraction`, `IWebAppBuild` for instance). Its description: override
   `ClientFeature.ContributeDescription( builder )` (`builder.RequestPath`, `builder.AddFacts`) instead of
   `PartyClient.Description`.
2. **`PartyClient` is a container:** read a client's features with `PartyClient.Features` or `GetFeature<T>()` (a
   feature type or an abstraction): `client.GetFeature<HostingClientFeature>()` instead of a cast to `TypeScriptClient`.
3. **The options of a client are the options of a client feature:** an option is an `IPartyComponentOption<TFeature>`
   (`[PartyClientFeatureOption]`, see U8), selected from the section of the feature and created with it: `ClientFeature.Options`,
   `ClientFeature.GetOption<T>()`. An option reads its configuration in its constructor (U8). `TypeScriptClientOption`
   becomes `HostingClientOption`, and `StaticFilesOption` is an option of the "Hosting" feature.
4. **`TypeScriptClient` becomes two features:** `NgAppClientFeature` (CK.TS.Angular: `Info`, the `ClientInfo`; the web
   build `IWebAppBuild`) and `HostingClientFeature` (CK.TS.AspNet.Clients: `Info`, `RequestPath`, `UseDist`,
   `StaticFiles`, `Build`). `ClientInfo.UseDist` and `TypeScriptBinPathAspectConfiguration.UseDist` are gone: it is the
   run time key `"Hosting:UseDist"`. `TypeScriptBinPathAspectConfiguration.RequestPath` can be set (it sets the `Kind`).
5. **A script that reads the running status:** the `Clients` section has no `Kind`: the default client application is
   the one whose `RequestPath` is `/`.

**Behavior:**
- A party component (`IPartyComponent`) is a resource group (`IResourceGroup`): the topology attributes declare its
  requirements. The engine generates the `PartyComponentDirectory` (CK.AppIdentity): their requirements, their
  abstractions and their order. A requirement that is neither a party component nor an abstraction is an engine error.
- At start, the "PartyClient" feature checks the features of each client: a key that is no feature (nor its type set), a
  client without feature, a missing requirement, two implementations of a unique abstraction are errors. A feature is
  declared by its section or by `true`.

**Check:** no `IPartyClientFactory`, `TypeScriptClient` nor `TypeScriptClientOption` is left, and the clients start.
CK-TS-AspNet (`HostingClientFeature`), CK-TS-Angular (`NgAppClientFeature`) and the tests of CK-AppIdentity are migrated
this way.

## U4. The features are declared by their key (`AllowFeatures` and `DisallowFeatures` are gone)

A party declares a feature by its key: `true` or the feature's section declares it, `false` turns off a feature that is
allowed by default. The declarations are looked up in the party's own keys, then in the `"Defaults"` above it: the
nearest level wins.

**Detect:** `AllowFeatures` or `DisallowFeatures` in a `ckonf.json`, an `appsettings.json` (a projection: regenerate it,
see U1) or a test configuration (`c["Local:AllowFeatures"]`, `c["Parties:0:AllowFeatures"]`,
`c["Defaults:DisallowFeatures"]`...).

**Do:**

1. **Each name of `AllowFeatures` becomes its key, `true`**, unless the party already has the feature's section (the
   section declares it: only remove the name). Each name of `DisallowFeatures` becomes its key, `false`. A comma-separated
   string or an array gives several keys. The names don't change: on a remote, the asymmetric features keep their
   "XXXRemote" name. From CK-Sample-AspNetAuth:

   ```jsonc
   // Before.
   "Local": { "AllowFeatures": "HttpEndpoint", "HttpEndpoint": { "Urls": "http://localhost:5300" } },
   "Parties": [ { "PartyName": "$Auth3", "AllowFeatures": "AuthCenterRemote", "Address": "tcp:127.0.0.1:37300" } ]
   // After.
   "Local": { "HttpEndpoint": { "Urls": "http://localhost:5300" } },
   "Parties": [ { "PartyName": "$Auth3", "AuthCenterRemote": true, "Address": "tcp:127.0.0.1:37300" } ]
   ```

2. **In a test configuration**, a `true` that the test then completes with settings is a conflict (a configuration key
   can't have a value and children): `c["Local:AuthCenter"] = "true"` followed by
   `c["Local:AuthCenter:CriticalLifetimes:Basic"] = "00:05:00"` throws "Unable to set ... since 'Local:AuthCenter' above
   has value 'true'". Write only the settings (the section declares the feature), or clear the value first:
   `c["Local:AuthCenter"] = null;`.

**Behavior:**
- A key whose value is `true` or `false` and that names no feature of the application is reported: a warning, an error
  in `StrictConfigurationMode` (a typo could leave a feature allowed by default on). Every key of a party is now checked
  (U7).
- On a remote, the key of the Local side of an asymmetric feature ("AuthCenter") is an error, unless it is `false`: a
  remote declares "AuthCenterRemote". A local party can declare "XXXRemote" (the Remote side, for the remotes added
  later).
- The declarations follow the activation of the feature (U5): `false` on a `Required` feature (or client feature, or
  option) is an error, the party doesn't start; a redundant `true` (on a feature allowed by default or required) or
  `false` (on an optional one) is a warning. A `ByDefault` or `Required` client feature is there without its key.
- **The types drive:** a key that declares a feature, a client feature or an option of a feature's section (`true` or a
  section) must name a member of the type set of its party (of its local party for a remote) or of its client. A
  party or a client without `"Types"` has all the types of its App: only a restricted set (with `"Types"`) can make it
  fail. It is a build error (the engine checks the projection of the party) and a start error; `false` is fine.
- Declaring a client (`Local:PartyClient:X`) declares the "PartyClient" feature.

**Check:** no `AllowFeatures` nor `DisallowFeatures` is left and the parties start without "names no feature" warnings.
CK-Sample-AspNetAuth, CK-AspNet, CK-AppIdentity-Transport and CK-Mailer are migrated this way.

## U5. The feature drivers declare their feature: `[PartyFeature]`

The name of a feature, its activation and the side of an asymmetric feature are declared by an attribute on each
concrete driver and read by the engine (in the generated `PartyComponentDirectory`, CK.AppIdentity): the constructor
arguments of `ApplicationIdentityFeatureDriver` are gone. A feature driver, a client feature and an option are
**configurable types** (`IConfigurableTypeAttribute`, CK.ResourceSpace.Abstractions): the configuration names them (the
key of their section) and switches them; the name is explicit, it doesn't come from the type name.

**Detect:** a concrete class that specializes `ApplicationIdentityFeatureDriver` or `ApplicationIdentityStaticFeatureDriver`
(directly or not: every concrete driver), a concrete class that specializes `ClientFeature` (U3); a call to
`base( s, ... )` with more than the service; `ClientFeature.GetFeatureName`; an interface that extends
`IPartyComponentAbstraction`; a test that builds a map with `Types.Add( typeof( ApplicationIdentityService ), ... )`
or that registers feature drivers by hand (`services.AddSingleton<XXXFeatureDriver>()`).

**Do:**

1. **Every concrete feature driver declares its feature:** `[PartyFeature( "Name", TypeActivation.X )]` (namespace
   `CK.AppIdentity`; `TypeActivation` is in `CK.Core`). The attribute is not inherited: each concrete driver has its own,
   a driver without it is an engine error. The base constructor takes the service only: `base( s )`.
   - **The name** is the key of the feature in a party: keep the former name, the type name without its "FeatureDriver"
     suffix (`HttpEndpointFeatureDriver` is `"HttpEndpoint"`).
   - **The activation** replaces `isAllowedByDefault`: `TypeActivation.Optional` (was `false` or nothing: a party has the
     feature only when it declares it), `TypeActivation.ByDefault` (was `true`: a party has it unless it says `false`),
     or `TypeActivation.Required` (a party always has it). From CK.AppIdentity.KeyManagement:

     ```csharp
     [PartyFeature( "KeyManagement", TypeActivation.ByDefault )]
     public partial class KeyManagementFeatureDriver : ApplicationIdentityFeatureDriver
     {
         public KeyManagementFeatureDriver( ApplicationIdentityService s, IDataProtectionProvider protectorProvider, ICoreKeyStore keyStore )
             : base( s )
     ```

   - **An asymmetric feature** (`base( s, "AuthCenter" )`): the feature name and the side on each driver, and **the Local
     side requires the Remote side** (its package references the Remote side's package; from
     CK.AppIdentity.AuthCenter.Local and .Remote). The configuration name of the Remote side is the feature name followed
     by "Remote" ("AuthCenterRemote"), computed by the attribute; the two sides have the same activation:

     ```csharp
     [PartyFeature( AuthCenterFeature.Name, TypeActivation.Optional, Side = FeatureSide.Local )]
     [Requires<RemoteAuthCenterFeatureDriver>]
     public sealed class LocalAuthCenterFeatureDriver : ApplicationIdentityFeatureDriver

     [PartyFeature( AuthCenterFeature.Name, TypeActivation.Optional, Side = FeatureSide.Remote )]
     public sealed class RemoteAuthCenterFeatureDriver : ApplicationIdentityFeatureDriver
     ```

     A driver common to the two sides is an abstract base class (it has no attribute); consider first a static helper
     that both sides call.
2. **Every concrete client feature declares its name:** `[PartyClientFeature( "Name", TypeActivation.X )]`
   (CK.AppIdentity.PartyClient; a client has an `Optional` feature only when it declares it). From CK.TS.Angular:

   ```csharp
   [PartyClientFeature( "NgApp", TypeActivation.Optional )]
   public sealed class NgAppClientFeature : ClientFeature, ITypeScriptFramework, IWebAppBuild
   ```

   `ClientFeature.Name` is available once the feature is created.
3. **A party component abstraction declares its cardinality:** an interface that extends `IPartyComponentAbstraction`
   carries `[IsSingle]` (at most one implementation in a party or a client) or `[IsMultiple]` (namespace `CK.Core`).
4. **The tests:**
   - a test that builds a map and lists its types adds the generated directories, `typeof( PartyComponentDirectory )`
     (and `typeof( PartyComponentOptionDirectory )` when options are used), and both sides of an asymmetric feature
     (`RemoteAuthCenterFeatureDriver` with `LocalAuthCenterFeatureDriver`);
   - a test that assembles its services without the engine uses CK.Testing.AppIdentity (CK-AppIdentity, `explo/uc`
     versions): `services.AddApplicationIdentityForTests( configuration, drivers, otherComponents )`, or
     `services.AddPartyComponentDirectory( otherComponents )` once its drivers are registered, and
     `services.AddPartyComponentOptions( optionTypes )` for the options (U8).

**Behavior:**
- A name is made of letters, digits and dots, starting with a letter (no `_`), and is unique in its scope: among the
  feature drivers (the keys of a party, the "XXXRemote" ones included) and among the client features (the keys of a
  client); a party feature and a client feature can have the same name. The engine reports a duplicate, a missing
  attribute, an attribute on a type that is not a concrete package, a `TypeActivation.None`.
- The two sides of an asymmetric feature agree on their activation.
- **The scopes are checked at build time and at start:** the features a local party has (its declared ones and the ones
  that are not optional and not turned off) and the features of each client (the same, with the features of its party as
  its outer scope): a required feature (`[Requires<T>]`) or abstraction must be there, a single abstraction (`[IsSingle]`)
  is implemented at most once. The uniqueness is the one of what is declared, not of what is available: a client that
  declares two `IWebAppBuild` is an error, a client that can have both is fine. A party that declares a feature that
  requires another one must declare it too (it was only seen when a client required it).
- The activation is also the optionality of the package (`IsOptional`): the engines that process a type set apply it
  (the ResourceSpace keeps an optional package only when something requires it). A type set that nothing restricts has
  all the types of its App, the optional drivers too: a party has an `Optional` feature when it declares it.
- A driver whose type is not in the `PartyComponentDirectory` throws at construction ("is not a party component of the
  application"); a service without the directory throws "No PartyComponentDirectory".
- An abstraction without `[IsSingle]` nor `[IsMultiple]` is an engine error, as a requirement that is neither a party
  component nor an abstraction (the Local side of an asymmetric feature without its Remote side, for instance).

**Check:** every concrete feature driver has its `[PartyFeature]` and every concrete client feature its
`[PartyClientFeature]`, no `base( s, ... )` with more than the service is left, the engine runs and the parties start.
CK-AspNet, CK-AppIdentity-Transport, CK-Mailer, CK-Cris, CK-Metrics, CK-Object-Mixer, CK-TS-AspNet and CK-TS-Angular are
migrated this way.

## U6. The description of a party is intrinsic

The description of a party (its public URL, its routes, its client applications...) is computed by the
`ApplicationIdentityService` itself, after the feature drivers have set the parties up: it is not a feature any more.

**Detect:** `PartyDescriptionFeatureDriver` (a test that lists it in its types or registers it); a `"PartyDescription"`
key in a configuration.

**Do:** remove them. A driver contributes to the descriptions as before (`ContributeDescription`,
`ContributeDerivedDescription`, `OnDescriptionChangedAsync`).

**Behavior:** every party is described; `PublicUrl` is a key of the local party (U7).

**Check:** no `PartyDescriptionFeatureDriver` is left. CK-AspNet and CK-AppIdentity-Transport are migrated this way.

## U7. The keys of a party are a closed set

The keys of a party are its own keys or features (`true`, `false` or the feature's section). The settings of a feature
are in its section, and what a package adds to a feature is an option of the feature (U8), in its section too.

**Detect:** in a `ckonf.json`, an `appsettings.json` (a projection: regenerate it) or a test configuration, the keys of the
table below directly in a `Local`, a remote (`Parties[i]`) or a `"Defaults"`.

**Do:** move them.

| Before | After |
|---|---|
| `RecoveryPublicKey`, `OperationalKeyDays`, `MaxSignatureDays`, `KeyRotationDays`, `TrustAnchors` (a `Local`) | `"KeyManagement": { ... }` |
| `AutoTrustKey`, `MaxClockOffset` (a remote, a `"Defaults"`) | `"KeyManagement": { ... }` |
| `ListeningAddress`, `ListeningTypes`, `NegotiationTimeout`, `AlwaysListening`, `DisallowEviction`, `RouteHosts` (of a party that is not an AuthCenter) | `"Transport": { ... }` |
| `AuthCenterParty` (a `Local`) | `"AuthCenterRemote": { "Party": "..." }` |
| `CoreKeyStore:CngProvider`, `CoreKeyStore:AllowFallback`, `CoreKeyStore:MachineWide` (CK.AppIdentity.KeyManagement.Cng) | `"KeyManagement": { "CngKeyStore": { "Provider": "...", "AllowFallback": ..., "MachineWide": ... } }` |
| `PasswordLostTokenLifetime` (a `Local`, CK.UserProfile.UserPassword.Lost) | `"AuthCenter": { "PasswordLost": { "TokenLifetime": "02:00:00" } }` |

The `RouteHosts` of an Authentication Center stay in its `"AuthCenter"` section. From CK-Sample-AspNetAuth (Worker2 and
Auth3):

```jsonc
// Before.
"Local": { "AuthCenterParty": "Sample/Auth3/$Auth3/#Dev", "TrustAnchors": { "Sample/Auth3/$Auth3/#Dev": "#Dev" } },
"Defaults": { "ListeningAddress": [ "tcp:127.0.0.1:37300" ] },
"Parties": [ { "PartyName": "$Front", "AuthCenterRemote": true, "AutoTrustKey": "Once" } ]
// After.
"Local": {
  "AuthCenterRemote": { "Party": "Sample/Auth3/$Auth3/#Dev" },
  "KeyManagement": { "TrustAnchors": { "Sample/Auth3/$Auth3/#Dev": "#Dev" } }
},
"Defaults": { "Transport": { "ListeningAddress": [ "tcp:127.0.0.1:37300" ] } },
"Parties": [ { "PartyName": "$Front", "AuthCenterRemote": true, "KeyManagement": { "AutoTrustKey": "Once" } } ]
```

In a test configuration: `c["Parties:0:AutoTrustKey"]` becomes `c["Parties:0:KeyManagement:AutoTrustKey"]`,
`c["Defaults:ListeningAddress:0"]` becomes `c["Defaults:Transport:ListeningAddress:0"]`.

**Behavior:**
- **The keys of a party:** in a `Local`, `PublicUrl`, `Assemblies` and `DefaultAssembly`; on a remote, its identity
  (`FullName`, `DomainName`, `PartyName`, `EnvironmentName`), `Address`, `Assemblies` and `DefaultAssembly`. Any other key
  must be a feature: an unknown key is a warning, an error in `StrictConfigurationMode`.
- A section declares its feature (U4): `"Defaults": { "Transport": { ... } }` declares Transport for the parties below
  (KeyManagement and Transport are allowed by default anyway).
- A setting is looked up as before (the party's own section, then the nearest `"Defaults"`), at its new path. The local
  settings (`RecoveryPublicKey`...) are still read from the `Local` only.
- **CngKeyStore** is an option of KeyManagement, enabled by default when CK.AppIdentity.KeyManagement.Cng is referenced
  (as before): `"KeyManagement": { "CngKeyStore": false }` puts the keys of a party in files. Its section is resolved as a
  whole (the party's, otherwise the nearest `"Defaults"`'s), not value by value. An invalid value (an unknown key, a value
  that is not a boolean) fails the setup instead of a warning. `CngCoreKeyStore`'s constructor is
  `( DefaultCoreKeyStore fallback, IServiceProvider? services )`.
- **PasswordLost** is an option of the AuthCenter (CK.UserProfile.UserPassword.Lost now references
  CK.AppIdentity.AuthCenter.Local), enabled by default: `"AuthCenter": { "PasswordLost": false }` turns the lost
  password flow off (no recovery mail, the anonymous command answers the same; a recovery link is refused). An
  application that is not an Authentication Center has no lost password flow. `PasswordLostTokenService.FallbackLifetime`
  is `PasswordLostOption.DefaultTokenLifetime`, and the service has `Option` and `IsEnabled`.
- A configured `EnlistRemoteUrl` is still a start error (it is no more supported), wherever it is.

**Check:** the parties start without "unknown key" warnings. CK-Sample-AspNetAuth, CK-Ng-Admin, CK-Ng-User-UserProfile and
the tests of CK-AppIdentity-Transport are migrated this way.

## U8. An option declares its data

An option (`IPartyComponentOption<TOwner>`: an option of a feature driver or of a client feature) declares its name and
its activation, reads its configuration in its constructor, and its owner holds it.

**Detect:** `[FeatureOption]` or `[PartyClientOption]`; an override of `HttpEndpointOption.Initialize`, `HostingClientOption.Initialize` or
`ClientFeature.InitializeOptions`; a call to `PartyComponentOptionInfo.Create( services )`; an owner that selects its
options itself (`PartyComponentOptionDirectory.TrySelect` followed by `Create`).

**Do:**

1. **The attribute names the option:** `[FeatureOption]` becomes `[PartyFeatureOption( "Name", TypeActivation.X )]`
   (CK.AppIdentity.Abstractions), `[PartyClientOption]` becomes `[PartyClientFeatureOption( "Name", TypeActivation.X )]`
   (CK.AppIdentity.PartyClient). Keep the former name, the type name without its "Option" suffix; `IsEnabledByDefault = true`
   becomes `TypeActivation.ByDefault`, nothing (opt-in) `TypeActivation.Optional`. From CK.UserProfile.UserPassword.Lost:

   ```csharp
   [PartyFeatureOption( "PasswordLost", TypeActivation.ByDefault )]
   public sealed class PasswordLostOption : IPartyComponentOption<LocalAuthCenterFeatureDriver>
   ```

   `PartyComponentOptionInfo.IsEnabledByDefault` is computed from its `Activation`.
2. **The option's constructor reads its configuration:** a parameter of type `IConfiguration`
   (Microsoft.Extensions.Configuration; a plain one, at most one) is the option's section, null when the option is enabled
   by default or by `true`. It reads it into its properties and **throws on an invalid value**; the other parameters are
   services, as before. From CK.AppIdentity.HttpEndpoint:

   ```csharp
   [PartyFeatureOption( "ForwardedHeaders", TypeActivation.Optional )]
   public sealed class ForwardedHeadersOption : HttpEndpointOption
   {
       public ForwardedHeadersOption( IConfiguration? configuration )
       {
           if( configuration == null )
           {
               Throw.CKException( "It must be a section with \"KnownProxies\" or \"KnownNetworks\": the proxies are never trusted by default." );
           }
           // ... reads the keys, Throw.CKException( ... ) on an invalid value ...
           Options = options;
       }

       public ForwardedHeadersOptions Options { get; }
   ```

   The messages don't need the configuration path: the owner logs "Unable to create the option '{path}': {message}".
3. **An owner gets its options:** a feature driver reads the options of a party with `GetOptions( party )` or
   `GetOption<T>( party )` (null when it is disabled): the base class creates them before the driver's setup. A client
   feature reads `Options` or `GetOption<T>()`. `ClientFeature.InitializeOptions` is gone. A service of the package that
   brings an option asks the owner (`keyManagement.GetOption<CngKeyStoreOption>( party )`): it never reads the
   configuration itself.
4. **An owner that checks the keys of its section** accepts its option names:
   `PartyComponentOptionDirectory.IsOptionName( ownerType, key )`.
5. **A test without engine** registers its options with `services.AddPartyComponentOptions( typeof( MyOption ) )`
   (CK.Testing.AppIdentity): they are created by reflection, in the given order (no requirements).

**Behavior:**
- The option of a feature driver is looked up in the feature's section of the party, then in the nearest `"Defaults"`
  (its whole section).
- An option that throws fails the setup of the party (an option of a feature driver) or makes the client an error (an
  option of a client feature).
- The name of an option is unique among the options of its owner; a `TypeActivation.Required` option is always enabled.
- The engine refuses more than one `IConfiguration` parameter and a parameter of a derived configuration type
  (`IConfigurationSection`, `ImmutableConfigurationSection`).

**Check:** no `[FeatureOption]`, `[PartyClientOption]`, `Initialize` nor `InitializeOptions` override is left and the engine runs. CK-AspNet (`ForwardedHeadersOption`),
CK-TS-AspNet (`StaticFilesOption`), CK-AppIdentity-Transport (`CngKeyStoreOption`) and CK-Ng-User-UserProfile
(`PasswordLostOption`) are migrated this way.

## U9. The renamed packages: `CK.Testing.Engine.*`, CK.TypeCache, CK.TypeSet

"Engine" in a package name now means that the package generates and compiles C# code. The testing packages that run
the engine are named `CK.Testing.Engine.*`; the cache of the types and the type sets, which generate nothing (a
configuration tool can use them), lose "Engine".

| Was (explo/cris-auth) | Is (explo/uc) | Repository, version line |
|---|---|---|
| CK.Testing.StObjEngine | CK.Testing.Engine | CK-StObj, 37.0.0-0.uc |
| CK.Testing.AspNetServer | CK.Testing.Engine.HttpEndpoint | CK-AspNet, 25.0.0-0.uc |
| CK.Testing.SqlServerEngine | CK.Testing.Engine.SqlServer | CK-Database, 37.0.0-0.uc |
| CK.Testing.SqliteEngine | CK.Testing.Engine.Sqlite | CK-Sqlite, 19.0.0-0.uc |
| CK.Testing.TypeScriptEngine | CK.Testing.Engine.TypeScript | CK-TypeScript, 4.0.0-0.uc |
| CK.Testing.AppIdentity.Cris | CK.Testing.Engine.AppIdentity.Cris | CK-AppIdentity-Transport, 4.0.0-0.uc |
| CK.Testing.AppIdentity.AuthCenter | CK.Testing.Engine.AppIdentity.AuthCenter | CK-AppIdentity-Transport, 4.0.0-0.uc |
| CK.Testing.Cris.Authentication | CK.Testing.Engine.Cris.Authentication | CK-Cris, 3.0.0-0.uc |
| CK.Engine.TypeCollector (CK-StObj) | CK.TypeCache, and CK.TypeSet for `BinPathTypeGroup` | CK-ResourceSpace and CK-Engine, 36.0.2-0.uc |

CK.Testing.AppIdentity and CK.Testing.AppIdentity.TransportLayer keep their names (they don't run the engine).

**Detect:** a `<PackageReference>` to a name of the first column; `using CK.Engine.TypeCollector;`;
`using CK.Testing.AppIdentity.Cris;`; a `CKDEPRECATED` build error.

**Do:**
1. Replace the package references (the second column, the version of its repository's line). A test project rarely
   references CK.TypeCache or CK.TypeSet directly: CK.Engine brings them.
2. `using CK.Engine.TypeCollector;` becomes `using CK.TypeCache;` (`GlobalTypeCache`, `ICachedType`, `AssemblyCache`,
   the engine attributes' implementations), plus `using CK.TypeSet;` where `BinPathTypeGroup` is used (the project must
   reference CK.TypeSet, directly or through CK.Engine).
3. `CK.Testing.AppIdentity.Cris` (the namespace of CK.Testing.AppIdentity.Cris: `RunningApplication`,
   `RunningApplicationPair`, the test commands) becomes `CK.Testing.Engine.AppIdentity.Cris`.

The helpers of the other testing packages stay in the `CK.Testing` namespace: their consumers change only the
package reference.

**Behavior:** the last version of CK.Testing.StObjEngine, CK.Testing.AspNetServer, CK.Testing.SqlServerEngine,
CK.Testing.SqliteEngine, CK.Testing.TypeScriptEngine and CK.Testing.AppIdentity.Cris is a tombstone: a project that
depends on it, directly or not, fails with a `CKDEPRECATED` error that names the replacement. The generated
`jest.config.js` of the TypeScript tests changes its comment ("CK.Testing.Engine.TypeScript uses this"): a build impact
to commit.

**Check:** no name of the first column is left in a `.csproj` and no `CK.Engine.TypeCollector` in the code; the
repository builds. Every repository of the World is migrated this way.

## The final checklist

1. Every builder has a `ckonf.json` (its `"Apps"` with the keys of their party directly in them, the client
   applications declared with their features `"NgApp"` and `"Hosting"`, the `"Aspects"`), its `CKSetup.xml` has no `<BinPaths>` (or is gone), and
   the hosts' `appsettings.json` start with the "Generated" line, committed (U1).
2. Every `Local:HttpEndpoint` has its `Urls` (or `Kestrel`) directly, `AddHttpEndpoint` is replaced by
   `ConfigureHttpEndpoint` and `Endpoints` by `Endpoint` (U2).
3. No `IPartyClientFactory`, `TypeScriptClient` nor `PartyClient` subclass is left: the clients are bags of client
   features, declared with `NgApp` and `Hosting` (U1, U3).
4. No `AllowFeatures` nor `DisallowFeatures` is left: the features are declared by their key (U4).
5. Every concrete feature driver calls `base( s )` and declares its feature with `[PartyFeature( "Name", TypeActivation.X )]`,
   every concrete client feature with `[PartyClientFeature( "Name", TypeActivation.X )]`; every party component abstraction declares `[IsSingle]` or `[IsMultiple]`; the engine maps of the tests
   list the generated directories, the tests without engine use CK.Testing.AppIdentity (U5).
6. No `PartyDescriptionFeatureDriver` nor `"PartyDescription"` key is left (U6).
7. Every key of a party is one of its own keys or a feature: the settings are in the sections of their feature (U7).
8. Every option declares its name and its activation (`[PartyFeatureOption]`, `[PartyClientFeatureOption]`), no option
   has an `Initialize`: each one reads its `IConfiguration` in its constructor; no
   `ClientFeature.InitializeOptions` override is left (U8).
9. No package reference to a former name of U9 is left, no `CK.Engine.TypeCollector` nor `CK.Testing.AppIdentity.Cris` namespace (U9).
