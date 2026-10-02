---
id: 14-2c
title: "#14 slice 2 — review fixes (second round)"
issue: 14
status: approved
route: impl/opencode
date: 2026-10-01
---

# Brief: #14 slice 2 — review fixes (second round)

Follow-up to `artifacts/briefs/14-2-semantic-validation.md` and
`artifacts/briefs/14-2b-review-fixes.md`, which both still apply in full (rules, general rules,
definition of done, out of scope). **Do not read `docs/specs/` or `docs/adr/`.** Touch only
`src/Aiakos.Spec/` and `tests/Aiakos.Spec.Tests/`.

The branch passes all 50 scoring cases. Review found one more problem: a harness value that is
simply wrong is now reported as "planned for M2". Fix it and add the two fixtures below. Change
nothing else.

## Fix

1. **Only `opencode` and `codex` are AIK4002; any other harness value stays slice 1's AIK2004.**
   Now: `SchemaValidator` no longer has `Values = ["claude-code"]` on seat `harness` and
   `defaults.harness`, and `SemanticValidator` reports AIK4002 for every value other than
   `claude-code`. So `harness: foo` gives
   `rig.yaml:12:14: error AIK4002: harness 'foo' in seat 'impl' is not supported in this version (planned for M2)`.
   Required: `claude-code` is valid; `opencode` and `codex` give AIK4002 (as now); any other
   value gives slice 1's AIK2004, in the same form as on `main`:
   `invalid value '<v>' for field 'harness'<where>` + hint `allowed values: claude-code`. Seat
   and `defaults.harness` both follow this rule. A harness value that failed with AIK2004 still
   counts as present for rule 8 (no AIK4001), and no other diagnostic is added for it.

## New fixtures (base = `Fixtures/valid/minimal`, one change each)

Write the hint lines as `  hint: …` and put a final newline after the last line.

| Fixture `invalid/…` | Change | `expected/diagnostics.txt` |
|---|---|---|
| `AIK2004-harness-seat` | rig add 12: `    harness: foo` | `rig.yaml:12:14: error AIK2004: invalid value 'foo' for field 'harness' in seat 'impl'` + `  hint: allowed values: claude-code` |
| `AIK2004-harness-agent` | agent 6: `  harness: foo` | `agents/impl/agent.yaml:6:12: error AIK2004: invalid value 'foo' for field 'harness' in defaults` + `  hint: allowed values: claude-code` (nothing else) |

The existing fixtures `AIK4002-seat` and `AIK4002-agent` must stay green unchanged.

## Definition of done

- `dotnet build -c Release` at the repo root: 0 warnings, 0 errors.
- `dotnet test --project tests/Aiakos.Spec.Tests -c Release`: all green, including every
  existing fixture unchanged.
- If the same test still fails after three attempts, stop and report what you tried.
- LF, UTF-8 without BOM, final newline. No `NoWarn`, `#pragma warning disable`, `[SuppressMessage]`,
  `Version=` on `PackageReference`.
- One new commit on the current branch, subject
  `fix(spec): only opencode and codex are AIK4002 (#14)`.
  Do not amend the existing commits, do not push, do not open a PR.
