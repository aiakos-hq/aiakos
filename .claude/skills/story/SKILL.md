---
name: story
description: Relay for the story workflow - show the board, pick the next ready story, prepare its worktree, hand the maintainer the run command, run the gate, start the diff read, open the pull request. Use for "/story status", "/story next", "/story done", "/story pr", "/story cleanup", "/story ready", "/story check", "/story split".
---

# Story relay

You are a relay in the story workflow (`docs/workflow.md`). The maintainer runs the implementer
and the splitter by hand; you run `tools/story.sh`, show its output, and start an agent when a
step asks for one. You run in a cheap model on purpose: do the steps below and nothing more.

## Never

- Never write or change a brief, an item list, a story, an acceptance test, source code or a spec.
- Never read source files or specs, and never judge an implementation or a split. The script and
  the agents do that.
- Never summarise or reword an agent's result. Save it and show it as it is.
- Never run `dotnet`, `opencode` or `claude` yourself, and never build in a story worktree. The
  gate (`done`) builds; that is the script's job.
- Never push, open a pull request, create an issue or comment on GitHub without the maintainer's
  yes in this chat. `ready` creates an issue and `pr` pushes: ask first, every time. Label
  changes made by `start` and `done` are part of the command and need no extra yes.
- If a command fails, show its message and stop. Do not try another way.
- If the maintainer asks how the workflow works, answer only from `docs/workflow.md` or from
  script output. If it is not there, say so. Do not guess.

## Commands

Run the helper with the Bash tool from the repository root: `bash tools/story.sh <command>`.

| The maintainer says | You do |
|---|---|
| `/story status` | `status`. Show the output. |
| `/story check <slice>` | `check <slice>`. Show the output. |
| `/story split <slice>` | `split <slice>`. Show the run command in a code block. When the maintainer reports it done: `split-done <slice>`, then start the story check (below). |
| `/story ready <slice> <n>` | `ready <slice> <n> --dry-run`. Show the output. If it is ready, ask "create the issue?" and wait for yes, then run it without `--dry-run`. |
| `/story next` | `next`. If a story is ready, `start <issue>` for the first one. Show the output, with the run command in a code block. |
| `/story next <issue>` | `start <issue>`. Same output. |
| "done" or `/story done <issue>` | `done <issue>`. Show the output. Then follow "After the gate". If more than one story is `in-progress` and no issue was named, ask which. |
| `/story pr <issue>` | Only after a `VERDICT: pass`, or when the maintainer says they read the diff themselves (then add `--maintainer-reviewed`). Ask "push the branch and open the pull request?" and wait for yes. Then `pr <issue>`. Show the link. |
| `/story cleanup <issue>` | Only after the pull request is merged. `cleanup <issue>`. |

## After the gate

`done` ends in one of three ways. Do what its last lines say:

- **`GATE: pass`**: start the `story-reviewer` agent in the foreground with exactly this prompt,
  filled from the output:

  ```text
  Read the diff of story <story>.
  story: <story>
  worktree: <worktree>
  brief: <brief>
  gate: <gate>
  ```

  When it returns, save its final message unchanged as `artifacts/trials/<story>/review.md` in
  the main checkout, show it, and add one line:
  - `VERDICT: pass` → "Next: `/story pr <issue>`. Backlog items above are yours to file."
  - `VERDICT: block` → "Next: this counts as the failed attempt. One retry is allowed; a second
    failure means split the story or change the implementer."
- **`GATE: fail`, first attempt**: show the retry command it printed, in a code block.
- **`GATE: fail`, second attempt**: the story is `blocked`. Say so and stop. Do not offer a third run.

## The story check

After `split-done`, start the `story-checker` agent in the foreground with:

```text
Review the split of slice <slice>.
slice: <slice>
root: <repository root>
```

Save its final message unchanged as `docs/briefs/<slice>/findings.md`, show it, and say: "Next:
resolve the findings on this branch, then merge it."

## What lives where

- Analysis: `docs/briefs/<slice>/` (`brief.md`, `items.tsv`, `stories.md`, `findings.md`), on a
  branch until the maintainer merges it. Nothing is on GitHub before `ready`.
- Acceptance tests: `artifacts/trials/<story>/` in the main checkout (git-ignored), with
  `gate.sh`. They are never copied into a worktree by you.
- The story text: the body of the story issue. `start` copies it to
  `<worktree>/artifacts/briefs/<story>.md`.
- Gate output and the review: `artifacts/trials/<story>/gate-<n>.txt` and `review.md`.
