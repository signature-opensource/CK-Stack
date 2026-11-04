# Design principles

Rules for designing the packages and the applications of this stack. They apply to people and to coding agents alike.
Each one comes from a case of the stack: the case is given, so that the rule can be weighed rather than recited.

## Package First

**What an application does is in its packages, never in its host code.** A package brings a feature with everything it
needs: its services, its middlewares, its configuration, its contribution to what other packages read. The application
references it; it doesn't wire it.

- **A host is one line.** `Program.cs` is `await CK.Core.CKomposableAppHost.RunAsync( args );` (CK.AppHost): the
  bootstrap is the same for every party, so it is a package, not a copy. Copies of a host bootstrap drift: the Zorro demo's
  was an old copy that nobody had updated when it was replaced.
- **A package contributes through an extension point**, never through code the application must call:
  `WebApplicationContributions` (the services and middlewares of the web applications of a host, CK.AspNet), the
  `IPartyDescriptionContributor` (what a party states about itself, CK.AppIdentity), the `IDevRunningStatusContributor`.
- **One package per feature or scheme; a generic package holds generic types only.** A login scheme is its package
  (`CK.Cris.Authentication.Basic`, `.Passkey`...): an application gets a scheme by referencing it.

## Derive, don't store

**A value that can be computed from another one is computed, never stored or configured a second time.** Two copies of
the same fact drift, and the drift is silent.

The case: the public URL of a party was configured three times on its AuthCenter (`Local:FrontUrl` for the mails, the OIDC
`CallbackUrls`, the passkey `Origins`), plus once on the party itself (its endpoint). In the sample, the three copies
already disagreed: the mail links pointed to the Admin, the passkey origins to the Front only. Now a party states its URL
(its endpoint's, or `Local:PublicUrl` behind a proxy) and announces it; the AuthCenter derives everything from it.

- **No redundant property**, stored or serialized: a client application is the default one when its request path is
  `/`, there is no `Kind` to keep in sync.
- **The URL of a client application is derived** (`{PublicUrl}{RequestPath}`), never stored.

## A party states facts about itself

**What a party is (its public URL, its client applications, the commands it accepts) is stated by the party, and
announced to the others.** No party configures a copy of another party's facts. The announcement travels over the
authenticated channel: it is as trustworthy as the party itself (which can already do anything its role allows).

## Commands across parties

**A command is a command: where it runs is a deployment detail.** A command has at most one local handler, and when it
exists, it runs the command. Otherwise the command is dispatched to **the** remote that accepts it. The code that sends a
command never encodes where it runs.

The case: the commands of the AuthCenter had their own HTTP route and relay commands that only encoded a location, so
moving the AuthCenter to another party meant changing the code that sent them. The route and the relays are gone: a
client application posts every command to `/.cris`.

- **The routes are resolved when the channels connect, not per call.** Asking the remotes at call time depends on who is
  connected and leaves ambiguities. Two remotes that accept the same command are a configuration error; none is a
  clear error ("no local handler and no remote accepts it"), never a silent fallback.

**The receiver decides what it accepts, from its code.** A party never executes whatever a remote sends. What a remote may
send derives from the topology (inside the System, the commands the receiver handles except the `[Private]` ones; from
outside, the public ones only); for whom it may act is the receiver's policy (`ICallerPartyService`). "Forward whatever
has no local handler" is rejected: a missing package would silently become a remote execution.

**The exposure of a command is declared on its contract.** Who may send a command is part of its type: `[Internal]` (inside
the System, never from HTTP) and `[Private]` (inside the process, never from HTTP nor from another party), in CK.Cris.
In a Poco family, the most restrictive declaration wins: an extension can restrict a command, never open it. The
protection follows the type: no list to keep in sync.

The case: a generic package without a rights model (CK.DB.UserInvitation) exposed its management commands to any
signed-in user, who could create an invitation with any groups (the platform administrators included). Its management
commands are `[Internal]`: the application checks the rights, then calls them.

**The identity is never carried by impersonation: the caller party is a third role.** Relaying "Alice impersonating Bob"
through the party Paul by impersonation gives "Paul impersonating Bob" and loses Alice: the two slots of the
impersonation belong to the users. The party that sends a command is carried apart: `AuthenticationInfo.CallerPartyId`.

**Trust between parties is bounded, audited and revocable, not absolute.** Pretending that everything can be secured
against an evil party inside the System is a lie: a remote that vouches for its users can act as them at the Normal
level, and that is the price of the vouching that makes distribution and deferral work. What the System promises is
narrower and real:

1. **High-value actions need the human's fresh proof (Critical).** A caller party alone never reaches Critical: the
   password, the passkeys, the administration. A Critical command crosses a party boundary only with the user's live
   proof: it can't be deferred to another party later.
2. **Bounded damage.** A remote is accepted only the commands its role needs, and the receiver caps the level and can
   refuse privileged actors.
3. **Audit.** Every action carries its `CallerPartyId`: who vouched for what is known.
4. **Revocation.** Disabling the PartyActor of a remote cuts it off.

## Fail closed, with an explicit opt-in

**When a value is missing, the operation fails visibly; it never falls back to an implicit default.** An implicit
default can work "by chance" while leaking information or misrouting a user, and it hides the missing value.

The case: a mail link without the URL of the party through which the user acts. Falling back to the local party's URL
could mail an internal address to an external user, or send a user to another application with another policy, and it
would hide data created without a party. So: no URL, no mail, an error in the log. When a default is really needed,
it is an explicit setting that names it (an administrator decides it), never "the local one".

- An invalid configuration is a start-up error, not a warning that keeps a default.
- A request asked of the code that can't be honored is an error, never silently ignored (`EMail.PrimaryMustBeValidated`
  rather than "added as non-primary").

## Security rules

Each rule comes from a defect found in the stack.

- **Never build a link from the request.** A link built from the `Host` header of a request is the password reset
  poisoning attack (an attacker's host in the victim's mail). Links are built from what the party states about itself.
- **Never accept from a client a value that must equal a stored one.** The registration of an invited user took the
  address from its command and registered it as validated: whoever held an invitation link registered any address. The
  command no longer carries it: the server uses the invitation's.
- **A token is single use, and bound to what issued it.** A password recovery token stayed valid until it expired, after
  a successful recovery too. It now carries a fingerprint of the current password hash (it dies when the password
  changes: single use without storage) and the party through which it was issued (redeemed only through it).
- **An invariant is enforced where the data is.** "The primary e-mail is validated as soon as a validated e-mail exists"
  was kept by the callers only: the database package now enforces it, so that a query relying on it is right.
- **An anonymous endpoint answers the same whatever happens.** The lost password command answers the same message for
  a known address, an unknown one, or a mail that could not be sent: it can't be used to probe the accounts.

## Naming

- **An asymmetric feature is `XXX` in the `Local` of a party, `XXXRemote` on a remote** (`AuthCenter` and
  `AuthCenterRemote`): the `Local` gives the role (it is the XXX, or it uses the remote that allows `XXXRemote`).
  Its packages are CK.AppIdentity.XXX.Local, CK.AppIdentity.XXX.Remote, CK.IO.XXX for its commands and an optional
  CK.AppIdentity.XXX: the feature name appears in all of them (see the asymmetric features of CK-AppIdentity).
- **A party is named after what it is** (Backend, Front, Admin), never after a feature it implements (AuthCenter).
- **Vocabulary:** a "client application" is the browser application (never "front", which is a party name); a party
  talking to an AuthCenter is "a remote" (never "member").
- **A part is created for a semantics, never to share properties of the same name.**
