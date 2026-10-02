---
id: 14-2b
title: "#14 slice 2 — review fixes"
issue: 14
status: approved
route: impl/opencode
date: 2026-10-01
---

# Brief: #14 slice 2 — review fixes

Follow-up to `artifacts/briefs/14-2-semantic-validation.md`, which still applies in full
(rules, general rules, definition of done, out of scope). **Do not read `docs/specs/` or
`docs/adr/`.** Touch only `src/Aiakos.Spec/` and `tests/Aiakos.Spec.Tests/`.

The first implementation passes its own tests. Review found two ways a credential-like value
still reaches the output, one damaged-container case that is not skipped, and that the golden
fixtures were never added. Fix them and add the fixtures below. Every fix is covered by a golden
fixture.

## Fixes

1. **The credential scan reads every scalar value, including values that failed slice 1.**
   Now: `SemanticValidator` skips a value that has a slice 1 AIK2004 at its position
   (`if (HasDiagnosticAt(document.File, node.Mark, "AIK2004")) return;`), so
   `checkout: sk-ant-example` gives
   `rig.yaml:11:15: error AIK2004: invalid value 'sk-ant-example' for field 'checkout' in seat 'impl'`.
   Required: rule 20 scans every scalar value no matter what slice 1 reported for it. General
   rule 6 then removes the AIK2004, so the output is only
   `rig.yaml:11:15: error AIK4020: credential-like value (Anthropic API key)` plus its hint.
   General rule 1 is unchanged for every other rule: they still skip values that failed slice 1.
2. **No diagnostic other than AIK4020 contains a credential-like value in its message or hint.**
   Now: general rule 6 removes only diagnostics at the same position as an AIK4020, so a value
   that is repeated elsewhere is printed, for example
   `rig.env.yaml:3:6: error AIK5001: rig 'demo' does not match rig name 'sk-ant-example'`.
   Required: add to general rule 6: after the same-position removal, also remove every
   diagnostic other than AIK4020 whose message or hint contains a match of any rule 20 regex.
   This applies to every code from every slice, for example AIK5001, AIK4001, AIK4004, AIK5004
   and AIK5006.
3. **AIK5004 for repos is skipped when any agent seat's `repos` list is damaged.** Now:
   only a damaged env `repos` mapping skips AIK5004 for repos. Required (first brief, general
   rule 2): AIK5004 for repos is also skipped when any agent seat's `repos` list has a slice 1
   diagnostic inside. AIK5006 already does this.
4. **Add the golden fixtures of the first brief.** Now: the slice 2 cases are only inline tests
   in `SemanticTests`. Required: every row of the first brief's "Golden fixtures" table as a
   directory `Fixtures/invalid/<name>/` (`rig.yaml`, `agents/impl/agent.yaml`, `rig.env.yaml`,
   `expected/diagnostics.txt`), built from `Fixtures/valid/minimal` with the change shown, and
   the expected text exactly as shown there. Write the hint lines as `  hint: …`, put a final
   newline after the last line, and repeat the full hint where the table says "same hint" or
   "+ hint". Also add `Fixtures/valid/full` (below) and a `ValidTests` test that loads it and
   expects zero diagnostics. Keep the inline `SemanticTests`; delete an inline test only if a
   fixture now covers exactly the same thing.

## New fixtures (base = `Fixtures/valid/minimal`, one change each)

All of these are in addition to the 42 fixtures from fix 4.

| Fixture `invalid/…` | Change | `expected/diagnostics.txt` |
|---|---|---|
| `AIK4020-schema-value` | rig 11: `    checkout: sk-ant-example` | `rig.yaml:11:15: error AIK4020: credential-like value (Anthropic API key)` + `  hint: never put secrets in rig files; name the secret and bind it in rig.env.yaml` |
| `AIK4020-rig-name` | rig 3: `name: sk-ant-example` | `rig.yaml:3:7: error AIK4020: credential-like value (Anthropic API key)` + same hint (nothing else: fix 2 removes the AIK5001) |
| `AIK4020-seat-id` | rig 9: `  - id: sk-ant-example`; agent: remove 5–6 | `rig.yaml:9:9: error AIK4020: credential-like value (Anthropic API key)` + same hint (nothing else: fix 2 removes the AIK4001) |
| `AIK4020-repo-name` | rig 6: `    - name: sk-ant-example` | `rig.yaml:6:13: error AIK4020: credential-like value (Anthropic API key)` + same hint (nothing else: fix 2 removes the AIK5004 and the AIK5006) |
| `AIK2004-seat-repos-damaged` | rig insert after 7: `    - name: lib`, `      url: https://github.com/example/lib.git`; add 14: `    repos: [lib, Bad]` | `rig.yaml:14:18: error AIK2004: invalid value 'Bad' for field 'repos[1]' in seat 'impl'` + `  hint: must match ^[a-z][a-z0-9._-]{0,63}$` (nothing else: fix 3 skips the AIK5004 for `lib`) |

Use only the fake value `sk-ant-example`; do not write longer token-like strings.

## `Fixtures/valid/full` (exact content, zero diagnostics)

`rig.yaml`:

```yaml
apiVersion: aiakos.dev/v1
kind: Rig
name: demo
workspace:
  repos:
    - name: app
      url: https://github.com/example/app.git
    - name: lib
      url: git@github.com:example/lib.git
seats:
  - id: impl
    agent_ref: local:agents/impl
    checkout: shared
  - id: review
    agent_ref: local:agents/impl
    repos: [app]
    workdir_repo: app
    requires: {auth: api-key, secrets: [anthropic_api_key]}
  - id: pm
    kind: human
    description: Product owner
```

`agents/impl/agent.yaml`:

```yaml
apiVersion: aiakos.dev/v1
kind: Agent
name: impl
description: Implements issues
defaults:
  harness: claude-code
harnesses:
  claude-code:
    permission_mode: acceptEdits
    permissions:
      allow: [Edit, "Bash(dotnet test:*)", mcp__github__get_issue]
```

`rig.env.yaml`:

```yaml
apiVersion: aiakos.dev/v1
kind: RigEnv
rig: demo
seat_root: ~/aiakos/seats
placement:
  default_node: local
  seats:
    review: {node: local}
repos:
  app:
    path: /home/dev/app
  lib:
    path: ~/src/lib
secrets:
  anthropic_api_key:
    file: ~/.config/aiakos/secrets/anthropic_api_key
```

## Definition of done

- `dotnet build -c Release` at the repo root: 0 warnings, 0 errors.
- `dotnet test --project tests/Aiakos.Spec.Tests -c Release`: all green, including every
  existing fixture unchanged.
- If the same test still fails after three attempts, stop and report what you tried.
- LF, UTF-8 without BOM, final newline. No `NoWarn`, `#pragma warning disable`, `[SuppressMessage]`,
  `Version=` on `PackageReference`.
- One new commit on the current branch, subject
  `fix(spec): credential redaction, damaged seat repos and slice 2 golden fixtures (#14)`.
  Do not amend the existing commit, do not push, do not open a PR.
