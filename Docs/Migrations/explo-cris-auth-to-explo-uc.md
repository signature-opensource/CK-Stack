# Migrating from explo/cris-auth to explo/uc

This guide lists what a repository (or a World) that uses the packages of the **`explo/cris-auth`** branch must change
to move to the packages of the **`explo/uc`** branch: the first steps of the Unified Configuration (see the Stack's
`.wip` epic when you have access to it). Apply [the cris-auth guide](v2026.282-to-explo-cris-auth.md) first.

Each item below has an identifier, a **Detect** step (what to search for: it is meant to be scripted), the **Do** step,
the **Behavior** and a **Check**.

## Contents

| Id | Area | Breaking? |
|---|---|---|
| [U1](#u1-the-builders-configuration-ckonfjson-the-hosts-appsettingsjson-are-projections) | The builder's configuration: the `ckonf.json` of a builder (its App or its Apps, their party, the client applications declared with their features and their type set, the aspects); the hosts' `appsettings.json` are generated from it | Yes (the builders, the hosts' configuration) |
| [U3](#u3-the-client-features) | The client features: a `PartyClient` is a bag of `ClientFeature` ("NgApp", "Hosting"), `IPartyClientFactory` and `TypeScriptClient` are gone | Yes (the code that builds or reads clients, the running status) |
| [U2](#u2-one-httpendpoint-per-local-party) | One HttpEndpoint per local party: `Local:HttpEndpoint` has its `Urls` (or `Kestrel`) directly, `ConfigureHttpEndpoint`, `HttpEndpointFeatureDriver.Endpoint` | Yes (the configuration of the parties, the application's configuration of the endpoint) |
| [U4](#u4-the-features-are-declared-by-their-key-allowfeatures-and-disallowfeatures-are-gone) | The features are declared by their key (`true`, the section, `false`): `AllowFeatures` and `DisallowFeatures` are gone | Yes (configuration) |
| [U5](#u5-the-feature-drivers-declare-their-feature-partyfeature) | The feature drivers declare their feature with `[PartyFeature( "Name", TypeActivation.X )]` (the base constructor takes the service only), the client features with `[PartyClientFeature]`, the abstractions their cardinality, the maps of the tests list the generated directories | Yes (the feature drivers, the client features, the tests) |
| [U6](#u6-the-description-of-a-party-is-intrinsic) | The description of a party is intrinsic: `PartyDescriptionFeatureDriver` is gone | Yes (the tests) |
| [U7](#u7-the-keys-of-a-party-are-a-closed-set) | The keys of a party are a closed set: the settings move into their feature's section (KeyManagement, Transport, `AuthCenterRemote:Party`, the CngKeyStore and PasswordLost options) | Yes (configuration) |
| [U8](#u8-an-option-declares-its-data) | An option declares its name and its activation (`[PartyFeatureOption]`, `[PartyClientFeatureOption]`), reads its `IConfiguration` in its constructor, its owner holds it (`GetOption<T>`): `Initialize` and `InitializeOptions` are gone | Yes (the options, their owners) |
| [U9](#u9-the-renamed-packages-cktestingengine-cktypecache-cktypeset) | The renamed packages: the testing packages that run the engine are `CK.Testing.Engine.*`, CK.Engine.TypeCollector is CK.TypeCache (and CK.TypeSet), with their namespaces | Yes (the package references, two namespaces) |
| [U10](#u10-no-tenant-domain-is-added-at-run-time) | No tenant domain is added at run time: `AddTenantDomainAsync`, `AddPartiesAsync` and `AddedDynamicParties` leave the public API, the tenants are configured | Yes (the code that adds tenants, the tests) |
| [U11](#u11-the-mail-features-mailsender-and-mailrelay) | The mail features: "EmailSender" becomes "MailSender" (named senders per local party, the "MailKit" option), the relay "MailSender" becomes "MailRelay" (several relays, a sender each) | Yes (configuration, the sender kinds, the relay packages) |
| [U12](#u12-the-fluid-aspect-is-gone) | The Fluid aspect is gone: the engine of a template package checks its templates | Yes (the builders and tests that configure it) |
| [U13](#u13-pfeature-is-renamed-model) | "PFeature" is renamed "Model": `[IsModel]`, `[IsModelDefiner]`, `[RemoveModel]` | Yes (the `AssemblyInfo.cs` that use them) |
| [U14](#u14-the-database-is-the-sqlserver-aspect-of-ckonfjson) | The database is the "SqlServer" Builder aspect of `ckonf.json`; the aspects are types of the models; the required engines are checked | Yes (the builders, the run time connection string) |
| [U15](#u15-globalization-is-an-app-aspect-of-ckonfjson) | Globalization is the "Globalization" App aspect of `ckonf.json`; its configuration classes are in CK.Globalization.StObj | Yes (a `CKSetup.xml` that names them) |
| [U16](#u16-the-central-configuration-of-a-solution) | The central configuration of a solution: a `ckonf.json` at the root of the repository describes its builders and the links between their parties, the builders' `ckonf.json` are generated from it | No (optional) |
| [U17](#u17-the-fleets-and-the-templates-of-parties) | The fleets and the templates of parties: `"InstanceCount"`/`"Instances"` on an App (one process per instance, `--Instance`) or on an entry of `"Parties"` (a tenant domain or a remote per instance), the `{Index}`/`{Name}` placeholders; `"MaxInstanceCount"` opens a fleet to dynamic instances, enlisted by approval, and a tenant template to run-time tenants; a local party runs in one process at a time; `ICoreKeyStore.DeleteAllKeys` | Yes (a custom `ICoreKeyStore`); behavior |
| [U18](#u18-the-configuration-levels-and-lock) | The configuration levels (`Compile`, `Init`, `Run`): a `"$LOCK"` in `ckonf.json` restricts a value to `Compile` (burnt in the generated code, not in the host's `appsettings.json`); a package restricts the values of a kind of party (`[ConfigurationLock]`: KeyManagement's `AutoTrustKey` is `Never` on an Opened remote template, checked at build time) | Behavior (an Opened remote template with another `AutoTrustKey` fails the build) |
| [U19](#u19-the-run-properties-and-the-dynamic-configuration) | The run properties: `[ConfigurationSection]` on an App singleton, `[ConfigurationLevel( Run )]` on its properties; CK.Cris.DynamicConfiguration initializes them from the configuration and changes them by a command (CK.IO.DynamicConfiguration), CK.AppIdentity.DynamicConfiguration keeps the changes; `CrisExecutionHost.ParallelRunnerCount` is one | No (new) |
| [U20](#u20-the-secrets) | The secrets: a secret in `ckonf.json` is a handle `{ "$SECRET": "<Name>" }`, projected (or burnt) as is; the host resolves it at start from the configuration key `Secrets:<Name>` (`#Dev/Secrets.json` in development); `[ConfigurationSecret]`, `AppConfiguration.AddSecretKey`; the "SqlServer" aspect accepts a handle | No (new); `SqlServerAspect.Databases` items are `SqlServerAspect.DatabaseConfiguration` |
| [U21](#u21-the-scheme-families-are-options-of-the-authcenter) | The scheme families of the AuthCenter are its options: `AuthCenterOidcOption` ("Oidc"), `AuthCenterPasskeyOption` ("Passkey"), `AuthCenterPasswordOption` ("Password": the "Basic" scheme), `AuthCenterWeakOption` ("Weak"), `IAuthCenterSchemeFamilyOption`; a scheme is available only through an enabled family and its provider; the family feature drivers, `IAuthCenterSchemeConfiguration` and `LocalAuthCenter.GetSchemeSettings` are gone | Yes (an AuthCenter references the families of its logins, a family of its own, a test that registers the family types); configuration (`"AuthCenterOidc"`/`"AuthCenterPasskey"` keys) |
| [U22](#u22-the-payload-of-the-oidc-login-provider-is-an-ioidcloginpayload) | The payload of the "Oidc" login provider implements `IOidcLoginPayload` (CK.AuthenticationInfo.Oidc, CK-Auth-Abstractions): the AuthCenter fills it without reflection and checks it when a login starts | Yes (an "Oidc" login provider of its own) |
| [U23](#u23-the-deployments) | The deployments: the folders of `Deployment/` next to the central `ckonf.json` (`#Production`, `Paris/Shop/#Staging`) are overlays of it (JSON Merge Patch, `"$BASE"`), merged in `ckonf.merged.json` by every builder run; the values per deployment (`"$PER_DEPLOYMENT"`, `[ConfigurationPerDeployment]`, the HttpEndpoint's `Urls`, the Transport's `ListeningAddress`, the address of a remote and of a link) must be written by each deployment | No (new); behavior (a deployment that inherits a value per deployment fails the build) |
| [U24](#u24-the-name-of-an-app-is-the-identity-of-its-party) | In a central configuration, the name of an App (and of its `.App`, `.Builder` and `.Host` projects) is the identity of its party: its domain then its party name, `/` mapped to `.` (`Sample.Auth3.Front`), without the optional `"GlobalDomainPrefix"` (`"Sample"`: `Auth3.Front`) | Yes: the projects of a central configuration are renamed (a name that disagrees fails the build) |
| [U25](#u25-the-assets-of-the-deployments) | The assets of the deployments: `Deployment/GenerateAssets.cs` calls CK.AppDeployment, that publishes a host per deployed App (generated copies of the builder and its hosts, built for the deployment) in one asset per App (`Production-Auth3.Front.zip`; `"SingleZipAsset"`: one per deployment), with its manifest in `CK.AppHost.Published.json`; the settings are the `"Deployment"` key (`Builders`, `Publish`: the MSBuild properties of the publication, as-is, `Apps`: the ones of an App); a host built for a deployment has the environment of its party; the client applications are built by the publication; a published host's content root is its folder | No (new); behavior (the environment of the host and of the party are checked at start) |
| [U26](#u26-the-pre-authorized-enlistment-ticket) | The pre-authorized enlistment ticket: "Expect a party" (approval page) or `ckey ticket open` on the Authentication Center opens a one-time ticket, the party starts with it (`Local:KeyManagement:EnlistTicket`, an optional secret handle `{ "$SECRET?": "Enlist:Worker2" }`) and is trusted at once; it carries the anchor of the center | No (new) |
| [U27](#u27-the-routes-are-gone-a-system-is-a-star-around-its-authentication-center) | The routes are gone: a System is a star around its Authentication Center (a command beyond one hop goes through it); the approval page is the client application with the "Enlistment" fact, stated by the center; the pending approvals are asked through the hub; `RouteHosts` is removed (a setup error) | Yes (a `RouteHosts` configuration, the route APIs) |
| [U28](#u28-the-dataprotection-key-ring-is-in-the-store-of-the-party) | The DataProtection key ring is in the private store of the root party (`-Local/DataProtection-Keys`), encrypted by the secret `KeyRingKey`, the CNG store (the TPM), DPAPI on Windows, or nothing (a warning outside `#Dev`) | Yes (a store created before: its keys can't be opened; delete it in development) |
| [U29](#u29-a-section-can-be-a-configuration-model) | A section can be a configuration model: a `[ConfigurationModel]` class (CK.AppBuilder.Abstractions, read by CK.ConfigurationModel), named by `[Configuration<TModel>]` on an aspect, an option, a feature driver (`GetModel<TModel>( party )`) or a client feature (`ClientFeatureDriver<T, TModel>`), read key by key along the `"Defaults"` and validated, described as a JSON Schema; "Globalization", "HttpEndpoint" (and "InProcess", "ForwardedHeaders"), "DesktopWindow", "AuthCenter" (and "Oidc", "Passkey", "PasswordLost", "UserInvitation") have one | **Yes:** the Oidc schemes move to `"Oidc": { "Schemes": { ... } }`; behavior (the unknown keys are the reader's errors, the AuthCenter's settings are read with its `"Defaults"`) |

## U1. The builder's configuration: `ckonf.json` (the hosts' `appsettings.json` are projections)

A builder ("XXX.Builder", run by `CKomposableAppBuilder` of CK.AppBuilder) can have a `ckonf.json`: the configuration of
what it builds in one engine run, its **App** (or its **Apps**: an App is an "X.App"/"X.Host" pair, one process, its
root party and its tenants) with the configuration of its host, and the **aspects**. The engine
configuration is deduced from it (an App is a BinPath of the engine), and the `appsettings.json` of each host is
generated from it. It is the first step of the Unified Configuration. A builder without `ckonf.json` works as before (its
`CKSetup.xml`), but the help that added the client applications to the host's `appsettings.json` (M14 of the cris-auth
guide) is gone: migrate.

**Detect:** a "XXX.Builder" project without `ckonf.json`; a `CKSetup.xml` with `<BinPaths>`, `<TypeScript>` or
`<Globalization>`; a builder `Program.cs` that configures the engine (`EnsureDefaultTypeScriptAspectConfiguration`,
`EnsureAspect<...>`).

**Do:**

1. **Create `ckonf.json` in the builder project.** A builder builds one App, `"App": { ... }`, named after the builder's
   folder without ".Builder" ("Front.Builder" builds the App "Front"; `"Name"` departs from it). A builder that builds
   several Apps (sharing the Builder aspects: one database) lists them, `"Apps": [ { "Name": "Back1", ... }, { "Name":
   "Back2", ... } ]` (CK-Sample-MultiBinPath); `"App"` and `"Apps"` together are an error, the names are unique. An App
   is named after its projects: the App "Front" is the "../Front.App" project (its bin folder is the builder's: `bin/{Configuration}/{TargetFramework}`) and the
   "../Front.Host" project. `"AppProject"` and `"HostProject"` (relative to the `ckonf.json` folder) depart from this
   convention; a path that leaves the solution's folder is an error. The content of the App is the host's
   `appsettings.json` (move it, with its comments) **without its `"CK-AppIdentity"` level**: the keys of the party
   (`FullName`, `Local`, `Parties`, `Defaults`, `DomainName`, `PartyName`, `EnvironmentName`, `StoreRootPath`,
   `TrashBinRetention`, `StrictConfigurationMode`) are directly in the App, next to the other sections of the host
   (`Logging`, `CK-Monitoring`...); the features are declared by their key (U4). Plus the compile time keys below:

   ```jsonc
   // Front.Builder/ckonf.json
   {
     "App": {
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
   ```

   The root of `ckonf.json` has `"App"` (or `"Apps"`), `"Aspects"` (see step 3) and `"ProjectCompileKeys"` (see the
   Behavior); any other key is an error. A solution of several builders can describe them in one central
   configuration instead (U16).

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
  keys, its `Name`; a feature whose keys are all compile time is written `true`). **The keys of the party are in its
  `"CK-AppIdentity"` section** (first), the other keys of the App follow as they are: the run time configuration is the
  one of the cris-auth guide (the user-secrets and the environment variables keep their `CK-AppIdentity:...` keys). Edit
  the `ckonf.json`, never the projection. `"ProjectCompileKeys": true` (at the root) keeps the compile time keys of the
  clients in the projection (the run time accepts them).
- **The configuration is checked at build time:** the party of each App is validated when the builder runs (an
  invalid configuration fails the build), the keys and the App names are case insensitive (`Back1` and `back1` is a
  duplicate: an error),
  an unknown key of the root is an error, and a key named `CK-AppIdentity` in an App that has a party is an error (it is
  the section of the projection: the old `"CK-AppIdentity": { ... }` level must be removed).
- **The order of the Apps means nothing:** the engine receives them sorted by name.
- **The builder needs no new package:** the engines that deduce the configuration are the ones the builders already
  reference (`CK.AppIdentity.Engine`, M30 of the cris-auth guide; `CK.TS.Angular.Engine` for the "NgApp" clients;
  `CK.Globalization.Engine`), in their versions of the `explo/uc` branch.

**Check:** the builder's log has "Reading '.../ckonf.json'." and "written from 'ckonf.json'"; the host's
`appsettings.json` starts with the "Generated" line; the generated code (`$StObjGen`) and the `ck-gen` of the client
applications are unchanged by the migration. CK-Sample-AspNetAuth (five builders, through its central configuration:
U16), CK-Sample-MultiBinPath (two Apps in one builder), the samples of CK-Ng-Admin, CK-Ng-User-UserProfile, CK-Ng-Zorro-Contrib and
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
**configurable types** (`IConfigurableTypeAttribute`, CK.AppBuilder.Abstractions): the configuration names them (the
key of their section) and switches them; the name is explicit, it doesn't come from the type name.

**Detect:** a concrete class that specializes `ApplicationIdentityFeatureDriver` or `ApplicationIdentityStaticFeatureDriver`
(directly or not: every concrete driver), a concrete class that specializes `ClientFeature` (U3); a call to
`base( s, ... )` with more than the service; `ClientFeature.GetFeatureName`; an interface that extends
`IPartyComponentAbstraction`; a test that builds a map with `Types.Add( typeof( ApplicationIdentityService ), ... )`
or that registers feature drivers by hand (`services.AddSingleton<XXXFeatureDriver>()`).

**Do:**

1. **Every concrete feature driver declares its feature:** `[PartyFeature( "Name", TypeActivation.X )]` (CK.AppBuilder.Abstractions,
   namespace `CK.AppIdentity`; `TypeActivation` is in `CK.Core`; CK.AppIdentity brings the package). The attribute is not inherited: each concrete driver has its own,
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

The description of a party (its public URL, its client applications...) is computed by the
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
| `ListeningAddress`, `ListeningTypes`, `NegotiationTimeout`, `AlwaysListening`, `DisallowEviction` | `"Transport": { ... }` |
| `AuthCenterParty` (a `Local`) | `"AuthCenterRemote": { "Party": "..." }` |
| `CoreKeyStore:CngProvider`, `CoreKeyStore:AllowFallback`, `CoreKeyStore:MachineWide` (CK.AppIdentity.KeyManagement.Cng) | `"KeyManagement": { "CngKeyStore": { "Provider": "...", "AllowFallback": ..., "MachineWide": ... } }` |
| `PasswordLostTokenLifetime` (a `Local`, CK.UserProfile.UserPassword.Lost) | `"AuthCenter": { "PasswordLost": { "TokenLifetime": "02:00:00" } }` |

`RouteHosts` is gone (U27). From CK-Sample-AspNetAuth (Worker2 and Auth3):

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
  is `PasswordLostOption.DefaultTokenLifetime`, and the service has `Option` and `IsEnabled`. It exists only with the
  "Password" option of the AuthCenter and the "MailSender" feature of the party (`[Requires<AuthCenterPasswordOption>]`,
  `[Requires<MailSenderFeatureDriver>]`): left to its default it is disabled without them, enabled explicitly it is an
  error. "UserInvitation" (CK.UserManagement.UserInvitation, required) requires "MailSender" too.
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
   (CK.AppBuilder.Abstractions, namespace `CK.AppIdentity`), `[PartyClientOption]` becomes
   `[PartyClientFeatureOption( "Name", TypeActivation.X )]` (same package and namespace). Keep the former name, the type name without its "Option" suffix; `IsEnabledByDefault = true`
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

   Instead of an `IConfiguration`, an option can receive a configuration model (U29): its section is then read and
   validated for it, key by key along the `"Defaults"`.
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
  (its whole section; an option with a configuration model is read key by key along all of them, U29).
- An option that throws fails the setup of the party (an option of a feature driver) or makes the client an error (an
  option of a client feature).
- The name of an option is unique among the options of its owner; a `TypeActivation.Required` option is always enabled.
- **An option can require a configurable type of its level or above** (`[Requires<T>]`, not optional): a party feature
  for an option of a feature driver or of a client feature (`[Requires<MailSenderFeatureDriver>]`), an aspect for an
  option of an aspect. Outside the options of its owner, the requirement doesn't order it but means "only with it", as
  between options: an option enabled by default is disabled without it (an information is logged), an option enabled
  explicitly (or required) is an error, at run time and, for an explicit option of a party, at build time. An option of
  a party component can require a feature driver only (the engine refuses another type).
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

Two moves keep the package names:
- **CK.AppBuilder and CK.AppDeployment** are produced by CK-AppBuilder (version line 37.0.1-0.uc), no longer by CK-StObj:
  a `<PackageReference>` only takes the new version.
- **The declarations of the configuration** are in **CK.AppBuilder.Abstractions** (CK-Abstractions, 32.0.0-0.uc): the
  configuration attributes of CK.Abstractions (`IConfigurableTypeAttribute`, `TypeActivation`, `[Configuration...]`),
  `[ConfigurationModel]` and `IConfigurationModel`, the aspect declarations (`[AppBuilderAspect]`, `[AppAspect]`, their
  options; `ICKomposableAppBuilder` is in CK.Engine.Configuration), `[PartyFeature]`, `[PartyFeatureOption]`,
  `IPartyComponent`, `IApplicationIdentityFeatureDriver`, `IPartyComponentOption<TOwner>`, `[PartyClientFeature]`,
  `[PartyClientFeatureOption]` and `[PartyClientFact]` (its `IsValidKey` and `IsValidValue` are the rules of the facts).
  The namespaces don't change. CK.AppIdentity and CK.Engine.Configuration bring it: only a project that uses them
  without these references (a CK.Abstractions only package) references it.

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

## U10. No tenant domain is added from a free configuration at run time

A tenant domain is configured: it is a `Parties` entry of the root that has its own domain (`"FullName":
"Acme/$Acme"`), in the `ckonf.json` of the App (or the configuration of a test). A tenant created at run time is an
instance of an Opened tenant template (U17): nothing creates a tenant from a free configuration anymore (it would have
nothing to be created again from at the next start).

**Detect:** `AddTenantDomainAsync`, `AddPartiesAsync` or `AddedDynamicParties` (CK.AppIdentity.Abstractions).

**Do:**
- A tenant added at run time becomes a configured one:

  ```csharp
  // Was: var acme = await service.AddTenantDomainAsync( monitor, c => { c["FullName"] = "Acme/$Acme"; c["Parties:0:PartyName"] = "AcmeBox"; } );
  c["Parties:0:FullName"] = "Acme/$Acme";
  c["Parties:0:Parties:0:PartyName"] = "AcmeBox";
  // ...
  var acme = service.TenantDomains.Single();
  ```

- `AddPartiesAsync` that added remotes only: `AddMultipleRemotesAsync` on the root (an `ILocalParty`), or
  `AddRemoteAsync` for one remote. A remote can still be added at run time to a configured tenant domain
  (`tenant.AddRemoteAsync`) and destroyed (`IOwnedParty.DestroyAsync`).
- A test whose subject was the lifecycle of a dynamic tenant (destroying it, a static feature refused on it) is removed:
  CK.AppIdentity tests it.

**Behavior:** `ITenantDomainParty` is still an `IOwnedParty`; `IsDynamic` is false for every tenant domain (a
configured one can't be destroyed).

**Check:** none of the three names is left. CK-AppIdentity-Transport (AuthCenter and transport tests) and CK-AspNet
(HttpEndpoint tests) are migrated this way.

## U11. The mail features: "MailSender" and "MailRelay"

How a party sends its mails is the **"MailSender"** feature of CK.Mailer (it was "EmailSender"): each local party (the
root and its tenant domains) has its own **named** senders, grouped by kind (an option of the feature: "MailKit", from
CK.Mailer.MailKit; it was "Smtp"). The party that sends the mails of other parties is the **"MailRelay"** feature (it was
"MailSender"): CK.AppIdentity.MailRelay.Local, CK.AppIdentity.MailRelay.Remote and CK.IO.MailRelay (CK-Mailer, the
`explo/uc` version line of CK.Mailer) replace CK.AppIdentity.MailSender.Local, .Remote and CK.IO.MailSender.

**Detect:** a `"MailSender"` or `"MailSenderRemote"` key (the relay); an `"EmailSender"` key; a reference to
CK.AppIdentity.MailSender.Local, CK.AppIdentity.MailSender.Remote or CK.IO.MailSender; `EmailSenderFeatureDriver`,
`IEmailSenderFactory`, `EmailSenderFactory`, `SmtpEmailSenderFactory`, `RemoteMailSender`, `MailSenderHandler`,
`LocalMailSenderFeatureDriver`, `RemoteMailSenderFeatureDriver`.

**Do, in this order** (the key `"MailSender"` changes its meaning: rename the relay first):
1. **The relay:** `"MailSender": true` (the relay, in its `Local`) becomes `"MailRelay": true`, `"MailSenderRemote"`
   (on a remote) becomes `"MailRelayRemote"`; the package references and the namespaces `CK.AppIdentity.MailSender` and
   `CK.IO.MailSender` become `CK.AppIdentity.MailRelay` and `CK.IO.MailRelay`; the types:
   `LocalMailSenderFeatureDriver` -> `LocalMailRelayFeatureDriver`, `RemoteMailSenderFeatureDriver` ->
   `RemoteMailRelayFeatureDriver`, `MailSenderHandler` -> `MailRelayHandler` (`RemoteMailSender` is gone: a relay is a
   sender of its local party).
2. **The senders:** `"EmailSender"` becomes `"MailSender"`, each sender is named inside its kind, `"Default"` names the
   default one when there are several. From CK-Sample-AspNetAuth (Auth3):

   ```jsonc
   // Was: "EmailSender": { "Smtp": { "SendEmail": false, "UsePickupDirectory": true, "PickupDirectory": "Mails", "From": "noreply@sample.local" } }
   "MailSender": {
     "MailKit": {
       "NoReply": {
         "SendEmail": false,
         "UsePickupDirectory": true,
         "PickupDirectory": "Mails",
         "From": "noreply@sample.local"
       }
     }
   }
   ```

   An array of senders of one kind (`"Smtp": [ {...}, {...} ]`) becomes several named senders. A
   `"RequiredAuthentication"` key (a typo, it was ignored) is removed: the key is `"RequiresAuthentication"`.
3. **A party that sent through its relay:** `"EmailSender": { "MailSender": true }` is removed: each remote that declares
   `"MailRelayRemote"` is a sender of its local party, named after it (`"$Backend"`). `"MailRelayRemote": { "Sender": "Support" }`
   asks the relay for its "Support" sender.
4. **A sender kind** (an `EmailSenderFactory`) becomes an option of the feature: a `MailSenderOption` decorated with
   `[PartyFeatureOption( "Name", TypeActivation.Optional )]` that reads its named senders in its constructor
   (`MailSenderOption.ReadNamedSenders`, see U8).
5. **The code that sends mails:** `IDefaultEmailSender` is unchanged (the default sender of the root party). The senders of
   another local party: `party.GetMailSender( name )` (`MailSenderExtensions`, the default one when `name` is null) or its
   `MailSenders` feature.
6. **PasswordLost** (CK.UserProfile.UserPassword.Lost) accepts `"AuthCenter": { "PasswordLost": { "Sender": "..." } }`,
   **UserInvitation** (CK.UserManagement.UserInvitation, which now references CK.AppIdentity.AuthCenter.Local) is the
   "UserInvitation" option of the AuthCenter (always enabled: `"UserInvitation": false` is an error) and accepts
   `"AuthCenter": { "UserInvitation": { "Sender": "..." } }`;
   `PasswordLostMailer` and `UserManagementMailer` (CK.UserManagement.UserInvitation) no longer take an
   `IDefaultEmailSender` in their constructor (a specialization drops it): they send with the senders of their local party.

**Behavior:**
- A mail goes through **one** sender (several senders used to send each mail several times).
- The senders of a tenant domain are its own (they all went to the root).
- With several senders and no `"Default"`, an unknown `"Default"`, an unknown key of a MailKit sender or an invalid
  value: the start of the application fails. A remote that declares `"MailSender"` is a start error.
- A missing sender (an unknown name, a relay not connected yet, a party without "MailSender") fails the sending of the
  mail (the `SendResponse.ErrorMessages` say why), it doesn't throw.
- A relay sends with the sender that the caller names, else its `"MailRelay": { "Sender" }`, else its default; it never
  relays to another relay.

**Check:** no `"EmailSender"`, `"MailSenderRemote"` nor `"Smtp"` key is left, no `"MailSender": true` (it is
`"MailRelay"`), and the application starts. CK-Ng-User-UserProfile, CK-Ng-Admin and CK-Sample-AspNetAuth (their
samples, the PasswordLost tests) are migrated this way.

## U12. The Fluid aspect is gone

CK.Template.Fluid is a library: its templates need no engine aspect. The build-time check of the templates (a
`Res/Templates/*.liquid` resource that doesn't parse fails the build) is done by the engine of each template package
(`FluidTemplatePackageAttributeImpl`, CK.Template.Fluid.Engine), for every application that uses it.

**Detect:** `FluidAspectConfiguration` (`<Aspect Type="CK.Setup.FluidAspectConfiguration, CK.Template.Fluid" />` in a
`CKSetup.xml`, `EnsureAspect<FluidAspectConfiguration>()` in a test).

**Do:** remove it.

**Behavior:** the templates of every template package are checked, with or without configuration (they were checked
only when the aspect was configured).

**Check:** no `FluidAspectConfiguration` is left. CK-Ng-Admin (its sample) is migrated this way.

## U13. "PFeature" is renamed "Model"

An assembly whose types the engine registers and engineers (the deployed code, as opposed to an Engine) is a **Model**
(CK.Abstractions, `CK.Setup` namespace).

**Detect:** `IsPFeature`, `IsPFeatureDefiner`, `RemovePFeature` (an `AssemblyInfo.cs`), `AssemblyKind.PFeature`.

**Do:** `[assembly: IsPFeature]` becomes `[assembly: IsModel]`, `[IsPFeatureDefiner]` becomes `[IsModelDefiner]`,
`[RemovePFeature( "X" )]` becomes `[RemoveModel( "X" )]`; in code that reads the assemblies (CK.TypeCache):
`AssemblyKind.PFeature`/`PFeatureDefiner` become `Model`/`ModelDefiner`, `CachedAssembly.PFeatures`/`AllPFeatures`
become `Models`/`AllModels`.

**Behavior:** the legacy attributes recognized by their name (`IsModelAttribute`, `IsModelDependentAttribute`,
`IsSetupDependencyAttribute`, `ExcludeFromSetupAttribute` of the former CK.Setup packages) are no longer recognized: an
assembly that still uses them is classified by its references only.

**Check:** no `PFeature` is left. CK-Cris-TypeScript (CK.Cris.AspNet) and CK-Sqlite (CK.Sqlite.Model) are migrated this way.

## U14. The database is the "SqlServer" aspect of `ckonf.json`

An aspect is now a configurable type of a model, found in the type sets of the Apps: the database is the "SqlServer"
Builder aspect (`SqlServerAspect`, CK.SqlServer.Setup.Model, CK-Database), configured in the root `"Aspects"` of the
builder's `ckonf.json`. The 3 steps setup (Setupable) is an implementation detail of it: it has no configuration of its own.

**Detect:** a `CKSetup.xml` with `SetupableAspectConfiguration` or `SqlSetupAspectConfiguration`; a builder's `Program.cs`
that calls `EnsureAspect<SetupableAspectConfiguration>()` or `EnsureAspect<SqlSetupAspectConfiguration>()`; a
configuration key `AppIdentity:Local:SqlServer:DefaultDatabaseConnectionString`.

**Do:**
1. Move the database configuration to the root of the builder's `ckonf.json` (from CK-Ng-Admin's sample):

   ```jsonc
   "Aspects": {
     "SqlServer": { "DefaultDatabaseConnectionString": "Server=.;Database=AdminSampleDB_LOCAL;Integrated Security=True;TrustServerCertificate=true" }
   },
   ```

   The keys: `DefaultDatabaseConnectionString`, `Databases` (by logical name: `"histoDB": { "ConnectionString": "...",
   "AutoCreate": true }`), `GlobalResolution`, `IgnoreMissingDependencyIsError`. Remove the `<Aspect>` elements from the
   `CKSetup.xml` (and the file when nothing is left in it), and the `EnsureAspect` code from the `Program.cs` (it becomes
   `return await CKomposableAppBuilder.RunAsync();`).
2. The run time connection string is `"SqlServer": { "DefaultDatabaseConnectionString" }`, a root section of the host's
   `appsettings.json` (projected from `ckonf.json`); `AppIdentity:Local:SqlServer:...` never applied (the section is
   `CK-AppIdentity`).
3. A model package that brings an aspect declares it: a package decorated with `[AppBuilderAspect( "Name", TypeActivation.X )]`
   or `[AppAspect( "Name", TypeActivation.X )]` (CK.AppBuilder.Abstractions, namespace `CK.Setup`), whose constructor reads
   its `IConfiguration` section and that implements `IAppBuilderAspectApply` or `IAppAspectApply` (CK.Engine.Configuration:
   the `IAppBuilderAspect` and `IAppAspect` of CK.AppBuilder.Abstractions are markers), whose `Apply` configures the engine
   (see the README of the App builder model, CK.Engine.Configuration).

**Behavior:**
- "SqlServer" is present as soon as an App references CK.SqlServer.Setup.Model (`TypeActivation.ByDefault`); without
  `DefaultDatabaseConnectionString`, the build fails. Tests that build their `EngineConfiguration`
  (`EnsureSqlServerConfigurationAspect`) are unchanged.
- An unknown key of the "SqlServer" section, an invalid value: the build fails.
- The engines that the models require (their `[RequiredEngine]`) must be referenced by the builder (or the test
  project): the missing ones are listed in one error before the run (they were a `TypeLoadException` deep in it).

**Check:** no `SetupableAspectConfiguration` nor `SqlSetupAspectConfiguration` in a builder; the hosts' `appsettings.json`
have a `"SqlServer"` section. CK-Ng-Admin, CK-Ng-User-UserProfile, CK-Ng-Zorro-Contrib, CK-Sample-AspNetAuth (Auth3)
and CK-Sample-MultiBinPath (two Apps, one database) are migrated this way.

## U15. Globalization is an App aspect of `ckonf.json`

The back-end translations of an App are the "Globalization" App aspect (`GlobalizationAppAspect`,
CK.Globalization.StObj, CK-Globalization-Engine): `GlobalizationAspectConfiguration` and
`GlobalizationBinPathAspectConfiguration` moved from CK.Globalization.Engine to CK.Globalization.StObj (same namespace,
`CK.Setup`), which now requires its engine (`[RequiredEngine( "CK.Globalization.Engine" )]`).

**Detect:** a `CKSetup.xml` with `CK.Setup.GlobalizationAspectConfiguration, CK.Globalization.Engine` or a
`<Globalization ActiveCultures="..." />` BinPath element; a call to `GlobalizationBinPathAspectConfiguration.GetActiveCultures()`.

**Do:**
- Declare the aspect in the App of the builder's `ckonf.json` (from CK-Ng-Admin's sample):
  `"Aspects": { "Globalization": { "ActiveCultures": "fr" } }` (or an array of culture names), and remove it from the
  `CKSetup.xml`. A `CKSetup.xml` that keeps it names `CK.Setup.GlobalizationAspectConfiguration, CK.Globalization.StObj`.
- `GetActiveCultures()` is an extension method of the engine (`GlobalizationBinPathAspectConfigurationExtensions`,
  CK.Globalization.Engine): the call compiles unchanged with a reference to the engine.

**Behavior:** the aspect is optional: an App has it when its `"Aspects"` declare it (an unknown key of its section, like
every aspect key that names no aspect of the Apps, fails the build before the contributors run).

**Check:** no `CK.Globalization.Engine` in a `CKSetup.xml`. CK-Ng-Admin and CK-Ng-User-UserProfile (their samples) declare
it this way.

## U16. The central configuration of a solution

A solution of several builders (several parties) can describe them once: a `ckonf.json` at the root of the repository
(its git root, next to `#Dev`). Each builder then generates its own `ckonf.json` from its entry before reading it: the
domain, the defaults shared by the Apps and the links between the parties are written once. It is optional: a
repository without it is unchanged.

**Detect:** a repository with several "XXX.Builder" projects whose `ckonf.json` repeat the same domain, the same host
sections (`Logging`, `CK-Monitoring`), or describe the two sides of a connection in their `Parties`.

**Do:**
1. **Create the `ckonf.json` at the root of the repository** (from CK-Sample-AspNetAuth, shortened):

   ```jsonc
   {
     "Domain": "Sample/Auth3",
     "Defaults": {
       "App": {
         "Logging": { "LogLevel": { "Default": "Warning" } },
         "CK-Monitoring": { "GlobalDefaultFilter": "Debug", "GrandOutput": { "Handlers": { "Console": true, "TextFile": { "Path": "Text" } } } }
       }
     },
     "Builders": {
       "Auth3/Auth3.Builder": {
         "Aspects": { "SqlServer": { "DefaultDatabaseConnectionString": "Server=.;Database=CKSampleAuth3;Integrated Security=True;TrustServerCertificate=true" } },
         "App": {
           "Defaults": { "Transport": { "ListeningAddress": [ "tcp:127.0.0.1:37300" ] } },
           "Local": { "AuthCenter": { ... } }
         }
       },
       "Front/Front.Builder": { "App": { "Local": { "HttpEndpoint": { "Urls": "http://localhost:5300" } } } },
       "Worker/Worker.Builder": { "App": {} }
     },
     "Links": [
       { "From": "$Front", "To": "$Auth3", "Link": { "AuthCenterRemote": true, "Address": "tcp:127.0.0.1:37300", "KeyManagement": { "AutoTrustKey": "Once" } } },
       { "From": "$Worker", "To": "$Auth3", "Link": { "Address": "tcp:127.0.0.1:37300", "KeyManagement": { "AutoTrustKey": "Once" } } },
       { "From": "$Auth3", "To": "$Front", "Link": { "AuthCenterRemote": true, "KeyManagement": { "AutoTrustKey": "Once" } } },
       { "From": "$Auth3", "To": "$Worker", "Link": { "KeyManagement": { "AutoTrustKey": "Never" } } }
     ]
   }
   ```

   - **`"Builders"`** are keyed by the path of their folder, relative to the root and downward only (`..` is an
     error). The entry of a builder is the content of its `ckonf.json` (U1: `"Aspects"`, `"App"` or `"Apps"`) without
     what the other keys give.
   - **`"Domain"`** (and `"Environment"`, `#Dev` by default) gives the `FullName` of each App that doesn't write it:
     `{Domain}/${Name}/{Environment}`. The party of an App is `$Name` (or the party of its explicit `FullName`).
   - **`"Defaults": { "App": { ... } }`** is merged in each App: an App's own value wins, the objects are merged.
   - **`"Links"`:** a link is the remote entry that `From` has for `To`: `"Link"` is the content of the entry (without
     its `PartyName`, which is `To`), appended to the `Parties` of `From` in the order of the links. Each side of a
     connection is a link of its own; a link without its reverse is valid. A remote outside the solution stays in the
     `Parties` of its App; a remote both linked and in the `Parties` is an error.
2. **Move the comments into it.** The builders' `ckonf.json` are generated: run the builders (a `ckli build` commits them
   as build impacts) and commit them.

**Behavior:**
- **A builder's `ckonf.json` is generated** when the repository has a central configuration: its first line is
  `// Generated from ../../ckonf.json: do not edit.`, it is written when it changes, without the comments. Edit the
  central file, never the projection.
- **Every builder of the repository must be in `"Builders"`:** otherwise its build fails ("The builder ... is not in
  the central configuration").
- **The names of the Apps are unique in the solution;** a root key that no package handles is an error (`Domain`,
  `Environment` and `Links` are handled by CK.AppIdentity.Engine). The keys of a link are checked by the builder of
  `From`, like the `Parties` it validates.

**Check:** the builders' `ckonf.json` start with the "Generated from" line; the hosts' `appsettings.json` are unchanged
by the move. CK-Sample-AspNetAuth (five builders, eight links) is migrated this way.

## U17. The fleets and the templates of parties

A party described once can have several instances: a **template** is a section with `"InstanceCount": n` or
`"Instances": [ "Top", "Red" ]` (`ConfigurationTemplate`, CK.Configuration). Its values use the placeholders of an
instance: `{Index}` (1-based), `{Name}` (the item of `"Instances"`, or the index), `{n+Index}` (`"{5400+Index}"`), and,
in a template inside a fleet, the fleet's qualified by its App name (`{Trolley.Name}`): the innermost template wins. The
placeholders are in the values, never in the keys; a `{...}` that is none of these forms is left as is. It is new: nothing
to migrate, but a project that wants several identical parties uses it.

**Detect:** several builders, or several entries of `"Parties"`, that differ only by a name, a port or a path.

**Do:**
1. **A fleet is an App with instances** (from CK-Sample-AspNetAuth, its central `ckonf.json`):

   ```jsonc
   "Worker/Worker.Builder": {
     "App": {
       "InstanceCount": 2,
       "CK-Monitoring": { "GrandOutput": { "Handlers": { "TextFile": { "Path": "Text/{Name}" } } } }
     }
   }
   ```

   Its party name must use `{Name}` or `{Index}` (a build error otherwise): with a central configuration, its derived
   `FullName` is `{Domain}/${App}-{Name}/{Environment}` (`$Worker-1`, `$Worker-2`); without it, write it
   (`"FullName": "Sample/$Worker-{Name}/#Dev"`). Template what the instances must not share (ports, paths).
2. **The links of the central configuration name a fleet by its pattern:** `{ "From": "$Worker-{Name}", "To": "$Auth3" }`
   is a remote of every instance; `{ "From": "$Auth3", "To": "$Worker-{Name}" }` is a remote template on Auth3 (the
   fleet's `"InstanceCount"` copied in the entry): one remote per instance, its values can use the placeholders
   (`"Address": "tcp:10.0.0.1:{37300+Index}"`).
3. **A template entry of `"Parties"`** (with or without a central configuration) is a tenant domain or a remote per
   instance: `{ "FullName": "Shop-{Name}/$Shop-{Name}", "Instances": [ "Paris", "Lyon" ] }`.
4. **Start an instance:** the host of a fleet needs `--Instance=<Name or index>` (the `"Instance"` configuration key);
   `#Dev/Start.ps1` starts every configured instance. A test that builds the host with `CKomposableAppHost.Build` passes it
   in its arguments (`CKomposableAppHost.Build( ["--Instance=2"], ... )`).
5. **An Opened fleet accepts dynamic instances** (from CK-Sample-AspNetAuth, its central `ckonf.json`):

   ```jsonc
   "Worker/Worker.Builder": {
     "App": {
       "InstanceCount": 2,
       "MaxInstanceCount": 4,
       "CK-Monitoring": { "GrandOutput": { "Handlers": { "TextFile": { "Path": "Text/{Name}" } } } }
     }
   }
   ```

   `"MaxInstanceCount"` bounds the instances, the configured ones included (`"InstanceCount": 0` is then valid). The
   dynamic instances come after the configured ones and are named by their index (`$Worker-3`, `$Worker-4`): a
   configured name in `"Instances"` can't start with a digit. A dynamic instance is started by hand
   (`dotnet run -- --Instance=3`). The link to the fleet copies `"MaxInstanceCount"` in the remote template of the
   other side; without a central configuration, write it in the entry of `"Parties"`.
6. **An Opened tenant template holds run-time tenant domains** (from CK-Sample-AspNetAuth, Worker2 in its central
   `ckonf.json`):

   ```jsonc
   "Parties": [
     { "DomainName": "Sample/Auth3/Customers/{Name}", "PartyName": "${Name}", "InstanceCount": 0, "MaxInstanceCount": 5 }
   ]
   ```

   A template whose party name is the last part of its domain, with their placeholders, and without `"Address"`, is a
   tenant template; an Opened one is written with a `"DomainName"` that ends with `/{Name}` and the `"PartyName"`
   `"${Name}"` (no `"FullName"`): its run-time tenants are named by their creator (`Acme` is
   `Sample/Auth3/Customers/Acme/$Acme`). In its inner templates, its placeholders are `{Tenant.Name}` and
   `{Tenant.Index}`; a party name inside it can't use `{Tenant.Index}`, and a tenant template can't contain another one.
   The application creates, moves and removes them (`IApplicationIdentityService.AddTenantInstanceAsync( monitor,
   template, "Acme" )`, `SetTenantIndexAsync`, `RemoveTenantInstanceAsync`; `TenantTemplates`, `UnloadedTenants`), or an
   operator does it on the tenants page (CK.Ng.AppIdentity.Tenants of CK-Ng-Admin on the page's App, CK.AppIdentity.Tenants
   of CK-AppIdentity-Transport on the page's App and on the App of the template; both Apps are linked to the
   AuthCenter: the page's App needs no link with the App of the template).
7. **A feature driver that keeps secrets in the private store of a party** deletes them when a run-time tenant is
   removed: it overrides `ApplicationIdentityFeatureDriver.ForgetLocalPartyAsync( monitor, fullName, localStore )`
   (CK.AppIdentity). **A custom `ICoreKeyStore`** (CK.AppIdentity.KeyManagement) implements `DeleteAllKeys( monitor,
   ownerFullName, localStore )`: it destroys every key of the party, never into a trash bin (a store that takes the
   `DefaultCoreKeyStore` delegates to it for the keys it doesn't hold, as `CngCoreKeyStore` does).

**Behavior:**
- One build per fleet: the host's `appsettings.json` keeps the template (`"InstanceCount"` at its root, the
  placeholders); CK.AppHost resolves it at start for its instance. A missing or unknown instance in a fleet, an
  `"Instance"` in a host that is not a fleet: the start fails.
- The builder validates the party of every instance. The instances share their type sets, named by the unresolved
  party name (`$Worker-{Name}`): `PartyComponentDirectory` finds the set of an instance through it.
- A fleet without `"MaxInstanceCount"` is Closed: only the configured instances exist.
- **The holder of an Opened remote template** (Auth3 for the Worker) accepts an unknown caller that is one of its
  dynamic instances as a candidate: a dynamic remote, trusted by an approval only (the enlistment: the approval page
  says "A new instance of $Worker-{Name}"). An `"AutoTrustKey"` other than `"Never"` on an Opened template is refused
  (a `SecurityCritical` error, `"Never"` applies). The holder listens from its start (its candidates call before any
  of their remotes exists). A candidate that doesn't call anymore is destroyed.
- **Nothing else is stored:** an approved instance exists because its identity is pinned in its folder of the store;
  it is created again at the next start. Lowering `"MaxInstanceCount"` below an approved index trashes its pins (a
  warning). The approval page lists the approved instances (`IGetPartyInstancesQCommand`) and removes one
  (`IRemovePartyInstanceCommand`): its pins are trashed, never its folder (in a shared store, it is also the instance's
  own folder, with its private keys), and it can be approved again.
- **A run-time tenant is kept by its record:** `-Local/Instance.json` (`{ "Index": 3 }`) in its folder of the store; it
  is created again at start, before the approved remote instances. Its index is the first free one at its creation (no
  configured instance, no loaded tenant has it, up to `"MaxInstanceCount"`). A record that the configuration contradicts
  (its index taken by a configured instance, or above a lowered bound) is not loaded but kept (`UnloadedTenants`, with
  its reason): `SetTenantIndexAsync` moves it to a free index. Its removal destroys its keys and trashes the rest of its
  private store: a new tenant of the same name is a new identity.
- **The central configuration checks the tenant templates:** an Opened one owns its domain prefix (no other tenant
  template with it, no party of the System directly under it), and in a fleet App a tenant template uses the
  placeholder of its App (`{Worker.Name}`): each process of a fleet has its own tenants.
- **A local party runs in one process at a time:** it holds a lock on its private store (`-Local/$Lock.Party`) while it
  runs. Two hosts started with the same `--Instance`, or the same App started twice on the same store, fail to start
  ("already running in another process"); a test that starts a party must dispose it before starting it again. The
  lock is released when the start fails. The test projects of a solution run in parallel processes: the test server of
  CK.Testing.Engine.HttpEndpoint (`CreateRunningAspNetServerAsync`) uses the `TestStore` folder of its test project
  (unless its identity configures a `"StoreRootPath"`), not the store of the user that all the test projects shared.
- **An enlistment can't be pushed out:** a holder keeps 4 pending keys per remote, a revealed one is never evicted, and
  when all are revealed a new key is refused until one expires. The pending approval says how many keys compete for a
  party (`IPendingApproval.CompetingKeyCount`): the approval page warns when there are several.
- **The AuthCenter is the hub of the System's administration:** a party command whose target is not a remote of the
  current local party goes through its remote AuthCenter (`IPartyRelay` of CK.AppIdentity.Cris, implemented by
  CK.AppIdentity.AuthCenter), as an `IPartyRoutedCommand` along [the party, the AuthCenter, the target]: it was "no
  route" before. The AuthCenter is also the directory: `IGetAcceptingRemotesQCommand` lists its remotes that accept a
  command (the tenants page finds the Apps that hold tenant templates this way).

**Check:** the builder's log has "Checking the 2 instances of the party" ("Checking the 3 instances" for an Opened
fleet: its first dynamic instance is checked too); `#Dev/Start.ps1` lists `Worker.Host[1]` and `Worker.Host[2]`.
CK-Sample-AspNetAuth (the Worker, an Opened fleet of two, at most four; Worker2, an Opened tenant template of five) is
migrated this way.

## U18. The configuration levels and `"$LOCK"`

A configuration value has a level (`ConfigurationLevel`, CK.AppBuilder.Abstractions, CK-Abstractions): `Compile`
(fixed by the build), `Init` (read when the application starts) or `Run` (changed while it runs). The level is a
capability of the code that reads the value: the engine inputs (the compile keys of the contributors) are `Compile`, the
sections that the code reads at start are `Init`. The configuration can only lower it, with a `"$LOCK"`. It is new: nothing
to migrate, but a project that wants a value fixed by its build uses it.

**Detect:** an Opened remote template (`"MaxInstanceCount"` on an entry of `"Parties"`, or a link to an Opened fleet)
whose `"KeyManagement": { "AutoTrustKey" }` is not `"Never"`; an `IAppBuilderContributor` (or an aspect) of your own that
reads a section of the hosts' configuration at start, or projects a value (`AppConfiguration.ProjectValue`).

**Do:**
1. To fix a value at build time, write a `"$LOCK"` in the object that holds it (in a builder's `ckonf.json`, or in the
   central one: a builder's entry, or `Defaults:App` for every App). From CK-Sample-AspNetAuth (the central
   `ckonf.json`, Auth3's database):

   ```jsonc
   "Aspects": {
     "SqlServer": {
       "DefaultDatabaseConnectionString": "Server=.;Database=CKSampleAuth3;Integrated Security=True;TrustServerCertificate=true",
       "$LOCK": { "DefaultDatabaseConnectionString": "Compile" }
     }
   },
   ```

   The patterns are relative to the object that holds the `"$LOCK"`: the configuration's `:` paths, where a whole segment
   `*` is any member of an object and `[*]` any item of an array (`"Parties:[*]:Address": "Compile"`,
   `"HttpEndpoint:Kestrel:Endpoints:*:Certificate": "Compile"`). An index (`Parties:0`) is refused. A `"$LOCK"` in a party
   (`"Local"`, an entry of `"Parties"`) locks its value; in a `"Defaults"`, it locks the value of every party below it
   (its own key, otherwise the nearest `"Defaults"`), at the path of each party.
2. An Opened remote template keeps `"AutoTrustKey": "Never"` (or nothing): its dynamic instances are trusted by an
   approval only.
3. A contributor of your own declares what it reads at start, so that a `"$LOCK"` can restrict it:
   `AppConfiguration.DeclareInitSection( path )`; its sections of a kind (the parties of the AppIdentity contributor):
   `AppConfiguration.DeclareAnchor( path, kinds, fallbacks )`. An aspect that projects a value from its configuration
   gives its origin: `ProjectValue( path, value, origin )` (the configuration path of the value, `"ckonf:Aspects:SqlServer:DefaultDatabaseConnectionString"`):
   a `"$LOCK"` on the origin burns the projected value. A package restricts the values of a kind of party with
   `[ConfigurationLock( "OpenedRemoteTemplate:AutoTrustKey", ConfigurationLevel.Compile, Value = "Never" )]` on its feature
   driver: the kind of the parties ("Party", "Local", "TenantDomain", "TenantTemplate", "Remote", "RemoteTemplate",
   "OpenedRemoteTemplate", or "Client" for a client feature), then the path in the type's section. On an option, the
   path is in the option's section, in each concrete owner of the App (`<Owner>:<Option>`: an option of a base owner
   applies to the derived ones). A contributor that knows where the sections of its types are resolves them
   (`IAppBuilderContributor.ResolveSections`); the default is the configurable name.

**Behavior:**
- A `"$LOCK"` is a build input: the builder removes it before anything reads the configuration, it is never projected.
- A value restricted to `Compile` is burnt: it is not written in the host's `appsettings.json` (except a value that is an
  item of an array: its removal would shift the next ones), it is in the generated code (`CompiledConfiguration`,
  CK.AppIdentity), and CK.AppHost adds it after the other configuration sources, before the resolution of a fleet's
  placeholders. A configured value that differs, or a value that a lock covers without being burnt, is ignored with a
  warning (the values are never logged). A literal pattern that matches no value burns its absence.
- The build fails on an invalid level or pattern, a locked value that no code declares (`"Logging"`, `"CK-Monitoring"`:
  not declared yet), a level above the capability of the code (`"Run"` on a value read at start). In the central
  `Defaults:App`, the lowest level of a pattern wins.
- KeyManagement's `AutoTrustKey` other than `Never` written on an Opened remote template fails the build (it was refused
  at run time only, which it still is); a value from a `"Defaults"` is replaced by `Never` (an info).
- A changed `"$LOCK"` changes the signature of the generated code: the engine runs again.

**Check:** the build succeeds; a locked value is not in the host's `appsettings.json` and is in its generated code
(`CompiledConfiguration_CK`). CK-Sample-AspNetAuth locks Auth3's connection string this way.

## U19. The run properties and the dynamic configuration

A value that its code can change while the application runs is a **run property**: a `[ConfigurationLevel( Run )]`
property of an App singleton (an `ISingletonAutoService` or a real object) that declares its section of the App's
configuration with `[ConfigurationSection( "Name" )]` (CK.AppBuilder.Abstractions, CK-Abstractions). The packages of
the dynamic configuration (CK-Cris-Abstractions, CK-Cris, CK-AppIdentity-Transport) initialize them and change them while
the application runs. It is new: nothing to migrate, but an application that wants to change a value without a restart
uses it.

**Detect:** a singleton property that the application changes while it runs from its own code or a command of its own
(a count of workers, a timeout); a section of the hosts' configuration read by such a singleton.

**Do:**
1. Declare the section and the run properties (from CK.Cris.Executor, CK-Cris):

   ```csharp
   [ConfigurationSection( "CrisExecutionHost" )]
   public sealed partial class CrisExecutionHost : ICrisExecutionHost, ISingletonAutoService
   {
       [ConfigurationLevel( ConfigurationLevel.Run )]
       public int ParallelRunnerCount { get => _runnerCount; set { Throw.CheckOutOfRangeArgument( value >= 1 && value <= 1000 ); Push( value ); } }
   }
   ```

   The declaration is a contract: the setter is thread safe and validates (it throws on an invalid value). The value is
   a basic one (a number, a boolean, a string, an enum, a `TimeSpan`, a `Guid`); a decorated property whose type is a
   class gives access to its own decorated properties (`"Section:Options:Delay"`). The owner doesn't read the
   configuration.
2. In the App: reference **CK.Cris.DynamicConfiguration** (CK-Cris) and, in a CK-AppIdentity application,
   **CK.AppIdentity.DynamicConfiguration** (CK-AppIdentity-Transport: the changes are kept in the store of the root party);
   in its builder, **CK.Cris.DynamicConfiguration.Engine** (CK-Cris). The application that sends the commands references
   **CK.IO.DynamicConfiguration** (CK-Cris-Abstractions); a client application that sends them registers them
   (`[RegisterTypeScriptType( typeof( IConfigurePropertyCommand ) )]`, `IGetRunPropertiesQCommand`), as CK-Sample-AspNetAuth's
   Admin does.
3. Configure the initial value in the App's `ckonf.json`, a root key of the App (from CK-Sample-AspNetAuth, Auth3):

   ```jsonc
   "CrisExecutionHost": { "ParallelRunnerCount": 2 },
   ```

   A `"$LOCK"` restricts it (U18): to `Init`, it is initialized and not changed; to `Compile`, it is burnt.

**Behavior:**
- At start (the `DynamicConfigurationService` is an `IHostedService`), the initial value of each run property is resolved,
  then set once: a configured value that changed since a change was recorded wins (the latest decision), otherwise the
  recorded change, otherwise the configured value; without any of them, the property keeps the value of its code.
- `IConfigurePropertyCommand` (a party command at the Critical level, sent to a local party of the application, directly
  or through the hub of the System): `PropertyPath` ("CrisExecutionHost:ParallelRunnerCount"), `Value` and `CurrentValue`
  as texts (invariant culture). The current value is compared (the optimistic concurrency): a conflict fails and its
  message gives the actual value; an invalid value fails with the setter's message; a change made while impersonating
  is refused. A success is recorded ("DynamicConfiguration.json" in the `-Local` store of the root party).
- `IGetRunPropertiesQCommand` lists the run properties that can be changed (the restricted ones are not listed), with
  their type and their current value as a text.
- The accessors are generated (`RunPropertyDirectory`): no reflection at run time. A type that declares a section and is
  not an App singleton, a run property of another type, are build errors.

**Check:** the build succeeds; `IGetRunPropertiesQCommand` lists the run properties; a change survives a restart.
CK-Sample-AspNetAuth changes Auth3's `ParallelRunnerCount` from the Admin this way.

## U20. The secrets

A secret (a client secret, a password, a connection string with credentials) is never written in a configuration file:
its place holds a **handle**, the name of the secret, that the host resolves when it starts (`ConfigurationSecret`,
CK.AppBuilder.Abstractions). It is new: a plain value still works, but it is in the files.

**Detect:** a secret value in a `ckonf.json` or an `appsettings.json` (`ClientSecret`, `Password`, a connection string
with `Password=`); secrets in the user-secrets of a host (`UserSecretsId` in its `.csproj`).

**Do:**
1. Replace the value by a handle in the `ckonf.json` (from CK-Sample-AspNetAuth's README, a scheme of the AuthCenter):

   ```jsonc
   "Google": {
     "Authority": "https://accounts.google.com",
     "ClientId": "...",
     "ClientSecret": { "$SECRET": "Google:ClientSecret" },
     "Scopes": "openid email"
   }
   ```

   `"$SECRET"` is the only key of its object; a name is made of segments separated by `:` made of letters, digits, `_`
   and `-`. The connection strings of the "SqlServer" aspect (U14) accept a handle too
   (`"DefaultDatabaseConnectionString": { "$SECRET": "Db" }`, and the `ConnectionString` of its `"Databases"`).
2. Give the values, by name, as the configuration keys `Secrets:<Name>` of the host:
   - in development, in the `#Dev/Secrets.json` file at the root of the solution (`#Dev` is ignored by git), whose root is
     the content of the `Secrets` section, shared by the parties of the solution:

     ```json
     { "Google": { "ClientSecret": "..." } }
     ```

   - elsewhere, from any configuration source of the host: an environment variable (`Secrets__Google__ClientSecret`), a
     vault provider added by the `configure` action of `CKomposableAppHost.Build`.
3. A package that knows that a value is a secret declares it, so that a plain value is reported at build time:
   `[ConfigurationSecret( path )]` on a configurable type (built like a `[ConfigurationLock]` path: a kind of party, then
   the path in the section of the type, with the `*` and `[*]` patterns), or `AppConfiguration.AddSecretKey( path )`
   from a contributor that reads its keys by hand. In the stack: the client secrets of the "Oidc" scheme family
   (`[ConfigurationSecret( "Party:*:ClientSecret" )]` on `AuthCenterOidcOption`, U21) and the default connection
   string of the "SqlServer" aspect.
4. An aspect that needs the value at build time (the setup of a development database) resolves it with
   `AppBuilderContext.TryResolveSecret( monitor, name, out value )` (from `#Dev/Secrets.json`) and projects the handle:
   `AppConfiguration.ProjectValue( "<path>:$SECRET", name, "<origin>:$SECRET" )`.

**Behavior:**
- The handle is projected as is in the host's `appsettings.json`; a `"$LOCK"` to `Compile` on the value burns the handle,
  never a value (U18).
- The host (CK.AppHost) resolves every handle after the burnt values and the instance of a fleet, through a last
  configuration source: the code reads the value at the path of the handle. A missing secret fails the start, with all
  the missing names at once; a value is never logged.
- An optional handle, `{ "$SECRET?": "<Name>" }`, names a secret that may be missing: its path is then absent (as if
  nothing were configured there), the missing optional names are logged. The deployment chooses it (a one-time
  enlistment ticket, U26; a feature activated by its secret); `ConfigurationSecret.IsHandlePath( path, out valuePath,
  out isOptional )` and `IsHandleKey` recognize both keys.
- At build time, an invalid handle is an error; a plain value where a secret is declared is an information line (the
  development configuration accepts it).
- The legacy SQL setup still writes the connection strings it uses in the generated code and its logs.

**Check:** the build succeeds; the hosts' `appsettings.json` hold the handles; the host starts with the values in
`#Dev/Secrets.json` and fails with the list of the missing names without them.

## U21. The scheme families are options of the AuthCenter

A scheme family of the AuthCenter (OpenID Connect, passkeys, the password and the weak logins) is an option of the
`LocalAuthCenterFeatureDriver` (CK.AppIdentity.AuthCenter.Local, CK-AppIdentity-Transport): its section is the
sub-section of its name (`"AuthCenter:Oidc"`, `"AuthCenter:Passkey"`), that its constructor reads, and the AuthCenter
creates the family of each local Authentication Center. **A scheme is available only through an enabled family and its
registered login provider:** the "Basic" scheme needs the "Password" option (CK.AppIdentity.AuthCenter.Password), the
"Weak" scheme the "Weak" option (CK.AppIdentity.AuthCenter.Weak). An option holds the family only, never the provider
(the provider is the backend's: CK.DB.User.UserPassword, CK.DB.Actor.WeakActor...). The configuration of the schemes
doesn't change.

**Detect:** `AuthCenterOidcFeatureDriver`, `AuthCenterPasskeyFeatureDriver`, `AuthCenterOidcConfiguration`,
`AuthCenterPasskeyConfiguration`, `IAuthCenterSchemeConfiguration`, `LocalAuthCenter.GetSchemeSettings`; the keys
`"AuthCenterOidc"` or `"AuthCenterPasskey"` in a configuration; an AuthCenter application (it references
CK.AppIdentity.AuthCenter.Local) that references a login provider (CK.DB.User.UserPassword, CK.DB.Actor.WeakActor) without
its family.

**Do:**
0. **An AuthCenter references the families of its login methods:** CK.AppIdentity.AuthCenter.Password for "Basic" and
   CK.AppIdentity.AuthCenter.Weak for "Weak" (from Auth3.Auth3.App, CK-Sample-AspNetAuth):

   ```xml
   <PackageReference Include="CK.AppIdentity.AuthCenter.Password" Version="4.0.0-0.uc..." />
   <PackageReference Include="CK.AppIdentity.AuthCenter.Weak" Version="4.0.0-0.uc..." />
   ```

   A test that lists the types adds `typeof( Password.AuthCenterPasswordOption )` and `typeof( Weak.AuthCenterWeakOption )`;
   `AddAuthCenterApplication` (CK.Testing.Engine.AppIdentity.AuthCenter) brings both.
1. **A test that lists the types of the family** lists the option instead (from CK.AppIdentity.AuthCenter.Tests):

   ```csharp
   engine.FirstBinPath.Types.Add( typeof( Oidc.AuthCenterOidcOption ), typeof( Oidc.AuthCenterOidcHttpClient ) );
   engine.FirstBinPath.Types.Add( typeof( Passkey.AuthCenterPasskeyOption ), typeof( Passkey.AuthCenterPasskeyHandlers ) /* ... */ );
   ```

   A test without engine registers the option: `services.AddPartyComponentOptions( typeof( Oidc.AuthCenterOidcOption ) )`
   (CK.Testing.AppIdentity), with `services.AddSingleton<Oidc.AuthCenterOidcHttpClient>()`.
2. **A configuration that disables a family** writes it in the AuthCenter's section: `"AuthCenterOidc": false` becomes
   `"AuthCenter": { "Oidc": false }` (the same for `"Passkey"`).
3. **A family of its own** is an option of the AuthCenter that creates the family (from CK.AppIdentity.AuthCenter.Oidc):

   ```csharp
   [PartyFeatureOption( "Oidc", TypeActivation.ByDefault )]
   [ConfigurationSecret( "Party:*:ClientSecret" )]
   public sealed class AuthCenterOidcOption : IPartyComponentOption<LocalAuthCenterFeatureDriver>, IAuthCenterSchemeFamilyOption
   {
       public AuthCenterOidcOption( IConfiguration? configuration, AuthCenterOidcHttpClient http )
       {
           _http = http;
           Schemes = OidcSchemeOptions.Read( configuration );
       }

       public IAuthCenterScheme CreateFamily( IActivityMonitor monitor, ILocalParty local ) => new AuthCenterOidc( Schemes, _http.Client );
   }
   ```

   Its constructor reads its section (null when it is absent) and throws on an invalid value (U8); its feature driver and
   its `IAuthCenterSchemeConfiguration` are removed.

**Behavior:**
- The families are created for every local Authentication Center, the dynamic tenants included, right after its
  `LocalAuthCenter`; `LocalAuthCenter.SchemeFamilies` lists them as before.
- **A scheme without an enabled family is not available:** `IGetLoginSchemesQCommand` lists the schemes of the enabled
  families whose provider is registered (nothing without a local AuthCenter), and the login of any other scheme fails with
  `"Authentication.UnknownScheme"`. `"AuthCenter": { "Password": false }` (or `"Weak"`, `"Passkey"`, `"Oidc"`) turns a
  login method off for a party. The password management commands are not affected.
- An invalid section of a family fails the setup of the party ("Unable to create the option 'AuthCenter:Oidc': ..."),
  a sub-section of the AuthCenter that is not an option of an installed package is still an error.

**Check:** no type of the Detect list is left; the scheme listing shows the same schemes (an AuthCenter without the
"Password" family no longer lists "Basic").

## U22. The payload of the "Oidc" login provider is an `IOidcLoginPayload`

The OpenID Connect scheme family (CK.AppIdentity.AuthCenter.Oidc) fills the payload of the "Oidc" login provider through
a contract: `IOidcLoginPayload` (CK.AuthenticationInfo.Oidc, CK-Auth-Abstractions), no more by reflection on its
`SchemeSuffix`, `Sub` and `Claims` properties. CK.DB.User.UserOidc's `IUserOidcInfo` implements it.

**Detect:** an `ILoginProvider` whose `ProviderNames` contain "Oidc", other than the one of CK.DB.User.UserOidc (a
test provider, a provider of its own).

**Do:** its payload implements `IOidcLoginPayload` (a reference to CK.AuthenticationInfo.Oidc). From
CK.Testing.Engine.Cris.Authentication (CK-Cris):

```csharp
public sealed class FakeOidcPayload : IOidcLoginPayload
{
    public string SchemeSuffix { get; set; } = "";
    public string Sub { get; set; } = "";
    public string Claims { get; set; } = "";
}
```

A Poco payload declares it as a base interface: `public interface IUserOidcInfo : IPoco, IOidcLoginPayload` (its
properties are declared `new`).

**Behavior:**
- The start of an Oidc login fails when there is no "Oidc" login provider or when its payload is not an
  `IOidcLoginPayload`: the user is never sent to the identity provider (the code is not consumed for nothing).
- The `Claims` are always set (a JSON object, empty when not provided).

**Check:** an Oidc login completes.

## U23. The deployments

A solution with a central configuration (U16) may describe its deployments: its `ckonf.json` is the development one
(`#Dev`), each other deployment is a folder of its `Deployment` folder (next to the central `ckonf.json`), an overlay of
it. It is new: a solution without `Deployment` folder is unchanged.

**Detect:** a solution whose configurations of another environment are kept by hand (an `appsettings.Production.json`,
a copy of the `ckonf.json`).

**Do:**
1. **A deployment is a folder whose name starts with `#`** (its environment); the folders above it are its domain
   (none: the central domain). Its `ckonf.json` is a JSON Merge Patch (RFC 7396) of the central configuration: an object
   merges, `null` removes, any other value (an array included) replaces. From CK-Sample-AspNetAuth's
   `Deployment/#Production/ckonf.json`:

   ```jsonc
   {
     "Builders": {
       "Auth3/Auth3.Builder": {
         "Aspects": { "SqlServer": { "DefaultDatabaseConnectionString": { "$SECRET": "Auth3:Database" } } },
         "App": { "Defaults": { "Transport": { "ListeningAddress": [ "tcp:0.0.0.0:37300" ] } }, "Demo": null }
       },
       "Front/Front.Builder": { "App": { "Local": { "HttpEndpoint": { "Urls": "http://*:5300" } } } }
     },
     "Links": [ { "From": "$Front", "To": "$Auth3", "Link": { "AuthCenterRemote": true, "Address": "tcp:auth3.sample.example.com:37300", "KeyManagement": { "AutoTrustKey": "Never" } } } ]
   }
   ```

   `"$BASE": "#Production"` overlays another deployment instead of the central configuration. `"Domain"` and
   `"Environment"` can't be written (the path gives them); `Deployment/#Dev`, a path that spells the central domain and
   a `.` in a domain part are errors; `Deployment/Assets`, the folders whose name starts with `.` and the folders without
   a `#` folder below them are ignored.
2. **Mark what each deployment must write itself** in the central configuration: `"$PER_DEPLOYMENT"` on any object, an
   array of patterns relative to it (from CK-Sample-AspNetAuth's `ckonf.json`):

   ```jsonc
   "App": {
     "$PER_DEPLOYMENT": [ "Demo", "Local:MailSender", "Local:AuthCenter:Passkey:RpId" ],
     ...
   },
   "$PER_DEPLOYMENT": [ "Links:[*]:Link:KeyManagement:AutoTrustKey" ],
   ```

   The packages declare theirs: `[ConfigurationPerDeployment( "Party:Urls" )]` on a configurable type (the HttpEndpoint
   feature), `[ConfigurationPerDeployment( "Party:ListeningAddress" )]` (the Transport feature), the address of a link
   (the AppIdentity engine: `[assembly: CentralConfigurationKey( "Links", PerDeployment = ["[*]:Link:Address"] )]`), the
   address of a remote written in the `Parties` of an App (the AppIdentity engine:
   `[assembly: ConfigurationPerDeployment( "Remote:Address" )]`, a property of the parties of a kind), the size of a
   fleet (the App builder), and `AppConfiguration.AddPerDeploymentKey( path )` for a reader of its own.
3. **Commit the `ckonf.merged.json`** that the builders write next to each overlay (a build impact of the first build:
   `ckli build --build-impacts commit`).

**Behavior:**
- Every builder run merges the deployments and writes their `ckonf.merged.json` when it changes (its first line lists its
  sources); the markers are build inputs, removed from every projection and from the merged files.
- A value per deployment that a deployment inherits (from the central configuration or from its base) instead of writing
  it in its own overlay (`null` counts) fails the build: "The deployment '#Production' inherits "...": a value per
  deployment is written by the deployment itself". An array or a `null` of the overlay writes everything below it.
- CKli warns that a `Deployment` folder without `GenerateAssets.cs` is ignored by its build: the assets of the
  deployments are U25.

**Check:** the build succeeds; each deployment folder has its `ckonf.merged.json`, committed.

## U24. The name of an App is the identity of its party

In a solution with a central configuration (U16), the name of an App was its party name (`Front/Front.Builder` built the
App `Front`, the party `$Front`). It is now the identity of its party: its domain then its party name, the `/` of the
domain mapped to `.`, and so are its projects. The parts of a domain and a party name have no `.`: the name is
reversible. The party names, the full names and the links don't change.

**Detect:** a central `ckonf.json` with a `"Domain"`: the build fails with "The name of ... must be the identity of its party:
'Sample.Auth3.Front' (...)".

**Do:**
1. **Optionally, choose a `"GlobalDomainPrefix"`** in the central `ckonf.json` (only there: a deployment can't write
   it): the `"Domain"` or a prefix of it (whole parts), left out of the names. From CK-Sample-AspNetAuth:

   ```jsonc
   "Domain": "Sample/Auth3",
   "GlobalDomainPrefix": "Sample",
   ```

   The App of `$Front` is then `Auth3.Front`; with the whole domain as prefix, the names stay the party names.
2. **Rename each triplet** to the name the error gives: the folders and the `.csproj` files of `X.App`, `X.Builder` and
   `X.Host`, the `ProjectReference`s, the `.slnx`, the keys of `"Builders"` (`"Front/Auth3.Front.Builder"`), the
   assembly qualified names of the configuration (`"CK.Sample.Front.IFrontInfoQCommand, Auth3.Front.App"`), the tests
   that load a host (`Assembly.Load( "Auth3.Front.Host" )`), the `launchSettings.json` profiles. The parent folder is
   free (the sample keeps the party name: `Front/Auth3.Front.Builder`).
3. **Delete the old manifests** of `#Dev/Parties` (named after the Host projects: `Auth3.Front.Host.json`); the build
   writes the new ones.

**Behavior:**
- An App whose explicit `FullName` (or `PartyName`) disagrees with its name is an error; a `FullName` outside the
  `"GlobalDomainPrefix"` too. A fleet's name is its party without the placeholder (`Auth3.Worker` for `$Worker-{Name}`).
- Without `"Domain"`, the names are the party names (no `.`).

**Check:** the build succeeds; the `FullName`s of the projected `ckonf.json` are unchanged.

## U25. The assets of the deployments

The deployments of U23 are built: CKli runs `Deployment/GenerateAssets.cs` after the build of the repository (the
version as its argument), and the assets of each deployment are part of the release: one per App by default
(`Production-Auth3.Front.zip`, a zip of `Deployment/Assets/Production-Auth3.Front`), one per deployment with
`"Deployment": { "SingleZipAsset": true }` (`Production.zip`, a folder per App in it). CKli runs it on demand (`ckli build --with-assets`, and always for `ckli publish`). It is new: a solution without
`GenerateAssets.cs` is unchanged.

**Do:**
1. **Write `Deployment/GenerateAssets.cs`** (CK.AppDeployment, a package of CK-AppBuilder):

   ```csharp
   #:package CK.AppDeployment@37.0.1-0.uc.0.ci.4
   return await CK.Setup.AppDeployment.RunAsync( args );
   ```

   CKli upgrades the `#:package` version (CKli 0.55) and runs the script with the local feed.
2. **Write `Deployment/.gitignore`** with `Assets/` (CKli creates it otherwise: a build impact).
3. **Choose the deployed builders** when not all of them can be deployed: a builder whose engine sets up a database at
   build time (the SqlServer aspect) fails in the build of a deployment. From CK-Sample-AspNetAuth's
   `Deployment/#Production/ckonf.json`:

   ```jsonc
   "Deployment": {
     "Builders": [ "Front/Auth3.Front.Builder", "Admin/Auth3.Admin.Builder", "Worker/Auth3.Worker.Builder", "Worker2/Auth3.Worker2.Builder" ],
     // The MSBuild properties of the publication of the hosts (applied as-is), then the ones of an App over them.
     "Publish": { "RuntimeIdentifier": "linux-x64" },
     "Apps": { "Auth3.Worker2": { "Publish": { "SelfContained": true, "PublishSingleFile": true } } }
   },
   ```

   `"Publish"` is not modeled: any MSBuild property (`RuntimeIdentifier`, `SelfContained`, `PublishSingleFile`,
   `PublishTrimmed`, `PublishAot`, `InvariantGlobalization`...), a string, a number or a boolean; MSBuild and the SDK
   report the rest at publish. An App's empty value resets a property (the SDK default); a deployment removes an
   inherited one with `null`. In the central configuration, the `"Deployment"` key holds the defaults of every
   deployment. AOT and trimming are not expected to work yet (the stack uses run-time reflection).
4. **Write the size of each fleet** in each deployment (`"InstanceCount"`, `"Instances"`, `"MaxInstanceCount"` are values
   per deployment: inherited, they fail the build):

   ```jsonc
   "Worker/Auth3.Worker.Builder": { "App": { "InstanceCount": 3, "MaxInstanceCount": 6 } },
   ```

**Behavior:**
- For each deployment and deployed builder, CK.AppDeployment copies the builder and the hosts of its Apps (the files git
  tracks) in `Deployment/.build/{AssetName}/` (ignored by git; `#Production` is `Production`, `Paris/Shop/#Staging` is
  `Paris.Shop.Staging`), the copied builder builds the deployment (`ckonf.deployment.json`: its `ckonf.json` is the
  projection of the merged configuration, its `.App` projects are the originals), and each host is published
  (`dotnet publish -c Release`) in `Deployment/Assets/{AssetName}-{App}` (or `{AssetName}/{App}` with
  `"SingleZipAsset"`: the App name, without `.Host`). Its publication properties are written in a
  `<PropertyGroup>` of the copied host project (not on the command line: the generated builder runs on the build
  machine). The manifest written by the builder (`$StObjGen/Deployment.manifest`) is added, with the version, to the
  `CK.AppHost.Published.json` of the published host (`"Deployment"`): the full name, the version, the deployment, the
  publication properties, the names of the secrets the host needs, its values per deployment. The manifest of
  a fleet lists its instances (`"Instances"`): each configured one with its full name (`Sample/Auth3/$Worker-1/#Production`),
  its start arguments (`--Instance=1`) and its values per deployment resolved, and the indexes of the dynamic ones of an
  Opened fleet (`"Dynamic": { "From": 4, "To": 6 }`). A fleet is one published host: the platform starts one process per
  instance.
- The remote templates of the links to a fleet follow its size (copied from the same merged configuration).
- A secret handle of a fleet App may use its placeholders (`{ "$SECRET": "Worker:{Name}:Key" }`: a secret per
  instance, resolved with the instance at start): the build checks the name of each instance, the manifest lists the
  names of each instance (`"Secrets"` of each configured one). A secret that the build itself needs has no placeholder.
- The build of a deployment never reads a secret (`AppBuilderContext.Deployment`: `TryResolveSecret` fails), never
  writes in `#Dev`, never sets up a database.
- A published host carries its real `InformationalVersion` (`{version}/{commit}/{date}`, as CKli builds).
- The `Address` of a remote may be a host name (`tcp:auth3.sample.example.com:37300`, `mtls:` too): it is resolved when
  connecting, at each connection. A `ListeningAddress` stays an IP address (a host name is an error).
- A host built for a deployment burns the full name of its party: its environment (`Production`) is the .NET environment
  of the host unless `--environment`, `DOTNET_ENVIRONMENT` or `ASPNETCORE_ENVIRONMENT` gives one. At start, a `#Dev`
  party in a host that is not in Development is a warning; another party in a host in Development is an error.
- The client applications are built by the publication of their host and published with it: the builder writes
  `$StObjGen/Clients.props` (the TypeScript engine), the publish target of CK.TS.AspNet.Clients runs the yarn `build`
  script of each one (`ng build`) and publishes its output at the same relative path (`Clients/App/dist/App/browser`).
  `node` must be on the path; `CKClientsPublish=false` opts out. The development build doesn't change.
- Every publication of a host writes `CK.AppHost.Published.json` next to its executable (the effective `"Publish"`
  properties: the `Executable` to start, `RuntimeIdentifier`, `SelfContained`, `PublishSingleFile`...), without the XML
  documentation of its assemblies and packages (`PublishDocumentationFiles` and `PublishReferencesDocumentationFiles`
  default to false). A published host uses its own folder as its
  content root, whatever the directory it is started from, unless `--contentRoot`, `DOTNET_CONTENTROOT` or
  `ASPNETCORE_CONTENTROOT` gives one, and logs its version and its deployment at start. A host of the base SDK (a
  worker) publishes its `appsettings.json` too. A self-contained host may reference its builder (CK.AppHost turns off
  the SDK check NETSDK1150: the reference only orders the builds).

- The manifest of a deployed host (in `CK.AppHost.Published.json`, under "Deployment", and per configured instance of a
  fleet) lists the names of its required secrets ("Secrets") and of its optional ones ("OptionalSecrets").

**Check:** `ckli build --with-assets` produces the assets of each deployment (`$Local/Assets/{Repo}/{Version}/Production-Auth3.Front.zip`...); a
published host starts in the environment of its deployment.

## U26. The pre-authorized enlistment ticket

**What changes:** an operator who expects a party (a deployed host, an instance of a fleet) opens an enlistment ticket
for it; the party starts with it and the Authentication Center trusts its key at once: no approval code, no pending
approval. It replaces the anchors file of a deployment (the ticket carries the anchor of the center).

**Why:** a deployed party can't show an approval code to an operator, and its first start must anchor its center: the
ticket does both, and while it is open nobody else can enlist under the expected name.

**Migration:**
1. Nothing to change in the code: CK.AppIdentity.AuthCenter.Enlistment (on the center) holds the tickets
   (`AuthCenterEnlistmentTickets`, the `IEnlistmentTicketAuthority` of the transport), the approval page
   (CK.Ng.AppIdentity.Enlistment) opens them ("Expect a party"), `ckey ticket open|list|revoke` too on the center.
2. In a deployment, the expected party reads its ticket from `Local:KeyManagement:EnlistTicket`, a secret: give it as an
   optional handle (U20), the ticket is needed at the first start only:

   ```jsonc
   "KeyManagement": { "TrustAnchors": null, "EnlistTicket": { "$SECRET?": "Enlist:Worker2" } }
   // a fleet: { "$SECRET?": "Enlist:Worker-{Name}" }
   ```

   The operator gives `Secrets__Enlist__Worker2=ckt1....` (or the vault's `Secrets:Enlist:Worker2`) at the first start.
3. In a deployment where no key is trusted on first use (`"AutoTrustKey": "Never"`), every party linked to the
   Authentication Center takes a ticket this way, the party that hosts the approval page included. Its ticket comes
   first: it is opened with `ckey ticket open <FullName>` on the center's machine (the page isn't running yet); the next
   ones are opened on the page. The ticket replaces the anchors of the parties: the center trusts the key that proves
   it, and the party anchors the center from it.

**Behavior:**
- A ticket is a one-time P-256 key (`EnlistTicket`, `ckt1.` + base64url): the center keeps its public key only (one
  json file per ticket in its local store "EnlistTickets"), the text is shown once. Its lifetime is 1 day by default,
  7 days at most. Opening, listing and revoking require the Critical level (forwarded to the center by any party).
- The ticket anchors its center at the checkpoint it carries (a different configured anchor of the center is an error).
- The proof travels in an optional field of the initial message of the transport (all the parties of a System use the
  same transport version): the id and a signature over the full name, the identity checkpoint, the holder and the
  ephemeral key of the connection. The center trusts the key at once and marks the ticket used.
- While a ticket is open for a party, a key presented without a valid proof is refused (no pending approval).

**Check:** open a ticket on the approval page, start the expected party with it: it is trusted without a code, the
ticket is listed as used.

## U27. The routes are gone: a System is a star around its Authentication Center

**What changes:** the parties announce no routes any more. Every party that designates an Authentication Center is linked
to it; a command for a party that is not a remote goes through the center (the hub, `IPartyRelay`): at most
[party, center, target]. What a route carried is a fact read where it is needed: the approval page is the client
application with the `"Enlistment"` fact (`[PartyClientFact( "Enlistment", ... )]`), the center reads it from the
descriptions of its remotes and states its URL (`EnlistmentUrlStatement`); the pending approvals and the instances are
asked through the hub (`[Internal] IGetHeldEnlistmentQCommand`), no more collected along a route.

**Why:** the routes served an arbitrary topology (announced, computed and propagated by every party, authorized against
hijacking by `RouteHosts`). In a star, they computed nothing; the center already knows every identity: nothing to hijack.
A large System will add delegates of the center (the AuthCenterProxy), never routes.

**Detect:** `RouteHosts` in a configuration (`"Transport:RouteHosts"`, `"AuthCenter:RouteHosts"`, a `$PER_DEPLOYMENT`
pattern); `PartyDescription.Routes`, `Collected`, `FindRoute`, `PartyRoute`, `CollectedItem`, `HostRoute`, `Collect`,
`ContributeDerivedDescription`/`DerivedBuilder`; `IPartyRoutedCommand.RouteName`/`Destination`,
`PartyRoutedCommandSender.SendAsync( ..., routeName, ... )`; `AuthCenterFeature.RouteName`; `IAuthorizeRouteHostCommand`;
`IApprovePartyCommand.Routes`, `IPendingApproval.Routes`.

**Do:**
1. Remove the `RouteHosts` (and its `$PER_DEPLOYMENT` pattern): nothing replaces it, the approval page is found from its
   fact.
2. Link every party that designates an AuthCenter (`Local:AuthCenterRemote:Party`) to it: the center must be one of its
   remotes.
3. A driver that contributed to the second phase of the description contributes in `ContributeDescription` (the
   description has one phase: `PartyDescription.Builder.Build()`).
4. A routed command is sent along a path: `PartyRoutedCommandSender.SendToAsync( monitor, services, origin, command,
   destination )` (a direct remote, otherwise through the hub) or `SendAlongPathAsync`.

**Behavior:**
- A `RouteHosts` configuration fails the setup (an explicit message); so does an AuthCenter designated by
  `Local:AuthCenterRemote:Party` that is not a remote of the party.
- Several approval pages among the remotes of the center: an error of the center (`SecurityCritical`), no page is stated.
- A holder accepts an approval countersigned by its AuthCenter (without AuthCenter: from itself or a direct remote that
  serves the approval page). The removal of an instance (`IRemoveHeldPartyInstanceCommand`) requires the operator at the
  Critical level: without AuthCenter, only a page served by the holder removes it.
- The descriptions, the initial message of the transport and the signed System list lose their route parts (the wire
  format changes: all the parties of a System use the same version).

**Check:** the sample: the Admin is the approval page without configuration; a Worker waiting on Auth3 is approved from
the Admin through Auth3. The references are CK-AppIdentity-Transport's `EnlistmentPageTests` and `EnlistmentTests`.

## U28. The DataProtection key ring is in the store of the party

**What changes:** the DataProtection key ring of a process (it protects the identity key files, the encryption keys of
the parties and what ASP.NET protects) is in the private store of the root party, `-Local/DataProtection-Keys`, no more
in the folder of the account (`%LOCALAPPDATA%\ASP.NET\DataProtection-Keys`, `~/.aspnet/DataProtection-Keys`). Its
encryption at rest: the secret `Local:KeyManagement:KeyRingKey` of the root party, otherwise the CNG store
(CK.AppIdentity.KeyManagement.Cng: the TPM), otherwise DPAPI on Windows, otherwise none (a warning outside `#Dev`).

**Why:** the folder of the account is shared by all its applications, unencrypted on Linux, and absent when the account
has no profile (a container, a service account): ASP.NET then keeps the ring in memory and a restart loses the identity
of the party. The store of the party is where its identity is.

**Detect:** a `#Dev/Store` (or a deployed store) created before this version: its key files were protected by the ring of
the account. A host that calls `PersistKeysTo...` or `ProtectKeysWith...` keeps its choice (nothing to detect).

**Migration:**
1. In development: delete `#Dev/Store` (or the store of the parties that don't start) and start again: new identities,
   new pins, new enlistments. A party that can't open its keys says why ("Its key ring may have changed").
2. In a deployment on Linux (or in a container), encrypt the ring with a secret key, the base64 of 32 random bytes:

   ```jsonc
   "Local": { "KeyManagement": { "KeyRingKey": { "$SECRET": "KeyRing" } } }
   ```

   To change it, give the new key as `KeyRingKey` and the old one as `KeyRingPreviousKey` (a secret too): the existing
   keys of the ring stay readable. A secret can be a file: CK.AppHost reads `$CREDENTIALS_DIRECTORY` (systemd) and
   `$CK_SECRETS_DIRECTORY` (a mounted secret volume), one secret per file (`__` separates the segments of its name). On
   Linux with a TPM, bind the key to the machine with a systemd credential:

   ```
   head -c 32 /dev/urandom | base64 -w0 | systemd-creds encrypt --with-key=tpm2 --name=KeyRing - /etc/credstore.encrypted/KeyRing
   # In the unit of the service:
   LoadCredentialEncrypted=KeyRing
   ```
3. On Windows with CK.AppIdentity.KeyManagement.Cng, nothing to do: the ring is protected by the provider of the root
   party (its `CngKeyStore` option), bound to the computer as the identity keys are.

**Behavior:**
- The protection is chosen at the first use of the ring; each file records it: a change applies to the new keys of the
  ring (one every 90 days), the existing ones stay readable as long as their protection is available.
- An invalid `KeyRingKey` or `KeyRingPreviousKey` fails the start.
- The ring and the identity key files are in the same store: a copy of the store gives the keys away unless the ring is
  encrypted (a secret key kept apart, the TPM, or DPAPI on the same computer and account).

**Check:** start a party: `-Local/DataProtection-Keys/key-*.xml` is in its store; with `KeyRingKey`, the file's
`decryptorType` is `SecretKeyXmlDecryptor`; nothing new in the folder of the account.

## U29. A section can be a configuration model

A configurable type (an App aspect, a Builder aspect, a feature driver, a client feature, an option of a feature driver
or of a client feature) can receive its configuration as a **model**: a class whose properties are the keys of its section, read and validated for it, and
described as a JSON Schema for the tools (the configurator shows it). A type that reads its `IConfiguration` keeps
working: nothing has to change.

**Detect:** an aspect or an option whose constructor reads the keys of its section by hand (`configuration["Key"]`,
`configuration.GetSection( ... )`, a `Read( IConfiguration )` helper that throws on an invalid value); a feature driver
that reads its section in its setup (`LocalConfiguration.Configuration.TryGetSection( "XXX" )`); a client feature driver
whose `Create` reads its section; an `"AuthCenter": { "Oidc": { "<Scheme>": { ... } } }` (the schemes are now in
`"Schemes"`).

**Do:**
1. **Reference CK.ConfigurationModel** (CK-ConfigurationModel, the `explo/uc` line) in the project that declares the
   model, unless it comes through another reference (CK.AppIdentity and CK.AppBuilder bring it).
2. **Declare the model:** a public `[ConfigurationModel]` class (CK.AppBuilder.Abstractions, namespace `CK.Core`) with a
   parameterless constructor. Its keys are its public
   properties with a setter or an `init` accessor: `required` is required, nullable is optional, an initializer is the
   default; a get-only property is a derived member, not a key. An optional `IConfigurationModel.Validate( IConfigurationValidationContext )`
   (the `Path` of the section, `Error` and `Warn`, messages in the invariant culture) reports what the types can't say. From CK.AppIdentity.AuthCenter.Passkey (CK-AppIdentity-Transport):

   ```csharp
   [ConfigurationModel]
   public sealed class AuthCenterPasskeyOptionConfiguration : IConfigurationModel
   {
       public string? RpId { get; init; }

       public string? RpName { get; init; }

       public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes( 5 );

       public string? NormalizedRpId => RpId?.Trim().ToLowerInvariant();

       public string? EffectiveRpName => RpName ?? NormalizedRpId;

       public void Validate( IConfigurationValidationContext context )
       {
           var rpId = NormalizedRpId;
           if( rpId != null && (rpId.Length == 0 || Uri.CheckHostName( rpId ) != UriHostNameType.Dns) )
           {
               context.Error( $"'{context.Path}:RpId' must be a domain name (\"example.com\" or \"localhost\"), without scheme nor port." );
           }
           // ...
       }
   }
   ```

   The supported property types: a basic value (string, bool, int, long, double, decimal, an enum by name, `TimeSpan`,
   `Guid`, `Uri`), a nested model, a list (an array, `IReadOnlyList<T>`, `IReadOnlyCollection<T>`, `IEnumerable<T>`) of
   values or models, a dictionary by name of models or of values (`IReadOnlyDictionary<string, TModel>`,
   `IReadOnlyDictionary<string, TimeSpan>`), an opaque `IConfiguration`. A model is a class: a section whose keys are
   names (the schemes of an option) is a dictionary property of it (`"Oidc": { "Schemes": { ... } }`), never the section
   itself.
   An unsupported property type is an error of the engine run (a modeled option) or of the App build (a modeled aspect).
   `[ConfigurationLevel]`, `[ConfigurationSecret]` and `[ConfigurationPerDeployment]` (CK.AppBuilder.Abstractions) also decorate
   the properties of a model (without a path).
3. **Name it on its owner and receive it:** `[Configuration<TModel>]` (CK.AppBuilder.Abstractions) on the aspect or the option, and
   a constructor parameter of the model type instead of the `IConfiguration`. The option (or the aspect) always
   receives a model: without a section, it has its defaults. From CK.AppIdentity.AuthCenter.Passkey:

   ```csharp
   [PartyFeatureOption( "Passkey", TypeActivation.ByDefault )]
   [Configuration<AuthCenterPasskeyOptionConfiguration>]
   public sealed class AuthCenterPasskeyOption : IPartyComponentOption<LocalAuthCenterFeatureDriver>, IAuthCenterSchemeFamilyOption
   {
       public AuthCenterPasskeyOption( AuthCenterPasskeyOptionConfiguration configuration, IServiceProvider services )
   ```

   From CK.Globalization.StObj (CK-Globalization-Engine), an App aspect:

   ```csharp
   [AppAspect( "Globalization", TypeActivation.Optional )]
   [Configuration<GlobalizationAppAspectConfiguration>]
   public sealed class GlobalizationAppAspect : IAppAspectApply
   {
       public GlobalizationAppAspect( GlobalizationAppAspectConfiguration configuration )
   ```

4. **A feature driver** names its model with `[Configuration<TModel>]` and gets the model of a party with
   `ApplicationIdentityFeatureDriver.GetModel<TModel>( party )` (read before its setup, with its options) instead of
   reading its section. The model of an asymmetric feature is the one of its side: the Local side reads the local
   parties ("XXX"), the Remote side the remotes ("XXXRemote"). From CK.AppIdentity.HttpEndpoint (CK-AspNet):

   ```csharp
   [PartyFeature( "HttpEndpoint", TypeActivation.Optional )]
   [Configuration<HttpEndpointConfiguration>]
   public sealed class HttpEndpointFeatureDriver : ApplicationIdentityStaticFeatureDriver, IDevRunningStatusContributor
   ...
       var m = GetModel<HttpEndpointConfiguration>( ApplicationIdentityService );
   ```

   **A client feature** names its model on its feature type, and its driver specializes `ClientFeatureDriver<T, TModel>`:
   its `Create( monitor, client, configuration, model )` receives it (the "DesktopWindow" of CK.AppHost.Desktop).
5. **A test that reads a model** uses `ConfigurationModelReader.TryRead<T>( section, path, collector, out var model )`
   with `new UserMessageCollector( new CurrentCultureInfo( new TranslationService(), NormalizedCultureInfo.CodeDefault ) )`
   (CK.Globalization). A test without engine that registers a modeled option (`services.AddPartyComponentOptions`) or a
   modeled component (`services.AddPartyComponentDirectory`) gives it its model as the engine does.

**Behavior:**
- **Key by key:** each key is taken from the nearest level that has it: the option's section in the party, then in each
  `"Defaults"` above it (a nested model key by key, a dictionary entry by entry, a list as a whole). An aspect has only
  its own section. A list of values also accepts a comma separated string (`"ActiveCultures": "fr, en"`).
- **Every error is reported, then the creation fails:** an unknown key at any level (the names of the owner's options are
  not unknown keys of an aspect), a missing required key, a value that can't be converted (the invariant culture:
  `"00:05:00"` for a `TimeSpan`), then `Validate`. A modeled option that fails fails the setup of the party, a modeled
  aspect fails the build.
- **The description of a model** is read by reflection at build time (the App builder, the engines, the configurator); at
  run time, the options engine generates it with the creation of the model (no reflection: an `[UnsafeAccessor]` calls
  its constructor and its `init` setters, so its initializers are its defaults). A type that is not a model (not a
  `[ConfigurationModel]`, an unsupported property type, no parameterless constructor, not public for an option) fails the
  engine run or the App build with the reason.
- **A configuration key with a value and keys** (`"HttpEndpoint": "true"` and `"HttpEndpoint:Urls"` from two sources) is a
  section: its keys are read.
- The "Passkey" option of the AuthCenter: its configuration (`"RpId"`, `"RpName"`, `"Timeout"`) doesn't change; the
  scheme is available when an `"RpId"` is configured (and an `IPasskeyStore` registered); an `"RpName"` without
  `"RpId"`, and an unknown key of its section, are now errors.
- The models of the packages (their keys don't change, except Oidc's): "HttpEndpoint" (`HttpEndpointConfiguration`:
  `Urls`, `Kestrel`, `DefaultEndpoint`), its options "InProcess" and "ForwardedHeaders", "DesktopWindow"
  (`DesktopWindowConfiguration`), "AuthCenter" (`AuthCenterConfiguration`: the lifetimes, `CriticalLifetimes`; read key
  by key with the `"Defaults"` of the party), its options "Oidc" (`AuthCenterOidcOptionConfiguration`: **its schemes are
  in `"Schemes"`**, each one an `OidcSchemeConfiguration`; the secret path is `"Party:Schemes:*:ClientSecret"`),
  "PasswordLost" and "UserInvitation". Their errors are the reader's (`Unknown key '<path>'`) and the models' `Validate`;
  `HttpEndpointFeatureDriver` no longer takes the `PartyComponentOptionDirectory`, nor `LocalAuthCenterFeatureDriver`
  the `IServiceProvider`.

**Check:** the engine runs (or the App builds), the owner starts (or the App builds) with its configuration, and
the configurator shows the JSON Schema of the model in the detail of the type.

## The final checklist

1. Every builder has a `ckonf.json` (its `"App"` or `"Apps"` with the keys of their party directly in them, the client
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
10. No `AddTenantDomainAsync`, `AddPartiesAsync` nor `AddedDynamicParties` is left: the tenant domains are configured (U10).
11. No `"EmailSender"`, `"MailSenderRemote"` nor `"Smtp"` key, no relay `"MailSender": true` (it is `"MailRelay"`), no reference to CK.AppIdentity.MailSender.* or CK.IO.MailSender: the senders are named in `"MailSender"` (U11).
12. No `FluidAspectConfiguration` is left (U12).
13. No `PFeature` is left: `[IsModel]`, `[IsModelDefiner]`, `[RemoveModel]` (U13).
14. The database is `"Aspects": { "SqlServer" }` in `ckonf.json`: no Setupable/SqlSetup aspect in a builder (U14).
15. Globalization is declared in the `"Aspects"` of an App of `ckonf.json`, no `CK.Globalization.Engine` type in a `CKSetup.xml` (U15).
16. A repository with a central `ckonf.json` lists every builder in its `"Builders"`, and the builders' `ckonf.json` start with the "Generated from" line (U16).
17. A fleet's party name uses `{Name}` or `{Index}`, its hosts are started with `--Instance`; an Opened fleet
    (`"MaxInstanceCount"`) trusts its dynamic instances by approval only; an Opened tenant template has a `"DomainName"`
    ending with `/{Name}` and the `"PartyName"` `"${Name}"`; a custom `ICoreKeyStore` implements `DeleteAllKeys`; no test
    starts the same party twice at once; a repository whose tests use `CreateRunningAspNetServerAsync`
    (CK.Testing.Engine.HttpEndpoint) ignores `/Tests/*/TestStore/` (U17).
18. No Opened remote template has an `"AutoTrustKey"` other than `"Never"`; a contributor that reads a section of the hosts at
    start declares it (`DeclareInitSection`), an aspect that projects a value gives its origin (U18).
19. A value that the application changes while it runs is a run property (`[ConfigurationSection]`, `[ConfigurationLevel( Run )]`);
    its App references CK.Cris.DynamicConfiguration (and CK.AppIdentity.DynamicConfiguration), its builder the engine (U19).
20. No secret value is left in a configuration file: a handle `{ "$SECRET": "<Name>" }`, its value in `#Dev/Secrets.json`
    in development, `Secrets:<Name>` elsewhere (U20).
21. No family feature driver, `IAuthCenterSchemeConfiguration` nor `"AuthCenterOidc"`/`"AuthCenterPasskey"` key is left:
    the scheme families are options of the AuthCenter (U21).
22. The payload of every "Oidc" login provider implements `IOidcLoginPayload` (U22).
23. Each deployment (a `Deployment/#X` folder) writes its values per deployment, and its `ckonf.merged.json` is committed (U23).
24. The App projects of a central configuration are named after the identity of their party (U24).
25. A solution with deployments has its `Deployment/GenerateAssets.cs`; the builders that can't be deployed are left out (U25).
26. A "Passkey" section has
    no `"RpName"` without `"RpId"` nor any unknown key (U29).
