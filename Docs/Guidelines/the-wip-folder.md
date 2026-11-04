# The .wip folder

Where the working notes of a stack live: the plans, the design discussions, the investigation logs, the notes that a
coding agent keeps across its sessions. They don't belong in the repositories. A repository's documentation describes
the current state, and its history is in the commit messages (see [the documentation guidelines](documentation.md)).
These guidelines apply to people and to coding agents alike.

## Where it is

The `.wip` folder is at the root of the stack, next to the Stack repository:

```
<stack root>/               C:\Dev2\CK for instance
  .PublicStack/             the Stack repository
  .wip/                     ignored by CKli
    Shared/                 the shared notes: a clone of CK-Stack.wip
    User/                   your own notes
  Core/, Web/, ...          the repositories of the stack
```

**CKli ignores `.wip` entirely** (CKli 0.47.5 or later). It is not a repository of the stack, `ckli issue --fix` never
touches it, and it can hold any number of git repositories, with or without a remote.

## Shared/: the notes of the team

`Shared/` is a clone of https://github.com/signature-opensource/CK-Stack.wip. This repository is private: clone it
if you have access to it.

```
git clone https://github.com/signature-opensource/CK-Stack.wip .wip/Shared
```

- **It holds what the team works on together:** the plans and design discussions of a work that spans several
  repositories, the decisions taken along the way, what is left to do.
- **Everybody who has access reads it.** Write for the team, and pull before you write.
- **Without access, skip it:** nothing in the stack depends on it.

## User/: your own notes

`User/` is yours only: your drafts, your logs, the notes of your coding agents. It is a plain folder, or the clone of
a personal git repository (a private one) when you want a history or a backup.

## Rules

- **Never point into `.wip`:** a code comment or a document of a repository never links to it (see
  [the documentation guidelines](documentation.md)). Its notes move and get archived, and the others don't have your
  `User/`.
- **Nothing builds on it:** no build, test or tool of a repository reads `.wip`.
- **A decision that matters leaves the notes:** it goes in the code, a README or the Stack's documents (a guideline,
  a migration guide). The notes are working material, not the reference.
- **No secret in it:** no password, no key, no connection string with credentials. `Shared/` is read by others, and
  `User/` may be pushed to a remote.
- **A coding agent writes its notes in `User/`.** It writes in `Shared/` only when it is asked to: what it writes
  there is read by the team.
