---
id: 13-1
title: "#13 slice 1 — pure seat state machine and harness state profile"
issue: 13
status: draft
route: impl/sonnet
date: 2026-10-01
---

# Brief: #13 slice 1 — pure seat state machine and harness state profile

**Start only after #10 slice 1 is merged** (the proto enums in `Aiakos.Contracts.Node.V1`).

This brief is self-contained. **Do not read `docs/specs/` or `docs/adr/`**, with one exception:
`docs/specs/0006-seat-actor.md` lines 379–638 (axis definitions, transition tables, event
pipeline) and 707–726 (findings). Those tables are normative; this brief says how to read them
where they are silent or disagree. Where the brief and those lines disagree, **the brief wins**;
say so in the commit body. Where both are silent, do the thing that changes no axis and opens no
finding, and say so.

## Goal

The whole seat state logic as pure functions with no Akka, database, clock or random: three axes
(session, activity, resumability), the reported/last-known overlay, the event pipeline (epoch,
duplicate, gap, stale launch, late), the `up` / `down` / `send` decisions, and the delivery
outcome table. The actor (a later slice) only does I/O around it.

## Files to create or touch (nothing else)

| Path | What |
|---|---|
| `src/Aiakos.Orchestrator/Seats/*.cs` | Everything below. File-scoped namespace `Aiakos.Orchestrator.Seats` |
| `tests/Aiakos.Orchestrator.Tests/Seats/*.cs` | Tests (below). Namespace `Aiakos.Orchestrator.Tests.Seats` |
| `tests/Aiakos.Orchestrator.Tests/Aiakos.Orchestrator.Tests.csproj` | Add `<PackageReference Include="CsCheck" />` |
| `Directory.Packages.props` | In `<ItemGroup Label="Tests">`: `<PackageVersion Include="CsCheck" Version="…" />`, latest stable, exact |

No `Version=` on `PackageReference`, no `NoWarn`, no `#pragma warning disable`, no
`[SuppressMessage]`. No reference to Akka, Npgsql, `DateTimeOffset.UtcNow`, `Guid.NewGuid`,
`Random` or logging anywhere under `Seats/`.

## Public surface (exact names; #12 and later #13 slices compile against them)

```csharp
namespace Aiakos.Orchestrator.Seats;

public interface IHarnessStateProfile            // implemented for Claude Code in #12
{
    string Harness { get; }
    string NewNativeSessionId();                 // never called by the state machine
    bool IsValidNativeSessionId(string id);
    bool IsReadiness(HarnessEventKind kind, IReadOnlyDictionary<string, string> attributes);
    bool IsConversationEvidence(HarnessEventKind kind, IReadOnlyDictionary<string, string> attributes);
    bool IsSessionRotation(HarnessEventKind kind, IReadOnlyDictionary<string, string> attributes);
    bool FreshRelaunchReusesSessionId { get; }
    bool EmitsInputResolved { get; }
    TimeSpan ReadyTimeout { get; }
    TimeSpan ConfirmTimeout { get; }
    TimeSpan QuietTimeout { get; }
}

public enum SessionValue { Absent, Starting, Present, Exited, Unknown }
public enum ActivityValue { None, Idle, Working, NeedsInput, Unknown }
public enum ResumabilityValue { None, FreshOnly, Resumable, Lost, Unknown }
public enum SeatOverlay { NodeLinkLost, OrchestratorRestarted }
public enum SeatDesired { Down, Up }
public enum EventDisposition { Applied, Late, StaleLaunch, Orphan, Evidence, Duplicate }

public sealed record CurrentLaunch(Guid LaunchId, LaunchMode Mode, bool ReusedNativeSessionId, bool StopRequested);

public sealed record SeatState   // init-only properties, all required; mirrors the seat_state row
{
    // reported:   Session, SessionReason, SessionSince, Activity, ActivityDetail, ActivityReason,
    //             ActivitySince, Resumability, ResumabilityReason, ResumabilitySince
    // overlay:    SeatOverlay? Overlay; KnownSession, KnownSessionReason, KnownActivity,
    //             KnownActivityDetail, KnownActivityReason
    // context:    SeatDesired Desired; CurrentLaunch? Launch; string? NativeSessionId;
    //             string? PendingInputRequest; ActivityValue? PreCompactionActivity; bool ReadinessSeen
    // sequencing: Guid? NodeInstanceId; long NextSeq; long LastSourceSeq; long? CatchUpSeq;
    //             DateTimeOffset? LastEventAt
    public static SeatState Initial(DateTimeOffset now);   // absent / none / none, Desired Down, NextSeq 1
}

public abstract record SeatInput;
public sealed record UpRequested(bool Fresh, bool NodeConnected, Guid NewLaunchId, string NewNativeSessionId) : SeatInput;
public sealed record DownRequested : SeatInput;
public sealed record SendRequested(bool Force, bool NodeConnected, bool DeliveryInFlight) : SeatInput;
public sealed record NodeAttached(Guid NodeInstanceId, SeatInventoryEntry? Inventory) : SeatInput;
public sealed record SeatInventoryEntry(Guid? LaunchId, SessionLifecycle Lifecycle, long LastSeq);
public sealed record NodeLinkLost : SeatInput;
public sealed record OrchestratorRestarted : SeatInput;
public sealed record CommandDispatchFailed(SeatCommandKind Kind) : SeatInput;
public enum SeatCommandKind { Start, Deliver, Keys, Capture, Stop }
public sealed record QuietTimeoutFired : SeatInput;
public sealed record LaunchWatchdogFired : SeatInput;
public sealed record UnknownProlongedFired : SeatInput;
public sealed record EventReceived(Guid NodeInstanceId, long Seq, long SourceSeq, Guid? LaunchId, SeatEventBody Body) : SeatInput;

public abstract record SeatEventBody;
public sealed record HarnessBody(HarnessEventKind Kind, string NativeSessionId, IReadOnlyDictionary<string, string> Attributes) : SeatEventBody;
public sealed record LaunchResultBody(LaunchOutcome Outcome, string Reason, int? ExitCode) : SeatEventBody;
public sealed record StartNotCompletedBody(CommandStatus Status, string Reason) : SeatEventBody;   // REJECTED, FAILED, TIMED_OUT
public sealed record StopResultBody(StopOutcome Outcome) : SeatEventBody;
public sealed record StopNotCompletedBody(CommandStatus Status) : SeatEventBody;
public sealed record CaptureBody(bool PaneDead) : SeatEventBody;
public sealed record OtherCommandResultBody : SeatEventBody;       // deliver and keys results
public sealed record SessionObservedBody(string NativeSessionId, bool MatchesExpected) : SeatEventBody;
public sealed record ProcessExitedBody(int? ExitCode, int? Signal) : SeatEventBody;
public sealed record ObservationGapBody(GapReason Reason) : SeatEventBody;
public sealed record UnknownBody : SeatEventBody;

public sealed record SeatStep(SeatState State, SeatReply? Reply, EventDisposition? Disposition,
    IReadOnlyList<SeatTransition> Transitions, IReadOnlyList<FindingChange> Findings, IReadOnlyList<SeatEffect> Effects);
public sealed record SeatTransition(string Axis, bool Reported, string From, string To, string? Reason, string Rule);
public sealed record FindingChange(string Kind, bool Open);        // Open false = resolve if open
public abstract record SeatReply;
public sealed record Accepted(bool Forced = false) : SeatReply;
public sealed record AlreadyUp : SeatReply;
public sealed record Rejected(string Reason) : SeatReply;
public abstract record SeatEffect;
public sealed record StartLaunch(Guid LaunchId, LaunchMode Mode, string Decision, string NativeSessionId, bool NewSession, bool AbandonPreviousSession) : SeatEffect;
public sealed record DispatchStop(Guid LaunchId) : SeatEffect;
public sealed record RequestCapture(Guid LaunchId) : SeatEffect;
public sealed record AdoptRotatedSession(string NativeSessionId, string PreviousNativeSessionId) : SeatEffect;

public static class SeatStateMachine
{
    public static SeatStep Apply(SeatState state, SeatInput input, IHarnessStateProfile profile, DateTimeOffset now);
}

public enum DeliveryState { Pending, Sent, Confirmed, SubmittedUnconfirmed, NotDelivered, Failed, Unknown }
public abstract record DeliveryInput;
public sealed record DeliveryDispatched : DeliveryInput;
public sealed record DeliveryDispatchFailed : DeliveryInput;
public sealed record DeliveryResultArrived(DeliveryOutcome Outcome) : DeliveryInput;
public sealed record DeliveryNotCompleted(CommandStatus Status) : DeliveryInput;
public sealed record DeliveryNodeReplaced : DeliveryInput;          // node reattached with a new instance
public sealed record DeliveryStep(DeliveryState State, IReadOnlyList<FindingChange> Findings);
public static class DeliveryStateMachine { public static DeliveryStep Apply(DeliveryState state, DeliveryInput input); }

public static class SeatVocabulary   // stored strings; constants for reasons, finding kinds, decisions, rejection reasons
{
    public static string ToStored(SessionValue v);        // absent starting present exited unknown
    public static string ToStored(ActivityValue v);       // none idle working needs-input unknown
    public static string ToStored(ResumabilityValue v);   // none fresh-only resumable lost unknown
    public static string ToStored(SeatOverlay v);         // node-link-lost orchestrator-restarted
    public static string ToStored(EventDisposition v);    // applied late stale-launch orphan evidence; Duplicate throws
    public static string ToStored(HarnessEventKind v);    // lower-kebab without prefix: input-requested
    public static string ToStored(LaunchMode v);          // fresh resume fork
    public static string ToStored(LaunchOutcome v);       // ready failed unknown
    public static string ToStored(StopOutcome v);         // stopped killed not-running
    public static string ToStored(DeliveryOutcome v);     // confirmed submitted-unconfirmed not-delivered
    public static string ToStored(CommandStatus v);       // completed rejected failed timed-out
}
```

Proto enums (`HarnessEventKind`, `LaunchMode`, `LaunchOutcome`, `StopOutcome`, `DeliveryOutcome`,
`CommandStatus`, `SessionLifecycle`, `GapReason`) come from `Aiakos.Contracts.Node.V1`. For every
proto enum, `*_UNSPECIFIED` and undefined numbers are stored as `unknown`.

## Rules

1. **Pure.** `Apply` reads only its arguments. `now` is used only for the `…Since` fields and
   `LastEventAt`. Equal arguments give equal results.
2. **Tables.** Implement S1–S16, A1–A16, U1–U8, the launch mode decision table and the delivery
   table as written. `—` and `n/a` mean: no transition, no finding. Open only findings that a
   cell or rule below names. `SeatTransition.Rule` is the row id (`S11`, `A2`, `U8`), or `R10`,
   `R16`, `R17`, `R18` for derived changes. Emit a transition only when value or reason changes.
3. **Known and reported.** Tables act on `Known*`. Without an overlay, reported = known and each
   change is one transition with `Reported = true`. With an overlay, reported session and
   activity are `unknown` with the overlay's reason; known changes are transitions with
   `Reported = false`. Resumability has no overlay.
4. **R10 always holds** for known and for reported values: activity is `none` exactly when
   session is `absent` or `exited`; activity is `unknown` when session is `starting`
   (`not-ready`) or `unknown` (`session-unknown`). While known session is `starting` or
   `unknown`, activity rows A2–A12 change nothing (the event is still `Applied`); only readiness
   (S11 then A1) leaves that state. This overrides the sentence "evaluated while starting,
   present or unknown".
5. **Entering `present`.** By S11: activity `idle` (A1). By S5 from `starting` without
   `ReadinessSeen`: `unknown/sources-disagree`. By S5 from `unknown`, or by S15: `unknown/observation-gap`.
6. **Event pipeline**, per `EventReceived`, in this order:
   1. *Epoch.* `NodeInstanceId` differs from the state's: when the state's is null, adopt it and
      set `NextSeq = 1`; otherwise apply rule 11 first, adopt it, `NextSeq = 1`.
   2. *Duplicate.* `Seq < NextSeq` → `Duplicate`, state unchanged.
   3. *Gap.* `Seq > NextSeq` → rule 11. Then in every case `NextSeq = Seq + 1`.
   4. *Attribute.* `ObservationGapBody` applies whatever its launch (rule 11). Any other body
      whose `LaunchId` is not the current launch's (or there is none) → `StaleLaunch`, no axis
      change; a `HarnessBody` of kind readiness, `PROMPT_SUBMITTED`, `TOOL_STARTED`,
      `TOOL_FINISHED` or `INPUT_REQUESTED` also opens `orphan-harness`.
   5. *Orphan.* `SESSION_ENDED` while `ReadinessSeen` is false → `Orphan`, no change (S13).
   6. *Order.* See rule 7.
   7. *Apply* session, then activity, then resumability. `CaptureBody`, `OtherCommandResultBody`
      and `UnknownBody` are `Evidence`; a `CaptureBody` with `PaneDead` while known session is
      `present` opens `sources-disagree` and changes no axis.
   Every `HarnessBody` of the current launch, late or not, sets `LastEventAt = now` and resolves
   `activity-stale`.
7. **Stale guard.** Only a `HarnessBody` with `SourceSeq > 0` whose kind is not `TELEMETRY`,
   `OTHER` or unspecified takes part. `SourceSeq <= LastSourceSeq` → `Late`: only U2 and U8 may
   apply. Otherwise `LastSourceSeq = SourceSeq`. `TELEMETRY` and `OTHER` are never late and never
   move `LastSourceSeq`: a statusLine tick that overtakes a `Stop` hook must not make the `Stop`
   late. `LastSourceSeq` is kept across launches.
8. **Readiness and native IDs.** An event is a readiness event only when `profile.IsReadiness`
   is true **and** its `NativeSessionId` equals the state's. It sets `ReadinessSeen`. With another
   ID: U8 when `profile.IsSessionRotation` is true, the attribute `previous_session_id` equals
   the state's ID and `profile.IsValidNativeSessionId(new)`; otherwise U7. A mismatching event is
   never S11 or A1. U8 sets `NativeSessionId` to the new ID, emits `AdoptRotatedSession`, sets
   `ReadinessSeen`, applies A1 and resolves `session-id-mismatch`.
9. **Conversation evidence (U2)** counts only when `profile.IsConversationEvidence` is true and
   the event's `NativeSessionId` equals the state's. It applies to late events too.
10. **Pending input.** A6 stores the attribute `request_id`, or `*` when absent. A5 and A7 resolve
    when the pending value is `*` or equals the event's `tool_use_id` or `request_id`. A2, A10,
    A11 and any change of session clear it. A4 detail is `tool:<tool_name>`.
11. **Lost observation** (sequence gap, `ObservationGapBody`, new epoch, new node instance): open
    `observation-gap`; U6; when known session is `present`, activity → `unknown/observation-gap`
    (A16); when a launch exists and known session is `starting`, `present` or `unknown`, emit
    `RequestCapture`. Never synthesize an event.
12. **`NodeAttached`.** Resolve `node-not-connected`. *Same instance, or the state has none:*
    adopt the ID; with an overlay, set `CatchUpSeq = Inventory?.LastSeq ?? 0` and clear the
    overlay as soon as `NextSeq > CatchUpSeq` (now, or after a later event); the inventory is
    otherwise ignored. *Different instance:* adopt it, `NextSeq = 1`, clear the overlay and
    `CatchUpSeq`, then S15 when the inventory has the current `LaunchId`, else S16, then rule 11.
    S15 lifecycles: `LAUNCHING` → `starting`, `RUNNING` → `present`, `EXITED` → `exited`,
    `UNKNOWN` and anything else → `unknown/orphan-or-running`. S15 resolves `inventory-mismatch`.
13. **Overlays.** `NodeLinkLost` and `OrchestratorRestarted` set the overlay only when known
    session is not `absent` or `exited`; transitions have rule `R16`. Clearing it restores
    reported = known with rule `R17`. Under an overlay a second overlay input changes only the
    overlay kind and reasons.
14. **`UpRequested`**, in this order, on reported values: `NodeConnected` false → `Rejected
    NODE_NOT_CONNECTED` and open `node-not-connected`; session `starting` or `present` →
    `AlreadyUp`, `Desired = Up`; `unknown` → `Rejected SEAT_STATE_UNKNOWN`; otherwise the launch
    mode decision table (`lost` without `Fresh` → `Rejected RESUME_LOST`). Accepted: `Desired =
    Up`; S1; U1 where the table assigns a new session; `Launch = new CurrentLaunch(NewLaunchId,
    mode, reused, false)`; `ReadinessSeen` false; pending input and pre-compaction cleared;
    `NativeSessionId` is `NewNativeSessionId` for a new session, else kept; one `StartLaunch`
    effect (`AbandonPreviousSession` when a session with an ID is replaced). `Fresh` accepted
    resolves `resume-lost` and `session-id-mismatch`. A rejected `up` changes nothing else.
15. **`DownRequested`**: `Desired = Down`; resolve `unexpected-exit`; when a launch exists and
    known session is not `absent`: `DispatchStop` and `StopRequested = true`. Always `Accepted`.
    S3 also resolves `orphan-harness`.
16. **`SendRequested`**, in this order, on reported values: `NodeConnected` false →
    `NODE_NOT_CONNECTED` (open `node-not-connected`); session `unknown` → `SEAT_STATE_UNKNOWN`;
    session not `present` → `SEAT_NOT_PRESENT`; `DeliveryInFlight` → `DELIVERY_IN_FLIGHT`;
    activity `working` → `SEAT_WORKING`; `needs-input` → `SEAT_NEEDS_INPUT`; `unknown` without
    `Force` → `SEAT_ACTIVITY_UNKNOWN`; otherwise `Accepted(Forced: activity was unknown)`. It
    never changes an axis.
17. **Launch results.** S5 or S11 into `present` resolves `launch-unconfirmed`,
    `launch-rejected`, `launch-failed`, `unexpected-exit`. U3 needs `Launch.Mode == RESUME`. U5
    needs mode `FRESH` with `ReusedNativeSessionId`. `LaunchResult FAILED` with another reason in
    mode `RESUME` opens `launch-failed` and leaves resumability. Known session entering `exited`
    while `Desired == Up` and `StopRequested` is false opens `unexpected-exit`.
18. **`CommandDispatchFailed`**: `Start` → S9; `Stop` → S4; others change nothing.
19. **Timers are validated, not trusted.** `QuietTimeoutFired` acts (A15) only when known session
    is `present`, known activity is `working` and `now - LastEventAt >= profile.QuietTimeout`.
    `LaunchWatchdogFired` acts (S10, reason `launch-result-missing`) only when known session is
    `starting`. `UnknownProlongedFired` opens `state-unknown-prolonged` only when reported session
    is `unknown` and `now - SessionSince >= 5 min`; the finding resolves when reported session
    leaves `unknown`. Otherwise each returns the state unchanged.
20. **Other resolutions.** `TURN_ENDED` resolves `turn-failed`; `PROMPT_SUBMITTED` resolves
    `delivery-unconfirmed`. `sources-disagree` and `observation-gap` are never resolved here.
21. **Unrecognized enum values are the honest unknown:** `LaunchOutcome` → S7; `StopOutcome` →
    S4; `HarnessEventKind` → `OTHER`; `SessionLifecycle` → `UNKNOWN`; `CommandStatus` in
    `StartNotCompletedBody` → S10 (`launch-unconfirmed`), in `DeliveryNotCompleted` → `Unknown`.
22. **Delivery table.** Final states absorb every input. `SubmittedUnconfirmed` opens
    `delivery-unconfirmed`.

## Tests (`tests/Aiakos.Orchestrator.Tests/Seats/`)

Plain xUnit v3, no fixture, no Docker, no Akka. Every test ends with the shared assertion
`SeatAssert.Invariants(step)`: rule 4 for known and reported values; a reason on every `unknown`;
no overlay ⇒ reported = known. `TestProfiles.cs` holds two test doubles. `ClaudeLike`: readiness =
`SESSION_STARTED` with `source` in `startup`, `resume`, `fork`, `clear`; evidence =
`PROMPT_SUBMITTED`, `TURN_ENDED`, `TURN_FAILED`; rotation = `SESSION_STARTED` with `source=clear`
and `previous_session_id`; reuses the ID; no `INPUT_RESOLVED`; quiet 10 min. `OpenCodeLike`:
readiness and evidence = `SESSION_STARTED`; no rotation; new ID; emits `INPUT_RESOLVED`.

- `SessionTableTests`, `ActivityTableTests`, `ResumabilityTableTests`: one test per row, named
  by row id (`S05_LaunchResultReady`), covering every column of the row, with values, reasons,
  findings and effects.
- `LaunchDecisionTests`: every resumability value with and without `Fresh`, for both profiles.
- `SendDecisionTests`, `DeliveryTableTests`, `OverlayTests`, `PipelineTests` (each pipeline step,
  rule 7 with a `TELEMETRY` overtaking a `TURN_ENDED`, rule 12 both branches), `TimerTests`.
- `VocabularyTests`: for every value of every proto enum above (`Enum.GetValues`) `ToStored`
  returns a non-empty lower-kebab string, defined values are distinct, and the exact strings in
  the surface comments hold.
- `GoldenScriptTests`, each a list of inputs with the expected axes after every step:
  1. *Permission turn:* readiness; `PROMPT_SUBMITTED`; `TOOL_STARTED` (`Bash`, `t1`);
     `INPUT_REQUESTED` (`request_id=t1`); `OTHER`; `TOOL_FINISHED` (`t1`); `TURN_ENDED`;
     `COMPACTION_STARTED`; `COMPACTED` → idle, working `tool:Bash`, needs-input, same, working,
     idle, working `compacting`, idle; resumability becomes `resumable` at the prompt.
  2. *Kill and resume:* a ready seat with one turn; `ProcessExited(signal 9)` → `exited`,
     `unexpected-exit`; `up` → `RESUME`, decision `resume`; a `SESSION_ENDED` before readiness is
     `Orphan`; readiness → `present`; `LaunchResult READY` changes nothing.
  3. *Failed resume:* as 2, then `ProcessExited(exit 1)` and `LaunchResult FAILED
     RESUME_SESSION_NOT_FOUND` → `exited`, `lost`, `resume-lost`, no `StartLaunch`; `up` →
     `RESUME_LOST`; `up` with `Fresh` → `FRESH`, `fresh-explicit`, new ID, previous abandoned.
  4. *Turn failure:* prompt, then `TURN_FAILED` → idle and `turn-failed`; the next `TURN_ENDED`
     resolves it.
  5. *Exit without `SESSION_ENDED`:* working, then `ProcessExited` → `exited` / `none`.
  6. *Rotation:* idle and `resumable`; `OTHER` (`reason=clear`); `SESSION_STARTED` (`source=clear`,
     new ID, `previous_session_id`) → `present`, idle, `fresh-only`, `AdoptRotatedSession`, no
     `session-id-mismatch`; a prompt with the new ID → `resumable`; `down`, `StopResult`, `up` →
     `RESUME` with the new ID. With an invalid new ID the same event is U7.
  7. *Escape denial:* needs-input and no event → `send` is `SEAT_NEEDS_INPUT`, with `Force` too;
     the next `PROMPT_SUBMITTED` → working.
  8. *Parallel tools:* two `TOOL_STARTED`, `INPUT_REQUESTED` without an ID, `TOOL_FINISHED` of
     the other tool → working (the accepted early resolve); with `request_id` set it stays
     needs-input.
  9. *OpenCode:* `ACTIVE`; `INPUT_REQUESTED p1`; `ACTIVE` (still needs-input); `INPUT_RESOLVED
     p1` → working; `TURN_ENDED` → idle.
- `SeatPropertyTests` (CsCheck, at least 1 000 cases each). The generator builds complete Claude
  turns (prompt, zero or more tool calls with an optional permission request, an optional
  compaction, `TURN_ENDED`), statusLine `TELEMETRY` ticks anywhere, increasing `Seq` and
  `SourceSeq`:
  - any arrival order of the harness events gives the same final axes as `SourceSeq` order, and
    no step has activity `none` while session is `present`;
  - repeating events or whole ranges changes neither the final state nor the number of
    non-`Duplicate` dispositions;
  - dropping a run of `Seq` gives activity `unknown/observation-gap` until the next event that
    the activity table's `unknown` column maps to a value, and turns `fresh-only` into `unknown`;
  - switching `NodeInstanceId` mid-stream applies rule 11 once and restarts `NextSeq`.
- `KnownLimitationTests`: one pinned case where the guard loses history mid-turn (arrival
  `COMPACTED`, `PROMPT_SUBMITTED`, `COMPACTION_STARTED` for `SourceSeq` 3, 1, 2 ends `idle`,
  sorted order ends `working`). The test asserts the `idle` and its comment says why.

## Definition of done

- `dotnet build -c Release` at the repo root: 0 warnings, 0 errors.
- `dotnet test --project tests/Aiakos.Orchestrator.Tests -c Release --filter-namespace
  Aiakos.Orchestrator.Tests.Seats`: all green. The project's other tests need Docker; do not
  change them or their fixture.
- All files LF, UTF-8 without BOM, final newline.
- One commit on the current branch, subject
  `feat(orchestrator): seat state machine and harness state profile (#13)`. The body lists every
  place where you followed the brief against the spec lines, and has the sentence "Risks: checks
  0006-RK4 (permutation property and the pinned limitation), 0006-RK11 (vocabulary test),
  0005-RK3 and 0006-RK10 (Escape denial script), 0005-RK9 and 0006-RK2 (parallel tools script)."
  Do not push, do not open a PR.

## Out of scope (do not implement, do not stub)

Actors, `SeatRegion`, stashing, supervision; the database, migrations, repositories and queries;
generating IDs or tokens; building `StartSeat` or any proto message; usage samples; launch
outcome and exit code bookkeeping; timers themselves (only the three `…Fired` inputs); the real
Claude Code profile (#12); findings storage, counts and severities; logging, metrics, spans;
changes to `docs/`, `CLAUDE.md`, CI, or any other project.
