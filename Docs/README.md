# The documents of the stack

The conventions and the migration guides of the stack, versioned with its Stack repository. They apply to people and
to coding agents alike. What a package does is documented with the package (its `README.md`), not here.

A new guideline or migration guide adds its row to these tables.

## Guidelines

The rules for working in the repositories of the stack ([Guidelines/](Guidelines)).

| Guideline | Read it when |
|---|---|
| [Branching](Guidelines/branching.md) | Before the first commit in a repository: no commit in a bare branch (`stable`, `develop`), only in its `dev/` counterpart (a Stack repository commits on `main`). |
| [Documentation](Guidelines/documentation.md) | Writing or reviewing a `README.md` or another Markdown document: where it lives, what it says, how it links. |
| [Design principles](Guidelines/design-principles.md) | Designing a package, a feature or an application (a configuration, a party, a token, a link). |
| [Managing projects across repositories](Guidelines/managing-projects-across-repositories.md) | Moving a project to another folder or repository, retiring a package (its tombstone) or renaming it: `ckli project move`, `ckli project retire`. |
| [Migration guides](Guidelines/migration-guides.md) | Writing or testing a migration guide. |
| [Working with ckli](Guidelines/working-with-ckli.md) | Building, testing or changing the package versions of a repository with ckli. |
| [The .wip folder](Guidelines/the-wip-folder.md) | Keeping working notes: the shared notes of the team (`Shared/`) and your own (`User/`). |

## Migrations

What a repository (or a World) that uses the stack must change to move from a release to the next one
([Migrations/](Migrations)).

| Guide | From | To |
|---|---|---|
| [v2026.282 to explo/cris-auth](Migrations/v2026.282-to-explo-cris-auth.md) | The v2026.282.0 release | The packages of the `explo/cris-auth` branch: the authentication made of Cris commands and an Authentication Center. |
