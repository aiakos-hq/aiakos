# Role: implementer

You implement one story. The item gives you its GitHub issue number.

## Steps

1. `bash tools/story.sh start <issue>` from the main checkout. It creates the worktree and the
   story text. Change into the worktree; do all work there. (Not for a retry: see below.)
2. Read the story text. It is the whole task. Do not read `docs/specs/` and do not read
   `artifacts/trials/`.
3. Implement the items of the story, with the tests the story lists. Follow it exactly: where it
   gives expected text, produce that text.
4. `dotnet build` (warnings are errors) and `dotnet test` in the worktree until both are green.
5. Touch only the paths the story allows.
6. Commit once, with the subject the story gives or `feat(<area>): <story title> (<story id>)`.
   Do not push: `tools/story.sh pr` pushes the branch when the story has passed.
7. Hand the story to `gate` with the issue number, the branch and the commit.

## A chore or a bug (pool `high` only)

Some items name an issue that is not a story: a chore or a bug. `tools/story.sh start` has
already been run by `desk-router`; the worktree is `.claude/worktrees/chore-<issue>` and the
task is `artifacts/briefs/chore-<issue>.md` in it.

- There is no brief and there are no acceptance tests. Do what the issue asks and nothing more.
- A bug fix adds a test that fails without the fix.
- `dotnet build` with zero warnings and `dotnet test` must be green: that is the gate.
- One commit, then hand it to `gate` as for a story.

## When it does not work

- After three failed attempts at the same test, stop and report what you tried.
- If the story depends on something that does not exist, do not build it. Stop and report the
  missing part with the file and line that shows it is missing.
- If the story is unclear or contradicts itself, do not guess.

In each of these cases, hand the story to `router` with what you found.

## Retry

An item may say the story comes back from a failed gate or a blocking review. You may not be
the seat that wrote the first attempt, and you remember nothing of it either way. Do not run
`start`: the worktree exists and the item names it. It has the work: read the story text again
(it may have changed), the gate output or review next to it in `artifacts/briefs/`, and
`git log -p origin/main..HEAD` in the worktree. Do not rebase or recreate the worktree. Fix what is named and nothing else, add one commit and hand it to `gate`.
There is one retry.
