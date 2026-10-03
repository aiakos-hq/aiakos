# Aiakos delivery team

You are one seat of the team that builds Aiakos. The rules of the work are in `CLAUDE.md` and
`docs/workflow.md` at the repository root. Read both before your first task. This file says how
the seats work together.

## The work record

- Work arrives as an OpenRig queue item. Run `rig whoami --json`, then
  `rig queue list --owned`. Claim an item before you work on it.
- Every item names a slice (`10-2`) or a story (`10-2-1`) and, for a story, its GitHub issue.
- A slice has one author, from its brief to its last acceptance test. There are two author
  seats; work on a slice goes back to the author that owns it, never to the other one.
- Hand work on with `rig queue handoff`. State the story, the branch, the commit and what you
  ran. A chat message is not a handoff.
- When you must stop and wait, park the item with `rig queue block` and say what resumes it.
  Do not stop silently.

## Rules that do not bend

1. **Done is decided before the run.** A story is done when its acceptance tests pass. Do not
   add requirements after the fact.
2. **Only four things block a story:** a failed acceptance test, an exception on any input, a
   leaked secret value, an earlier test that turned red. You may claim one only for something
   you ran. Everything else is a backlog item and does not hold the story.
3. **One retry.** When the gate fails a second time the story stops and goes to `lead`.
4. **Tag every finding:** `context-gap` (the brief lacked it) or `judgment-gap` (the brief had
   it and the work got it wrong).
5. **Nothing is checked by the seat that wrote it.**
6. **Do not build what the brief did not ask for.** If the brief depends on something that does
   not exist, stop and report it; the story may end partial.
7. **`tools/story.sh` enforces the definitions.** Use it; do not do its steps by hand.

## Git and GitHub

- One worktree per story, created by `tools/story.sh start`. Never build, test or commit in the
  main checkout.
- Do not read `artifacts/trials/` unless your role says so. It holds the acceptance tests.
- `lead` creates issues and pull requests; `tools/story.sh pr` pushes the story branch. Nobody
  merges: the maintainer does.
- Never force-push, never rewrite history on a pushed branch, never commit a secret.

## When you are unsure

`unknown` is a valid answer. Say what you did not check. Ask `lead` for a decision that is not
yours; `lead` asks the maintainer.
