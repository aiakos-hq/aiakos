---
id: 13-2
title: "#13 slice 2 — seat model migration and `SeatQueries`"
issue: 13
status: draft
route: impl/sonnet
paths: [src/Aiakos.Data/, tests/Aiakos.Data.Tests/, tests/Aiakos.Orchestrator.Tests/]
max_outputs: 9
date: 2026-10-03
---

# Brief: #13 slice 2 — seat model migration and `SeatQueries`

Part of #13. Self-contained. **Do not read `docs/specs/` or `docs/adr/`**, with one exception:
`docs/specs/0006-seat-actor.md` lines 739–977 (the SQL from `-- Rig instance` to the
`seat_finding_one_open` index). Read only those lines. Read `src/Aiakos.Data/` and
`tests/Aiakos.Data.Tests/` first; they define the migration, repository and test conventions you
must keep. Where the brief is silent, pick the simplest behaviour and say so in the commit body.

## Goal

The nine seat tables exist after migration, and `SeatQueries` reads them for `ps` and for
polling a launch or a command. Reads only: nothing in this slice writes seat data outside tests.

## Files to create or touch (nothing else)

| Path | What |
|---|---|
| `src/Aiakos.Data/Migrations/0002_seat_model.sql` | The spec's SQL block, **byte for byte**. If `0002_*.sql` already exists, use the next free number |
| `src/Aiakos.Data/Seats/SeatQueries.cs` | The four queries |
| `src/Aiakos.Data/Seats/SeatReadModels.cs` | The read records below |
| `tests/Aiakos.Data.Tests/SeatModelMigrationTests.cs`, `SeatQueriesTests.cs` | Tests (below) |
| `tests/Aiakos.Data.Tests/Infrastructure/SeatSeed.cs` | Test-only helper that inserts rows with plain SQL |
| An existing test that counts or names applied migration scripts | Only that expectation |

No new packages. No `Version=` on `PackageReference`, no `NoWarn`, no `#pragma warning disable`,
no `[SuppressMessage]`. Never edit `0001_tenant.sql`.

## Public surface (exact names; the CLI's API and the SeatActor slices compile against them)

```csharp
namespace Aiakos.Data.Seats;

public sealed class SeatQueries(NpgsqlDataSource dataSource)
{
    public Task<IReadOnlyList<SeatStatusRow>> ListAsync(Guid tenantId, string? rigName, CancellationToken cancellationToken);
    public Task<SeatDetail?> GetDetailAsync(Guid tenantId, string address, CancellationToken cancellationToken);
    public Task<SeatLaunchRow?> GetLaunchAsync(Guid tenantId, Guid launchId, CancellationToken cancellationToken);
    public Task<SeatCommandRow?> GetCommandAsync(Guid tenantId, Guid commandId, CancellationToken cancellationToken);
}

public sealed record SeatStatusRow(
    Guid SeatId, string Address, string Rig, string Member, string Kind, string? Harness, string? Node, string Desired,
    string? Session, string? SessionReason, DateTime? SessionSince,
    string? Activity, string? ActivityDetail, string? ActivityReason, DateTime? ActivitySince,
    string? Resumability, string? ResumabilityReason, DateTime? ResumabilitySince,
    string? NativeSessionId, Guid? LaunchId, string? LaunchOutcome, string? LaunchDecision,
    bool SpecDrift, string? PendingOp, string? LastDeliveryOutcome,
    int? ContextUsedPercent, string? Model, DateTime? LastEventAt,
    long OpenFindings, string? WorstSeverity);

public sealed record SeatDetail(SeatStatusRow Seat, SeatLaunchRow? Launch,
    IReadOnlyList<SeatTransitionRow> Transitions, IReadOnlyList<SeatFindingRow> Findings,
    IReadOnlyList<SeatCommandRow> Deliveries);

public sealed record SeatLaunchRow(
    Guid LaunchId, Guid SeatId, string Mode, string Decision, string DecidedBy, string? DecisionNote,
    string NodeName, string SpecHash, string BindingHash, string? Outcome, string? OutcomeReason,
    string? ObservedSessionId, int? ExitCode, int? ExitSignal, string? Evidence,
    DateTime RequestedAt, DateTime? OutcomeAt, DateTime? EndedAt, string? EndReason);

public sealed record SeatCommandRow(
    Guid CommandId, Guid SeatId, Guid? LaunchId, string Kind, string Status, string? Outcome,
    string? ErrorReason, bool Forced, string? TurnId, string? Result, string RequestedBy,
    DateTime CreatedAt, DateTime? SentAt, DateTime? CompletedAt);

public sealed record SeatTransitionRow(
    string Axis, bool Reported, string FromValue, string ToValue, string? Reason, string CauseType,
    Guid? LaunchId, DateTime At);

public sealed record SeatFindingRow(
    Guid FindingId, string Kind, string Severity, string Summary, int Occurrences,
    DateTime FirstSeenAt, DateTime LastSeenAt, Guid? LaunchId);
```

## Changes to earlier behaviour

- C1. An existing test that counts or names the applied migration scripts: only that expectation
  changes.

- C2. R9 replaces R3's direct integer cast: invalid context percentages give null in
  list and detail reads. Earlier valid-value results and all other fields stay unchanged.
- C3. R10 defines string input handling: malformed strings give no match, and a null
  address gives null rather than selecting another seat or throwing. Earlier valid-string
  results stay unchanged.

## Rules

R1. **Migration verbatim.** The file is the spec block unchanged: same tables, columns, checks,
   indexes, comments and order. Do not "fix" or reformat it. LF, final newline.
R2. **Tenant first.** Every query has `tenant_id = @tenantId` on the row it starts from and joins
   every other table on `tenant_id` as well. No method works without a tenant ID.
R3. **`ListAsync`** is one SQL statement and one round trip. One row per seat with
   `retired_at IS NULL`, agent and human, ordered by rig name then `member`; `rigName` null means
   all rigs. Columns and joins:
   - `seat` joined to `rig`; `seat_state`, `seat_session` (by `current_session_id`) and
     `seat_launch` (by `current_launch_id`) are `LEFT JOIN`s, so a human seat and a seat that
     was never launched come back with nulls.
   - `Node` = `seat.node_name`. `NativeSessionId` from `seat_session`. `LaunchOutcome` and
     `LaunchDecision` from `seat_launch`.
   - `SpecDrift` = the seat has a current launch, `seat_state.session` is `starting`, `present`
     or `unknown`, and the launch's `spec_hash` or `binding_hash` differs from the seat's.
     Otherwise false, never null, including when `current_launch_id` is null even if
     `seat_state.session` is `starting`, `present` or `unknown`.
   - `PendingOp` = `kind` of the oldest `seat_command` of the seat with status `pending` or
     `sent` (by `created_at`); null when none.
   - `LastDeliveryOutcome` = `outcome` of the newest command of kind `deliver`.
   - `ContextUsedPercent` follows R9; do not use an unchecked SQL integer cast. `Model` =
     `usage ->> 'model_id'`. A missing key or a null `usage` gives null, never 0.
   - `OpenFindings` = number of `seat_finding` rows of the seat with status `open` (0 when
     none). `WorstSeverity` = `error`, else `warning`, else `info` among them; null when none.
R4. **`GetDetailAsync`** finds the seat by `address` (not retired) and returns null when there is
   none. `Seat` is the same row `ListAsync` gives. `Launch` is the current launch or null.
   `Transitions`: the newest 20 by `at`, newest first. `Findings`: status `open`, `error` first,
   then `warning`, then `info`, each newest `last_seen_at` first. `Deliveries`: the newest 5
   commands of kind `deliver`, newest first. Several statements on one connection are fine.
R5. **`GetLaunchAsync`, `GetCommandAsync`**: the row with that ID and tenant, or null.
R6. **Never returned:** `seat_command.payload` (it holds the delivery body), `seat_launch.seat_token_hash`,
   `seat_event.raw`. `SeatCommandRow.Result` is `result::text`.
R7. **Conventions.** As in `TenantRepository`: raw string SQL constants, Dapper with
   `CommandDefinition` and the cancellation token, `ConfigureAwait(false)`, XML summary on public
   types. Timestamps are `DateTime` in UTC.
R8. **Read only.** No `INSERT`, `UPDATE` or `DELETE` anywhere under `src/` in this slice.

R9. **Context percentage parsing.** In both `ListAsync` and the `Seat` of `GetDetailAsync`,
   obtain `usage ->> 'context_used_percent'` and interpret it as a signed base-10 32-bit integer
   using the semantics of `Int32.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture)`.
   Return the parsed integer, including 0 and the Int32 endpoints; do not clamp to 0–100.
   Return null for a missing key, null `usage`, JSON null, non-integer text (including `42.5`
   and `42.0`), non-numeric text, objects, arrays, booleans and values outside Int32 range.
   Numeric strings such as `"42"` remain accepted. Invalid values do not throw, exclude the seat,
   or alter other returned fields; one invalid seat cannot fail the tenant's list.
R10. **String input, no exceptions.** No string value supplied as `rigName` or `address` may
   throw because of its contents. `ListAsync(tenantId, null, ct)` still means all rigs.
   `GetDetailAsync(tenantId, null!, ct)` rejects the missing address by returning null, never
   a seat and never an exception. A string containing NUL or an unpaired UTF-16 surrogate
   cannot match a database value: return an empty list for `rigName`, or null for `address`.
   All other strings are literal, parameterized exact matches (no wildcard interpretation);
   empty, nonexistent, SQL-looking and Unicode strings return empty/null when not found.
   Do not trim or normalize inputs. This rule concerns input contents, not cancellation or
   infrastructure failures.

## Expected outputs: seed and exact results

`SeatSeed` inserts one scenario into a fresh migrated database (IDs from `Guid.CreateVersion7()`):
tenant **T** = the default tenant, tenant **U** = a second tenant inserted by the test.

| Seat (rig `demo`, tenant T unless said) | Rows |
|---|---|
| `impl` agent, running | `seat_state` present / working (detail `tool:Bash`) / resumable, `usage` `{"context_used_percent": 42, "model_id": "opus"}`; a session; a `resume` launch with outcome `ready` and the seat's hashes; commands: `start` completed, `deliver` completed `confirmed` (older), `deliver` `sent` (newer, no outcome); findings: `turn-failed` error open, `activity-stale` warning open, `observation-gap` warning resolved; 25 transitions |
| `review` agent, never launched | `seat_state` absent / none / none, version 0, `usage` null; nothing else |
| `drift` agent, running | present / idle / fresh-only; launch `spec_hash` differs from the seat's |
| `stale` agent, stopped | absent / none / resumable; its last launch has an old `spec_hash` |
| `lead` human | `seat` row only |
| `gone` agent, retired | `retired_at` set |
| `impl` in rig `other` | same tenant, another rig |
| `impl` in rig `demo`, tenant U | another tenant, same rig name and address |

| ID | Call | Expected |
|---|---|---|
| `L1` | `ListAsync(T, null)` | 6 rows in order `demo`: `drift`, `impl`, `lead`, `review`, `stale`; then `other`: `impl` |
| `L2` | `ListAsync(T, "demo")` | those 5; `ListAsync(T, "nope")`: empty; `ListAsync(U, null)`: exactly U's one row |
| `L3` | row `impl@demo` | `Session` `present`, `Activity` `working`, `ActivityDetail` `tool:Bash`, `Resumability` `resumable`, `LaunchOutcome` `ready`, `LaunchDecision` `resume`, `SpecDrift` false, `PendingOp` `deliver`, `LastDeliveryOutcome` null (the newest delivery has none), `ContextUsedPercent` 42, `Model` `opus`, `OpenFindings` 2, `WorstSeverity` `error` |
| `L4` | row `review@demo` | `Session` `absent`, `Activity` `none`, `LaunchId` null, `SpecDrift` false, `ContextUsedPercent` null, `OpenFindings` 0, `WorstSeverity` null |
| `L5` | row `lead@demo` | `Kind` `human`, `Harness` null, `Session` null, `SpecDrift` false, `OpenFindings` 0 |
| `L6` | rows `drift`, `stale` | `SpecDrift` true; false |
| `D1` | `GetDetailAsync(T, "impl@demo")` | `Launch.Mode` `resume`; 20 transitions, newest first; findings `turn-failed` then `activity-stale`; 2 deliveries, newest first |
| `D2` | `GetDetailAsync(T, "gone@demo")`, `(T, "nope@demo")`, `(U, "review@demo")` | null |
| `Q1` | `GetLaunchAsync(U, <impl@demo launch of T>)`, `GetCommandAsync(U, <T's command>)` | null |

## Tests (`tests/Aiakos.Data.Tests`)

Same fixture pattern as `TenantRepositoryTests` (`IClassFixture<DatabaseFixture>`, migrate in
`InitializeAsync`). They need Docker running.

- T1. `SeatModelMigrationTests`: the nine tables exist in schema `aiakos` (`rig`, `seat`,
  `seat_session`, `seat_launch`, `seat_command`, `seat_event`, `seat_transition`, `seat_state`,
  `seat_finding`); `seat_event` has a unique constraint on exactly `(tenant_id,
  node_instance_id, seat_id, seq)`; a second `seat_event` insert with the same key and
  `ON CONFLICT DO NOTHING` affects 0 rows; `seat_state` rejects `session = 'running'` (check
  violation `23514`), `session = 'unknown'` without `session_reason`, and `session = 'absent'`
  with `activity = 'idle'`; a second `open` finding for the same seat and kind is a unique
  violation (`23505`), while a `resolved` one plus a new `open` one is accepted; a `seat` row
  whose `rig_id` belongs to another tenant is a foreign key violation (`23503`). The existing
  rule 6 guard test must pass unchanged.

`SeatQueriesTests` holds the expected outputs above, one test per row.

- T2. A test that serialises every returned record of the `impl@demo` detail with
  `System.Text.Json` and asserts the text does not contain the seeded delivery body
  `SENTINEL-BODY` nor the seeded token hash bytes.

- T3. `SeatQueriesTests`: in the seeded scenario, replace only `impl@demo`'s
  `usage.context_used_percent` in turn with JSON values `42.5`, `42.0`, `"n/a"`,
  `99999999999`, `-2147483649`, `true`, `[]`, `{}`, and `null`, then with a missing key and
  null `usage`. For each case, `ListAsync(T, null)` returns all 6 rows and the impl row's
  `ContextUsedPercent` is null; `GetDetailAsync(T, "impl@demo").Seat.ContextUsedPercent`
  is null. No call throws. Also test `0`, `-2147483648`, `2147483647`, and `"42"`:
  list and detail return exactly 0, -2147483648, 2147483647, and 42 respectively.
- T4. `SeatQueriesTests`: for each input `"a\0b"`, `""`, `"nope"`, `"'; SELECT 1; --"`,
  `"😀%_"`, and a string consisting of the unpaired surrogate `\uD800`,
  `ListAsync(T, input)` returns an empty list and `GetDetailAsync(T, input)` returns null,
  without throwing. `GetDetailAsync(T, null!)` and `GetDetailAsync(U, null!)` both return
  null without throwing; `ListAsync(T, null)` still returns 6 rows.
- T5. `SeatQueriesTests`: set `review@demo`'s state in turn to `starting`, `present`, and
  `unknown` (reason `acceptance-unknown` for the last), with activity `idle`, and keep
  `current_launch_id` null. For each state, its list row and detail's `Seat` have
  `LaunchId` null and `SpecDrift` false (never null); detail's `Launch` is null.

## Definition of done

- `dotnet build -c Release` at the repo root: 0 warnings, 0 errors.
- `dotnet test --project tests/Aiakos.Data.Tests -c Release`: all green (Docker required).
  `dotnet test --project tests/Aiakos.Orchestrator.Tests -c Release` still green: it applies the
  same migrations.
- `git grep -nE "(UPDATE|DELETE)[^;]*seat_(event|transition)" -- src` prints nothing.
- If the same test still fails after three attempts, stop and report what you tried.
- All files LF, UTF-8 without BOM, final newline.
- One commit on the current branch, subject
  `feat(data): seat model migration and SeatQueries (#13)`. The body lists every choice made
  where the brief was silent, and has the sentence "Risks: this slice checks none of #13's open
  risks; 0006-RK8 (event log growth) is measured in stage B." Do not push, do not open a PR.

## Out of scope (do not implement, do not stub)

Any write path (repositories for the SeatActor, the `up` API, creating `seat_state` rows); the
state machine and actors; rig revisions; the node token table; API endpoints and the CLI;
partitioning or retention of `seat_event`; changes to `docs/`, `CLAUDE.md`, CI, or any project
other than `Aiakos.Data` and its tests.
