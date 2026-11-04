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
