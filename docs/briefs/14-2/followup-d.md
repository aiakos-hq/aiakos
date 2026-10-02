# Brief: #14 slice 2 — review fixes (third round)

This follows `artifacts/briefs/14-2-semantic-validation.md`, `artifacts/briefs/14-2b-review-fixes.md`
and `artifacts/briefs/14-2c-review-fixes.md`. All three still apply in full: rules, general rules,
definition of done and out of scope. **Do not read `docs/specs/` or `docs/adr/`.** Touch only
`src/Aiakos.Spec/` and `tests/Aiakos.Spec.Tests/`.

The branch passes all 50 scoring cases and the 14-2c fixtures. Review ran three more cases that
give the wrong output. Fix them, add the fixtures below and change nothing else.

## Fixes

1. **`harness: foo` on a seat with an invalid `kind` still gets slice 1's AIK2004.**
   Now: the harness check sits in `SemanticValidator.ValidateHarness`, which is skipped for
   seats whose `kind` failed slice 1. So `kind: robot` together with `harness: foo` gives only
   `rig.yaml:12:11: error AIK2004: invalid value 'robot' for field 'kind' in seat 'impl'`.
   Required: as on `main`, any harness value other than `claude-code`, `opencode` and `codex`
   gets `invalid value '<v>' for field 'harness'<where>` + hint `allowed values: claude-code`,
   whatever the seat's `kind`. Every other seat rule still skips such a seat (general rule 3).
2. **The `<where>` suffix for a seat without a valid id is the slice 1 one.** Now:
   `SeatWhere` returns ` in seats`, so `id: Impl` with `harness: foo` gives
   `rig.yaml:12:14: error AIK2004: invalid value 'foo' for field 'harness' in seats`.
   Required: when the seat's `id` is missing or failed slice 1, `<where>` is ` in seats[<i>]`.
   `<i>` is the 0-based index of the seat in `seats`, the same suffix slice 1 prints for that
   seat. This applies to every message that uses `<where>` for a seat.
3. **A seat's agent is the agent file its `agent_ref` resolves to, not the first seat's
   text.** Now: `FindAgent` matches `agent.AgentReference == reference` (ordinal text), so a
   second seat with `local:./agents/impl` next to `local:agents/impl` finds no agent. It then
   gets no AIK4001 and no AIK4012, and `anthropic_api_key` is not counted as required for it.
   Required: resolve each seat's `agent_ref` to the agent directory the way `RigLoader` does
   when it loads the file. Compare directories with the same comparer `RigLoader` uses for
   `agentPaths`. Every seat whose reference resolves to a loaded directory uses that agent
   document.

## New fixtures (base = `Fixtures/valid/minimal`, one change each)

Write the hint lines as `  hint: …` and put a final newline after the last line.

| Fixture `invalid/…` | Change | `expected/diagnostics.txt` |
|---|---|---|
| `AIK2004-harness-invalid-kind` | rig add 12–13: `    kind: robot`, `    harness: foo` | `rig.yaml:12:11: error AIK2004: invalid value 'robot' for field 'kind' in seat 'impl'` + `  hint: allowed values: agent, human` + `rig.yaml:13:14: error AIK2004: invalid value 'foo' for field 'harness' in seat 'impl'` + `  hint: allowed values: claude-code` (nothing else) |
| `AIK2004-harness-invalid-id` | rig 9: `  - id: Impl`; rig add 12: `    harness: foo` | `rig.yaml:9:9: error AIK2004: invalid value 'Impl' for field 'id' in seats[0]` + `  hint: must match ^[a-z][a-z0-9-]{0,23}$` + `rig.yaml:12:14: error AIK2004: invalid value 'foo' for field 'harness' in seats[0]` + `  hint: allowed values: claude-code` (nothing else) |
| `AIK4001-agent-ref-spelling` | agent: remove 5–6; rig add 12–13: `  - id: review`, `    agent_ref: local:./agents/impl` | `rig.yaml:9:5: error AIK4001: seat 'impl' has no harness` + `  hint: set harness on the seat or defaults.harness in agents/impl/agent.yaml` + `rig.yaml:12:5: error AIK4001: seat 'review' has no harness` + `  hint: set harness on the seat or defaults.harness in agents/impl/agent.yaml` |
| `AIK4012-agent-ref-spelling` | rig add 12–14: `  - id: review`, `    agent_ref: local:./agents/impl`, `    requires: {auth: api-key}` | `rig.yaml:14:22: warning AIK4012: seat 'review' uses auth: api-key but does not list secret 'anthropic_api_key'` + `  hint: add anthropic_api_key to requires.secrets` + `rig.env.yaml:1:1: error AIK5004: secret 'anthropic_api_key' has no source` + `  hint: set secrets.anthropic_api_key.file` |

All existing fixtures, including `AIK2004-harness-seat`, `AIK2004-harness-agent`,
`AIK4002-seat` and `AIK4002-agent`, must stay green unchanged.

## Definition of done

- `dotnet build -c Release` at the repo root: 0 warnings, 0 errors.
- `dotnet test --project tests/Aiakos.Spec.Tests -c Release`: all green, including every
  existing fixture unchanged.
- If the same test still fails after three attempts, stop and report what you tried.
- LF, UTF-8 without BOM, final newline. No `NoWarn`, `#pragma warning disable`, `[SuppressMessage]`,
  `Version=` on `PackageReference`.
- One new commit on the current branch, subject
  `fix(spec): harness and seat context for invalid seats, agent lookup by directory (#14)`.
  Do not amend the existing commits, do not push, do not open a PR.
