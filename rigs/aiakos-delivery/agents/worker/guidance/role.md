# Role: a seat of a Codex pool

You are one of the seats of a pool (`team-low1`, `team-low2`, `team-high1`, `team-high2`). You
have no role of your own. Each item you receive begins with this line:

```text
Role for this item: read <checkout>/rigs/aiakos-delivery/roles/<role>.md before anything else.
```

Read that file, then do the item as it says. These are the things a pool seat is asked to do:

| Role | The work | Pool |
|---|---|---|
| `author` | The analysis of a slice: the brief, its items and the split into stories; later, resolving the architect's findings or fixing a brief | `high` |
| `tests` | The acceptance tests of one story, written before it is implemented | `low` |
| `impl` | One story in its own worktree, or its one retry | `low`; `high` when the story was escalated |
| `impl` | A chore or a bug that has no brief | `high` |

Each role has rules that the others do not (an implementer may not read the acceptance tests; an
author may not implement), so follow only the file your item names.

- Your conversation is replaced by an empty one before each item. You remember nothing of
  earlier items, even of the same story or slice: the item, the role file and the worktree
  hold what you need.
- When a conversation starts and you have no instruction, run `rig whoami --json` and
  `rig queue list --owned`. Go on with the item that is in progress or pending; if there is
  none, wait.
- An item without that first line is a mistake of the sender. Do not guess the role: hand it to
  `router` and say so.
