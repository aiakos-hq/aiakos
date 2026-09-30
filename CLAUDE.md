# Aiakos — instructions for agents

Aiakos is a file-defined control plane for teams of AI coding agents, built in .NET.
Read [`docs/plan.md`](docs/plan.md) for the architecture and roadmap before changing anything
non-trivial, and the ADRs in [`docs/adr/`](docs/adr/) for decisions that are already made —
do not reopen them without writing a new ADR that supersedes the old one.

## Current stage

**Stage A (manual).** There is no code yet. Work is done by a human with a single agent
session. Milestone **M1** is the self-hosting threshold; after it, this repository is developed
by an Aiakos-managed rig defined in [`rigs/aiakos-dev/`](rigs/aiakos-dev/).

**M0 (spikes) is done.** All five spikes are in [`docs/spikes/`](docs/spikes/): Claude Code in
WSL tmux, session resume, Aspire launching a WSL node, the OpenCode API, and a Docker seat.
Specs and code build on their findings.

**Next up: the M1 specs**, in three waves that follow the dependencies:
1. #9 (solution skeleton), #10 (orchestrator ↔ node gRPC contract) and #14 (rig file format).
2. #11 (tmux session host), #12 (Claude Code adapter) and #13 (SeatActor).
3. #15 (CLI) and #16 (the `aiakos-dev` rig, which is M1's acceptance test).

Implementation of an issue starts once its spec is merged, beginning with #9. Specs go to
`docs/specs/NNNN-*.md` from [`docs/specs/TEMPLATE.md`](docs/specs/TEMPLATE.md).

**Environment:** Windows 11 host; WSL2 distro `Ubuntu` (agents, tmux, Claude Code run there);
Docker Desktop (`docker-desktop` WSL distro). Keep repos that agents edit on the WSL filesystem
when working inside WSL (performance, file watching); this repo itself is checked out on Windows.
**WSL mirrored networking is required** for local development: `%USERPROFILE%\.wslconfig` must
contain `[wsl2]` / `networkingMode=mirrored` (apply with `wsl --shutdown`). WSL and Windows then
reach each other on `127.0.0.1` with no firewall rules; NAT mode is not supported
(see [spike 0003](docs/spikes/0003-aspire-wsl-node.md)).

**Related repositories:** `aiakos-hq/aiakos.dev` (website; renders this repo's `docs/`, never
edit docs there) and `aiakos-hq/.github` (org profile, CONTRIBUTING, SECURITY, CODE_OF_CONDUCT).
`main` is protected: every change goes through a pull request.

## How work is organized

- **Tickets**: GitHub Issues in `aiakos-hq/aiakos` (use `gh`). Every piece of work has an issue.
- **Specs**: `docs/specs/NNNN-short-title.md` from [`docs/specs/TEMPLATE.md`](docs/specs/TEMPLATE.md).
  Non-trivial work needs a spec before implementation (issue label `spec-needed` until then).
  The spec is updated in the same PR as the code when the implementation deviates.
- **Decisions**: `docs/adr/NNNN-short-title.md` from [`docs/adr/TEMPLATE.md`](docs/adr/TEMPLATE.md).
- **Spikes**: throwaway experiments; findings go to `docs/spikes/NNNN-short-title.md`. Spike code
  is not merged into `src/`.
- **Risks** are indexed in [`docs/risks.md`](docs/risks.md); check the relevant ones before
  implementing an issue and at milestone boundaries.

Labels: `type/*` (feature, bug, spike, chore, docs), `area/*`, and status labels
`spec-needed`, `ready`, `in-progress`, `needs-review`, `blocked`.
Milestones `M0`–`M8` follow the roadmap in `docs/plan.md` §10.

## Rules

1. **The bootstrap rule**: the team that builds Aiakos runs on the *last released* Aiakos, never
   on the working tree it is changing. Never point a running rig at a development build.
2. Identity comes from the environment / per-seat token, never from request bodies.
3. Honest state: `unknown` is a valid answer; no silent fallbacks (e.g. resume must never
   silently start a fresh session).
4. Terminals are transport, the database is the record.
5. Host-specific concerns (terminals, sandboxes, harnesses, chat) stay behind interfaces
   (`ISessionHost`, `ISandbox`, `IHarnessAdapter` on the orchestrator and `IHarnessDriver` on
   the node (ADR 0018), `ISeatChannel`, `IChatConnector`).
6. Every table has `tenant_id` from its first migration (single default tenant for now).
   Only migration bookkeeping (the DbUp journal in schema `aiakos_meta`) is exempt.
7. Line endings are LF (see `.gitattributes`); code is edited both on Windows and in WSL.

## Definition of done

- Linked issue and (if non-trivial) spec; acceptance criteria met.
- Tests added/updated and passing; build has no warnings introduced.
- Docs under `docs/` updated in the same PR.
- A reviewer (human or reviewer seat) has checked the exact diff.
- The maintainer approves and merges. All changes go through PRs to `main`.

## Commands

No code yet. When the solution exists, build/test/run commands go here.

## Commits and PRs

- Small, reviewable PRs; one issue per PR where possible. Reference the issue (`Closes #N`).
- Conventional-style subjects: `feat(orchestrator): …`, `fix(node): …`, `docs: …`, `chore: …`.
