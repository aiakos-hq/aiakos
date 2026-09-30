# Aiakos — Plan

> A file-defined control plane for teams of AI coding agents.
> Named after **Aiakos (Αιακός)**, the king of Aegina for whom Zeus turned the island's ants
> into the **Myrmidons** — a disciplined, loyal crew. Aiakos summons the crew, governs it and
> judges its work. Mascot idea: a honey badger (the survivor) leading a column of ants.

This document summarizes the design discussion that started from studying
[OpenRig](https://github.com/mvschwarz/openrig). It is the starting point for the new repository.

---

## Step 0 — Secure the name (before anything else)

Availability checked on 2026-09-28: `Aiakos` was free on NuGet, npm, PyPI and crates.io;
`aiakos.dev`, `.io`, `.ai`, `.sh` had no DNS (likely unregistered); `aiakos.com` and the GitHub
username `aiakos` are taken. No agent-tooling projects use the name.

Status:
- [x] GitHub organization **`aiakos-hq`** created (contact: Gmail plus-address)
- [x] Domain **`aiakos.dev`** purchased
- [x] Website repo **`aiakos-hq/aiakos.dev`** — Statiq site scaffolded from the maintainer's blog
  (landing, news, docs rendered from `aiakos/docs`, Pagefind search); live at https://aiakos.dev
- [x] Main repo **`aiakos-hq/aiakos`** — stage A set up (this plan, ADRs, templates, labels, milestones, issues)
- [x] **`aiakos-hq/.github`** — org profile + community health files
- [x] GitHub Pages + DNS (Namecheap) + org-level domain verification + HTTPS enforced
- [x] Email: `hello@aiakos.dev` → Gmail via **Namecheap email forwarding** (Domain tab → Redirect Email)
- [x] `main` protected (ruleset "Protect main": PRs only, 0 approvals while solo, no force-push/deletion);
      private vulnerability reporting enabled
- [ ] NuGet ID `Aiakos` + prefix reservation — issue #6
- [ ] Docs rebuild trigger: fine-grained token (*Contents: write* on `aiakos.dev`) as a secret in
      `aiakos`, plus a workflow sending `repository_dispatch` `docs-updated` on `docs/**` changes
      (until then the site rebuilds nightly)
- [ ] Later: required status checks on `main` (once CI exists, M1); raise approvals to 1 when a
      reviewer seat or second maintainer exists; optional GitHub Project board

### 0.1 GitHub organization
1. Go to https://github.com/organizations/plan → **Free**. ✅ `aiakos-hq`
2. Settings → Authentication security → require 2FA.
3. Settings → Pages → **verify `aiakos.dev`** (TXT record) to prevent domain takeover.
4. Create the repositories listed in §0.4.

### 0.2 NuGet package ID
Needed for one concrete reason: the CLI ships as a **.NET tool** (`dotnet tool install -g aiakos`),
which is also how the self-hosting team pins the last released version (see §11). Everything
else ships as containers or native-AOT binaries (see §0.5).

1. Sign in at https://www.nuget.org (Microsoft account), enable 2FA.
2. Optional but recommended: create a nuget.org **organization** `aiakos` to own the packages.
3. API Keys → Create: scope *Push new packages and package versions*, glob `Aiakos*`.
4. Publish an honest early-preview package (first publish owns the ID). The package is
   [`src/Aiakos.Cli`](../src/Aiakos.Cli/): a placeholder `dotnet tool` (`PackageId=Aiakos`,
   command `aiakos`, `0.0.1-preview.1`) that prints a greeting and its version. The real CLI
   (#15) replaces it under the same ID.

   ```powershell
   dotnet pack src/Aiakos.Cli -c Release -o .\nupkg
   ```

   ```powershell
   dotnet nuget push .\nupkg\Aiakos.0.0.1-preview.1.nupkg --api-key <KEY> --source https://api.nuget.org/v3/index.json
   ```

   Unlisting later still keeps the ID reserved.
5. Request **ID prefix reservation** `Aiakos.*` by emailing account@nuget.org (owner account/org,
   links to GitHub org + published package). Protects all future `Aiakos.*` packages.

### 0.3 Also worth doing now
- ✅ `aiakos.dev` registered at **Namecheap** (Namecheap DNS). Records for GitHub Pages: apex `A`
  185.199.108–111.153 (+ `AAAA` 2606:50c0:8000–8003::153), `www` `CNAME` → `aiakos-hq.github.io.`,
  TXT `_github-pages-challenge-aiakos-hq`; parking records removed; **Enforce HTTPS** on
  (`.dev` is HTTPS-only).
- ✅ Email: Namecheap email forwarding `hello@aiakos.dev` → Gmail (keep Namecheap's MX/SPF records).
  Later move GitHub/NuGet contacts to it.
- Optionally claim `aiakos` on npm/PyPI if a JS plugin (e.g. OpenCode) or Python SDK is plausible.
- Before any public release: trademark search (EUIPO/TMview, USPTO; classes 9 and 42).
- Later: NuGet **trusted publishing** (OIDC) from GitHub Actions instead of long-lived keys.

### 0.4 Repositories in `aiakos-hq`

| Repo | Purpose | When |
|---|---|---|
| **`aiakos`** | Code monorepo: orchestrator, node agent, CLI, hook relay, Aspire AppHost, tests, `docs/` (incl. `specs/`, `adr/`), starter rigs under `rigs/` | now |
| **`aiakos.dev`** ✅ | Website (Statiq, reusing the blog's pipelines/drafts/CI): landing, news, **docs rendered from `aiakos/docs`** | now |
| **`.github`** | Org profile (`profile/README.md`), default issue/PR templates, `CONTRIBUTING.md`, `SECURITY.md`, `CODE_OF_CONDUCT.md`, reusable workflows | now |
| `rigs` | Shareable library of agents/rigs/skills (like OpenRig's `specs/`); starts as `aiakos/rigs/`, split out when it grows or gets outside contributors | later |
| `seat-images` | Dockerfiles for sandboxed seat images (Claude/OpenCode/Codex + hook relay + CLI), published to `ghcr.io/aiakos-hq/*` | M6 (start as `aiakos/images/`) |
| `homebrew-tap` / `winget` manifests | Package-manager distribution of the CLI binaries | when publicly released |

Principle: **one code repo until something has a different release cadence or audience.**
Contracts (gRPC, spec schemas) change together, and agents work best with everything in one tree.

Docs: Markdown lives in `aiakos/docs/` (changed in the same PR as the code; agents read it).
The `aiakos.dev` workflow checks out both repos, renders docs via a `DocsPipeline` to
`aiakos.dev/docs` (search via Pagefind), and is triggered by `repository_dispatch` from
`aiakos` on `docs/**` changes (+ nightly). Versioned docs later (`/docs/latest`, `/docs/vX.Y`).

### 0.5 Distribution (for reference)

| Component | Channel |
|---|---|
| Orchestrator | Container image; `aspire publish` → Docker Compose |
| Node agent | Native-AOT binaries (GitHub Releases), baked into seat images |
| Hook relay | POSIX `sh` script projected into each seat home (M1); native-AOT binary in seat images (M6) ([ADR 0028](adr/0028-hook-transport.md)) |
| CLI | `dotnet tool` (NuGet) + binaries/winget/Homebrew |
| Extension SDK, templates | NuGet, only if/when opened to plugins |

---

## 1. What we learned from OpenRig

OpenRig (TypeScript, Node, SQLite, tmux) runs Claude Code / Codex seats as one team.
Ideas worth keeping:

- **Rigs defined in shareable files**: rig spec → reusable agent specs → shared libraries →
  culture file → portable bundles; resolved at launch and *projected* into each harness's
  native files (CLAUDE.md / AGENTS.md / skills / settings).
- **Seat identity from the environment** (`seat@rig`), never from request bodies.
- **tmux as transport, not truth**: messages are pasted into panes (bracketed paste + separate
  Enter); the durable record lives in the database.
- **Queue with a closure contract ("hot potato")**: an item cannot be closed without a reason
  (`handed_off_to`, `blocked_on`, `denied`, `canceled`, `no-follow-on`, `escalation`) and a
  target where relevant; handoff is atomic.
- **Honest state**: three orthogonal axes (session / activity / resumability); `unknown` is a
  valid answer; no silent fresh-fallback on resume.
- **Hooks over screen scraping**: harness hooks report activity to the daemon.
- **Long-running health**: context tracking, refocus after compaction, seat handover, snapshot/restore.
- **Human in the loop**: human-gate views, Slack gateway, humans as first-class seats.
- **herdr/cmux** only *render* views of tmux sessions (OpenRig owns semantics, provider owns pixels).

What OpenRig does *not* fit: native Windows, sandboxed agents (it inspects host processes and
host files), and much of its process machinery is specific to its own agent-driven development.

---

## 2. Goals and constraints

- .NET/C#; developer machine is **Windows** (local dev via **Aspire**).
- **Built with itself**: Claude Code is the **first** harness (the builders are Claude Code seats),
  **OpenCode is the priority second harness**, then Codex.
- Two operating modes:
  - **Local** — seats use the developer's **subscription** logins, no sandbox.
  - **Sandboxed ("enterprise")** — seats in Docker or brig, **API keys** from a secret store.
- Single user on the local network now; **multitenant** later.
- Orchestrator may run on the PC for single use; later it must be always-on (Linux box/server).
- Agents reachable from **Telegram and Slack**.
- Rig definitions are **files you can share**, like OpenRig.

---

## 3. Architecture

```
                          ┌────────────────────── Orchestrator (Aiakos) ───────────────────────┐
 You ── Web UI (Blazor) ─►│  API (REST + SignalR)   MCP server (agents)   Chat gateway          │◄── Telegram / Slack
 You ── CLI (aiakos) ────►│                                                                     │
                          │  Akka.NET: RigActor → SeatActor (one per seat, state machine)       │
                          │            NodeProxyActor (one per node) · Health/Watchdog actors   │
                          │  Postgres (DbUp + Dapper): rigs, seats, queue, transitions, chat,   │
                          │            audit — tenant_id everywhere                             │
                          └───────────────▲─────────────────────────────────────────────────────┘
                                          │ gRPC bidirectional stream (node dials out; token)
                          ┌───────────────┴──────── Node agent ("Ergates") ─────────────────────┐
                          │  ISessionHost: tmux (later herdr/tuios)   ISandbox: none/Docker/brig │
                          │  ISeatProbe: reads seat homes on host     Hook/event ingest          │
                          └───────────────┬─────────────────────────────────────────────────────┘
                                          │ pane runs the harness (optionally via sandbox exec)
                                  ┌───────┴──────── seat ("Myrmidon") ──────────┐
                                  │ opencode / claude / codex                   │
                                  │ hooks/events → node agent                   │
                                  │ per-seat home, checkout per policy          │
                                  └─────────────────────────────────────────────┘
```

### Responsibilities
- **Orchestrator** owns meaning: rigs, seats, identity, queue, routing, policy, humans, history.
  Never touches terminals or containers directly.
- **Node agent** owns the machine: sessions, sandboxes, files, probes, local event ingest.
  No business logic.
- **SeatActor** is the single place a seat's truth lives; merges hook events, heartbeats,
  sandbox events and pane liveness into the three state axes
  ([ADR 0031](adr/0031-three-axis-seat-state.md)). It is the only writer of seat state and
  commits each input's events and conclusions in one transaction
  ([ADR 0032](adr/0032-seat-actor-sole-writer.md)).

### Core interfaces
```csharp
interface IHarnessAdapter  { Project(...); BuildLaunch(fresh|resume|fork); InterpretEvent(...); }   // orchestrator
interface IHarnessDriver   { readiness; delivery + confirmation; resume verification; NormalizeEvent (raw payload forwarded); }   // node
interface ISeatChannel     { SendAsync; Events; AnswerPermissionAsync; }   // API-driven or terminal-driven
interface ISessionHost     { StartAsync; DeliverAsync (lead + paste + submit + confirm); SendKeysAsync; CaptureAsync; GetStatusAsync; StopAsync; ListAsync/AdoptAsync; WatchAsync; }   // node
interface ISandbox         { EnsureAsync; Wrap(cmd); Paths (PathMap); OrchestratorUrlFromInside; FileSystem; }
interface ISeatProbe       { GetNativeSessionIdAsync; GetHarnessProcessAsync; }
interface IChatConnector   { inbound messages; SendAsync; buttons/approvals; }
```
Each harness has two halves ([ADR 0018](adr/0018-harness-adapter-split.md)): the orchestrator's
`IHarnessAdapter` builds launches and interprets events into state; the node's `IHarnessDriver`
handles readiness, delivery, confirmation and resume verification, and normalizes events while
always forwarding the raw payload. The session host never sends input on its own; delivery is
one composite operation ([ADR 0023](adr/0023-no-self-initiated-input.md)).
A seat = **harness × session host × sandbox** (× node).

### Two harness styles
| Style | Harnesses | Send | Monitor |
|---|---|---|---|
| **API-driven** | OpenCode (`opencode serve` + attached TUI for humans); later Claude Agent SDK, Codex app-server | HTTP | Event stream (SSE) |
| **Terminal-driven** | Claude Code, Codex | Paste into tmux | Hooks + statusLine |

### Where actors are used (and not)
- Actors for live things: seats, sessions, nodes, detectors (ordered mailbox, supervision, timers).
- Relational DB for queryable records (queue, chat, audit, specs). Actors cache live state only,
  so Akka.Cluster sharding by tenant is a later scaling step, not a rewrite.

---

## 4. Technology decisions

| Area | Decision |
|---|---|
| Runtime | .NET 10, C# |
| Local dev host | **Aspire** AppHost (Postgres, orchestrator, WSL node via `wsl.exe`), Aspire dashboard for OpenTelemetry |
| Concurrency | Akka.NET + Akka.Hosting (live entities only) |
| Database | **Postgres** from day one (row-level security, `SKIP LOCKED`, `LISTEN/NOTIFY`) |
| Data access | **DbUp** (embedded `.sql` migrations) + **Dapper** (thin repositories) |
| Orchestrator ↔ node | gRPC bidirectional stream, node dials out; Tailscale + node token |
| Session host | tmux ≥ 3.4 first (WSL/Linux), a private server per instance ([ADR 0022](adr/0022-private-tmux-server-per-instance.md), [ADR 0026](adr/0026-minimum-tmux-version.md)); herdr/tuios later (native Windows via ConPTY) |
| Sandbox | Docker/nerdctl first; brig later |
| Harnesses | Claude Code (M1, needed to self-host) → OpenCode (M2) → Codex |
| Agent-facing API | MCP server (official C# SDK, HTTP transport) + `aiakos` CLI in seat images |
| Hook relay | POSIX `sh` script (curl + flock) to the node's loopback ingest, always exits 0, per-seat `source_seq`; native-AOT binary deferred to M6 ([ADR 0028](adr/0028-hook-transport.md)) |
| Spec format | YAML (YamlDotNet) + JSON Schema for editor validation |
| CLI | System.CommandLine |
| UI | Blazor (interactive server) + SignalR |
| Chat | Telegram (Telegram.Bot, long polling) → Slack (SlackNet, Socket Mode) |
| Observability | OpenTelemetry + Serilog; Aspire dashboard locally, standalone dashboard/Grafana on servers |
| Testing | xUnit + Akka.TestKit; integration tests against real tmux/Docker on Linux CI |
| Deployment | `aspire publish` → Docker Compose on the Linux box |

### Principles
1. Identity from environment/per-seat token, never from request bodies.
2. Honest state: `unknown` is valid; no silent fallbacks.
3. Queue never loses work: closure reason + target, atomic handoff.
4. Terminals are transport; the database is the record.
5. Every host-specific concern behind an interface.

---

## 5. Rig definition files (shareable, OpenRig-style)

Layers:
1. **Rig spec** (`rig.yaml`): seats (`agent_ref`, `harness`, `model`, `profile`, `checkout`,
   `requires`), edges, `culture_file`, `workspace` (repos), channels. Seat IDs are flat and
   unique within the rig; a seat's address is `seat@rig`. Pods (M2) are an optional grouping
   attribute, not part of the address ([ADR 0014](adr/0014-flat-seat-addresses.md)).
2. **Agent spec** (`agent.yaml` + folder): `imports` (`local:` / `path:`, later `git:…@ref`),
   `resources` (skills, guidance, subagents, hooks, harness-tagged runtime fragments),
   `profiles` (`uses:` selections, `namespace:id` refs), `startup` (files + delivery hints:
   `guidance_merge`, `skill_install`, `send_text`).
3. **Shared libraries**: resource-only agent specs imported by many agents.
4. **Culture file**: prose on how the team works.
5. **Bundles**: deterministic archive (manifest, vendored agents, rewritten refs, hashes, `.sha256`).

Harness-neutral agents: guidance → `AGENTS.md` (OpenCode, Codex) / `CLAUDE.md` (Claude);
skills in the common `SKILL.md` folder format; runtime fragments tagged per harness.

**Shareable vs local binding** — machine, secret and tenant specifics never go in shared files:

| Shared (`rig.yaml`, agents, culture) | Local binding (`rig.env.yaml` or `up` args) |
|---|---|
| Seats, roles, edges, harness, model class | Which node each seat runs on |
| Requirements: `sandbox: required`, `auth: api-key \| subscription` | Actual node / sandbox |
| Repos by name + URL, checkout policy | Local paths, worktree root |
| Secret **names** | Secret sources: node-local `file:` references in v1 (`store:`/`env:` sources may be added later, still resolved on the node; [ADR 0016](adr/0016-secrets-as-node-file-references.md)) |
| Channel roles ("lead receives inbound") | Slack channel / Telegram chat IDs |
| Egress needs | Node proxy/firewall |

```yaml
# rig.yaml (shared)
apiVersion: aiakos.dev/v1
kind: Rig
name: product-team
culture_file: CULTURE.md
workspace:
  repos:
    - { name: api, url: git@github.com:acme/api.git }
    - { name: web, url: git@github.com:acme/web.git }
seats:                           # flat, rig-unique IDs: lead@product-team, impl@product-team
  - id: lead
    pod: dev                     # optional grouping (M2), not part of the address
    agent_ref: local:agents/lead
    harness: opencode
    checkout: shared
  - id: impl
    pod: dev
    agent_ref: local:agents/implementer
    harness: claude-code
    checkout: seat-worktree
    requires: { sandbox: required, auth: api-key, secrets: [anthropic_api_key] }
edges:
  - { kind: delegates_to, from: lead, to: impl }
channels:
  inbound: lead
```

```yaml
# rig.env.yaml (local, never shared)
apiVersion: aiakos.dev/v1
kind: RigEnv
nodes:
  lead: wsl-local
  impl: linux-box
repos:
  api: { path: ~/src/api }
secrets:
  anthropic_api_key: { file: ~/.config/aiakos/secrets/anthropic_api_key }   # read on the node, delivered as a file
channels:
  telegram: { chat_id: -100123456 }
```

Tooling: `aiakos spec validate`, `aiakos up --plan` (checks requirements against bindings),
`aiakos bundle create|inspect|install`, `aiakos import openrig <dir>` (converter, not format
compatibility). The DB stores rig instances with a hash of the resolved spec.

### Repos and checkouts
A rig is a **dev team** with a workspace of several repos; queue items carry `target_repo`.
Checkout policy per seat:
- `shared` — seats share a checkout (OpenRig's default behavior)
- `shared-readonly` — reviewers
- `seat-worktree` — own worktree + branch per repo (required for sandboxed seats)
- `task-worktree` — worktree + branch per queue item (later, for API-driven workers)

Alternatives considered: rig-per-repo (simple, poor cross-repo), shared only (no isolation).

---

## 6. Node profiles and auth

- **`local` node** (WSL on the PC): no sandbox; harnesses use subscription logins; tmux.
  Aspire launches the node agent via `wsl.exe`. **WSL mirrored networking is a prerequisite**
  (`networkingMode=mirrored` in `%USERPROFILE%\.wslconfig`): OTLP, gRPC and hook traffic between
  WSL and Windows then use `127.0.0.1` without firewall rules. In NAT mode, Windows Firewall
  blocks WSL → Windows by default. Verified in [spike 0003](spikes/0003-aspire-wsl-node.md).
- **`sandboxed` node** (dedicated Linux box): Docker first, brig later; API keys delivered as
  files from a secret store; mandatory worktree checkout; egress control.
- Verify per provider whether subscription logins via third-party harnesses (e.g. OpenCode) are
  supported and permitted by the provider's terms.

---

## 7. Monitoring (incl. sandboxed seats)

Signal sources, most to least authoritative:
1. **Harness events** — OpenCode event stream; Claude/Codex hooks (`SessionStart`,
   `UserPromptSubmit`, `Pre/PostToolUse`, `Notification` → needs-input, `Stop` → idle,
   `PreCompact`, `SessionEnd`). Payloads carry the native session ID.
2. **Claude statusLine** — context window usage, model, cost.
3. **Seat/node heartbeat** — silence ⇒ `unknown`; process watch; buffered replay when offline.
4. **Sandbox events** — Docker Engine API (die/OOM/restart, stats) or brig/containerd status.
5. **Session host** — pane alive/exited, capture as evidence, herdr/tuios state as cross-check.

SeatActor merges them: hook events win (sequence numbers); heartbeat timeout ⇒ reported
`unknown` (an overlay over the last-known values, [ADR 0031](adr/0031-three-axis-seat-state.md));
sandbox death ⇒ `exited`; disagreement ⇒ health finding, never a guess. Pane text only explains
an outcome, it never triggers input ([ADR 0030](adr/0030-screen-classification-never-acts.md)).
An unexpected exit becomes a finding; a relaunch or fresh start always needs a recorded
decision, and automatic restarts wait for the M7 watchdogs
([ADR 0033](adr/0033-no-unrecorded-relaunch.md)).

Sandbox specifics: hook config projected into the per-seat home volume; hooks POST to the node
agent (`host.docker.internal` / bridge IP); per-seat token as a file
([ADR 0025](adr/0025-secrets-never-through-tmux.md)); egress allowlist; seat homes kept on
the host so the node agent can read session files directly.

### brig (later `ISandbox`)
microVM per agent (urunc/KVM on Linux), Apache 2.0, prerelease (`0.1.0-rc`, urunc from a
feature branch). Good: host-side guest homes, secret store with tmpfs files, `brigd` socket
(`ensure/status/stop`), `brig sh <ref> <cmd>` for panes, `--json`. Gaps: egress policy not
enforced on Linux (use nftables + allowlisting proxy), host reachability undocumented, one
process per guest (seat agent as wrapper if needed), mounted project is the real checkout
(use worktrees), brigd forgets inventory on restart.

---

## 8. Chat (Telegram, Slack)

Inbound: connector → **admission** (allowlist, fail-closed) → **routing** (thread/topic→seat
map, `@seat` mention, default to the lead; never guess) → queue item or SeatActor → deliver
when idle with a provenance header.

Outbound: MCP tools `reply`, `ask_human`, `notify`; optional turn-finished summaries;
**approval buttons** for permission prompts (OpenCode permission API; Claude/Codex via keys or
hook decisions); long output as files/links.

Security: chat text is untrusted input to agents with shell access — allowlist senders,
sandbox the seats, provenance labels, approvals for risky actions, rate limits, audit.

Telegram: long polling, forum topics per rig/seat. Slack: Socket Mode, threads per work item.
Netclaw (Akka.NET agent with Slack/Discord) is a possible future chat-facing lead, not a core
dependency.

---

## 9. Multitenancy seams (build now, activate later)
- `tenant_id` on every table from the first migration (single default tenant).
- `CallerContext` (tenant, user) on every API call.
- Tenant-scoped IDs (`tenant/rig/seat`).
- Postgres is the record; actors are caches.
- Nodes always dial out with node tokens (tenant-owned nodes later).

---

## 10. Roadmap (each milestone MVP-complete)

**Step 0 — Secure the name** (GitHub org, NuGet ID + prefix reservation, domain). See top.

Milestones map 1:1 to GitHub milestones in `aiakos-hq/aiakos`. Stage A (manual) covers M0–M1;
**M1 is the self-hosting threshold**; from M2 on, the work is done by Aiakos seats (see §11).

**M0 — Spike (throwaway).** Prove manually in WSL: Claude Code in tmux with paste delivery and
hooks → host endpoint (state + session ID); `--resume` after tmux/WSL restart; Aspire →
`wsl.exe` node with OTLP to the dashboard; OpenCode serve/API/events + TUI attach (for M2);
Docker seat with API key, hooks to host, resume after container restart (for M6).
*Done:* each works and is written down (spike notes in `docs/spikes/`).

**M1 — Self-hosting MVP (Claude Code).** Aspire AppHost + Postgres + DbUp/Dapper; orchestrator
+ WSL node agent over gRPC; **Claude Code terminal-driven seats** in tmux (paste delivery, hooks
→ state, session-ID capture, resume); SeatActor with the three state axes; identity via
environment; minimal `rig.yaml`/`agent.yaml` + `rig.env.yaml` with guidance/skills projection
into `CLAUDE.md`/`.claude/skills`; checkout policies `shared` + `seat-worktree`; CLI
`up/down/send/capture/ps`; packaged as a `dotnet tool` so the team can run a pinned release.
*Done:* the `aiakos-dev` rig (implementer + reviewer, you as lead) builds M2 in its worktrees,
running on the released M1 tool; full flow visible as traces in Aspire; `down`/`up` resumes.

**M2 — A team that coordinates + OpenCode.** Full spec model (imports, shared libraries,
profiles, delivery hints, culture); **OpenCode API-driven adapter**; Codex terminal adapter;
MCP server (`whoami`, `list_seats`, `send`); `spec validate`, `up --plan`, JSON Schema.
*Done:* an OpenCode implementer and a Claude reviewer collaborate across two repos via MCP.

**M3 — Work queue.** Items with closure contract and `target_repo`; transition log; atomic
handoff; humans as seats; MCP tools + CLI.
*Done:* task flows owner → checker → done with a closure reason; nothing can vanish.

**M4 — Chat.** Telegram then Slack: admission, routing, notifications, approval buttons.
*Done:* send a task, get the result and approve a push from the phone.

**M5 — Dashboard & health.** Blazor: seats (3 axes), context usage, queue, human gate, live
events; health findings; `aiakos terminal` views (herdr/tmux over ssh); bundles
(`create/inspect/install`) and `import openrig`.
*Done:* a day of work runs from the dashboard.

**M6 — Sandboxed node (enterprise profile).** Linux-box node agent; Docker `ISandbox`; secret
store + API keys as files; mandatory worktrees; egress (nftables + allowlisting proxy);
orchestrator deployable to the box via `aspire publish`.
*Done:* one rig with local seats on the PC and sandboxed seats on the box.

**M7 — Long-running health.** Refocus after compaction; handover at context limit with recap;
snapshot/restore across reboots; watchdogs.
*Done:* a rig runs for days, survives reboots, same addresses and history.

**M8 — Growth.** brig `ISandbox`; `task-worktree`; git-sourced agent libraries; multitenancy
activation (auth, RLS, tenant-owned nodes); native-Windows node via herdr/tuios; headless worker
seats; Akka.Remote nodes.

---

## 11. How we build it — Aiakos builds Aiakos

### Bootstrap stages
| Stage | Who works | Aiakos capability |
|---|---|---|
| **A — manual** (now → M1) | You + a single Claude Code session | none; the process (issues, specs, ADRs) already applies |
| **B — self-hosting threshold** (after M1) | `aiakos-dev` rig: implementer + reviewer Claude seats, you as lead | seats from `rig.yaml` in WSL tmux, send/capture/ps, hook state, identity, resume |
| **C — team coordinates itself** (after M2–M3) | seats hand off to each other | MCP server + queue with closure contract |
| **D — manage from anywhere** (after M4) | you, from the phone | Telegram approvals and notifications |

### The bootstrap rule
**The team never runs on the code it is changing.** Like a compiler bootstrapping, the
`aiakos-dev` rig runs on the **last released** tool (`dotnet tool install -g aiakos --version x.y`,
own `AIAKOS_HOME` and port), while seats develop the next version in their worktrees. A broken
change cannot take down the team that has to fix it. Releases are cut when a milestone's
acceptance passes; the team upgrades deliberately.

### Work management
| What | Where |
|---|---|
| **Tickets** (what/why, status, discussion) | GitHub Issues + a GitHub Project board; agents use `gh` |
| **Specs** (requirements, design, acceptance criteria, test plan) | `docs/specs/NNNN-title.md` in `aiakos`, linked from the issue, reviewed in the same PR as the code |
| **Decisions** | `docs/adr/NNNN-title.md` (Architecture Decision Records), e.g. DbUp+Dapper, Postgres day one |
| **Spike notes** | `docs/spikes/` |
| **Live execution record** (from M3) | Aiakos queue items linked to issues; later a GitHub connector syncs them |

### Conventions to set up in stage A
- `CLAUDE.md` (later `AGENTS.md`): build/test commands, architecture rules, the bootstrap rule.
- Issue templates (feature, bug, spike) and a spec template (context, requirements, acceptance
  criteria, test plan, out of scope); ADR template.
- Labels: `spec-needed`, `ready`, `in-progress`, `needs-review`, `blocked`, `area/*`.
- Milestones M0–M8 in GitHub.
- Definition of done: tests pass; spec and docs updated; reviewer seat checked the exact diff;
  you approve the merge. Branch protection on `main`; all work via PRs.
- The `aiakos-dev` rig definition lives in `aiakos/rigs/aiakos-dev/` (dogfooding the file model).

---

## 12. Open items / risks
- OpenCode: exact serve/attach commands, event schema, supported subscription logins and terms.
- herdr license: its README says Apache 2.0, OpenRig's docs describe it as AGPL — verify.
- Docker Sandboxes on Windows vs plain Docker: custom images/entrypoints, egress to host.
- brig maturity (urunc feature branch); Linux egress enforcement is DIY.
- Line endings / path mapping when Linux agents edit Windows-checked-out repos.
- Chat-driven prompt injection — keep sandbox + approvals as the safety net.

## 13. References
- OpenRig: https://github.com/mvschwarz/openrig (as-built docs under `docs/as-built/`)
- herdr: https://github.com/ogulcancelik/herdr · herdr-win: https://github.com/hdosys/herdr-win
- tuios: https://github.com/Gaurav-Gosain/tuios
- brig: https://github.com/brig-sh/brig
- Netclaw: https://github.com/netclaw-dev/netclaw
