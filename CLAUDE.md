# Aiakos — instructions for agents

Aiakos is a file-defined control plane for teams of AI coding agents, built in .NET.
Read [`docs/plan.md`](docs/plan.md) for the architecture and roadmap before changing anything
non-trivial, and the ADRs in [`docs/adr/`](docs/adr/) for decisions that are already made —
do not reopen them without writing a new ADR that supersedes the old one.

## Current stage

**Stage A.** Work is done by a team of agents in an OpenRig rig in WSL, defined in
[`rigs/aiakos-delivery/`](rigs/aiakos-delivery/); the maintainer approves briefs and merges
(see [`docs/workflow.md`](docs/workflow.md)). The solution skeleton
(#9, spec 0001) exists: `Aiakos.slnx` with the Aspire AppHost, the orchestrator, the node agent
in WSL, Postgres with DbUp migrations, OpenTelemetry and CI. The CLI in
[`src/Aiakos.Cli`](src/Aiakos.Cli/) is still the placeholder published on nuget.org as `Aiakos`
0.0.1-preview.1 to reserve the ID (#6). Milestone **M1** is the self-hosting threshold: from it,
Aiakos can run the rig in [`rigs/aiakos-dev/`](rigs/aiakos-dev/). The team moves from OpenRig to
that rig when Aiakos is stable enough not to slow the work, not on a fixed date.

**M0 (spikes) is done.** All five spikes are in [`docs/spikes/`](docs/spikes/): Claude Code in
WSL tmux, session resume, Aspire launching a WSL node, the OpenCode API, and a Docker seat.

**M1 specs are all accepted.** Specs 0001–0008 in [`docs/specs/`](docs/specs/) cover #9, #10,
#14, #11, #12, #13, #15 (CLI and the released instance) and #16 (the `aiakos-dev` rig and the M1
acceptance procedure). The decisions are in ADRs 0012–0038 in [`docs/adr/`](docs/adr/).

**Next up: implementation of #10** (gRPC contract, spec 0002); #9 (solution skeleton) is done.
Then #11/#12/#13, then #14 and #15, then cut release `0.1.0` (ADR 0036), then #16, whose
acceptance run on the released tool closes M1 (spec 0008).
Implementation of an issue starts once its spec is merged. Before implementing an issue, check
its line in [`docs/risks.md` → Open risks by issue](docs/risks.md#open-risks-by-issue) and say in
the PR which risks it checks or closes.
Specs go to `docs/specs/NNNN-*.md` from [`docs/specs/TEMPLATE.md`](docs/specs/TEMPLATE.md).

**Environment:** Windows 11 host; WSL2 distro `Ubuntu` (agents, tmux, Claude Code run there);
Docker Desktop (`docker-desktop` WSL distro) with **WSL integration enabled for `Ubuntu`**, so
`docker` works inside WSL and WSL paths can be bind-mounted
(see [spike 0005](docs/spikes/0005-docker-seat.md)). Keep repos that agents edit on the WSL filesystem
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

- **Slices and stories** (stage A): an M1 issue is implemented as several slices. A slice is one
  **brief**, `docs/briefs/<issue>-<n>/brief.md`, written from
  [`docs/briefs/TEMPLATE.md`](docs/briefs/TEMPLATE.md): a closed description in which every rule,
  change, expected output and test is an item with an ID (`items.tsv`). The `author` seat splits
  the brief into small **stories** (`stories.md`), a script checks the split, the `architect`
  seat writes `findings.md`, and merging that analysis is its approval. Stories are sized for a
  Sonnet-level implementer; a story goes to a stronger model only when it cannot be split
  further, with the reason written down. A story that meets the definition of
  ready becomes a GitHub sub-issue of the M1 issue, titled `<issue>-<n>-<m>: <title>`, with a
  routing label `impl` or `impl/senior` and the label `ready`; each story is one pull
  request. Done is decided before the run: a story's acceptance tests are written first, stay
  local in `artifacts/trials/<story>/` (git-ignored), and `tools/story.sh done` runs them as a
  gate. Only a failed acceptance test, an exception, a leaked secret or an earlier test turned
  red blocks a story; anything else becomes a new item. One retry at most, and no follow-up
  briefs. Judgment is checked by another vendor: Codex writes and implements, Claude
  checks the analysis and reads the diff; the gate is a script that a Codex seat runs. The whole flow, with the seats and the
  definitions of ready, done and partial, is in
  [`docs/workflow.md`](docs/workflow.md); the index of briefs is
  [`docs/briefs/README.md`](docs/briefs/README.md). `tools/story.sh` is where the rules are
  enforced; the seats call it, and the relay chat
  ([`.claude/skills/story`](.claude/skills/story/SKILL.md)) can still run it by hand. Do not use
  the earlier slice review loop (`tools/slice.sh`, `/slice`, follow-up briefs): it is removed.

Labels: `type/*` (feature, bug, spike, chore, docs), `area/*`, routing labels `impl`
and `impl/senior` (stories), and status labels `spec-needed`, `ready`, `in-progress`,
`needs-review`, `partial`, `blocked`.
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

Prerequisites: .NET SDK 10.0.1xx+ (see `global.json`); Docker Desktop running; WSL2 distro
`Ubuntu` with mirrored networking (see above). The node agent in WSL needs no .NET, but the
delivery rig builds and tests in the WSL checkout, so WSL has the same .NET SDK and `gh`.

- Build (warnings are errors): `dotnet build`
- Test (Docker required for the database tests): `dotnet test`
- Run the dev stack (Postgres, orchestrator, node in WSL): `dotnet run --project src/Aiakos.AppHost`
  — dashboard at http://localhost:15180
- WSL end-to-end test (Windows only, stack not running): `$env:AIAKOS_E2E_WSL=1; dotnet test --project tests/Aiakos.AppHost.Tests`
- Pack the placeholder tool: `dotnet pack src/Aiakos.Cli -c Release -o artifacts/packages`
- Check the proto (needs buf 1.73.0): `buf lint` and `buf breaking --against '.git#branch=main'`
- Story workflow helper (Git Bash): `bash tools/story.sh status|check|split|show|ready|next|start|done|pr|cleanup`
  (see [`docs/workflow.md`](docs/workflow.md))
- Add a migration: `src/Aiakos.Data/Migrations/NNNN_description.sql` (next number; never edit a
  merged one; every table in schema `aiakos` gets `tenant_id`).
- Reset the dev database (stack stopped): `docker rm -f <postgres container>` then
  `docker volume rm aiakos-dev-pgdata`.
- The dev stack uses instance `dev` (`~/.aiakos-dev`, ports 5180+). It never touches the released
  tool's instance (`~/.aiakos`, ports 7180+).

## Commits and PRs

- Small, reviewable PRs; one issue per PR where possible. Reference the issue (`Closes #N`).
- Conventional-style subjects: `feat(orchestrator): …`, `fix(node): …`, `docs: …`, `chore: …`.
