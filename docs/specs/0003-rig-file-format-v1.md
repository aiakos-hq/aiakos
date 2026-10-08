---
id: 0003
title: Rig file format v1
status: accepted         # draft | accepted | implemented | superseded
issue: https://github.com/aiakos-hq/aiakos/issues/14
milestone: M1
owner: "@bsakel"
---

# 0003 — Rig file format v1

## Context

Issue [#14](https://github.com/aiakos-hq/aiakos/issues/14) asks for the minimal M1 subset of the
rig definition files: seats, harness, `agent_ref`, guidance and skills, checkout policy (`shared`,
`seat-worktree`) and the local binding (node, repo paths), with the resolved spec and its hash
stored in the database. Issue [#16](https://github.com/aiakos-hq/aiakos/issues/16) (the
`aiakos-dev` rig: implementer + reviewer, the maintainer as lead) is the acceptance case: it must
be expressible in this format, and M1 is done when that rig, running on the released M1 tool,
implements an M2 issue through a reviewed PR.

Relevant decisions and plan sections:

- [ADR 0007](../adr/0007-shareable-rig-files.md): shareable files state *requirements*, a
  separate local binding (`rig.env.yaml`) states *provision*. Machine, secret and tenant
  specifics never go into shared files.
- [ADR 0008](../adr/0008-bootstrap-rule.md): the `aiakos-dev` rig runs on the last released
  Aiakos. Its rig files live in this repository, which the seats are changing.
- [ADR 0006](../adr/0006-claude-code-first-harness.md): Claude Code is the only M1 harness;
  OpenCode (M2) is API-driven and must fit the same model.
- [Plan §5](../plan.md#5-rig-definition-files-shareable-openrig-style) (layers, shared vs local
  table, checkout policies), [§6](../plan.md#6-node-profiles-and-auth) (node profiles, auth),
  [§10](../plan.md#10-roadmap-each-milestone-mvp-complete) (M1 vs M2 split),
  [§11](../plan.md#11-how-we-build-it--aiakos-builds-aiakos) (bootstrap rule).
- CLAUDE.md rules 2 (identity from the environment), 3 (honest state, no silent fallbacks),
  5 (host concerns behind interfaces) and 7 (LF line endings).

Spike findings the format must be able to express (or must deliberately leave to Aiakos):

- [Spike 0001](../spikes/0001-claude-wsl-tmux-hooks.md): hooks and the statusLine are the state
  signal. They are **projected by Aiakos** into a per-seat `--settings` file; users never write them.
- [Spike 0002](../spikes/0002-claude-session-resume.md): the orchestrator owns the session ID and
  seat name (`-n <seat>`); seat workspaces live under one **trusted seat root** (F8); resume must
  happen in the same workspace directory (F6); hooks and settings are passed per seat with
  `--settings`; the model is a launch argument.
- [Spike 0004](../spikes/0004-opencode-api.md): OpenCode needs per-seat config (`XDG_*`,
  `opencode.json`), a pinned permission ruleset (defaults are permissive), and
  `OPENCODE_DISABLE_CLAUDE_CODE=1` with explicitly projected skills. So agent definitions must be
  harness-neutral, with harness-specific fragments kept apart.
- [Spike 0005](../spikes/0005-docker-seat.md): a sandboxed seat's API key is delivered as a
  **file** (tmpfs, `apiKeyHelper`), never as an environment variable, argv or inline value.

Related specs written in parallel (referenced, not duplicated):

- Spec 0001 (issue [#9](https://github.com/aiakos-hq/aiakos/issues/9)) owns the solution
  layout, including the project that hosts the loader described here.
- Spec 0002 (issue [#10](https://github.com/aiakos-hq/aiakos/issues/10)) owns the
  orchestrator ↔ node wire contract. The *resolved seat parameters* defined in this spec are the
  input to it; this spec does not define protobuf messages.
- Issue [#12](https://github.com/aiakos-hq/aiakos/issues/12) (Claude Code adapter) owns the
  mechanics of launch, resume, hooks and statusLine. This spec defines *what* is projected from
  the files and *where*; #12 turns it into a command line and files on the node.
- Issue [#15](https://github.com/aiakos-hq/aiakos/issues/15) (CLI) owns `up/down/...` behaviour,
  including how diagnostics are printed and how a changed spec is applied to a running rig.

## Goals

- A small, strict, versioned file format (`rig.yaml`, `agent.yaml`, `rig.env.yaml`) that expresses
  the `aiakos-dev` rig completely.
- A loader that turns the files into one **resolved rig** (self-contained, content-addressed,
  hashed) plus one **resolved seat launch parameter set** per agent seat, with every error reported
  at once, with file, line, column, code and a fix hint.
- Guidance, culture and skills projected into Claude Code's native files without touching the
  repository checkout the seat works in.
- A clear line between shareable content and local binding: no machine paths, node names or
  secret values in shared files, and no secret values in any file.
- A v1 that the M2 extensions (imports, libraries, profiles, delivery hints, OpenCode, JSON
  Schema, `spec validate`, `up --plan`) can add to without breaking v1 files.

## Non-goals / out of scope

Deferred to M2 (v1 must not block them; see [Forward compatibility](#forward-compatibility-and-m2-extensions)):

- `imports`, shared (resource-only) agent libraries, `agent_ref` schemes other than `local:`.
- `profiles` and `uses:` selections, `namespace:id` resource references.
- `startup` delivery hints (`guidance_merge`, `skill_install`, `send_text`).
- Pods, edges (`delegates_to`, …) and rig-level routing.
- OpenCode and Codex harness sections (the keys are reserved; values are rejected in v1).
- JSON Schema publication, `aiakos spec validate`, `aiakos up --plan`.
- Subagents, harness hooks written by users, runtime fragments beyond the permission subset below.

Deferred further:

- Checkout policies `shared-readonly` (see [D2](#decisions-resolved-in-review)) and `task-worktree`
  (M8). The names are reserved.
- `channels` (M4), secret stores (`store:` source, M6), egress declarations (M6), bundles (M5),
  `aiakos import openrig` (M5).

Owned elsewhere:

- The protobuf shape of seat parameters (spec 0002), database tables (orchestrator specs; this
  spec only states *what* is stored), launch command lines, hook scripts and settings merging
  (#12), tmux session handling (#11), CLI output formatting and reconciliation (#15), where the
  CLI finds the orchestrator (`AIAKOS_HOME`, port; ADR 0008), node registration and node tokens.

## Requirements

### Files and envelope

- **R1** A rig is a directory (the *rig root*) containing `rig.yaml`. Agent definitions are
  directories containing `agent.yaml`, referenced from `rig.yaml`. The local binding is a
  `rig.env.yaml` file, by default at `<rig root>/rig.env.yaml`, overridable by the CLI (#15).
- **R2** Every file starts with `apiVersion: aiakos.dev/v1` and a `kind` of `Rig`, `Agent` or
  `RigEnv` respectively. A missing or unknown `apiVersion`, or a `kind` that does not match the
  file's role, is an error naming the versions this tool supports
  ([ADR 0013](../adr/0013-rig-file-header-and-compatibility.md)).
- **R3** Files are UTF-8 YAML 1.2, a single document each, at most 256 KiB. A UTF-8 BOM is
  accepted and ignored. Duplicate keys, anchors/aliases, merge keys (`<<`) and custom tags are
  errors.
- **R4** Within `aiakos.dev/v1`, unknown fields are errors (with a "did you mean" hint when the
  edit distance is ≤ 2). Fields whose name starts with `x-` are ignored at any level, so users can
  annotate files. Reserved field names (listed in
  [Reserved names](#reserved-fields-and-values)) produce a dedicated error saying which milestone
  introduces them.
- **R5** Field names are `snake_case`, except the envelope keys `apiVersion` and `kind`.

### Rig (`rig.yaml`)

- **R6** `rig.yaml` declares the rig `name`, optional `description`, optional `culture_file`, a
  `workspace.repos` list and a `seats` list, with the schema in [Design](#rigyaml).
- **R7** Seat IDs are unique within the rig. A seat's address is `<seat id>@<rig name>`. Seats
  exist only inside a `rig.yaml`: there is no way to reference, include or share a seat defined
  in another rig, so every seat is owned by exactly one rig. v1 has no pods; seat addresses stay
  flat ([ADR 0014](../adr/0014-flat-seat-addresses.md)).
- **R8** A seat is `kind: agent` (default) or `kind: human`. A human seat has only `id`, `kind`
  and `description`; any other field on it is an error. Human seats are recorded and listed but
  never launched in M1.
- **R9** An agent seat names its agent with `agent_ref: local:<path>`, where `<path>` is a
  directory relative to the rig root that contains `agent.yaml`.
- **R10** An agent seat resolves a `harness` and optionally a `model` (seat value, else the
  agent's `defaults`, see [Resolution](#resolution-and-defaults)). A seat with no resolved harness
  is an error. In v1 the only accepted harness is `claude-code`; `opencode` and `codex` are
  recognised and rejected with "not supported by this version (planned M2)".
- **R11** An agent seat has a `checkout` policy: `shared` or `seat-worktree` (default
  `seat-worktree`). `shared-readonly` and `task-worktree` are recognised and rejected with the
  milestone that introduces them.
- **R12** An agent seat lists the `repos` it works on (names from `workspace.repos`; default:
  all) and optionally `workdir_repo`, the repo whose checkout is the seat's working directory
  (default: the first of the seat's repos). A seat with zero repos is an error.
- **R13** An agent seat may state `requires`: `sandbox` (`optional` default, or `required`),
  `auth` (`subscription` default, or `api-key`) and `secrets` (a list of secret **names**).
  `auth: api-key` requires the harness's key secret to be named (`anthropic_api_key` for
  `claude-code`).

### Agent (`agent.yaml`)

- **R14** `agent.yaml` declares `name`, `description`, optional `defaults` (`harness`, `model`),
  `guidance` (ordered list of Markdown files), `skills` (list of skill directories) and an
  optional `harnesses` map of harness-specific fragments.
- **R15** Guidance and skills are harness-neutral. A skill directory contains a `SKILL.md` with
  YAML front matter holding at least `name` (equal to the directory name) and `description`.
  Skill names are unique per seat.
- **R16** The only harness fragment accepted in v1 is `harnesses.claude-code` with
  `permission_mode` (`default`, `acceptEdits`, `plan`, `auto`; default `default`) and
  `permissions` (`allow`, `ask`, `deny`: lists of Claude Code permission rule strings).
  `bypassPermissions` is rejected (it needs a sandbox; M6).
- **R17** No file can set anything Aiakos owns: hooks, statusLine, `apiKeyHelper`, session IDs,
  seat names, environment variables, identity or tokens. Attempts are errors that say the setting
  is owned by Aiakos (rule 2; spike 0001).

### Local binding (`rig.env.yaml`)

- **R18** `rig.env.yaml` declares the `rig` it binds (must equal `rig.yaml` `name`), the
  `seat_root` on the node, node placement (`placement.default_node` and per-seat overrides), a
  node-local `path` for each repo, and `secrets` sources.
- **R19** Every agent seat resolves to exactly one node. Every repo used by an agent seat has a
  binding. Every secret named by a seat's `requires.secrets` has a source. Bindings for unknown
  seats, repos or secrets are errors (they are usually typos). Human seats take no placement.
- **R20** Node paths (`seat_root`, repo `path`, secret `file`) are absolute POSIX paths or start
  with `~/`. They are interpreted on the node, never on the machine running the CLI. Windows
  paths (`C:\…`, `\\…`) and `$` expansions are errors; paths under `/mnt/<drive>/` are warnings
  (slow, and file watching is unreliable on the Windows filesystem from WSL).
- **R21** Secret sources in v1 are `file:` only (a node-local path). A `value:` (or any inline
  secret) is an error. The secret value is read on the node at launch and delivered to the seat as
  a file; it never passes through the CLI, the orchestrator, the database, logs, argv or
  environment variables ([ADR 0016](../adr/0016-secrets-as-node-file-references.md)).
- **R22** Shared files (`rig.yaml`, `agent.yaml`, guidance, culture, skills) must contain no
  node names, node paths or secret values. The loader rejects strings that match known credential
  patterns (for example `sk-ant-`, `ghp_`, `github_pat_`, `xox[bp]-`, PEM private-key headers) and
  URLs with embedded user info in any file, including `rig.env.yaml`.

### Resolution, hashing, projection

- **R23** Loading reports **all** diagnostics from all files in one pass, each as
  `<file>:<line>:<col>: <severity> AIK<code>: <message>` plus an optional `hint:` line. Any
  error prevents resolution. Warnings do not.
- **R24** File references in shared files (`culture_file`, `guidance`, `skills`, `agent_ref`) are
  relative, use `/`, and must resolve inside the rig root after normalisation. `..` escapes,
  absolute paths and symbolic links are errors.
- **R25** The loader embeds the content of every referenced file in the resolved rig (guidance and
  culture normalised to LF without BOM; skill files byte-exact) with its SHA-256. Nothing
  downstream reads rig files from disk again. A seat's projected content is at most 2 MiB
  (AIK3005; D9). See [ADR 0017](../adr/0017-resolved-rig-and-hashes.md).
- **R26** The resolved rig has a `spec_hash` (SHA-256 of the canonical form of the shared part)
  and a `binding_hash` (SHA-256 of the canonical form of the binding). The same shared files give
  the same `spec_hash` on Windows and Linux, independent of YAML key order, comments, `x-` fields
  and CRLF/LF in guidance and culture. Both hashes, the tool version and the resolved rig are
  what the orchestrator stores for a rig instance.
- **R27** For each agent seat the loader produces a **resolved seat parameter set** (see
  [Resolved seat parameters](#resolved-seat-parameters)) containing everything the node and the
  harness adapter need, and no identity token or secret value.
- **R28** For `claude-code`, guidance and skills are projected into a per-seat **projection root**
  outside every repository checkout: `<projection root>/CLAUDE.md` and
  `<projection root>/.claude/skills/<name>/…`. Projection never writes, modifies or deletes
  files inside a repository checkout, so the repo's own `CLAUDE.md` and `.claude/` stay intact
  and the worktree stays clean ([ADR 0015](../adr/0015-projection-outside-checkouts.md)). The
  adapter loads it with `--add-dir <projection root>` plus
  `CLAUDE_CODE_ADDITIONAL_DIRECTORIES_CLAUDE_MD=1` for `CLAUDE.md`; skills need only `--add-dir`.
  A `.claude/settings.json` inside the projection root is not read, so settings go only in the
  per-seat `--settings` file (verified by spec 0005;
  [ADR 0027](../adr/0027-claude-projection-and-settings.md), D3).
- **R29** The projected `CLAUDE.md` is generated deterministically: a generated header (seat
  address, rig, `spec_hash`, "do not edit"), the rig roster, the culture file, then the agent's
  guidance files in listed order, each preceded by a source comment. Same inputs give
  byte-identical output.
- **R30** Paths derived on the node are fixed by this spec: seat directory
  `<seat_root>/<rig>/<seat>`, projection root `<seat dir>/projection`, worktrees
  `<seat dir>/repos/<repo>`, seat branch `aiakos/<rig>/<seat>`. Claude Code inherits trust from
  a trusted ancestor only up to a git repository root, so a trusted `seat_root` does not cover
  the worktrees beneath it (spec 0005's experiment). The node sets trust for each seat's exact
  workdir before launch ([ADR 0029](../adr/0029-minimal-harness-user-config.md)) and reports a
  failure to do so instead of letting the trust dialog block.

### Compatibility and bootstrap

- **R31** v1 is additive-only: later minor releases may add optional fields and values to
  `aiakos.dev/v1` but never change the meaning of existing ones. A breaking change needs
  `aiakos.dev/v2`, and the tool keeps reading v1.
- **R32** Nothing in the files selects which Aiakos build runs the rig. Changes to
  `rigs/aiakos-dev/` must stay loadable by the currently pinned release (ADR 0008); the PR
  that uses a new field is the PR after the release that introduced it. Once CI exists, a CI job
  loads `rigs/aiakos-dev/` with the pinned release (D5;
  [ADR 0013](../adr/0013-rig-file-header-and-compatibility.md)).

## Design

### File layout

```text
rigs/<rig>/                       # rig root (shared, committed)
  rig.yaml                        # kind: Rig
  CULTURE.md                      # optional, referenced by culture_file
  agents/
    <agent>/                      # referenced as agent_ref: local:agents/<agent>
      agent.yaml                  # kind: Agent
      GUIDANCE.md                 # any file names; listed in agent.yaml guidance
      skills/
        <skill>/SKILL.md          # + any supporting files
  rig.env.yaml                    # kind: RigEnv (local, NOT committed; .gitignore'd)
```

The loader lives in `src/Aiakos.Spec` (YamlDotNet per plan §4). Public surface, for tests and
the CLI:

```csharp
LoadResult RigLoader.Load(string rigRoot, string? envPath);   // envPath null → <rigRoot>/rig.env.yaml
record LoadResult(ResolvedRig? Rig, IReadOnlyList<Diagnostic> Diagnostics);
record Diagnostic(Severity Severity, string Code, string File, int Line, int Column, string Message, string? Hint);
enum Severity { Error, Warning }
string DiagnosticFormatter.Format(IEnumerable<Diagnostic> diagnostics);   // the R23 text form, LF
```

`Diagnostic.File` is relative to the rig root with `/` separators (`rig.yaml`,
`agents/impl/agent.yaml`); an env file outside the rig root is shown as passed. The CLI prefixes
the rig root when it prints (#15).

`Load` is pure apart from reading the rig root and the env file: it never touches the node, git,
the network or the database. Node-side checks (paths exist, trust, git state) happen at launch
(#11, #12) and are reported as seat state, not as load diagnostics.

### Identifiers

| Identifier | Pattern | Notes |
|---|---|---|
| rig `name` | `^[a-z][a-z0-9-]{1,39}$` | Unique per orchestrator instance and tenant; appears in seat addresses, paths and branch names |
| seat `id` | `^[a-z][a-z0-9-]{0,23}$` | Used in tmux session names, paths, branch names, `-n` |
| repo `name` | `^[a-z][a-z0-9._-]{0,63}$` | |
| agent `name` | `^[a-z][a-z0-9-]{0,39}$` | Informational; the `agent_ref` path is the reference |
| skill `name` | `^[a-z0-9][a-z0-9-]{0,63}$` | Claude Code skill naming; must equal the directory name |
| secret name | `^[a-z][a-z0-9_]{0,63}$` | |
| node name | `^[a-z0-9][a-z0-9.-]{0,62}$` | Only in `rig.env.yaml`; nodes register under this name (spec 0002) |

Reserved seat IDs: `all`, `aiakos`, `system`, `orchestrator`, `node`, `human`.

### `rig.yaml`

| Field | Type | Req. | Default | Meaning |
|---|---|---|---|---|
| `apiVersion` | string | yes | — | `aiakos.dev/v1` |
| `kind` | string | yes | — | `Rig` |
| `name` | id | yes | — | Rig name |
| `description` | string | no | `""` | One line, shown in `ps` and the roster |
| `culture_file` | rel. path | no | none | Markdown projected into every agent seat |
| `workspace.repos` | list | yes (≥1) | — | Repos of the team |
| `workspace.repos[].name` | id | yes | — | |
| `workspace.repos[].url` | string | yes | — | `https://…` or `git@host:org/repo.git`; no user info/credentials |
| `workspace.repos[].default_branch` | string | no | `main` | Base for seat worktrees (`origin/<default_branch>`) |
| `seats` | list | yes (≥1 agent seat) | — | Ordered as written; order is used only for the roster |
| `seats[].id` | id | yes | — | |
| `seats[].kind` | `agent` \| `human` | no | `agent` | |
| `seats[].description` | string | no | agent's `description` | |
| `seats[].agent_ref` | `local:<rel. path>` | agent: yes | — | Directory containing `agent.yaml` |
| `seats[].harness` | enum | no | agent `defaults.harness` | v1: `claude-code` |
| `seats[].model` | string | no | agent `defaults.model`, else harness default | Passed verbatim to the harness (`opus`, `sonnet`, a full model ID) |
| `seats[].checkout` | enum | no | `seat-worktree` | `shared` \| `seat-worktree` |
| `seats[].repos` | list of repo names | no | all repos | |
| `seats[].workdir_repo` | repo name | no | first of `repos` | Must be in `repos` |
| `seats[].requires.sandbox` | `optional` \| `required` | no | `optional` | |
| `seats[].requires.auth` | `subscription` \| `api-key` | no | `subscription` | |
| `seats[].requires.secrets` | list of secret names | no | `[]` | Names only |

Semantics of the two M1 checkout policies:

- **`shared`**: the seat's working directory is the bound repo path itself
  (`repos.<name>.path` in `rig.env.yaml`). Several seats may share it. Aiakos creates nothing in
  it and assumes nothing about its branch. The node refuses to launch if the path is not an
  existing git work tree.
- **`seat-worktree`**: the node creates (once) a git worktree of the bound repo at
  `<seat dir>/repos/<repo>` on branch `aiakos/<rig>/<seat>`, starting from
  `origin/<default_branch>`. On every later `up` the existing worktree is reused as it is, never
  reset, cleaned or re-branched (work in progress and resume in the same directory, spike 0002 F6).
  If the path exists but is not a worktree of the bound repo, the launch fails with a clear
  reason; Aiakos never overwrites it. `down` never removes worktrees. The seat may create and
  switch feature branches inside its worktree; `aiakos/<rig>/<seat>` is only its parking branch.
- In M1 Aiakos does not clone: the bound repo path must already be a clone. If its `origin` URL
  differs from `url`, the node reports a warning (not an error; mirrors and forks are legitimate).

### `agent.yaml`

| Field | Type | Req. | Default | Meaning |
|---|---|---|---|---|
| `apiVersion` / `kind` | | yes | — | `aiakos.dev/v1` / `Agent` |
| `name` | id | yes | — | |
| `description` | string | yes | — | One line |
| `defaults.harness` | enum | no | none | Used when the seat sets none |
| `defaults.model` | string | no | none | Used when the seat sets none |
| `guidance` | list of rel. paths | no | `[]` | Markdown, projected in order; paths relative to the agent directory |
| `skills` | list of rel. paths | no | `[]` | Skill directories relative to the agent directory |
| `harnesses.claude-code.permission_mode` | enum | no | `default` | `default` \| `acceptEdits` \| `plan` \| `auto` |
| `harnesses.claude-code.permissions.allow` | list of rules | no | `[]` | Claude Code rule strings, e.g. `Bash(dotnet test:*)`, `Edit` |
| `harnesses.claude-code.permissions.ask` | list of rules | no | `[]` | |
| `harnesses.claude-code.permissions.deny` | list of rules | no | `[]` | |

Permission rules are validated for shape only (`Tool` or `Tool(specifier)`, `Tool` matching
`^[A-Za-z][A-Za-z0-9_]*$` or an `mcp__…` name, balanced parentheses). Their meaning is Claude
Code's. A `harnesses` section for a harness the seat does not use is ignored (it is not an error,
so one agent can serve several harnesses from M2 on).

Agent paths (`guidance`, `skills`) are relative to the agent directory but must still stay
inside the rig root (R24).

Skill directories: at most 100 files and 1 MiB per skill; files are copied byte-exact with mode
0644 (a Windows checkout has no executable bits; skills invoke scripts through an interpreter).
Guidance and culture files: Markdown, at most 256 KiB each.

### `rig.env.yaml`

| Field | Type | Req. | Default | Meaning |
|---|---|---|---|---|
| `apiVersion` / `kind` | | yes | — | `aiakos.dev/v1` / `RigEnv` |
| `rig` | id | yes | — | Must equal `rig.yaml` `name` |
| `seat_root` | node path | no | `~/aiakos/seats` | Root for seat directories; the node trusts each seat workdir (R30) |
| `placement.default_node` | node name | no | none | Node for seats without an override |
| `placement.seats.<seat id>.node` | node name | no | `default_node` | Per-seat node |
| `repos.<repo name>.path` | node path | yes for every used repo | — | Existing clone on the node(s) running seats that use it |
| `secrets.<secret name>.file` | node path | yes for every required secret | — | File holding the value, readable only by the node user |

All nodes a rig uses must see the same `seat_root` and repo paths in M1 (there is one node, the
WSL `local` node). Per-node path overrides are an M6 extension (reserved key `nodes`).

Discovery: `<rig root>/rig.env.yaml` unless the CLI passes another path (#15). The CLI (not the
loader, which stays free of git access) emits warning AIK5010 when the env file is tracked in a
git work tree, because the file is meant to stay local.

### Resolution and defaults

For each agent seat, in this order:

1. `agent` = load `agent.yaml` at `agent_ref`. The same agent directory may back several seats;
   it is loaded and hashed once.
2. `harness` = seat `harness` ?? agent `defaults.harness` ?? error AIK4001.
3. `model` = seat `model` ?? agent `defaults.model` ?? `null`. `null` means "the harness's
   own default"; it is recorded as `null` (not guessed), so `ps` shows `harness default`.
4. `description` = seat `description` ?? agent `description`.
5. `checkout`, `repos`, `workdir_repo`, `requires.*` from the seat with the defaults above.
6. `requires.auth: api-key` with `harness: claude-code` adds `anthropic_api_key` to the
   required secrets if missing (warning AIK4012, so the file states it explicitly next time).
7. `harness_settings` = the agent's `harnesses.<harness>` section with defaults applied.
8. Placement: `node` = `placement.seats.<id>.node` ?? `placement.default_node` ?? error AIK5003.
9. Paths: `seat_dir`, `projection_root`, per repo `checkout path` and `branch` (R30).
10. `requirements check` (minimal M1 form of M2's `up --plan`): `sandbox: required` fails at
    `up` time when the chosen node is not a sandboxed node (node profile comes from node
    registration, spec 0002). M1 has only `local` nodes, so such a rig does not start, with a
    clear reason. This check is in the orchestrator, not the loader, because the loader does not
    know nodes.

Rig-level checks: at least one agent seat; unique seat IDs; unique repo names; every seat's repos
exist; `workdir_repo` ∈ seat `repos`; env `rig` matches; no bindings for unknown seats, repos or
secrets (AIK5005–5007).

### Resolved rig and canonical form

The resolved rig is a tree of plain values (the loader's output record, serialisable as JSON).
Its **canonical form** is JSON with: keys sorted by ordinal comparison; no insignificant
whitespace; strings NFC-normalised; seats sorted by `id`; lists whose order carries meaning
(guidance, permission rules) kept in order; lists without meaning (`repos`, `requires.secrets`,
skills) sorted; file content replaced by `{ "path": "<rig-relative, />", "sha256": "…", "bytes": n }`
with contents stored beside it keyed by hash. `x-` fields, comments and YAML formatting are not
part of it.

- `spec_hash` = `sha256:` + hex SHA-256 of the canonical form of the shared part (rig, agents,
  embedded file hashes).
- `binding_hash` = the same over the resolved binding (placement, paths, secret *sources*).
- The orchestrator stores, per rig instance: rig name, `spec_hash`, `binding_hash`, loader/tool
  version, the canonical resolved rig and the file contents by hash (table design belongs to the
  orchestrator spec; `tenant_id` per rule 6). A later `up` with a different `spec_hash` is a spec
  change; how it is applied to running seats is #15's decision. Running seats are never
  hot-reloaded.

### Resolved seat parameters

One per agent seat. This is the input to the wire contract (spec 0002) and the harness adapter
(#12). Shown as YAML for readability:

```yaml
seat: impl                              # address = impl@aiakos-dev; identity token is NOT here
rig: aiakos-dev
spec_hash: sha256:…
binding_hash: sha256:…
node: wsl-local
harness: claude-code
model: opus                             # or null = harness default
auth: subscription                      # subscription | api-key
requires: { sandbox: optional }
seat_dir: ~/aiakos/seats/aiakos-dev/impl
workdir: ~/aiakos/seats/aiakos-dev/impl/repos/aiakos
checkouts:
  - repo: aiakos
    policy: seat-worktree
    url: https://github.com/aiakos-hq/aiakos.git
    source_path: ~/src/aiakos           # bound clone
    path: ~/aiakos/seats/aiakos-dev/impl/repos/aiakos
    branch: aiakos/aiakos-dev/impl
    base_ref: origin/main
projection:
  root: ~/aiakos/seats/aiakos-dev/impl/projection
  hash: sha256:…                        # over the file list below
  files:                                # content travels with the parameters (by hash)
    - { path: CLAUDE.md, sha256: …, bytes: 9120, mode: "0644" }
    - { path: .claude/skills/implement-issue/SKILL.md, sha256: …, bytes: 2210, mode: "0644" }
harness_settings:                       # the harness-specific fragment, defaults applied
  permission_mode: acceptEdits
  permissions: { allow: [ … ], ask: [], deny: [ … ] }
secrets:                                # references only; the node reads and delivers them
  - { name: anthropic_api_key, source: { file: ~/.config/aiakos/secrets/anthropic_api_key }, deliver_as: file }
```

Not in the parameters, on purpose: session ID (orchestrator-owned, spike 0002), seat token and
environment (orchestrator/node, rule 2), hooks and statusLine (adapter, spike 0001), tmux socket
and session name (#11), harness binary path (node configuration).

### Projection for Claude Code

Per seat, on the node, under the projection root (regenerated on every launch; the whole
directory is Aiakos-owned):

```text
<seat_dir>/projection/
  CLAUDE.md
  .claude/skills/<skill>/SKILL.md (+ supporting files)
```

Loading: the adapter adds the projection root with `--add-dir <projection root>` and sets
`CLAUDE_CODE_ADDITIONAL_DIRECTORIES_CLAUDE_MD=1` in the pane environment, so Claude Code loads
`CLAUDE.md` and `.claude/skills/` from it while the working directory stays the repo checkout
(whose own `CLAUDE.md` and `.claude/` still apply). This works identically for `shared` and
`seat-worktree`. Spec 0005's experiment verified it: `CLAUDE.md` needs `--add-dir` plus the
variable, skills need only `--add-dir`, and a `.claude/settings.json` in the projection root is
not read ([D3](#decisions-resolved-in-review)).

The permission fragment goes into the per-seat settings file that the adapter already passes with
`--settings` (spike 0002): `permissions.allow/ask/deny` and `permissions.defaultMode`
(= `permission_mode`). The adapter adds the hooks and statusLine to the same file, and for
`auth: api-key` an `apiKeyHelper` that reads the delivered key file (spike 0005). Because the
files cannot contain these keys (R17), there is no merge conflict to resolve: Aiakos-owned keys
are always the adapter's.

Generated `CLAUDE.md` layout (R29):

```markdown
<!-- Generated by Aiakos for impl@aiakos-dev (spec sha256:…). Do not edit: overwritten on every `aiakos up`. -->
# Aiakos seat impl@aiakos-dev

You are the seat `impl` in the rig `aiakos-dev`. Your identity comes from your environment;
never claim another seat's identity.

## Rig roster

| Seat | Kind | Description |
|---|---|---|
| lead | human | Maintainer. Owns priorities, approves specs, merges. |
| impl (you) | agent | Implements issues from specs and opens PRs. |
| review | agent | Reviews the exact diff against the spec and acceptance criteria. |

<!-- source: CULTURE.md sha256:… -->
…culture content, verbatim…

<!-- source: agents/implementer/GUIDANCE.md sha256:… -->
…guidance content, verbatim…
```

Content is inserted verbatim (headings are not rewritten), separated by one blank line, LF only,
ending with a single newline.

### Harness neutrality (M2 OpenCode, for reference only)

Nothing below is implemented in M1; it shows that v1 does not block it. For `opencode` the same
resolved seat maps to: guidance → `AGENTS.md` in a per-seat config directory, skills → the
seat's own skill directory with `OPENCODE_DISABLE_CLAUDE_CODE=1` (spike 0004 F12),
`harnesses.opencode.permission` (a ruleset of `{permission, pattern, action}`) → `opencode.json`,
`model` → `provider/model`. The harness-neutral fields (`guidance`, `skills`, `model`,
`defaults`) keep their meaning; only a new `harnesses.<name>` section is added.

### Secrets

- Shared files name secrets (`requires.secrets`); only `rig.env.yaml` says where the value is,
  and only as a node-local `file:` path. There is no inline form anywhere.
- The loader never reads secret files; it validates the reference shape only. The node reads the
  file at launch and delivers it as a file to the seat (for local seats: a path under the seat
  directory with mode 0600, or the source file itself; for sandboxed seats in M6: a tmpfs file
  written over stdin, spike 0005). The adapter points the harness at the file (`apiKeyHelper`).
- `binding_hash` covers the *reference*, not the value; rotating the key file does not change
  the hash.
- Credential-pattern scanning (R22) applies to every string value in every file and to guidance,
  culture and skill text. A match is error AIK4020 naming the file and line but never echoing the
  matched value.

### Diagnostics

Format (R23), one per line, all collected before failing:

```text
rigs/aiakos-dev/rig.yaml:21:5: error AIK2002: unknown field 'chekout' in seat 'impl'
  hint: did you mean 'checkout'?
rigs/aiakos-dev/rig.env.yaml:4:1: error AIK5003: seat 'review' has no node
  hint: set placement.default_node or placement.seats.review.node
```

| Code | Severity | Condition |
|---|---|---|
| AIK1001 | error | File not found / unreadable (`rig.yaml`, `agent.yaml`, env file) |
| AIK1002 | error | YAML syntax error (message from the parser, with position) |
| AIK1003 | error | Duplicate key, anchor/alias, merge key or custom tag |
| AIK1004 | error | File too large or not UTF-8 |
| AIK2001 | error | Missing or unsupported `apiVersion`; wrong `kind` for the file |
| AIK2002 | error | Unknown field (with did-you-mean hint) |
| AIK2003 | error | Missing required field |
| AIK2004 | error | Wrong type or invalid value (pattern, enum) |
| AIK2005 | error | Reserved field or value not supported in this version (names the milestone) |
| AIK3001 | error | Referenced file or directory missing |
| AIK3002 | error | Path escapes the rig root, is absolute, uses `\`, or is a symbolic link |
| AIK3003 | error | `agent_ref` scheme other than `local:` (names M2) |
| AIK3004 | error | Skill without `SKILL.md`, bad front matter, or `name` ≠ directory name |
| AIK3005 | error | Size limit exceeded (file, skill, projection) |
| AIK4001 | error | Agent seat has no harness |
| AIK4002 | error | Harness recognised but not supported in this version |
| AIK4003 | error | Duplicate seat ID, repo name, or skill name within a seat |
| AIK4004 | error | Seat references an unknown repo, or `workdir_repo` not in its repos |
| AIK4005 | error | Field not allowed on a human seat |
| AIK4006 | error | Rig has no agent seat |
| AIK4007 | error | Aiakos-owned setting in a file (hooks, statusLine, apiKeyHelper, env, identity) |
| AIK4008 | error | `permission_mode: bypassPermissions` |
| AIK4009 | error | Malformed permission rule |
| AIK4010 | error | Reserved seat ID |
| AIK4011 | error | Repo URL with credentials or unsupported form |
| AIK4012 | warning | `auth: api-key` without the harness key in `requires.secrets` (added) |
| AIK4020 | error | Credential-like value in a file (value not echoed) |
| AIK5001 | error | `rig.env.yaml` `rig` ≠ rig name |
| AIK5002 | error | Invalid node path (Windows path, relative, `$` expansion) |
| AIK5003 | error | Agent seat without a node |
| AIK5004 | error | Used repo without `path`; required secret without source |
| AIK5005 | error | Placement for an unknown or human seat |
| AIK5006 | error | Binding for an unknown repo |
| AIK5007 | error | Source for a secret no seat requires |
| AIK5008 | error | Secret source `value:` ("never inline secrets"). `store:` and `env:` are reserved: AIK2005 |
| AIK5009 | warning | Node path under `/mnt/<drive>/` |
| AIK5010 | warning | `rig.env.yaml` is tracked by git (emitted by the CLI, #15) |

#### Message texts, AIK1001–AIK2005

Golden tests compare these texts exactly. `<where>` is empty for a top-level field,
` in seat '<id>'` for a field directly in a seat with a valid ID, otherwise ` in <path>` with the
dotted path of the containing mapping (`workspace.repos[0]`, `seats[1].requires`,
`placement.seats`). A list item's field name is `<list>[<i>]`.

| Code | Message | Hint | Position |
|---|---|---|---|
| AIK1001 | `file not found` | — | 1:1 |
| AIK1002 | `YAML syntax error: <parser message>` (`malformed YAML` when the parser gives none); a second document is a syntax error | — | the parser's |
| AIK1003 | `duplicate key '<key>'` · `anchors and aliases are not supported` · `merge keys ('<<') are not supported` · `tag '<tag>' is not supported` | — | the key, anchor, alias or tagged node |
| AIK1004 | `file is larger than 256 KiB` · `file is not valid UTF-8` | — | 1:1 |
| AIK2001 | `unsupported apiVersion '<v>'` · `missing apiVersion` · `expected kind '<K>', found '<v>'` · `missing kind, expected '<K>'` | `this version supports aiakos.dev/v1` (apiVersion cases) | the value, or the mapping start when missing |
| AIK2002 | `unknown field '<f>'<where>` | `did you mean '<g>'?` when a field of the same mapping is at edit distance ≤ 2 (closest; ties: table order) | the key |
| AIK2003 | `missing required field '<f>'<where>` | — | start of the mapping lacking it |
| AIK2004 | `invalid value '<v>' for field '<f>'<where>` · `invalid key '<k>' in <path>` · `field '<f>'<where> must be a string` (or `a list`, `a mapping`) · `field 'workspace.repos' must have at least one item` · `file must contain a mapping` | `allowed values: a, b` · `must match <pattern>` | the value or key |
| AIK2005 | `reserved field '<f>'<where> is not supported in this version (planned for <M>)` · `reserved value '<v>' for field '<f>'<where> is not supported in this version (planned for <M>)` | — | the key or value |

Rules the texts depend on: any explicit tag counts as a custom tag; an empty scalar, `~` and
`null` are not strings; after AIK1001, AIK1002 or AIK1004 the file reports nothing else; after
AIK2001 the file's fields are not checked; an entry whose value is an alias or tagged counts as
present. Diagnostics are ordered by file (rig, agents in order of first reference, env), then
line, then column, then code.

#### Message texts, AIK3003–AIK5009

Golden tests compare these texts exactly. `<where>` is as above. For a seat without a valid ID
it is ` in seats[<i>]`, and the rules whose message names the seat ID (AIK4001, AIK4005, AIK4012
and the `workdir_repo` case of AIK4004) are not reported for that seat.

| Code | Message | Hint | Position |
|---|---|---|---|
| AIK2004 | `invalid value '<v>' for field 'agent_ref'<where>` (not `local:<path>` and not a reserved scheme) · `field 'repos'<where> must have at least one item` (a seat's `repos: []`) · `invalid value '<v>' for field 'harness'<where>` | `must be local:<path>` · — · `allowed values: claude-code` | the value |
| AIK3003 | `agent_ref scheme '<scheme>:'<where> is not supported in this version (planned for M2)` (`path:`, `git:`) | — | the value |
| AIK4001 | `seat '<id>' has no harness` | `set harness on the seat or defaults.harness in <agent file>` | start of the seat mapping |
| AIK4002 | `harness '<v>'<where> is not supported in this version (planned for M2)` (`opencode`, `codex`; on a seat and in `defaults`) | — | the value |
| AIK4003 | `duplicate seat id '<id>'` · `duplicate repo name '<n>'` | `first defined at line <l>` | the second value |
| AIK4004 | `unknown repo '<n>'<where>` · `workdir_repo '<n>' is not in the repos of seat '<id>'` | `defined repos: a, b` · `repos of the seat: a, b` | the item · the value |
| AIK4005 | `field '<f>' is not allowed on human seat '<id>'` (one per field other than `id`, `kind`, `description`) | — | the key |
| AIK4006 | `rig has no agent seat` | — | the `seats` key |
| AIK4007 | `setting '<key>' in harnesses.claude-code is owned by Aiakos` (`hooks`, `statusLine`, `apiKeyHelper`, `env`) | — | the key |
| AIK4008 | `permission_mode 'bypassPermissions' is not supported in this version` | `it needs a sandbox (planned for M6)` | the value |
| AIK4009 | `malformed permission rule '<rule>' for field '<list>[<i>]' in harnesses.claude-code.permissions` | `expected Tool or Tool(specifier)` | the item |
| AIK4010 | `seat id '<id>' is reserved` | `reserved ids: all, aiakos, system, orchestrator, node, human` | the value |
| AIK4011 | `repo url<where> contains credentials` · `repo url<where> has an unsupported form` | `URLs must not contain user info; use a credential helper or SSH` · `use https://host/path or git@host:org/repo.git` | the value |
| AIK4012 | `seat '<id>' uses auth: api-key but does not list secret 'anthropic_api_key'` | `add anthropic_api_key to requires.secrets` | the `auth` value |
| AIK4020 | `credential-like value (<kind>)`; kinds: `Anthropic API key`, `GitHub token`, `Slack token`, `private key`, `URL with user info` | `never put secrets in rig files; name the secret and bind it in rig.env.yaml` | the value |
| AIK5001 | `rig '<v>' does not match rig name '<name>'` | — | the value |
| AIK5002 | `invalid node path '<p>' for field '<f>'<where>` | first that applies: `Windows paths are not valid; node paths are POSIX paths on the node` · `'$' expansion is not supported` · `must be absolute or start with ~/` | the value |
| AIK5003 | `seat '<id>' has no node` | `set placement.default_node or placement.seats.<id>.node` | the `placement` key, or 1:1 when there is none |
| AIK5004 | `repo '<n>' has no path` · `secret '<n>' has no source` | `set repos.<n>.path` · `set secrets.<n>.file` | the `<n>` key when the entry exists, else the `repos` or `secrets` key, else 1:1 |
| AIK5005 | `placement for unknown seat '<id>'` · `placement for human seat '<id>'` | `agent seats: a, b` · `human seats take no placement` | the key |
| AIK5006 | `binding for unknown repo '<n>'` | `defined repos: a, b` | the key |
| AIK5007 | `secret '<n>' is not required by any seat` | — | the key |
| AIK5008 | `inline secret value in secrets.<id>` | `never inline secrets; use file: with a node-local path` | the `value` key |
| AIK5009 | `node path '<p>' for field '<f>'<where> is on the Windows filesystem` | `/mnt/<drive> paths are slow and file watching is unreliable` | the value |

Rules the texts depend on:

- **What a rule reads.** A rule reads only values that passed the checks above. A seat whose
  `kind` is invalid counts as an agent seat for AIK4006 only and is skipped by every other seat
  rule. Lists in hints are in file order, each name once.
- **Which files.** Rules inside one file run when that file parsed and has no AIK2001. Rules
  that compare files (AIK5001, AIK5003–AIK5007, and AIK4001 and AIK4012, which need the agent
  file) run only when every file they read is in that state. A cross-file rule is also skipped
  when an earlier check reported anything inside a container it reads: the env `repos` mapping or
  a seat's `repos` list for AIK5004 (repos) and AIK5006; the env `secrets` mapping or a seat's
  `requires` for AIK5004 (secrets) and AIK5007; `placement` for AIK5003 and AIK5005.
- **Agent file of a seat.** A seat's agent is the directory its `agent_ref` resolves to, so
  `local:agents/impl` and `local:./agents/impl/` name the same agent, loaded once.
- **Required secrets** are every name in an agent seat's `requires.secrets`, plus
  `anthropic_api_key` for a seat with `auth: api-key` and harness `claude-code`, listed or not.
- **Node paths.** AIK5009 applies to a path that passed AIK5002 and matches `^/mnt/[a-z](/|$)`.
- **Repo URLs.** Valid forms are `https://<host>/<path>` without user info and
  `git@<host>:<path>`. A repo URL never gets AIK4020.
- **Credential-like values (R22).** Every scalar value in the three files, also under `x-`
  fields, is matched against: `(?<![A-Za-z0-9])sk-ant-[A-Za-z0-9_-]{6,}`;
  `(?<![A-Za-z0-9])(ghp_|github_pat_)[A-Za-z0-9_]{6,}`; `(?<![A-Za-z0-9])xox[bp]-[A-Za-z0-9-]{6,}`;
  `-----BEGIN [A-Z ]*PRIVATE KEY-----`; `[A-Za-z][A-Za-z0-9+.-]*://[^/\s@]+@`. The scan also reads
  values that failed an earlier check.
- **No secret in output.** Every other diagnostic at the position of an AIK4020 is removed, and
  so is every other diagnostic whose message or hint contains a match. When the scan did not
  report that text itself (a mapping key, or a value in a file with AIK2001), the removed
  diagnostic is replaced by an AIK4020 at its position, so a file never ends up without a
  diagnostic. An agent file is not loaded through a credential-like `agent_ref`, because its
  diagnostics would carry that path in their file name.

### Reserved fields and values

Rejected with AIK2005 and the milestone, so users get a precise message and M2 can add them
without a format break:

- `rig.yaml`: `pods`, `edges`, `imports`, `profiles` (M2), `channels` (M4), `egress` (M6);
  seat `profile`, `uses`, `startup`, `pod` (M2); checkout `shared-readonly` (M6),
  `task-worktree` (M8). Recognised with their own codes: harness `opencode`, `codex` (AIK4002,
  M2) and `agent_ref` schemes `path:`, `git:` (AIK3003, M2).
- `agent.yaml`: `imports`, `resources`, `profiles`, `startup`, `subagents` (M2);
  `harnesses.opencode`, `harnesses.codex` (M2). (`hooks`, `statusLine`, `apiKeyHelper` and `env`
  are not reserved but Aiakos-owned: AIK4007.)
- `rig.env.yaml`: `nodes` (M6), `channels` (M4), `secrets.<n>.store`, `secrets.<n>.env` (M6).

### Forward compatibility and M2 extensions

| M2+ extension | How it fits v1 |
|---|---|
| `imports`, shared libraries, `path:`/`git:` refs | New keys and `agent_ref` schemes; resolved content is still embedded and hashed, so the canonical form and stored instance do not change shape |
| `profiles`, `uses:` | New optional keys on agent and seat; absent means the v1 behaviour |
| Delivery hints (`startup`) | New optional agent key; v1 projection is the default hint (`guidance_merge: claude-md`, `skill_install: dir`) |
| Culture | Already a v1 key (`culture_file`); M2 may add more culture options |
| Pods, edges | Pods as an optional grouping; seat IDs stay rig-unique so addresses do not change (D1, [ADR 0014](../adr/0014-flat-seat-addresses.md)) |
| OpenCode / Codex | `harnesses.<name>` sections and harness enum values |
| JSON Schema, `spec validate`, `up --plan` | The tables above are the schema source; the loader is the reference validator; `spec validate` is `RigLoader.Load` plus output |
| `shared-readonly`, `task-worktree`, secret stores, channels, egress | Reserved values and keys |

### Worked example: `aiakos-dev`

Expressible entirely in v1. `rigs/aiakos-dev/.gitignore` contains `rig.env.yaml`.

`rigs/aiakos-dev/rig.yaml`:

```yaml
apiVersion: aiakos.dev/v1
kind: Rig
name: aiakos-dev
description: The team that builds Aiakos. Runs on the last released Aiakos (ADR 0008).
culture_file: CULTURE.md

workspace:
  repos:
    - name: aiakos
      url: https://github.com/aiakos-hq/aiakos.git
      default_branch: main

seats:
  - id: lead
    kind: human
    description: Maintainer. Owns priorities, approves specs, merges.

  - id: impl
    agent_ref: local:agents/implementer
    description: Implements issues from specs and opens PRs.
    harness: claude-code
    model: opus
    checkout: seat-worktree
    requires: { auth: subscription }

  - id: review
    agent_ref: local:agents/reviewer
    description: Reviews the exact diff against the spec and acceptance criteria.
    harness: claude-code
    model: opus
    checkout: seat-worktree          # read-only intent is enforced by permissions (D2)
    requires: { auth: subscription }
```

`rigs/aiakos-dev/CULTURE.md` (excerpt):

```markdown
# How the aiakos-dev team works

- Every change has a GitHub issue; non-trivial work has a spec in docs/specs/ first.
- `impl` implements, `review` reviews the exact diff, `lead` (a human) approves and merges.
- Seats never run `aiakos up`/`down` for this rig, and nobody points it at a development
  build: the team runs on the last released Aiakos (the bootstrap rule).
- Small PRs, one issue each, conventional subjects, `Closes #N`.
```

`rigs/aiakos-dev/agents/implementer/agent.yaml`:

```yaml
apiVersion: aiakos.dev/v1
kind: Agent
name: implementer
description: Implements a GitHub issue against its spec, with tests, and opens a PR.
defaults:
  harness: claude-code
  model: opus
guidance:
  - GUIDANCE.md
skills:
  - skills/implement-issue
harnesses:
  claude-code:
    permission_mode: acceptEdits
    permissions:
      allow:
        - Bash(dotnet build:*)
        - Bash(dotnet test:*)
        - Bash(git status)
        - Bash(git diff:*)
        - Bash(gh issue view:*)
        - Bash(gh pr create:*)
      deny:
        - Bash(git push --force:*)
        - Bash(gh pr merge:*)
        - Bash(aiakos up:*)
        - Bash(aiakos down:*)
```

`rigs/aiakos-dev/agents/implementer/skills/implement-issue/SKILL.md`:

```markdown
---
name: implement-issue
description: Implement one GitHub issue end to end - read the issue and spec, branch, code, test, open a PR.
---

1. `gh issue view <n>`; open the linked spec in docs/specs/.
2. Create a branch `feat/<n>-<slug>` from `origin/main` in your worktree.
3. Implement against the acceptance criteria; add tests; `dotnet test` must pass.
4. Update docs and the spec in the same PR; open it with `Closes #<n>`.
```

`rigs/aiakos-dev/agents/reviewer/agent.yaml`:

```yaml
apiVersion: aiakos.dev/v1
kind: Agent
name: reviewer
description: Reviews a PR's exact diff against its spec and acceptance criteria.
defaults:
  harness: claude-code
guidance:
  - GUIDANCE.md
skills:
  - skills/review-pr
harnesses:
  claude-code:
    permission_mode: default
    permissions:
      allow:
        - Bash(gh pr view:*)
        - Bash(gh pr diff:*)
        - Bash(gh pr checkout:*)
        - Bash(gh pr review:*)
        - Bash(dotnet test:*)
      deny:
        - Edit
        - Write
        - NotebookEdit
        - Bash(git push:*)
        - Bash(gh pr merge:*)
```

`rigs/aiakos-dev/rig.env.yaml` (on the maintainer's machine, not committed):

```yaml
apiVersion: aiakos.dev/v1
kind: RigEnv
rig: aiakos-dev
seat_root: ~/aiakos/seats           # the node trusts each seat's workdir (R30)
placement:
  default_node: wsl-local
repos:
  aiakos: { path: ~/src/aiakos }    # a clone on the WSL filesystem, separate from the Windows checkout
```

Resolution gives two agent seats on `wsl-local`: `impl@aiakos-dev` (workdir
`~/aiakos/seats/aiakos-dev/impl/repos/aiakos`, branch `aiakos/aiakos-dev/impl`, `acceptEdits`,
model `opus`) and `review@aiakos-dev` (workdir `~/aiakos/seats/aiakos-dev/review/repos/aiakos`,
branch `aiakos/aiakos-dev/review`, `default` mode with edits and pushes denied, model `opus` from
the seat; without it the reviewer agent has no default model and the result would be `null`,
the harness default). `lead@aiakos-dev` is recorded and appears in both rosters but is not
launched. The `impl` resolved parameters are the example in
[Resolved seat parameters](#resolved-seat-parameters).

Note for #16: `rigs/aiakos-dev/README.md` currently plans `review` as `shared-readonly`; with this
spec it is `seat-worktree` plus deny rules (D2). #16 updates the README when it adds the files.

## Acceptance criteria

- [ ] AC1 — The `aiakos-dev` example above, copied verbatim into a test fixture (with both
  `GUIDANCE.md` files and the `review-pr` skill as small placeholder files), loads
  with zero errors and zero warnings, and `ResolvedRig` has three seats (one human), two resolved
  seat parameter sets matching the golden `resolved.json`.
- [ ] AC2 — Loading the same fixture from a Windows path and a Linux path, and from a copy whose
  guidance/culture files have CRLF line endings and reordered YAML keys and extra `x-` fields,
  gives the same `spec_hash`.
- [ ] AC3 — Changing one byte of a guidance file, one skill file or one permission rule changes
  `spec_hash`; changing only `rig.env.yaml` changes `binding_hash` and not `spec_hash`.
- [ ] AC4 — Every diagnostic code in the [table](#diagnostics) has at least one golden negative
  test whose expected output (file, line, column, code, message, hint) matches exactly; a fixture
  with five independent errors reports all five in one run.
- [ ] AC5 — Fixtures containing `sk-ant-…`, `ghp_…`, a PEM private key header, a
  `https://user:pass@…` URL, or `secrets.x.value:` fail with AIK4020/AIK4011/AIK5008, and the
  diagnostic text does not contain the secret value.
- [ ] AC6 — Fixtures using each reserved field/value (`pods`, `edges`, `imports`, `profiles`,
  `channels`, `shared-readonly`, `task-worktree`, `opencode`, `agent_ref: git:…`,
  `harnesses.opencode`, `bypassPermissions`, `hooks`) fail with AIK2005/AIK3003/AIK4002/AIK4007/
  AIK4008 naming the milestone or owner.
- [ ] AC7 — Projection of the `impl` seat produces exactly `CLAUDE.md` and
  `.claude/skills/implement-issue/SKILL.md` under the projection root, byte-identical to the
  golden files; no path in the projection plan lies inside a checkout path.
- [ ] AC8 — A path escape (`guidance: [../../etc/passwd]`), an absolute path and a symbolic link
  in a skill directory are rejected with AIK3002.
- [ ] AC9 — `RigLoader.Load` performs no network, git, database or node access: a test runs it
  on a fixture copied to a temp directory with an empty `PATH` and gets the same result as AC1.
- [ ] AC10 — The resolved seat parameter set contains no session ID, token, environment
  variable, hook, statusLine or secret value (asserted by a test over the serialised JSON).

## Test plan

**Unit / golden tests (loader project, xUnit).** Fixtures under
`tests/<spec-model tests project>/Fixtures/` (project name per spec 0001), one directory per case:

```text
Fixtures/
  valid/aiakos-dev/            # the worked example (+ rig.env.yaml) → expected/resolved.json, expected/hashes.txt
  valid/minimal/               # one agent seat, defaults only
  valid/multi-repo-shared/     # two repos, shared checkout, workdir_repo
  valid/api-key/               # auth: api-key + secrets.anthropic_api_key.file
  invalid/<code>-<case>/       # input files → expected/diagnostics.txt (exact text)
  projection/aiakos-dev-impl/  # expected/projection/CLAUDE.md, .claude/skills/…
```

- Golden comparison is exact text; an `UPDATE_GOLDEN=1` switch regenerates expected files for
  review in the diff.
- Hash stability: AC2/AC3 as parameterised tests, plus a test that runs on both Windows and Linux
  CI legs and asserts the checked-in `hashes.txt`.
- Canonical form: property tests that shuffle YAML key order and `x-` fields and assert the same
  canonical JSON.
- Diagnostics: one test per code (AC4); a "many errors" fixture; did-you-mean hints; line/column
  for errors in nested lists.
- Secret hygiene: AC5 plus a test that serialises every diagnostic and resolved object and scans
  for the planted values.
- Projection: AC7; header contains the seat address and `spec_hash`; source comments in order;
  LF only; single trailing newline; skills copied byte-exact with mode 0644.

**Integration (Linux CI and the maintainer's WSL, owned by #11/#12 but driven by this spec's
fixtures).** Launch the `aiakos-dev` fixture's `impl` seat on a node: the worktree is created at
the fixed path on the fixed branch; a second `up` reuses it untouched (a dirty file survives);
Claude Code in the seat lists the projected skill and follows a sentinel instruction from the
projected `CLAUDE.md` (verifies the `--add-dir` mechanism, D3); the repo's `git status` stays
clean after projection.

**Manual demo.** On the maintainer's machine: write the worked-example files into a scratch rig
directory, run `aiakos up` (released M1 tool), then `aiakos ps` shows `lead` (human, not
launched), `impl` and `review` with node, harness, model and short `spec_hash`; introduce a typo
(`chekout`) and see AIK2002 with the hint.

## Risks and open questions

No questions remain open. The maintainer accepted every recommendation in review (PR #25).

### Decisions (resolved in review)

- **D1 — Flat seats or pods?** Plan §5 sketches pods with pod-qualified addresses (`dev.impl`).
  *Decision:* v1 uses flat, rig-unique seat IDs (`impl@aiakos-dev`). In M2, pods become an
  optional grouping attribute, not a namespace. *Rationale:* seat addresses never change when
  pods are introduced. Recorded in
  [ADR 0014](../adr/0014-flat-seat-addresses.md); reflected in R7 and in
  [Forward compatibility](#forward-compatibility-and-m2-extensions).
- **D2 — `shared-readonly` for the reviewer in M1?** *Decision:* no. The reviewer uses
  `seat-worktree` plus deny rules for edits and pushes. `shared-readonly` stays reserved until
  M6, where the sandbox can enforce it as a read-only mount. *Rationale:* read-only cannot be
  enforced without a sandbox, and a worktree lets the reviewer `gh pr checkout` the exact diff
  without disturbing anyone. Reflected in R11 and the worked example. #16 updates
  `rigs/aiakos-dev/README.md`.
- **D3 — Projection loading mechanism.** *Decision:* the mechanism is `--add-dir <projection root>`
  with `CLAUDE_CODE_ADDITIONAL_DIRECTORIES_CLAUDE_MD=1` for `CLAUDE.md`, and `--add-dir` alone
  for skills. Spec 0005's experiment confirmed it (a `.claude/settings.json` inside the projection
  is not read), so no fallback is needed. In no case is anything written into the checkout.
  *Rationale:* the mechanism keeps the no-write rule and works for `shared` and `seat-worktree`
  alike. Recorded in [ADR 0015](../adr/0015-projection-outside-checkouts.md) and
  [ADR 0027](../adr/0027-claude-projection-and-settings.md); reflected in R28 and
  [Projection for Claude Code](#projection-for-claude-code).
- **D4 — Culture in v1?** *Decision:* yes. `culture_file` is plain Markdown projected into every
  agent seat. *Rationale:* #16 needs `CULTURE.md`, and M2 can add options without changing the
  v1 meaning. Reflected in R6 and R29.
- **D5 — Strict unknown fields vs a pinned older tool.** *Decision:* unknown fields are errors,
  and `x-` fields are an escape hatch. The `aiakos-dev` files must load with the pinned release
  (R32). Once CI exists, a CI job loads `rigs/aiakos-dev` with the pinned release. *Rationale:*
  typos fail loudly and the error names the field (rule 3). Recorded in
  [ADR 0013](../adr/0013-rig-file-header-and-compatibility.md); reflected in R4, R31 and R32.
- **D6 — `auth: api-key` in M1?** *Decision:* the format supports it, and the adapter implements
  it with `apiKeyHelper` reading the delivered key file. It is not part of M1 acceptance.
  *Rationale:* it is cheap and proves the secret model before M6. Reflected in R13, R21 and
  [Secrets](#secrets).
- **D7 — Model value.** *Decision:* the model is a string passed verbatim to the harness (e.g.
  `opus`) and recorded as given. `null` means the harness's own default. A `model_class` may come
  in M2. *Rationale:* v1 needs no mapping layer, and recording `null` keeps the harness default
  from being guessed. Reflected in R10 and
  [Resolution and defaults](#resolution-and-defaults).
- **D8 — YAML anchors?** *Decision:* anchors, aliases and merge keys are rejected in v1.
  *Rationale:* golden tests stay simpler, alias expansion cannot be abused, and relaxing the rule
  later is compatible while tightening it would not be. Reflected in R3.
- **D9 — Size limits vs gRPC message size.** *Decision:* each seat's projection is capped at
  2 MiB (AIK3005), below gRPC's 4 MiB default. Spec 0002 decides whether content is chunked or
  fetched by hash. *Rationale:* the projection content travels in the seat parameters. Reflected
  in R25 and the size limits under [`agent.yaml`](#agentyaml).
- **D10 — Seat branch name.** *Decision:* `aiakos/<rig>/<seat>`. *Rationale:* two rigs that use
  the same seat ID on one clone do not collide. Reflected in R30.
- **D11 — Where `rig.env.yaml` lives.** *Decision:* next to `rig.yaml`, git-ignored, with a CLI
  override (#15). The CLI warns when the file is tracked (AIK5010). *Rationale:* this is the
  simplest discovery rule, and the warning catches leaks. Locations per `AIAKOS_HOME` may come
  later. Reflected in R1 and [`rig.env.yaml`](#rigenvyaml).

ADRs recording the cross-cutting decisions of this spec:

1. [ADR 0013](../adr/0013-rig-file-header-and-compatibility.md): the `apiVersion: aiakos.dev/v1`
   + `kind` header, strict unknown fields with `x-` extensions, and additive-only evolution of a
   version (R2–R5, R31).
2. [ADR 0014](../adr/0014-flat-seat-addresses.md): a flat, rig-unique seat namespace, with pods
   as a grouping and not a namespace (D1).
3. [ADR 0015](../adr/0015-projection-outside-checkouts.md): projection never writes into a
   repository checkout, and uses a per-seat projection root owned by Aiakos (R28, D3).
4. [ADR 0016](../adr/0016-secrets-as-node-file-references.md): shared files name secrets,
   bindings reference files on the node, and values are delivered only as files, never through
   the orchestrator or database (R21, spike 0005).
5. [ADR 0017](../adr/0017-resolved-rig-and-hashes.md): the resolved rig is self-contained and
   content-addressed, with separate `spec_hash` and `binding_hash` (R25, R26).

### Risks

- **RK1 — Seat root collisions between two Aiakos instances** (the released team vs a development
  build under test) on the same node. Seat directories include the rig name, and development
  tests should use their own rig names and `seat_root`. The node should keep an ownership marker
  in each seat directory (#11/#12).
- **RK2 — The projected header and roster mention identity in text.** That text informs the model and
  carries no authority; authority comes from the environment token (rule 2). The header says so.
- **RK3 — The projection mechanism is unverified until #12** (D3). *Resolved 2026-09-30:* spec 0005's
  experiment verified it (see "Changes after acceptance").

## Changes after acceptance

- **2026-09-30 — wave 2 amendments** (spec 0005, accepted in review):
  - **R30, trust.** Claude Code inherits trust from a trusted ancestor only up to a git
    repository root, so a trusted `seat_root` does not cover the worktrees beneath it. The node
    now sets trust for each seat's exact workdir; the `seat_root` descriptions in the
    `rig.env.yaml` table and the worked example follow. Source: spec 0005's verification experiment (T1–T7);
    [ADR 0029](../adr/0029-minimal-harness-user-config.md).
  - **R28 and D3, projection mechanism confirmed.** `--add-dir` +
    `CLAUDE_CODE_ADDITIONAL_DIRECTORIES_CLAUDE_MD=1` loads the projected `CLAUDE.md`, `--add-dir`
    alone loads skills, and a `settings.json` inside the projection is not read. The fallback
    (projection at the seat directory, `shared` seats failing) is removed, and the related risk
    is resolved. Source: spec 0005's verification experiment (E1–E8);
    [ADR 0027](../adr/0027-claude-projection-and-settings.md).
- **2026-10-01 — slice 1 of #14 (envelope and diagnostics):**
  - **Loader project and surface.** The project is `Aiakos.Spec`. `Severity` and
    `DiagnosticFormatter.Format` are added to the public surface, and `Diagnostic.File` is
    defined as rig-root-relative.
  - **Message texts for AIK1001–AIK2005** are now part of the spec (Diagnostics), because golden
    tests compare exact text and the spec gave only the AIK2002 example.
  - **Milestones for every reserved name.** The list gave them for some names only; the rest
    follow the Non-goals section. `secrets.<n>.env` is set to M6 (it had none).
  - **`secrets.<n>.store` is AIK2005, not AIK5008.** It was listed under both; a reserved key is
    reported as reserved. AIK5008 keeps `value:`.
  - **Implemented in slices.** Slice 1 covers R1–R5 and R23. `Load` returns no `ResolvedRig`
    until slice 2; AIK3xxx–AIK5xxx, hashing and projection follow in slices 2–4.
- **2026-10-02 — slice 2 of #14 (semantic validation):**
  - **Message texts for AIK3003–AIK5009** are now part of the spec (Diagnostics), with the rules
    they depend on, for the same reason as in slice 1.
  - **`opencode` and `codex` are AIK4002, not AIK2005.** The table had AIK4002 for a recognised
    harness while the reserved list sent the same values to AIK2005. Any other harness value
    stays AIK2004.
  - **A seat's `repos: []` is AIK2004.** An empty list would mean "no repos", which a seat
    cannot have.
  - **The example for AIK5003** showed position 9:11; the position is the `placement` key.
  - **Diagnostics are ordered by code** after file, line and column.
  - **Not in the brief, decided during implementation:** a seat without a valid ID is named
    `seats[<i>]` and gets no diagnostic whose message needs the ID; AIK4001 is for agent seats
    only and not for an agent file that is not a mapping; hints list each name once; a
    credential-like mapping key, or one in a file with AIK2001, is reported as AIK4020 where the
    removed diagnostic stood.
  - **Still no `ResolvedRig`.** `Load` returns none until slice 3 (file references and the
    resolved rig); the sentence above said slice 2. AIK3001, AIK3002, AIK3004 and AIK3005 follow
    there.

- **2026-10-08 — #14 closing amendments (chore #286):**
  - **AC1, fixture.** The `tests/Aiakos.Spec.Tests/Fixtures/valid/full` fixture replaces the
    worked `aiakos-dev` example as the loader acceptance fixture. It has three seats (one
    human) and two resolved seat parameter sets; `LoadsFullRigWithoutDiagnostics` checks it.
  - **AC4, diagnostic ownership.** AIK5010 is checked by #15's rig compatibility validation,
    rather than a negative fixture for the rig loader in #14.
  - **AC7, projection files.** The golden projection includes the skill's `.metadata` file
    alongside `CLAUDE.md` and `SKILL.md`. `LoadProjectionIntegrationTests` checks the exact
    paths and bytes, including `.metadata`, and the header's statement that text carries no
    authority.
