---
id: 13-2
title: "#13 slice 2 — seat model migration and `SeatQueries`"
issue: 13
status: draft
route: impl/sonnet
date: 2026-10-01
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

## Rules

1. **Migration verbatim.** The file is the spec block unchanged: same tables, columns, checks,
   indexes, comments and order. Do not "fix" or reformat it. LF, final newline.
2. **Tenant first.** Every query has `tenant_id = @tenantId` on the row it starts from and joins
   every other table on `tenant_id` as well. No method works without a tenant ID.
3. **`ListAsync`** is one SQL statement and one round trip. One row per seat with
   `retired_at IS NULL`, agent and human, ordered by rig name then `member`; `rigName` null means
   all rigs. Columns and joins:
   - `seat` joined to `rig`; `seat_state`, `seat_session` (by `current_session_id`) and
     `seat_launch` (by `current_launch_id`) are `LEFT JOIN`s, so a human seat and a seat that
     was never launched come back with nulls.
   - `Node` = `seat.node_name`. `NativeSessionId` from `seat_session`. `LaunchOutcome` and
     `LaunchDecision` from `seat_launch`.
   - `SpecDrift` = the seat has a current launch, `seat_state.session` is `starting`, `present`
     or `unknown`, and the launch's `spec_hash` or `binding_hash` differs from the seat's.
     Otherwise false, never null.
   - `PendingOp` = `kind` of the oldest `seat_command` of the seat with status `pending` or
     `sent` (by `created_at`); null when none.
   - `LastDeliveryOutcome` = `outcome` of the newest command of kind `deliver`.
   - `ContextUsedPercent` = `(usage ->> 'context_used_percent')::int`, `Model` =
     `usage ->> 'model_id'`. A missing key or a null `usage` gives null, never 0.
   - `OpenFindings` = number of `seat_finding` rows of the seat with status `open` (0 when
     none). `WorstSeverity` = `error`, else `warning`, else `info` among them; null when none.
4. **`GetDetailAsync`** finds the seat by `address` (not retired) and returns null when there is
   none. `Seat` is the same row `ListAsync` gives. `Launch` is the current launch or null.
   `Transitions`: the newest 20 by `at`, newest first. `Findings`: status `open`, `error` first,
   then `warning`, then `info`, each newest `last_seen_at` first. `Deliveries`: the newest 5
   commands of kind `deliver`, newest first. Several statements on one connection are fine.
5. **`GetLaunchAsync`, `GetCommandAsync`**: the row with that ID and tenant, or null.
6. **Never returned:** `seat_command.payload` (it holds the delivery body), `seat_launch.seat_token_hash`,
   `seat_event.raw`. `SeatCommandRow.Result` is `result::text`.
7. **Conventions.** As in `TenantRepository`: raw string SQL constants, Dapper with
   `CommandDefinition` and the cancellation token, `ConfigureAwait(false)`, XML summary on public
   types. Timestamps are `DateTime` in UTC.
8. **Read only.** No `INSERT`, `UPDATE` or `DELETE` anywhere under `src/` in this slice.

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

| Call | Expected |
|---|---|
| `ListAsync(T, null)` | 6 rows in order `demo`: `drift`, `impl`, `lead`, `review`, `stale`; then `other`: `impl` |
| `ListAsync(T, "demo")` | those 5; `ListAsync(T, "nope")`: empty; `ListAsync(U, null)`: exactly U's one row |
| row `impl@demo` | `Session` `present`, `Activity` `working`, `ActivityDetail` `tool:Bash`, `Resumability` `resumable`, `LaunchOutcome` `ready`, `LaunchDecision` `resume`, `SpecDrift` false, `PendingOp` `deliver`, `LastDeliveryOutcome` null (the newest delivery has none), `ContextUsedPercent` 42, `Model` `opus`, `OpenFindings` 2, `WorstSeverity` `error` |
| row `review@demo` | `Session` `absent`, `Activity` `none`, `LaunchId` null, `SpecDrift` false, `ContextUsedPercent` null, `OpenFindings` 0, `WorstSeverity` null |
| row `lead@demo` | `Kind` `human`, `Harness` null, `Session` null, `SpecDrift` false, `OpenFindings` 0 |
| rows `drift`, `stale` | `SpecDrift` true; false |
| `GetDetailAsync(T, "impl@demo")` | `Launch.Mode` `resume`; 20 transitions, newest first; findings `turn-failed` then `activity-stale`; 2 deliveries, newest first |
| `GetDetailAsync(T, "gone@demo")`, `(T, "nope@demo")`, `(U, "review@demo")` | null |
| `GetLaunchAsync(U, <impl@demo launch of T>)`, `GetCommandAsync(U, <T's command>)` | null |

## Tests (`tests/Aiakos.Data.Tests`)

Same fixture pattern as `TenantRepositoryTests` (`IClassFixture<DatabaseFixture>`, migrate in
`InitializeAsync`). They need Docker running.

- `SeatModelMigrationTests`: the nine tables exist in schema `aiakos` (`rig`, `seat`,
  `seat_session`, `seat_launch`, `seat_command`, `seat_event`, `seat_transition`, `seat_state`,
  `seat_finding`); `seat_event` has a unique constraint on exactly `(tenant_id,
  node_instance_id, seat_id, seq)`; a second `seat_event` insert with the same key and
  `ON CONFLICT DO NOTHING` affects 0 rows; `seat_state` rejects `session = 'running'` (check
  violation `23514`), `session = 'unknown'` without `session_reason`, and `session = 'absent'`
  with `activity = 'idle'`; a second `open` finding for the same seat and kind is a unique
  violation (`23505`), while a `resolved` one plus a new `open` one is accepted; a `seat` row
  whose `rig_id` belongs to another tenant is a foreign key violation (`23503`). The existing
  rule 6 guard test must pass unchanged.
- `SeatQueriesTests`: the table above, one test per row.
- A test that serialises every returned record of the `impl@demo` detail with
  `System.Text.Json` and asserts the text does not contain the seeded delivery body
  `SENTINEL-BODY` nor the seeded token hash bytes.

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
