# Migration guides

Rules for the migration guides of this stack (`Docs/Migrations/`): what a repository (or a World) that uses the stack must
change to move from a release to the next one. They apply to people and to coding agents alike.

## A guide is self-sufficient

**A migration is done with the guide alone, without reading the code of the stack.** A step that needs the stack's code
to be done is a gap of the guide.

**Test it:** migrate a downstream repository with an agent (or a person) that may read only the guide and the repository
to migrate (not the stack's code, not an already migrated repository), and that returns a gap log: for each gap, the
item, what was missing, ambiguous or wrong, what was assumed, and the text the guide should have. Check each reported
claim against the code before folding it into the guide (an agent may attribute a behavior to the wrong package).

The case: three samples migrated this way found about 35 gaps in four items of a guide that "looked complete".

## Its structure

- **A contents table** with the identifier, the area and a "Breaking?" column (yes, behavior, configuration, no).
- **One item per change**, with an identifier (M1, M2...), in the order to apply them:
  - **Detect:** what to search for (a reference, a call, a configuration key, a file): it is meant to be scripted.
  - **Do:** the change, with the real code or configuration (copied from the stack, never reconstructed).
  - **Behavior:** what changes without any code change (a default, a check, an error).
  - **Check:** how to know that the item is done.
- **The final checklist**: one line per item.

## Its content

- **It describes what a consumer sees, from the released version.** What was born and changed on the branch, and never
  released, is not mentioned: there is nothing to migrate from it.
- **A new package reference says where the package comes from** (its repository, its version line): the versions of the
  stack's packages are not all aligned.
- **The guide is updated in the same change as the code** that a consumer sees: a guide that lags is worse than none.
- **A guide item names the reference repository** migrated that way, when there is one: a reader can compare.
