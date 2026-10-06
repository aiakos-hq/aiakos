# Briefs

A brief is the closed description of one slice of an M1 issue. It is written from
[`TEMPLATE.md`](TEMPLATE.md), split into stories, reviewed and then implemented story by story.
The flow, with the definitions of ready and done, is in [`../workflow.md`](../workflow.md).

Each slice has a folder:

| File | What |
|---|---|
| `brief.md` | The brief. Its header has the status, the route, the allowed paths and, when needed, its own size caps |
| `items.tsv` | Every rule, change, expected output and test of the brief, with what it needs |
| `stories.md` | The split into stories, IDs only |
| `findings.md` | The story review. A line that starts with `- [ ]` is an open finding |
| `followup-*.md` | Only in slices from before the story workflow: the follow-up briefs of the review loop |

Status in a brief's header:

- `draft`: being written, reviewed or amended. Its stories cannot become ready.
- `approved`: the architect reviewed it with no open finding and its pull request is merged. Its
  stories can become ready, one by one. `tools/story.sh analysis-pr` sets this in the same pull
  request as the brief, so the merge is the approval and no second pull request follows.
- `implemented`: all its stories are merged.
- `superseded`: replaced; kept only when something still refers to it.

Acceptance tests are not here: they stay local, so that an implementer cannot read them.
`bash tools/story.sh status` shows every story and its state.

## Index

| Slice | Issue | Route | Status | Stories | Open findings | Issue / pull request |
|---|---|---|---|---|---|---|
| [9-1 Deterministic orchestrator host disposal](9-1/brief.md) | #9 | `impl` + `impl/senior` | approved | [2](9-1/stories.md) | | #103 (superseded at ready) |
| [10-1 Proto and buf checks](10-1/brief.md) | #10 | `impl/opencode` | implemented | not split (before the story workflow) | | PR #44 |
| [10-4 Buffered node events](10-4/brief.md) | #10 | `impl` | approved | [7](10-4/stories.md) | | |
| [10-3 Authenticated node link](10-3/brief.md) | #10 | `impl` | approved | [10](10-3/stories.md) | | |
| [10-2 Contract helpers and contract tests](10-2/brief.md) | #10 | `impl/opencode` | approved | [1](10-2/stories.md) | | |
| [12-1 Claude orchestrator adapter](12-1/brief.md) | #12 | `impl` | approved | [5](12-1/stories.md) | | |
| [11-2 Tmux runner and start](11-2/brief.md) | #11 | `impl` | approved | [8](11-2/stories.md) | | |
| [11-1 Session host interface, validators and the fake](11-1/brief.md) | #11 | `impl/sonnet` | draft | [4](11-1/stories.md) | [2](11-1/findings.md) | |
| [13-1 Pure seat state machine and harness state profile](13-1/brief.md) | #13 | `impl/sonnet` | approved | [12](13-1/stories.md) |  | |
| [13-2 Seat model migration and `SeatQueries`](13-2/brief.md) | #13 | `impl/sonnet` | approved | [2](13-2/stories.md) |  | |
| [14-1 Rig file envelope and diagnostics](14-1/brief.md) | #14 | `impl/opencode` | implemented | not split (before the story workflow) | | PR #43 |
| [14-2 Semantic validation of the three YAML files](14-2/brief.md) | #14 | `impl/opencode` | implemented | not split (before the story workflow) | | #45 |
| [14-3 File references and the resolved rig](14-3/brief.md) | #14 | `impl` | approved | [6](14-3/stories.md) | | |
| [14-4 Canonical form and hashes](14-4/brief.md) | #14 | `impl` | approved | [3](14-4/stories.md) | | |
| [14-5 Claude Code projection plans](14-5/brief.md) | #14 | `impl` | approved | [4](14-5/stories.md) | | |

Slices 10-1, 14-1 and 14-2 were implemented as one piece each, before stories existed. 14-1 and
14-2 went through the review loop, whose follow-up briefs are kept next to them. 14-2 did not
converge in that loop and was finished by a direct fix, then merged as one legacy story through
the new gate.

## Not briefed yet

| Issue | Slices |
|---|---|
| #10 gRPC contract | 10-5 commands |
| #11 tmux session host | 11-3 delivery and capture, 11-4 stop and watcher, 11-5 adoption |
| #12 Claude Code adapter | 12-2 relay and ingest, 12-3 normalizer, 12-4 node driver, 12-5 end to end |
| #13 SeatActor | 13-3 actor shell, 13-4 lifecycle and delivery, 13-5 restart and integration |
| #15 CLI and released instance | 15-1 skeleton and dry run, 15-2 local API, 15-3 client commands, 15-4 instance host, 15-5 release |
| #16 aiakos-dev rig, M1 acceptance | 16-1 rig files, 16-2 pin and rig-compat, 16-3 acceptance run |

`tools/story.sh analysis-pr` updates the row of its slice (status, story count, findings). Change
other rows by hand in the pull request that causes the change.
