# How work flows (stage A)

This page describes how an M1 issue becomes merged code while Aiakos cannot yet run its own team.
It replaces the earlier slice review loop, in which a reviewer looked for problems after each run
and wrote follow-up briefs. That loop had no fixed bar and did not end
(slice 14-2: three runs and three reviews without a pass).

Two ideas carry the flow:

- **Done is decided before the run.** A story's acceptance tests exist first. When they pass, the
  story is done. Anything else that someone notices becomes a new item.
- **Work is small.** A brief is split into stories under a size cap before anyone implements it.

From M3 the Aiakos work queue takes over the parts that are files and scripts here.

## The units

| Unit | What it is | Where it lives |
|---|---|---|
| Issue | One M1 feature (`#14`) with its spec | GitHub |
| Slice | One brief: a closed description of a part of the issue | `docs/briefs/<slice>/`, for example `14-3` |
| Story | A part of a slice that one implementer run can finish | `stories.md` of the slice; ID `<slice>-<n>`, for example `14-3-2` |
| Item | One rule, change, expected output or test of the brief, with an ID | `items.tsv` of the slice |

## The flow

| Step | Who | What happens | Result |
|---|---|---|---|
| 1. Brief | Strong model, approved by the maintainer | Writes `brief.md` and `items.tsv` from [`briefs/TEMPLATE.md`](briefs/TEMPLATE.md) | A closed brief |
| 2. Split | Cheap model | `tools/story.sh split <slice>`: sorts the items into stories, IDs only | `stories.md` |
| 3. Check | Script | `tools/story.sh check <slice>`: traceability and size | Pass or a list of errors |
| 4. Story review | `story-checker` agent | Reads the brief and the split once for ties the script cannot see | `findings.md` |
| 5. Approval | Maintainer | Findings are resolved by commits; the branch is merged | The analysis is on `main` |
| 6. Acceptance | Strong model | Writes the story's acceptance tests and `gate.sh`, and runs them on `main` to see them fail | `artifacts/trials/<story>/` (local) |
| 7. Ready | Maintainer | `tools/story.sh ready <slice> <n>`: checks the definition of ready and creates the sub-issue | A GitHub issue labelled `ready` |
| 8. Run | Implementer | `tools/story.sh start <issue>`, then the printed command | One commit on a branch |
| 9. Gate | Script | `tools/story.sh done <issue>`: paths, build, acceptance tests | `needs-review`, one retry, or `blocked` |
| 10. Diff read | `story-reviewer` agent or the maintainer | Reads the diff once | Pass, or one of the four blocking kinds |
| 11. Pull request | Maintainer | `tools/story.sh pr <issue>`, merge, `cleanup` | Done |

Steps 1 to 5 happen in files on a branch. The back and forth between the analysis and its review
is the commit history of that branch, and `findings.md` is where the reviewer writes. Nothing
goes to GitHub before step 7. When M3 brings messages between seats, `findings.md` gives way to
comments.

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

The two numbers are a first guess. Record how many runs each story needs and adjust them.
A brief may set its own caps in its header (`max_rules`, `max_outputs`). That is meant for a
brief whose expected outputs are one-line test cases and not fixtures: slice 10-2 has 13 cases
for one rule. A slice that fits the caps as a whole is one story.

### 3. Ready

A story gets its GitHub issue and the label `ready` when:

1. the brief is approved;
2. the story lists its items and is under the size cap;
3. its acceptance tests exist and fail on `main` for the right reason;
4. every story it depends on is done;
5. the review findings are resolved or filed as their own items;
6. its route (which implementer) is set.

### 4. Done, for a story

- Its acceptance tests pass and all earlier tests stay green.
- The build has 0 warnings and only the brief's paths are touched.
- The diff has been read once, by the `story-reviewer` agent or by the maintainer.
- The pull request is merged and the sub-issue is closed.

### 5. What blocks, and the stop rule

Only four things block a story:

- a failed acceptance test;
- an exception on any input;
- a leaked secret value;
- an earlier test that turned red.

A reviewer may claim one of these only for something it ran. Everything else becomes a new
backlog item and does not hold the story.

**Stop rule:** one retry at most. When the gate fails a second time, the story was groomed
wrong: split it or change the implementer. There are no follow-up briefs.

### 6. Done, for the issue

- All its stories are done.
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
checkout, so that an implementer cannot read them. `gate.sh` there copies them into the worktree,
runs them and removes them again.

## Who does what

| Role | Model | Reads | Writes |
|---|---|---|---|
| Brief author | strong | spec, code | `brief.md`, `items.tsv`, acceptance tests |
| Splitter | cheap (`AIAKOS_SPLIT_MODEL`) | the brief and the item list only | `stories.md` |
| Story checker | strong | brief, split | findings (returned as text; the caller saves the file) |
| Implementer | by route: `impl/opencode` or `impl/sonnet` | its story | code, one commit |
| Story reviewer | strong | story, diff, gate output | a verdict (returned as text) |
| Relay ([`/story`](../.claude/skills/story/SKILL.md)) | cheap | script output | nothing; it runs `tools/story.sh` |
| Maintainer | — | all of it | approvals, merges |

## Commands

```bash
bash tools/story.sh check <slice>
bash tools/story.sh split <slice>
bash tools/story.sh split-done <slice>
bash tools/story.sh show <slice> <n>
bash tools/story.sh status
bash tools/story.sh ready <slice> <n> [--dry-run]
bash tools/story.sh next [<route>]
bash tools/story.sh start <issue>
bash tools/story.sh done <issue>
bash tools/story.sh pr <issue> [--maintainer-reviewed]
bash tools/story.sh cleanup <issue>
```

`AIAKOS_NO_WRITE=1` makes a command print its label changes instead of applying them.
