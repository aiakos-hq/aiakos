# Aiakos delivery team

You are one seat of the team that builds Aiakos. The rules of the work are in `CLAUDE.md` and
`docs/workflow.md` at the repository root. This file says how the seats work together.

## The seats

| Seat | What it does |
|---|---|
| `desk-lead` | The maintainer's console. It gets only what needs the maintainer |
| `desk-router` | Receives every report and makes the next move |
| `team-architect` | Checks the analysis of a slice; takes escalations |
| `team-reviewer` | Reads the diff of a story once |
| `team-gate` | Runs the baseline and the acceptance gate |
| `team-low1`, `team-low2` | Pool `low`: acceptance tests and stories |
| `team-high1`, `team-high2` | Pool `high`: briefs and splits, escalated stories, chores and bugs |

A seat of a pool has no role of its own. Its item begins with a line that names a role file in
`rigs/aiakos-delivery/roles/`; read that file before anything else.

## The work record

- Work arrives as an OpenRig queue item. Run `rig whoami --json`, then
  `rig queue list --owned`. Claim an item before you work on it.
- Every item names a slice (`10-2`) or a story (`10-2-1`) and, for a story, its GitHub issue.
- Pass work on with `tools/story.sh hand`, never with `rig queue handoff` or a chat message:

  ```bash
  bash tools/story.sh hand <target> [--role <role>] --item <your item> \
    --summary "<one line>" --body-file <file>
  ```

  `<target>` is `low`, `high`, `architect`, `reviewer`, `gate`, `router` or `maintainer`. For a
  pool, add `--role` (`author`, `tests` or `impl`); the command picks the seat. Your role file
  says which target comes next. When you do not know, the target is `router`.
- The body states the slice or story, the issue, the worktree, the branch and the commit, what
  you ran and what you did not check, and the files to read. Write it to a file under
  `artifacts/` first.
- When you must stop and wait, park the item with `rig queue block` and say what resumes it.
  Do not stop silently.

## Your conversation does not last

A long conversation is sent again with every request, so the rig does not keep one. Before an
item is delivered to a pool seat, to `team-architect` or to `team-reviewer`, that seat gets a
new, empty conversation. Every start of the rig does the same for every seat.

- When a conversation starts, run `rig whoami --json` and `rig queue list --owned`, and go on
  with the item that is in progress or pending. If there is none, wait.
- An item must make sense to a seat that knows nothing else. What the next seat or your later
  self needs is in the item, the repository or `artifacts/`, never only in what you remember.
- A slice lives in its analysis worktree and a story in its story worktree, not in a seat. Any
  seat of the right pool continues it from there.
- Do not rely on "as I said before". Read the item and the files it names.

## Rules that do not bend

1. **Done is decided before the run.** A story is done when its acceptance tests pass. Do not
   add requirements after the fact.
2. **Only four things block a story:** a failed acceptance test, an exception on any input, a
   leaked secret value, an earlier test that turned red. You may claim one only for something
   you ran. Everything else is a backlog item and does not hold the story.
3. **One retry.** When the gate fails a second time the story stops and goes to
   `team-architect`, which tags the failures, and then to `desk-router`.
4. **Tag every finding:** `context-gap` (the brief lacked it) or `judgment-gap` (the brief had
   it and the work got it wrong).
5. **Nothing is checked by the seat that wrote it.** Codex seats write and implement; Claude
   seats check the analysis and read the diff; a script decides the gate.
6. **Do not build what the brief did not ask for.** If the brief depends on something that does
   not exist, stop and report it; the story may end partial.
7. **`tools/story.sh` enforces the definitions.** Use it; do not do its steps by hand.

## Git and GitHub

- One worktree per story (`tools/story.sh start`) and per slice analysis
  (`tools/story.sh analysis`). Never create a worktree by hand. Never build, test or commit in the
  main checkout.
- Do not read `artifacts/trials/` unless your role says so. It holds the acceptance tests.
- `desk-router` creates issues and pull requests; `tools/story.sh pr` pushes the story branch.
  Nobody merges: the maintainer does.
- Never force-push, never rewrite history on a pushed branch, never commit a secret.

## When you are unsure

`unknown` is a valid answer. Say what you did not check. A decision that is not yours goes to
`router`. A decision that is the maintainer's goes to `maintainer`, with `--evidence` naming
the file or pull request to look at; it is typed into `desk-lead`, where the maintainer may be
writing, so send it only when the decision is really owed. Nothing else goes to `desk-lead`.
