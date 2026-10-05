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
  analysis and reads the diff. The gate is a script, so the seat that runs it (`qa`, Codex) decides
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
rig up aiakos-delivery --existing        # every other start: the same rig resumes
```

| Seat | Runtime | Role |
|---|---|---|
| `lead` | Claude Code | Runs `tools/story.sh`, creates sub-issues, opens pull requests, owns closure |
| `author`, `author2` | Codex | Write the brief, the item list and the acceptance tests; split the brief. Two seats, so two slices can be analysed at once; a slice stays with one author |
| `architect` | Claude Code (Opus) | Checks the brief and the split; attacks them before they are approved |
| `impl`, `impl2` | Codex | Implement one story each, in its own worktree. Two stories run at the same time only when neither depends on the other |
| `senior` | Codex, stronger model | Implements a story that was escalated |
| `qa` | Codex | Runs the gate and probes the behaviour |
| `reviewer` | Claude Code | Reads the diff once |

Which model sits behind a seat is one `model:` line in `rig.yaml`. Seats hand work to each other
through the OpenRig queue (`rig queue handoff`); every queue item names the story and its
sub-issue. To watch the team, open the rig TUI (`rig tui`) and run `terminal rig:aiakos-delivery`,
which opens every seat as a tile in herdr.

Credentials and the OpenRig state (`~/.openrig`) stay on the machine.
[`SETUP.md`](../rigs/aiakos-delivery/SETUP.md) lists what a new machine needs.

## The flow

| Step | Who | What happens | Result |
|---|---|---|---|
| 1. Brief | `author` | `tools/story.sh analysis <slice>` for a worktree, then writes `brief.md` and `items.tsv` from [`briefs/TEMPLATE.md`](briefs/TEMPLATE.md) | A brief on a branch |
| 2. Split | `author` | `tools/story.sh split <slice>`: sorts the items into stories, IDs only | `stories.md` |
| 3. Check | Script | `tools/story.sh check <slice>`: traceability and size | Pass or a list of errors |
| 4. Story review | `architect` | Reads the brief and the split once; looks for ties the script cannot see and for a story that is too large | `findings.md` |
| 5. Approval | Maintainer | Findings are resolved by commits; `lead` opens the pull request; the maintainer merges it | The analysis is on `main` |
| 6. Acceptance | `author`, then `qa` | `author` writes the story's acceptance tests and `gate.sh`; `qa` runs `tools/story.sh baseline <slice> <n>` and reads why they fail on `main` | `artifacts/trials/<story>/` (local), with `main-before.txt` |
| 7. Ready | `lead` | `tools/story.sh ready <slice> <n>`: checks the definition of ready and creates the sub-issue | A GitHub issue labelled `ready` |
| 8. Run | `impl` (or `senior`) | `tools/story.sh start <issue>`, then implements in the worktree | One commit on a local branch |
| 9. Gate | `qa` | `tools/story.sh done <issue>`: paths, build, acceptance tests | `needs-review`, one retry, `partial` or `blocked` |
| 10. Diff read | `reviewer` | Reads the diff once | Pass, or one of the four blocking kinds |
| 11. Pull request | `lead`, then the maintainer | `tools/story.sh pr <issue>`; the maintainer merges; `cleanup` | Done |

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

A story goes to the `senior` seat only when it cannot be split further. The architect writes the
reason in the story's block in `stories.md` (`route: impl/senior` and `escalation: <reason>`);
a story with that route and no reason fails the check. Most stories never need it.

### 4. Ready

A story gets its GitHub issue and the label `ready` when:

1. the brief is approved;
2. the story lists its items and is under the size cap;
3. its acceptance tests exist and fail on `main` for the right reason (`baseline` records the
   failure; `qa` reads the reason);
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
the issue label unchanged. Such logs do not count in the retry history; rerun the gate.
Compiler errors, warnings treated as errors and acceptance test failures still count.

**Sending a story back:** `tools/story.sh start <issue> --retry` is the only way. It keeps the
worktree and branch, merges `main` in, writes the story text again from the brief on `main`
(and updates the issue body), and sets `in-progress`.

**Stop rule:** one retry at most. After the second failed attempt, the tag decides:
a `context-gap` sends the story back to the analysis, where the brief is fixed or the story is
split; a `judgment-gap` escalates it to the `senior` seat. There are no follow-up briefs.

### 8. Done, for the issue

- All its stories are done or partial.
- The spec amendment and the docs are merged (see `CLAUDE.md`, "Definition of done").
- The pull request states which risks from [`risks.md`](risks.md) it checks or closes.
- Backlog items raised along the way are done or have their own issue.

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

Each story has its own worktree, and so has the analysis of each slice
(`tools/story.sh analysis <slice>`). `baseline` uses a worktree that it removes again. No seat
builds or commits in the main checkout, and no seat creates a worktree by hand.

## Who may do what on GitHub

| Action | Who |
|---|---|
| Create a sub-issue, set labels | `lead` |
| Push a story branch | `lead`, through `tools/story.sh pr` |
| Open a pull request | `lead` |
| Merge | Maintainer only |

## Commands

```bash
bash tools/story.sh check <slice>
bash tools/story.sh split <slice>
bash tools/story.sh split-done <slice>
bash tools/story.sh show <slice> <n>
bash tools/story.sh analysis <slice> [--remove]
bash tools/story.sh status
bash tools/story.sh baseline <slice> <n>
bash tools/story.sh ready <slice> <n> [--dry-run]
bash tools/story.sh next [<route>]
bash tools/story.sh start <issue> [--retry]
bash tools/story.sh done <issue>
bash tools/story.sh pr <issue> [--maintainer-reviewed]
bash tools/story.sh cleanup <issue>
```

`AIAKOS_NO_WRITE=1` makes a command print its label changes instead of applying them.
The script is the one place where the definitions above are enforced; the seats call it and do
not replace it. It can still be run by hand.

Useful OpenRig commands:

```bash
rig ps --nodes --rig aiakos-delivery   # seats and what they are doing
rig parked                             # seats that stopped while they owe work
rig queue list                         # work items and their owners
rig tui                                # the board; "terminal rig:aiakos-delivery" opens herdr
```
