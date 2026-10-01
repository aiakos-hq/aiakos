# Briefs

A brief is the whole task for one slice: an implementer reads the brief and nothing else. Briefs
are written from [`TEMPLATE.md`](TEMPLATE.md) and kept here, so they are versioned with the code
they produce. When the maintainer approves a brief, its text becomes the body of the slice issue
(see the "Slices" paragraph in [`CLAUDE.md`](../../CLAUDE.md)).

Status in each brief's header:

- `draft`: written, waiting for the maintainer's approval. Not ready to implement.
- `approved`: a slice issue exists. It is ready once its dependencies are merged and its scoring
  test and probes exist.
- `implemented`: merged.
- `superseded`: replaced; kept only when something still refers to it.

Follow-up briefs (`<slice>b`, `<slice>c`) come out of a review and apply on top of the slice's
brief. Scoring tests and probes are not here: they stay local, so an implementer cannot read them.

## Index

| Brief | Issue | Route | Status | Slice issue / pull request |
|---|---|---|---|---|
| [10-1 Proto and buf checks](10-1-proto-and-buf-ci.md) | #10 | `impl/opencode` | implemented | PR #44 |
| [10-2 Contract helpers and contract tests](10-2-contract-helpers.md) | #10 | `impl/opencode` | draft | |
| [11-1 Session host interface, validators and the fake](11-1-session-host-interface-and-fake.md) | #11 | `impl/sonnet` | draft | |
| [13-1 Pure seat state machine and harness state profile](13-1-seat-state-machine.md) | #13 | `impl/sonnet` | draft | |
| [13-2 Seat model migration and `SeatQueries`](13-2-seat-schema-and-queries.md) | #13 | `impl/sonnet` | draft | |
| [14-1 Rig file envelope and diagnostics](14-1-envelope-and-diagnostics.md) | #14 | `impl/opencode` | implemented | PR #43 |
| [14-1b Review fixes](14-1b-review-fixes.md) | #14 | `impl/opencode` | implemented | PR #43 |
| [14-2 Semantic validation of the three YAML files](14-2-semantic-validation.md) | #14 | `impl/opencode` | approved | #45 |
| [14-2b Review fixes](14-2b-review-fixes.md) | #14 | `impl/opencode` | approved | #45 |
| [14-2c Review fixes, second round](14-2c-review-fixes.md) | #14 | `impl/opencode` | approved | #45 |

## Not briefed yet

| Issue | Slices |
|---|---|
| #10 gRPC contract | 10-3 link, 10-4 events, 10-5 commands |
| #11 tmux session host | 11-2 runner and start, 11-3 delivery and capture, 11-4 stop and watcher, 11-5 adoption |
| #12 Claude Code adapter | 12-1 orchestrator half, 12-2 relay and ingest, 12-3 normalizer, 12-4 node driver, 12-5 end to end |
| #13 SeatActor | 13-3 actor shell, 13-4 lifecycle and delivery, 13-5 restart and integration |
| #14 rig loader | 14-3 file references and the resolved rig, 14-4 canonical form and hashes, 14-5 projection |
| #15 CLI and released instance | 15-1 skeleton and dry run, 15-2 local API, 15-3 client commands, 15-4 instance host, 15-5 release |
| #16 aiakos-dev rig, M1 acceptance | 16-1 rig files, 16-2 pin and rig-compat, 16-3 acceptance run |

Update the index in the same pull request that adds a brief or changes its status.
