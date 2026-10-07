# How work flows

This page describes how an M1 issue becomes merged code. The work is done by a team of agents
in an OpenRig rig (`@openrig/cli`) that runs in WSL; the maintainer approves
briefs and merges pull requests. OpenRig is the tooling for as long as Aiakos cannot run its own
team reliably.

Three ideas carry the flow:

- **Done is decided before the run.** A story's acceptance tests exist first. When they pass, the
  story is done. Anything else that someone notices becomes a new item.
- **Work is small.** A brief is split into stories that a Sonnet-level implementer can finish.
  A stronger model is the exception, for a story that cannot be split further.
- **Judgment is checked by another vendor.** Codex writes and implements; Claude checks the
  analysis and reads the diff. The gate is a script, so the seat that runs it (`gate`) decides
  nothing.

The earlier slice review loop, in which a reviewer looked for problems after each run and wrote
follow-up briefs, had no fixed bar and did not end (slice 14-2: three runs and three reviews
without a pass). The rules below exist to prevent that, whatever tool runs the team.

## The units

| Unit | What it is | Where it lives |
|---|---|---|
| Issue | One M1 feature (`#14`) with its spec | GitHub |
| Slice | One brief: a closed description of a part of the issue | `docs/briefs/<slice>/`, for example `14-3` |
| Story | A part of a slice that one implementer run can finish | `stories.md` of the slice; ID `<slice>-<n>`, for example `14-3-2`; a GitHub sub-issue once it is ready |
| Item | One rule, change, expected output or test of the brief, with an ID | `items.tsv` of the slice |

OpenRig has its own "mission" and "slice" folders. They are not used here; `docs/briefs/` is the
source.

## The rig

The rig is defined in [`rigs/aiakos-delivery/`](../rigs/aiakos-delivery/): `rig.yaml`, one folder
per role under `agents/`, `CULTURE.md` and `SETUP.md`. It is started from the WSL checkout:

```bash
rig up rigs/aiakos-delivery/rig.yaml     # first start, and after rig.yaml changed (new seats)
rig up aiakos-delivery --existing        # every other start: the same rig, new conversations
```

The seats are in two pods: `desk`, which the maintainer keeps open, and `team`.

| Seat | Runtime | Role |
|---|---|---|
| `desk-lead` | Claude Code (Sonnet) | The maintainer's console: shows the board and what waits for the maintainer, passes on what the maintainer asks for. No seat writes to it |
| `desk-router` | Claude Code (Haiku) | Receives every report and makes the next move: runs `tools/story.sh`, creates sub-issues, opens pull requests |
| `team-architect` | Claude Code (Opus) | Checks the brief and the split; attacks them before they are approved. Judges an odd baseline and tags the failures of a second failed gate |
| `team-reviewer` | Claude Code (Opus) | Reads the diff once |
| `team-gate` | Claude Code (Haiku) | Runs the baseline and the gate. Both are scripts |
| `team-low1`, `team-low2` | Codex | Pool `low`: write a story's acceptance tests; implement one story each, in its own worktree. Two stories run at the same time only when neither depends on the other |
| `team-high1`, `team-high2` | Codex, stronger model | Pool `high`: write the brief, the item list and the split; implement a story that was escalated, and every chore and bug (see "Chores and bugs") |

Which model sits behind a seat is one `model:` line in `rig.yaml`. A seat of a pool has no
role of its own: every item names a role file in
[`rigs/aiakos-delivery/roles/`](../rigs/aiakos-delivery/roles/) (`author`, `tests`, `impl`).
Below, "`author`", "`tests`" and "`impl`" mean a pool seat working in that role.

Seats pass work to each other with `tools/story.sh hand`, which puts an item in the OpenRig
queue; every item names the story and its sub-issue. To watch the team, open the rig TUI
(`rig tui`) and run `terminal rig:aiakos-delivery`, which opens the seats as tiles in herdr.

**A seat keeps no conversation between items.** A long conversation is sent again with every
request, so a seat that carried its finished work along spent most of its allowance on it.
`hand` picks an idle seat of a pool and gives the destination an empty conversation before it
delivers the item (`/new` for a Codex seat, a fresh launch for the architect and the reviewer);
a busy seat is never touched. A start of the rig does the same for every seat. What a seat needs
later is therefore in the queue item, the repository or `artifacts/`, never only in what it
remembers; a slice lives in its analysis worktree and a story in its story worktree, so any seat
of the right pool continues it.

Credentials and the OpenRig state (`~/.openrig`) stay on the machine.
[`SETUP.md`](../rigs/aiakos-delivery/SETUP.md) lists what a new machine needs.

## The flow

| Step | Who | What happens | Result |
|---|---|---|---|
| 1. Brief | `author` | `tools/story.sh analysis <slice>` for a worktree, then writes `brief.md` and `items.tsv` from [`briefs/TEMPLATE.md`](briefs/TEMPLATE.md) | A brief on a branch |
| 2. Split | `author` | `tools/story.sh split <slice>`: sorts the items into stories, IDs only | `stories.md` |
| 3. Check | Script | `tools/story.sh check <slice>`: traceability and size | Pass or a list of errors |
| 4. Story review | `architect` | Reads the brief and the split once; looks for ties the script cannot see and for a story that is too large | `findings.md` |
| 5. Approval | `router`, then the maintainer | Findings are resolved by commits. `router` runs `tools/story.sh analysis-pr <slice>`: it checks the split and the review, sets the status and the index row, and opens one pull request, marked "routine" or "read this one". The maintainer merges it | The analysis is on `main`, `approved` |
| 6. Acceptance | `tests`, then `gate` | `tests` writes the story's acceptance tests and `gate.sh`; `gate` runs `tools/story.sh baseline <slice> <n>`; when the result is not a plain failure for missing behaviour, `architect` reads why | `artifacts/trials/<story>/` (local), with `main-before.txt` |
| 7. Ready | `router` | `tools/story.sh ready <slice> <n>`: checks the definition of ready and creates the sub-issue | A GitHub issue labelled `ready` |
| 8. Run | `impl`, in pool `low` (or `high` when escalated) | `tools/story.sh start <issue>`, then implements in the worktree | One commit on a local branch |
| 9. Gate | `gate` | `tools/story.sh done <issue>`: paths, build, acceptance tests | `needs-review`, one retry, `partial` or `blocked` |
| 10. Diff read | `reviewer` | Reads the diff once | Pass, or one of the four blocking kinds |
| 11. Pull request | `router`, then the maintainer | `tools/story.sh pr <issue>`; the maintainer merges; `cleanup` | Done |

The maintainer acts at steps 5 and 11. `main` is protected, so no seat can merge.

Steps 1 to 5 happen in files on a branch. The back and forth between the analysis and its review
is the commit history of that branch, and `findings.md` is where the architect writes.

## Definitions

### 1. Closed brief

A brief may be split when:

- every rule, change, expected output and test has an ID and is in `items.tsv`;
- every rule has at least one expected output or test with exact text;
- it says what to do where it is silent, and which earlier results change;
- the maintainer has approved it (`status: approved`).

The script checks the first two. Nothing downstream may add a rule.

### 2. Story size

- At most 8 rules and 8 expected outputs.
- One file or one concern.
- Every story owns at least one rule or change.

The caps are the same for every story and are set for a Sonnet-level implementer. They are a
first guess: record how many runs each story needs and adjust them. A brief may set its own caps
in its header (`max_rules`, `max_outputs`). That is meant for a brief whose expected outputs are
one-line test cases and not fixtures: slice 10-2 has 13 cases for one rule. A slice that fits the
caps as a whole is one story.

### 3. Escalation

A story goes to the stronger pool (`high`) only when it cannot be split further. The architect writes the
reason in the story's block in `stories.md` (`route: impl/senior` and `escalation: <reason>`);
a story with that route and no reason fails the check. Most stories never need it.

### 4. Ready

A story gets its GitHub issue and the label `ready` when:

1. the brief is approved;
2. the story lists its items and is under the size cap;
3. its acceptance tests exist and fail on `main` for the right reason (`baseline` records the
   failure; `architect` reads the reason when it is not plain);
4. every story it depends on is done;
5. the review findings are resolved or filed as their own items;
6. its route is set (`impl`, or `impl/senior` with a reason).

### 5. Done, for a story

- Its acceptance tests pass and all earlier tests stay green.
- The build has 0 warnings and only the brief's paths are touched.
- The gate output and the reviewer's verdict name the commit they judged.
- The diff has been read once, by the `reviewer` seat or by the maintainer.
- The pull request is merged and the sub-issue is closed.

### 6. Partial

A story is partial when the brief depends on something that does not exist, so that some
acceptance tests cannot pass without building it. The implementer does not build the missing
part inside the story. The story then:

- merges what works, with the passing acceptance tests;
- lists the tests that do not pass and the missing capability, with the file and line that
  shows it is missing;
- gets a new item for the missing capability.

`tools/story.sh pr <issue> --partial` opens the pull request from
`artifacts/trials/<story>/partial.md`. Partial is decided by the maintainer at that pull
request. It is not a way to pass a story whose code is wrong.

### 7. What blocks, and the stop rule

Only four things block a story:

- a failed acceptance test;
- an exception on any input;
- a leaked secret value;
- an earlier test that turned red.

A reviewer may claim one of these only for something it ran. Everything else becomes a new
backlog item and does not hold the story.

Every finding carries one tag that says why it happened:

- `context-gap`: the brief or the story lacked what was needed. The fix belongs in the brief.
- `judgment-gap`: the brief had it and the implementer got it wrong.

**What counts as an attempt:** a gate run that reached the build and failed, or a review that
blocked. A run that stops at a process check before the build (uncommitted changes, a file
outside the brief's paths) is not an attempt; the implementer fixes it and runs the gate again.
A build that aborts with
`Fatal error`, `Internal CLR error` or `Unhandled exception`, or exits non-zero without
compiler diagnostics (error codes or warning lines), is an infrastructure failure, not an
attempt. The gate preserves the raw cause, ends with `GATE: infrastructure failure` and leaves
the issue label unchanged. Such logs do not count in the retry history; rerun the gate. When
the output has a compiler or analyzer error code, it is an ordinary failure even if one of those
texts also appears. After three infrastructure failures in a row the gate sets `blocked` and the
story goes to `router`: the same crash every time is a broken machine or a change that crashes the
compiler, and rerunning does not fix either.
Compiler errors, warnings treated as errors and acceptance test failures still count.

**A failure counts only against what it was judged by.** Every gate run records a version: a
hash of the story text (from the brief on `main`) and of the acceptance tests (`gate.sh` and the
test sources in `artifacts/trials/<story>/`). When the brief is amended or an acceptance test is
fixed, the version changes and earlier failures stop counting, because they may have been the
brief's or the tests' and not the implementer's. A blocking review carries the version of the
gate run it followed. Nobody has to authorise "one more run" for this; the gate says how many
earlier failures it left out.

**Waiving a run.** For a failure that was not the implementer's and that the version does not
catch, the maintainer has `desk-lead` run `tools/story.sh waive <issue> <run> <kind> "<evidence>"`. There are two kinds:
`infrastructure` (the machine, or a flaky test the change did not touch) and `test-defect` (the
acceptance test was wrong). A failure of the implementation cannot be waived. The waiver is a
file next to the gate log and a comment on the issue, and `status` lists it. A story can have
two; a third is the maintainer's decision.

**Sending a story back:** `tools/story.sh start <issue> --retry` is the only way. It keeps the
worktree and branch, merges `main` in, writes the story text again from the brief on `main`
(and updates the issue body), and sets `in-progress`. When `main` does not merge cleanly, merge
it by hand in the worktree and run the command again; merges of `main` made after the last gate
run are accepted, other commits are not.

The story text leaves out a table, or a whole section, of the brief that has no rows for the
story, and says so in a line at that place.

**Stop rule:** one retry at most. After the second failed attempt, `architect` tags every
failure and the tag decides:
a `context-gap` sends the story back to the analysis, where the brief is fixed or the story is
split; a `judgment-gap` escalates it to pool `high`. There are no follow-up briefs.

### 8. Done, for the issue

- All its stories are done or partial.
- The spec amendment and the docs are merged (see `CLAUDE.md`, "Definition of done").
- The pull request states which risks from [`risks.md`](risks.md) it checks or closes.
- Backlog items raised along the way are done or have their own issue.

## Chores and bugs

Some work is not a story: a flaky test, a fix to `tools/story.sh`, a small bug. It has an issue
but no brief and no acceptance tests, so it takes a shorter path, always in pool `high`.

An issue may take this path when all of these hold:

- it is labelled `type/chore` or `type/bug`, and its title is not a story title;
- it changes no rule of a spec or of a brief (that is a story, or a brief amendment);
- the maintainer labelled it `ready`. Nobody else does: with no acceptance tests written before
  the run, that label is the decision that the issue text is enough to work from.

| Step | Who | What happens |
|---|---|---|
| Start | `router` | `tools/story.sh start <issue>`: a worktree and a branch (`chore/…` or `fix/…`), the issue text copied in. Hands it to pool `high` |
| Run | `impl`, in pool `high` | Does what the issue asks and nothing more. A bug fix adds a test that fails without the fix. One commit |
| Gate | `gate` | `tools/story.sh done <issue>`: the same process checks, the build with zero warnings, then every existing test (`dotnet test`) in place of acceptance tests |
| Diff read | `reviewer` | As for a story, with the issue text as the story text. A bug fix without a test for it is a backlog item to raise, named in the verdict |
| Pull request | `router`, then the maintainer | `tools/story.sh pr <issue>`; the maintainer merges; `cleanup` |

The four blocking kinds, the one retry and `start <issue> --retry` apply as for a story.
`tools/story.sh next` lists ready chores and bugs under the ready stories.

## Files of a slice

```text
docs/briefs/14-3/
  brief.md       the closed brief; front matter: id, title, issue, status, route, paths, caps
  items.tsv      id, type (rule|change|output|test), scope (once|first|all), needs
  stories.md     the split: one block per story, IDs only
  findings.md    the story review; open findings are "- [ ]" lines
```

In the brief, an item is marked by its ID at the start of a line (`R7. …`, `- C1. …`) or in the
first column of a table (`` | `AIK3003-git` | … ``). Lines that continue an item are indented.
The story an implementer gets is the brief with the items of the other stories removed
(`tools/story.sh show <slice> <n>`), so a brief is written once and never copied by hand. The
rules of the stories it depends on stay in, marked as context, so that a reference to an earlier
rule can be read.

Acceptance tests are not in the repository. They stay in `artifacts/trials/<story>/` of the main
checkout. `gate.sh` there copies them into the worktree, runs them and removes them again. The
implementer works in its own worktree and is told not to read that folder; all seats share one
file system, so this is a convention and not a barrier.

Because they are not in the repository, acceptance tests guard a story only until it is merged.
Whatever must stay true afterwards needs a test that the implementer commits: a `T` item in the
brief. A brief for a bug fix always has one, for the bug itself. When a merged story turns out to
be guarded only by its acceptance tests, the reviewer names that as a backlog item and the
author ports the tests into the repository in a follow-up.

Each story has its own worktree, and so has the analysis of each slice
(`tools/story.sh analysis <slice>`). `baseline` uses a worktree that it removes again. No seat
builds or commits in the main checkout, and no seat creates a worktree by hand.

## Who may do what on GitHub

| Action | Who |
|---|---|
| Create a sub-issue, set labels | `router` |
| Push a story branch | `router`, through `tools/story.sh pr` |
| Open a pull request | `router` |
| Merge | Maintainer only |

## Commands

```bash
bash tools/story.sh check <slice>
bash tools/story.sh split <slice>
bash tools/story.sh split-done <slice>
bash tools/story.sh show <slice> <n>
bash tools/story.sh analysis <slice> [--remove]
bash tools/story.sh analysis-pr <slice> [--look "<reason>"]
bash tools/story.sh status
bash tools/story.sh baseline <slice> <n>
bash tools/story.sh ready <slice> <n> [--dry-run]
bash tools/story.sh next [<route>]
bash tools/story.sh start <issue> [--retry]
bash tools/story.sh done <issue>
bash tools/story.sh waive <issue> <run> infrastructure|test-defect "<evidence>"
bash tools/story.sh pr <issue> [--maintainer-reviewed]
bash tools/story.sh cleanup <issue>
bash tools/story.sh hand <low|high|architect|reviewer|gate|router|maintainer> [--role <role>] \
     [--item <qitem>] --summary "<one line>" (--body "<text>" | --body-file <path>)
```

`AIAKOS_NO_WRITE=1` makes a command print its label changes instead of applying them.
The script is the one place where the definitions above are enforced; the seats call it and do
not replace it. It can still be run by hand.

Useful OpenRig commands:

```bash
rig ps --nodes --rig aiakos-delivery   # seats and what they are doing
rig parked                             # seats that stopped while they owe work
rig queue list                         # work items and their owners
tail -n 20 artifacts/hand.log          # deliveries, and whether the seat got a clean conversation
rig tui                                # the board; "terminal rig:aiakos-delivery" opens herdr
```
