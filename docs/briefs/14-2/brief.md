---
id: 14-2
title: "#14 slice 2 — semantic validation of the three YAML files"
issue: 14
status: approved
route: impl/opencode
date: 2026-10-01
---

# Brief: #14 slice 2 — semantic validation of the three YAML files

Self-contained. **Do not read `docs/specs/` or `docs/adr/`.** Slice 1 is merged: read
`src/Aiakos.Spec/` and `tests/Aiakos.Spec.Tests/` first; they define the node tree, the schema
checks, the `<where>` suffix, the fixture layout and the test style you must keep.

## Goal

After the slice 1 checks, validate what the three files *mean*: seats, repos, harness, permission
rules, Aiakos-owned settings, the env bindings, node paths, secret sources and credential-like
values. Still no file references, no resolution: `Load` keeps returning `Rig = null`. The public
surface does not change.

## Files

- Touch only `src/Aiakos.Spec/**` and `tests/Aiakos.Spec.Tests/**`. No new projects or packages.
- Put the new checks in a new `internal sealed class SemanticValidator` (new file), called from
  `RigLoader` after the schema checks, with the parsed trees of all files. Change
  `SchemaValidator` only where this brief says a slice 1 behaviour changes.
- No `NoWarn`, `#pragma warning disable`, `[SuppressMessage]`, `Version=` on `PackageReference`.

## General rules

1. **Defensive input.** A semantic rule reads only values that passed slice 1: skip a value that
   is missing, has the wrong type or failed its pattern. Never throw.
2. **Which files.** Checks inside one file run when that file parsed and has no AIK2001. Checks
   that compare files (AIK5001, AIK5003–AIK5007, and AIK4001/AIK4012 which need the agent) run
   only when every file they read is in that state; otherwise they are skipped silently.
   **Damaged containers.** A cross-file check is also skipped when slice 1 reported anything
   inside a container it reads ("inside" = on the container, on a key, or on any value below it):
   AIK5004 for repos and AIK5006 when the env `repos` mapping or any agent seat's `repos` list
   has a slice 1 diagnostic inside; AIK5004 for secrets and AIK5007 when the env `secrets`
   mapping or any agent seat's `requires` has one; AIK5003 and AIK5005 when `placement` has one.
   This is why the slice 1 fixtures `AIK2004-key`, `AIK2004-list-item`, `AIK2005-env` and
   `many-errors` keep their golden text.
3. **Terms.** *Agent seat*: `kind` absent or `agent`. A seat whose `kind` failed slice 1 (for
   example `kind: robot`) counts as an agent seat for rule 13 only and is skipped by every other
   seat rule. Lists in hints (`defined repos: a, b`) are in file order. *Seat repos*: its `repos` list, or all
   `workspace.repos` names when absent; unknown names are ignored by later checks. *Seat harness*:
   seat `harness`, else the agent's `defaults.harness`. *Required secrets*: every name in any
   agent seat's `requires.secrets`, plus `anthropic_api_key` for rule 14.
4. **Positions** are 1-based: "value" = the scalar, "key" = the mapping key. `<where>` is the
   slice 1 suffix (` in seat '<id>'` or ` in <path>`), placed right after the field name.
5. **Order.** As in slice 1: by file, line, column. Ties: by code, then in the order found
   (seats, then repos, then secrets, in file order).
6. **No secret in output.** After all checks, remove every other diagnostic that has the same
   file, line and column as an AIK4020. AIK4011, AIK4020 and AIK5008 never contain the value.

## Changes to slice 1 behaviour

- `harness: opencode|codex` (seat and `defaults.harness`) is now AIK4002, no longer AIK2005.
- The "deferred" items now have codes: `hooks`, `statusLine`, `apiKeyHelper`, `env` under
  `harnesses.claude-code` → AIK4007; `bypassPermissions` → AIK4008; `secrets.<id>.value` →
  AIK5008; `agent_ref` → rule 7. Delete the test `AcceptsDeferredHarnessAndSecretFields`.
- `repos: []` on a seat → AIK2004 `field 'repos'<where> must have at least one item`.

## Rules (message · hint · position)

7. **agent_ref.** Scheme `path:` or `git:` → AIK3003
   `agent_ref scheme '<scheme>:'<where> is not supported in this version (planned for M2)` ·
   value. Anything else that is not `local:` followed by at least one character → AIK2004
   `invalid value '<v>' for field 'agent_ref'<where>` · `must be local:<path>` · value.
8. **AIK4001** agent seat with no seat harness (agent file loaded): `seat '<id>' has no harness`
   · `set harness on the seat or defaults.harness in <agent file>` · seat mapping start. An
   unsupported harness (rule 9) counts as present.
9. **AIK4002** `harness '<v>'<where> is not supported in this version (planned for M2)` · value.
10. **AIK4003** `duplicate seat id '<id>'` / `duplicate repo name '<n>'` ·
    `first defined at line <l>` · the second value.
11. **AIK4004** item of a seat's `repos` not in `workspace.repos`: `unknown repo '<n>'<where>` ·
    `defined repos: a, b` · the item. `workdir_repo` not in the seat repos:
    `workdir_repo '<n>' is not in the repos of seat '<id>'` · `repos of the seat: a, b` · value.
12. **AIK4005** human seat with any schema field other than `id`, `kind`, `description`:
    `field '<f>' is not allowed on human seat '<id>'` · key. One per field. No AIK2003 for
    `agent_ref` on human seats (already so).
13. **AIK4006** no agent seat (including `seats: []`): `rig has no agent seat` · the `seats` key.
    Skipped when `seats` is not a list.
14. **AIK4012 (warning)** agent seat with `requires.auth: api-key`, seat harness `claude-code`,
    and `anthropic_api_key` not in its `requires.secrets`:
    `seat '<id>' uses auth: api-key but does not list secret 'anthropic_api_key'` ·
    `add anthropic_api_key to requires.secrets` · the `auth` value. The secret still counts as
    required.
15. **AIK4007** `setting '<key>' in harnesses.claude-code is owned by Aiakos` · key.
16. **AIK4008** `permission_mode 'bypassPermissions' is not supported in this version` ·
    `it needs a sandbox (planned for M6)` · value.
17. **AIK4009** item of `permissions.allow|ask|deny` that does not match
    `^([A-Za-z][A-Za-z0-9_]*|mcp__[A-Za-z0-9_.-]+)(\(.+\))?$` or whose parentheses are not
    balanced: `malformed permission rule '<rule>' for field '<list>[<i>]' in harnesses.claude-code.permissions`
    · `expected Tool or Tool(specifier)` · the item.
18. **AIK4010** seat id in {`all`, `aiakos`, `system`, `orchestrator`, `node`, `human`}:
    `seat id '<id>' is reserved` · `reserved ids: all, aiakos, system, orchestrator, node, human`
    · value. The seat is otherwise treated normally.
19. **AIK4011** `workspace.repos[].url`. Valid: `https://<host>/<path>` with no `@` before the
    first `/` of the host part, or `git@<host>:<path>`. User info present:
    `repo url<where> contains credentials` · `URLs must not contain user info; use a credential helper or SSH`.
    Any other form: `repo url<where> has an unsupported form` ·
    `use https://host/path or git@host:org/repo.git`. Position: value. A repo url never gets AIK4020.
20. **AIK4020** every scalar value in all three files, including inside `x-` fields, that
    contains a match of one of these regexes (first kind in this order; a bare prefix such as
    `the ghp_ token` is not a match):
    - Anthropic API key: `(?<![A-Za-z0-9])sk-ant-[A-Za-z0-9_-]{6,}`
    - GitHub token: `(?<![A-Za-z0-9])(ghp_|github_pat_)[A-Za-z0-9_]{6,}`
    - Slack token: `(?<![A-Za-z0-9])xox[bp]-[A-Za-z0-9-]{6,}`
    - private key: `-----BEGIN [A-Z ]*PRIVATE KEY-----`
    - URL with user info: `[A-Za-z][A-Za-z0-9+.-]*://[^/\s@]+@`

    Message `credential-like value (<kind>)` ·
    `never put secrets in rig files; name the secret and bind it in rig.env.yaml` · value.
21. **AIK5001** env `rig` ≠ rig `name`: `rig '<v>' does not match rig name '<name>'` · value.
22. **AIK5002** node paths (`seat_root`, `repos.<id>.path`, `secrets.<id>.file`), first match:
    contains `\` or starts with `<letter>:` → hint
    `Windows paths are not valid; node paths are POSIX paths on the node`; contains `$` → hint
    `'$' expansion is not supported`; does not start with `/` or `~/` → hint
    `must be absolute or start with ~/`. Message `invalid node path '<p>' for field '<f>'<where>`
    · value.
23. **AIK5009 (warning)** valid node path matching `^/mnt/[a-z](/|$)`:
    `node path '<p>' for field '<f>'<where> is on the Windows filesystem` ·
    `/mnt/<drive> paths are slow and file watching is unreliable` · value.
24. **AIK5003** agent seat with neither `placement.seats.<id>.node` nor `placement.default_node`:
    `seat '<id>' has no node` · `set placement.default_node or placement.seats.<id>.node` ·
    reported in the env file at the `placement` key, or `1:1` when there is none.
25. **AIK5004** a repo in some agent seat's repos without `repos.<n>.path`:
    `repo '<n>' has no path` · `set repos.<n>.path`. A required secret without
    `secrets.<n>.file`: `secret '<n>' has no source` · `set secrets.<n>.file`. Position: the
    `<n>` key in the env file if the entry exists, else the `repos`/`secrets` key, else `1:1`.
    An entry that has `value` (rule 28) gets no AIK5004.
26. **AIK5005** key of `placement.seats` that is not an agent seat:
    `placement for unknown seat '<id>'` · `agent seats: a, b`; or, for a human seat,
    `placement for human seat '<id>'` · `human seats take no placement`. Position: key.
27. **AIK5006** key of `repos` not in `workspace.repos`: `binding for unknown repo '<n>'` ·
    `defined repos: a, b` · key. **AIK5007** key of `secrets` that is not a required secret:
    `secret '<n>' is not required by any seat` · key.
28. **AIK5008** `secrets.<id>.value`: `inline secret value in secrets.<id>` ·
    `never inline secrets; use file: with a node-local path` · the `value` key.

## Golden fixtures

Base = `Fixtures/valid/minimal` (rig.yaml 11 lines, agent.yaml 6, rig.env.yaml 8). Each fixture
is the base with the change shown. "add" appends lines; `→` is the exact `diagnostics.txt`
(hint lines are `  hint: …`).

| Fixture `invalid/…` | Change | Expected |
|---|---|---|
| `AIK3003-git` | rig 10: `    agent_ref: git:https://example.com/agents.git` | `rig.yaml:10:16: error AIK3003: agent_ref scheme 'git:' in seat 'impl' is not supported in this version (planned for M2)` |
| `AIK3003-path` | rig 10: `    agent_ref: path:../shared/impl` | `rig.yaml:10:16: error AIK3003: agent_ref scheme 'path:' in seat 'impl' is not supported in this version (planned for M2)` |
| `AIK2004-agent-ref` | rig 10: `    agent_ref: agents/impl` | `rig.yaml:10:16: error AIK2004: invalid value 'agents/impl' for field 'agent_ref' in seat 'impl'` + hint `must be local:<path>` |
| `AIK4001-no-harness` | agent: remove 5–6 | `rig.yaml:9:5: error AIK4001: seat 'impl' has no harness` + hint `set harness on the seat or defaults.harness in agents/impl/agent.yaml` |
| `AIK4002-seat` | rig add 12: `    harness: opencode` | `rig.yaml:12:14: error AIK4002: harness 'opencode' in seat 'impl' is not supported in this version (planned for M2)` |
| `AIK4002-agent` | agent 6: `  harness: codex` | `agents/impl/agent.yaml:6:12: error AIK4002: harness 'codex' in defaults is not supported in this version (planned for M2)` |
| `AIK4003-seat` | rig add 12–13: `  - id: impl`, `    agent_ref: local:agents/impl` | `rig.yaml:12:9: error AIK4003: duplicate seat id 'impl'` + hint `first defined at line 9` |
| `AIK4003-repo` | rig insert after 7: `    - name: app`, `      url: https://github.com/example/app2.git` | `rig.yaml:8:13: error AIK4003: duplicate repo name 'app'` + hint `first defined at line 6` |
| `AIK4004-unknown-repo` | rig add 12: `    repos: [app, lib]` | `rig.yaml:12:18: error AIK4004: unknown repo 'lib' in seat 'impl'` + hint `defined repos: app` |
| `AIK4004-workdir` | rig insert after 7: `    - name: lib`, `      url: https://github.com/example/lib.git`; add 14–15: `    repos: [app]`, `    workdir_repo: lib` | `rig.yaml:15:19: error AIK4004: workdir_repo 'lib' is not in the repos of seat 'impl'` + hint `repos of the seat: app` |
| `AIK2004-no-repos` | rig add 12: `    repos: []` | `rig.yaml:12:12: error AIK2004: field 'repos' in seat 'impl' must have at least one item` |
| `AIK4005-human` | rig add 12–14: `  - id: pm`, `    kind: human`, `    checkout: shared` | `rig.yaml:14:5: error AIK4005: field 'checkout' is not allowed on human seat 'pm'` |
| `AIK4006-no-agent-seat` | rig 9–11 replaced by `  - id: pm`, `    kind: human` | `rig.yaml:8:1: error AIK4006: rig has no agent seat` |
| `AIK4007-hooks` | agent add 7–9: `harnesses:`, `  claude-code:`, `    hooks: {}` | `agents/impl/agent.yaml:9:5: error AIK4007: setting 'hooks' in harnesses.claude-code is owned by Aiakos` |
| `AIK4008-bypass` | agent add 7–9: `harnesses:`, `  claude-code:`, `    permission_mode: bypassPermissions` | `agents/impl/agent.yaml:9:22: error AIK4008: permission_mode 'bypassPermissions' is not supported in this version` + hint `it needs a sandbox (planned for M6)` |
| `AIK4009-rule` | agent add 7–10: `harnesses:`, `  claude-code:`, `    permissions:`, `      allow: [Edit, "Bash(dotnet test", "mcp__github__get_issue", "Bash(git log:*)"]` | `agents/impl/agent.yaml:10:21: error AIK4009: malformed permission rule 'Bash(dotnet test' for field 'allow[1]' in harnesses.claude-code.permissions` + hint `expected Tool or Tool(specifier)` |
| `AIK4010-reserved-id` | rig 9: `  - id: node` | `rig.yaml:9:9: error AIK4010: seat id 'node' is reserved` + hint `reserved ids: all, aiakos, system, orchestrator, node, human` |
| `AIK4011-credentials` | rig 7: `      url: https://bob:hunter2@github.com/example/app.git` | `rig.yaml:7:12: error AIK4011: repo url in workspace.repos[0] contains credentials` + hint `URLs must not contain user info; use a credential helper or SSH` |
| `AIK4011-form` | rig 7: `      url: http://github.com/example/app.git` | `rig.yaml:7:12: error AIK4011: repo url in workspace.repos[0] has an unsupported form` + hint `use https://host/path or git@host:org/repo.git` |
| `AIK4012-api-key` | rig add 12–13: `    requires:`, `      auth: api-key`; env add 9–11: `secrets:`, `  anthropic_api_key:`, `    file: ~/.config/aiakos/secrets/anthropic_api_key` | `rig.yaml:13:13: warning AIK4012: seat 'impl' uses auth: api-key but does not list secret 'anthropic_api_key'` + hint `add anthropic_api_key to requires.secrets` |
| `AIK4020-anthropic` | rig add 12: `    model: sk-ant-example` | `rig.yaml:12:12: error AIK4020: credential-like value (Anthropic API key)` + hint `never put secrets in rig files; name the secret and bind it in rig.env.yaml` |
| `AIK4020-github` | agent 4: `description: uses ghp_example` | `agents/impl/agent.yaml:4:14: error AIK4020: credential-like value (GitHub token)` + same hint |
| `AIK4020-private-key` | rig add 12: `description: "-----BEGIN RSA PRIVATE KEY-----"` | `rig.yaml:12:14: error AIK4020: credential-like value (private key)` + same hint |
| `AIK4020-x-field` | rig add 12: `x-note: xoxb-example` | `rig.yaml:12:9: error AIK4020: credential-like value (Slack token)` + same hint |
| `AIK4020-url` | env 8: `    path: https://bob:hunter2@example.com/x` | `rig.env.yaml:8:11: error AIK4020: credential-like value (URL with user info)` + same hint (and nothing else: rule 6 removes the AIK5002) |
| `AIK5001-rig-name` | env 3: `rig: other` | `rig.env.yaml:3:6: error AIK5001: rig 'other' does not match rig name 'demo'` |
| `AIK5002-windows` | env 8: `    path: C:\src\app` | `rig.env.yaml:8:11: error AIK5002: invalid node path 'C:\src\app' for field 'path' in repos.app` + hint `Windows paths are not valid; node paths are POSIX paths on the node` |
| `AIK5002-relative` | env 8: `    path: src/app` | same line with `'src/app'` + hint `must be absolute or start with ~/` |
| `AIK5002-expansion` | env add 9: `seat_root: $HOME/seats` | `rig.env.yaml:9:12: error AIK5002: invalid node path '$HOME/seats' for field 'seat_root'` + hint `'$' expansion is not supported` |
| `AIK5009-mnt` | env 8: `    path: /mnt/c/src/app` | `rig.env.yaml:8:11: warning AIK5009: node path '/mnt/c/src/app' for field 'path' in repos.app is on the Windows filesystem` + hint `/mnt/<drive> paths are slow and file watching is unreliable` |
| `AIK5003-no-node` | env: remove 4–5 | `rig.env.yaml:1:1: error AIK5003: seat 'impl' has no node` + hint `set placement.default_node or placement.seats.impl.node` |
| `AIK5003-placement-key` | env 5: `  seats: {}` | `rig.env.yaml:4:1: error AIK5003: seat 'impl' has no node` + hint `set placement.default_node or placement.seats.impl.node` |
| `AIK5004-repo` | env: remove 6–8 | `rig.env.yaml:1:1: error AIK5004: repo 'app' has no path` + hint `set repos.app.path` |
| `AIK5004-repo-entry` | env 7: `  app: {}`, remove 8 | `rig.env.yaml:7:3: error AIK5004: repo 'app' has no path` + same hint |
| `AIK5004-secret` | rig add 12–13: `    requires:`, `      secrets: [deploy_key]` | `rig.env.yaml:1:1: error AIK5004: secret 'deploy_key' has no source` + hint `set secrets.deploy_key.file` |
| `AIK5004-secret-key` | rig add 12–13: `    requires:`, `      secrets: [deploy_key, npm_token]`; env add 9–11: `secrets:`, `  npm_token:`, `    file: ~/keys/npm` | `rig.env.yaml:9:1: error AIK5004: secret 'deploy_key' has no source` + hint `set secrets.deploy_key.file` |
| `AIK5005-unknown` | env insert after 5: `  seats:`, `    imp: {node: local}` | `rig.env.yaml:7:5: error AIK5005: placement for unknown seat 'imp'` + hint `agent seats: impl` |
| `AIK5005-human` | rig add 12–13: `  - id: pm`, `    kind: human`; env insert after 5: `  seats:`, `    pm: {node: local}` | `rig.env.yaml:7:5: error AIK5005: placement for human seat 'pm'` + hint `human seats take no placement` |
| `AIK5006-unknown-repo` | env add 9–10: `  lib:`, `    path: /home/dev/lib` | `rig.env.yaml:9:3: error AIK5006: binding for unknown repo 'lib'` + hint `defined repos: app` |
| `AIK5007-unused-secret` | env add 9–11: `secrets:`, `  old_key:`, `    file: ~/keys/old` | `rig.env.yaml:10:3: error AIK5007: secret 'old_key' is not required by any seat` |
| `AIK5008-inline` | rig add 12–13: `    requires:`, `      secrets: [deploy_key]`; env add 9–11: `secrets:`, `  deploy_key:`, `    value: hunter2` | `rig.env.yaml:11:5: error AIK5008: inline secret value in secrets.deploy_key` + hint `never inline secrets; use file: with a node-local path` |
| `many-semantic` | rig 9: `  - id: node`, add 12: `    repos: [lib]`; agent add 7–9 as in `AIK4007-hooks`; env 3: `rig: other`, env 8: `    path: src/app` | in this order: `rig.yaml:9:9` AIK4010 + hint; `rig.yaml:12:13: error AIK4004: unknown repo 'lib' in seat 'node'` + hint; `agents/impl/agent.yaml:9:5` AIK4007; `rig.env.yaml:3:6` AIK5001; `rig.env.yaml:8:11` AIK5002 + hint `must be absolute or start with ~/` |

Use only the fake values shown (`sk-ant-example`, `ghp_example`, `xoxb-example`); do not write
longer token-like strings, GitHub's push protection would block the commit.

## Other tests

- `Fixtures/valid/full` with zero diagnostics: two repos (one `git@github.com:example/lib.git`),
  three seats (`impl`, `review`, and human `pm` with only id, kind, description); `review` with
  `repos: [app]`, `workdir_repo: app`, `requires: {auth: api-key, secrets: [anthropic_api_key]}`;
  agent with `harnesses.claude-code` `permission_mode: acceptEdits` and rules `Edit`,
  `Bash(dotnet test:*)`, `mcp__github__get_issue`; env with `seat_root: ~/aiakos/seats`,
  `placement.seats.review.node`, both repo paths and `secrets.anthropic_api_key.file`.
- No formatted output of any fixture contains `hunter2`, `sk-ant-example`, `ghp_example` or
  `xoxb-example`.
- Unit tests: balanced-parentheses check; each credential kind (including `github_pat_` and
  `xoxp-`); not a match: `xghp_example` (prefix preceded by a letter), `rotate the ghp_ token`,
  `sk-ant-` alone, `https://example.com/a@b`; two seats without a node give two AIK5003 in seat
  order; cross-file checks are skipped when `rig.yaml` has a syntax error while env-only checks
  still run; a seat with `kind: robot` gives only slice 1's AIK2004 (no AIK4006, no AIK5003).
- All slice 1 fixtures stay green unchanged (general rule 2, "damaged containers", is what keeps
  four of them so), except tests of the behaviours listed under "Changes to slice 1 behaviour".

## Definition of done

- `dotnet build -c Release` at the repo root: 0 warnings, 0 errors.
- `dotnet test --project tests/Aiakos.Spec.Tests -c Release`: all green.
- If the same test still fails after three attempts, stop and report what you tried.
- LF, UTF-8 without BOM, final newline.
- One commit on the current branch, subject
  `feat(spec): semantic validation of rig files (#14)`. Do not push, do not open a PR.

## Out of scope

Reading any file other than the three YAML kinds (guidance, culture, skills, `SKILL.md`); path
safety of file references (AIK3001, AIK3002, AIK3004, AIK3005); duplicate skill names;
credential scanning of non-YAML files; `ResolvedRig` content, defaults, hashing, projection; CLI;
`docs/`, `CLAUDE.md`, CI, other projects.
