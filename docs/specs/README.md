# Specs

A spec describes *how* a piece of work will be done: requirements, design, acceptance criteria
and test plan. The *what/why* and status live in the linked GitHub issue.

- Copy [`TEMPLATE.md`](TEMPLATE.md) to `NNNN-short-title.md` (next free number, 4 digits).
- A spec is reviewed in a PR. It is `accepted` when merged, `implemented` when the work ships.
- When the implementation deviates, update the spec in the same PR (see "Changes after acceptance").

| # | Title | Status | Milestone |
|---|---|---|---|
| [0001](0001-solution-skeleton.md) | Solution skeleton | accepted | M1 |
| [0002](0002-orchestrator-node-grpc-contract.md) | Orchestrator ↔ node gRPC contract v1 | accepted | M1 |
| [0003](0003-rig-file-format-v1.md) | Rig file format v1 | accepted | M1 |
| [0004](0004-tmux-session-host.md) | Node agent: tmux session host | accepted | M1 |
| [0005](0005-claude-code-adapter.md) | Claude Code adapter | accepted | M1 |
| [0006](0006-seat-actor.md) | SeatActor: lifecycle and three-axis state | accepted | M1 |
| [0008](0008-aiakos-dev-rig.md) | The aiakos-dev rig and the M1 acceptance | draft | M1 |
