---
id: 14-1b
title: "#14 slice 1 — review fixes"
issue: 14
status: implemented
route: impl/opencode
date: 2026-10-01
---

# Brief: #14 slice 1 — review fixes

Follow-up to `artifacts/briefs/14-1-envelope-and-diagnostics.md`, which still applies in full
(public surface, rules, definition of done, out of scope). **Do not read `docs/specs/` or
`docs/adr/`.** Touch only `src/Aiakos.Spec/` and `tests/Aiakos.Spec.Tests/`.

The first implementation passes all existing tests. Review found four places where the output
does not follow rules 11–12 of the first brief. Fix them and add the fixtures below so each is
covered by a golden test.

## Fixes

1. **Reserved field: context goes after the field name** (first brief: "`<where>` goes right
   after the field name in every message"). Now:
   `reserved field 'profile' is not supported in this version (planned for M2) in seats[1]`.
   Required: `reserved field 'profile' in seats[1] is not supported in this version (planned for M2)`.
2. **Invalid key: context is the full dotted path of the mapping that holds the key.** Now:
   `invalid key 'Impl' in seats`. Required: `invalid key 'Impl' in placement.seats`.
   (`in repos` and `in secrets` stay as they are: those mappings are top-level.)
3. **List items: the field name is `<list>[<i>]` and the context is the mapping that holds the
   list.** Now: `invalid value 'Bad' for field 'secret' in seats[0].requires.secrets[1]`.
   Required: `invalid value 'Bad' for field 'secrets[1]' in seats[0].requires`. Apply the same
   naming to every list of scalars (`repos` of a seat, `guidance`, `skills`, `allow`, `ask`, `deny`).
4. **AIK1002 never shows a .NET exception text.** For `name: [demo` the message is now
   `YAML syntax error: Operation is not valid due to the current state of the object.` When the
   parser fails with anything other than a `YamlException`, the message is
   `YAML syntax error: malformed YAML`. Keep the position reported today.

## New fixtures (base = `Fixtures/valid/minimal`, one change each)

| Fixture `invalid/…` | Change | `expected/diagnostics.txt` |
|---|---|---|
| `AIK2005-field-in-seat` | rig.yaml add line 12 `    profile: fast` | `rig.yaml:12:5: error AIK2005: reserved field 'profile' in seat 'impl' is not supported in this version (planned for M2)` |
| `AIK2005-harness` | agent.yaml add line 7 `harnesses:` and line 8 `  opencode: {}` | `agents/impl/agent.yaml:8:3: error AIK2005: reserved field 'opencode' in harnesses is not supported in this version (planned for M2)` |
| `AIK2005-env` | rig.env.yaml add lines 9–13: `secrets:`, `  api_key:`, `    file: /home/dev/key`, `    store: vault`, `nodes: {}` | `rig.env.yaml:12:5: error AIK2005: reserved field 'store' in secrets.api_key is not supported in this version (planned for M6)` then `rig.env.yaml:13:1: error AIK2005: reserved field 'nodes' is not supported in this version (planned for M6)` |
| `AIK2004-key-nested` | rig.env.yaml insert after line 5: `  seats:` and `    Impl: {node: local}` | `rig.env.yaml:7:5: error AIK2004: invalid key 'Impl' in placement.seats` + `  hint: must match ^[a-z][a-z0-9-]{0,23}$` |
| `AIK2004-list-item` | rig.yaml add line 12 `    requires:` and line 13 `      secrets: [ok_name, Bad]` | `rig.yaml:13:26: error AIK2004: invalid value 'Bad' for field 'secrets[1]' in seats[0].requires` + `  hint: must match ^[a-z][a-z0-9_]{0,63}$` |
| `AIK2004-empty-file` | rig.yaml is empty (0 bytes) | `rig.yaml:1:1: error AIK2004: file must contain a mapping` |

Also add to `ValidTests`:

- **Deferred keys produce no diagnostics:** the base with agent.yaml extended by
  `harnesses: {claude-code: {permission_mode: bypassPermissions, hooks: {}, statusLine: x, apiKeyHelper: x, env: {A: b}}}`
  and rig.env.yaml extended by `secrets: {api_key: {value: abc}}`.
- **AIK1002 text:** for rig.yaml line 3 `name: [demo`, the formatted output does not contain
  `Operation is not valid`.

## Definition of done

- `dotnet build -c Release` at the repo root: 0 warnings, 0 errors.
- `dotnet test --project tests/Aiakos.Spec.Tests -c Release`: all green, including every existing
  fixture unchanged.
- If the same test still fails after three attempts, stop and report what you tried.
- LF, UTF-8 without BOM, final newline. No `NoWarn`, `#pragma warning disable`, `[SuppressMessage]`.
- One new commit on the current branch, subject
  `fix(spec): diagnostic context for reserved fields, keys and list items (#14)`. Do not amend
  the existing commit, do not push, do not open a PR.
