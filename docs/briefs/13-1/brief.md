---
id: 13-1
title: "#13 slice 1 — pure seat state machine and harness state profile"
issue: 13
status: approved
route: impl/sonnet
paths: [src/Aiakos.Orchestrator/Seats/, tests/Aiakos.Orchestrator.Tests/, Directory.Packages.props]
max_outputs: 9
date: 2026-10-04
---

# Brief: #13 slice 1 — pure seat state machine and harness state profile

**Amendment 2026-10-05:** A7 unknown-activity recovery follows spec 0006 table A7; the earlier acceptance expectation was wrong. S12 corrects that merged behavior and the A2 detail ambiguity; existing story IDs stay unchanged.

**Start only after #10 slice 1 is merged** (the proto enums in `Aiakos.Contracts.Node.V1`).

This brief is self-contained. **Do not read `docs/specs/` or `docs/adr/`**, with one exception:
`docs/specs/0006-seat-actor.md` lines 379–644 (axis definitions and event pipeline) and
713–732 (findings). The transition, launch-decision and delivery tables are copied below as
items; their rules and tests are self-contained. This brief says how to read the spec lines
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
`CommandStatus`, `SessionLifecycle`, `GapReason`) come from `Aiakos.Contracts.Node.V1`.

## Rules

The rules retain their existing IDs, with B6 split into B6a/B6b/B6c, B17 into B17a/B17b,
and the watchdog split from B19 as B19w. B2a/B2b/B2c and T1/T2/T3 are replaced by the
individual row outputs below. "Rule 7" means B7 and "rule 11" means B11. R10, R16 and R17
are spec IDs used as derived `SeatTransition.Rule` values; gap transitions use A16/U6.

V1. **Surface and stored vocabulary.** The first story creates all public records, enums and
   interfaces above, including `SeatState.Initial`, `SeatVocabulary` and its constants.
   `SeatStateMachine.Apply` and `DeliveryStateMachine.Apply` are introduced by their owning
   behavior stories, not implemented or stubbed by the surface story. `ToStored` returns
   the exact lower-kebab strings in the surface comments. `EventDisposition.Duplicate`
   throws `ArgumentException`; it is not stored. For every proto enum with a ToStored overload,
   `*_UNSPECIFIED` and undefined numbers are stored as `unknown`. Defined non-unspecified
   values are distinct within an enum. `SessionLifecycle` and `GapReason` are wire inputs
   only: this slice adds no `ToStored` overload for them.

B1. **Pure.** `Apply` reads only its arguments. `now` is used only for the `…Since` fields and
   `LastEventAt`. Equal arguments give equal results.
B2. **Tables and row tests.** Implement the output items below with their owning rules.
   `—` means no axis transition and no row-specific finding; an otherwise applicable
   current-launch event is `Applied`. `n/a` means no axis transition and no row-specific
   finding, recorded as `Evidence` if no selected row on another axis is applicable.
   If one selected axis row has `n/a` and another has `—`, the dash row is applicable:
   disposition is `Applied`. A current-launch event with no selected axis row is `Evidence`
   (for example, PROMPT_SUBMITTED while known session is exited), unless an explicit rule
   supplies another disposition, such as B4's ignored starting/unknown activity events.
   Pipeline dispositions (`Duplicate`, `StaleLaunch`, `Orphan`, `Late`) take precedence.
   Command/timer/attachment inputs have null disposition, since they are not events.
   Open only findings named by the selected cells or a rule. Resolve requests are emitted
   unconditionally, whether or not the finding is currently open; the pure state has no
   finding ledger. A row test asserts every finding it names, rejects extra opens except
   those explicitly required by another applicable rule, and ignores additional resolves
   introduced by later stories. `SeatTransition.Rule` is the row ID (`S11`, `A2`, `U8`),
   or `R10`, `R16`, `R17` for derived changes. Emit a transition only when value or reason
   changes. A13 delegates to S12/S13; it is not an output item or an extra test.
   Each row output owns one xUnit test named by its ID, covering every column and every
   named variant, including values, reasons, findings and effects. The late U2/U8 variants
   are the explicit exception: T8 in the B7 story owns them, so the earlier row tests use
   SourceSeq 0 or increasing. Before B6c is owned, row/script event fixtures use the
   same non-null NodeInstanceId as the state and Seq = state.NextSeq at each input;
   sequence-counter assertions belong to B6c's story. Create the appropriate
   SessionTableTests, ActivityTableTests or ResumabilityTableTests class at its first row;
   later stories add only their owned rows. Later stories never change an earlier row's
   expected values or require earlier tests to assert future resolve requests. Table
   annotations for persisted usage, exit-code merging, node_instance_id, seat_session rows,
   and timer reset describe the later actor's bookkeeping, not fields added to these pure
   functions. Assert the specified axes, findings and existing effects here.
   Before an input's owning story lands, an otherwise unhandled non-event input returns
   `SeatStep(state, null, null, [], [], [])`, unchanged, rather than throwing or guessing
   future behavior. An otherwise unhandled EventReceived body runs only the pipeline
   rules already implemented, then returns Evidence unless a pipeline rule classified it
   otherwise; its missing body/table handler changes no axis and adds no finding/effect.
   Existing generic pipeline work (such as LastEventAt or sequencing) is retained.
   Tests for a later handler are introduced by its owning story; this fallback is not a
   permanent substitute for that handler.
B3. **Known and reported.** Tables act on `Known*`. Without an overlay, reported = known and each
   change is one transition with `Reported = true`. With an overlay, reported session and
   activity are `unknown` with the overlay's reason; known changes are transitions with
   `Reported = false`. Resumability has no overlay.
B4. **R10 always holds** for known and for reported values: activity is `none` exactly when
   session is `absent` or `exited`; known activity is `unknown` when known session is
   `starting` (`not-ready`) or `unknown` (`session-unknown`). These reason rules apply to
   known values only; reported values under an overlay keep B3's overlay reasons.
   While known session is `starting` or
   `unknown`, activity rows A2–A12 change nothing (the event is still `Applied`); only readiness
   (S11 then A1) leaves that state. This overrides the sentence "evaluated while starting,
   present or unknown".
B5. **Entering `present`.** By S11: activity `idle` (A1). By S5 without
   `ReadinessSeen`, from either `starting` or `unknown`: `unknown/sources-disagree`.
   When S5 enters `present` with `ReadinessSeen` true from `starting` or `unknown`,
   known activity is also `unknown/sources-disagree`, not `unknown/not-ready` or
   `unknown/session-unknown`; the flag does not establish an activity value. Test both
   flags and both source sessions within S5. Existing cell-specific finding rules remain. S15 into `present`: `unknown/observation-gap`. S15 into `starting`
   clears `ReadinessSeen`, so a subsequent S5 follows the no-readiness branch.
B6a. **Event attribution and table application.** For `EventReceived`, after the
   implemented sequencing/body-specific handling and before table application,
   a body whose `LaunchId` is not the current launch's (or there is none) is
   `StaleLaunch`, with no axis change; a `HarnessBody` recognized by the profile as readiness,
   or of kind `PROMPT_SUBMITTED`, `TOOL_STARTED`, `TOOL_FINISHED` or `INPUT_REQUESTED`, also
   opens `orphan-harness`. For a current-launch event, after B6b's orphan check and B7's
   order check, apply session, activity, then resumability using the owned rows.
   `CaptureBody`, `OtherCommandResultBody` and `UnknownBody` are `Evidence`; a `CaptureBody`
   with `PaneDead` while known session is `present` opens `sources-disagree` without
   changing an axis. The rows themselves supply findings, reasons and effects.
B6b. **Harness event attribution.** `SESSION_ENDED` while `ReadinessSeen` is false is
   `Orphan`, no axis change (S13), before the order check. An unrecognized or unspecified
   `HarnessEventKind` is handled as `OTHER` (A14). Every `HarnessBody` of the current launch,
   late or orphan or not, sets `LastEventAt = now` and resolves `activity-stale`.
B6c. **Event sequencing.** Before attribution, per `EventReceived`, in order:
   1. If `NodeInstanceId` differs: when the state's is null adopt it and set `NextSeq = 1`;
      otherwise apply B11, adopt the new ID and set `NextSeq = 1`. Applying B11 for every
      differing previously-known instance is an explicit deviation from spec pipeline
      step 1, which also requires that events were expected from the previous instance.
   2. `Seq < NextSeq` is `Duplicate`, with no further work on the input.
   3. `Seq > NextSeq` applies B11 if step 1 has not already done so. A nonduplicate input
      sets `NextSeq = Seq + 1`, then continues through B6a, B6b and B7.
   Lost-observation handling B11 applies at most once per input, even when epoch change,
   sequence gap and an `ObservationGapBody` occur together: at most one open
   `observation-gap` request and one `RequestCapture`. Clear a caught-up overlay as in B12.
B7. **Stale guard.** Only a `HarnessBody` with `SourceSeq > 0` whose kind is not `TELEMETRY`,
   `OTHER` or unspecified takes part. `SourceSeq <= LastSourceSeq` → `Late`: only U2 and U8 may
   apply. Otherwise `LastSourceSeq = SourceSeq`. `TELEMETRY` and `OTHER` are never late and never
   move `LastSourceSeq`: a statusLine tick that overtakes a `Stop` hook must not make the `Stop`
   late. This exemption is an explicit deviation from spec pipeline step 5 (all harness
   events), to be recorded in the #13 spec amendment by lead. `LastSourceSeq` is kept across
   launches. Late U8 updates native ID/resumability and emits adoption, but never applies
   A1 or changes activity. Earlier stories test only `SourceSeq` 0 or increasing; late
   U2/U8 behavior is tested by the B7 story.
B8. **Readiness and native IDs.** An event is a readiness event only when `profile.IsReadiness`
   is true **and** its `NativeSessionId` equals the state's. It sets `ReadinessSeen`. With another
   ID: U8 when `profile.IsSessionRotation` is true, the attribute `previous_session_id` equals
   the state's ID and `profile.IsValidNativeSessionId(new)`; otherwise U7. A mismatching event is
   never S11. U8 sets `NativeSessionId` to the new ID, emits `AdoptRotatedSession`, sets
   `ReadinessSeen` and resolves `session-id-mismatch`. It applies A1 only when known session
   is `present` and the event is not late; otherwise session/activity remain unchanged.
   Test U8 for every session value and for invalid/mismatching previous IDs (U7); the late
   variant belongs to the B7 story. Late rotation preserving activity explicitly resolves
   the spec tension between rotation text (A1 even when late) and the activity table
   prohibition on late input in favor of that prohibition.
B9. **Conversation evidence (U2)** counts only when `profile.IsConversationEvidence` is true and
   the event's `NativeSessionId` equals the state's. Late events count when B7 is present;
   the B9 story tests `SourceSeq` 0 or increasing, and B7 owns the late variants.
B10. **Pending input.** A6 stores the attribute `request_id`, or `*` when absent. A5 and A7 resolve the pending request
    when the pending value is `*` or equals the event's `tool_use_id` or `request_id`. A2, A10,
    A11 and any change of session clear it. A4 detail is `tool:<tool_name>`.
    Matching guards only the needs-input activity cell, not A7 from unknown (B23).
B11. **Lost observation** (sequence gap, `ObservationGapBody`, new epoch, new node instance): open
    `observation-gap`; U6; when known session is `present`, activity → `unknown/observation-gap`
    (A16); when a launch exists and known session is `starting`, `present` or `unknown`, emit
    `RequestCapture`. Never synthesize an event. `ObservationGapBody` is handled before
    launch attribution and applies this rule whatever its launch, unless this input has
    already applied it through B6c. This is the explicit deviation from spec pipeline
    step 4: observation loss belongs to the node, not a launch. This body-specific exception
    is introduced here, not in the earlier B6a story.
B12. **`NodeAttached`.** Resolve `node-not-connected`. *Same instance, or the state has none:*
    adopt the ID; with an overlay, set `CatchUpSeq = Inventory?.LastSeq ?? 0` and clear the
    overlay as soon as `NextSeq > CatchUpSeq` (now, or after a later event); the inventory is
    otherwise ignored. *Different instance:* adopt it, `NextSeq = 1`, clear the overlay and
    `CatchUpSeq`, then S15 when the inventory has the current `LaunchId`, else S16, then rule 11.
    S15 lifecycles: `LAUNCHING` → `starting`, `RUNNING` → `present`, `EXITED` → `exited`,
    `UNKNOWN` and anything else → `unknown/orphan-or-running`. S15 resolves `inventory-mismatch`.
B13. **Overlays.** `NodeLinkLost` and `OrchestratorRestarted` set the overlay only when known
    session is not `absent` or `exited`; transitions have rule `R16`. Clearing it restores
    reported = known with rule `R17`. Under an overlay a second overlay input changes only the
    overlay kind and reasons.
B14. **`UpRequested`**, in this order, on reported values: `NodeConnected` false → `Rejected
    NODE_NOT_CONNECTED` and open `node-not-connected`; session `starting` or `present` →
    `AlreadyUp`, `Desired = Up`; `unknown` → `Rejected SEAT_STATE_UNKNOWN`; otherwise the launch
    mode decision table (`lost` without `Fresh` → `Rejected RESUME_LOST`). Accepted: `Desired =
    Up`; S1; U1 where the table assigns a new session; `Launch = new CurrentLaunch(NewLaunchId,
    mode, reused, false)`; `ReadinessSeen` false; pending input and pre-compaction cleared;
    `NativeSessionId` is `NewNativeSessionId` for a new session, else kept; one `StartLaunch`
    effect (`AbandonPreviousSession` when a session with an ID is replaced). `Fresh` accepted
    resolves `resume-lost` and `session-id-mismatch`. A rejected `up` changes nothing else.
B15. **`DownRequested`**: `Desired = Down`; resolve `unexpected-exit`; when a launch exists and
    known session is not `absent`: `DispatchStop` and `StopRequested = true`. Always `Accepted`.
    S3 also resolves `orphan-harness`.
B16. **`SendRequested`**, in this order, on reported values: `NodeConnected` false →
    `NODE_NOT_CONNECTED` (open `node-not-connected`); session `unknown` → `SEAT_STATE_UNKNOWN`;
    session not `present` → `SEAT_NOT_PRESENT`; `DeliveryInFlight` → `DELIVERY_IN_FLIGHT`;
    activity `working` → `SEAT_WORKING`; `needs-input` → `SEAT_NEEDS_INPUT`; `unknown` without
    `Force` → `SEAT_ACTIVITY_UNKNOWN`; otherwise `Accepted(Forced: activity was unknown)`. It
    never changes an axis.
B17a. **Session result findings.** S5 or S11 into `present` resolves `launch-unconfirmed`,
    `launch-rejected`, `launch-failed`, `unexpected-exit`. Known session entering `exited`
    while `Desired == Up` and `StopRequested` is false opens `unexpected-exit`.
    These rules read the supplied state; tests may construct `Desired`/`Launch` directly
    without an `UpRequested` or `DownRequested` input from a later story.
B17b. **Resumability of launch results.** U3 needs `Launch.Mode == RESUME`. U5 needs mode
    `FRESH` with `ReusedNativeSessionId`. `LaunchResult FAILED` with another reason in mode
    `RESUME` opens `launch-failed` and leaves resumability unchanged. Test these variants
    alongside U3/U4/U5; session consequences are already owned by B17a's story.
B18. **`CommandDispatchFailed`**: `Start` → S9; `Stop` → S4; others change nothing.
B19. **Timers are validated, not trusted.** `QuietTimeoutFired` acts (A15) only when known session
    is `present`, known activity is `working` and `now - LastEventAt >= profile.QuietTimeout`.
    `UnknownProlongedFired` opens `state-unknown-prolonged` only when reported session
    is `unknown` and `now - SessionSince >= 5 min`; the finding resolves when reported session
    leaves `unknown`. Otherwise each returns the state unchanged.
B19w. **Launch watchdog.** `LaunchWatchdogFired` acts only when known session is
    `starting`: S10 sets session `unknown/launch-result-missing`, derives activity by B4,
    and opens `launch-unconfirmed`. Otherwise it returns the state unchanged.
B20. **Other resolutions.** `TURN_ENDED` resolves `turn-failed`; `PROMPT_SUBMITTED` resolves
    `delivery-unconfirmed`. `sources-disagree` and `observation-gap` are never resolved here.
B21. **Unrecognized session result enums.** `LaunchOutcome` unspecified or undefined
    uses S7; `StopOutcome` unspecified or undefined uses S4. `CommandStatus` unspecified
    or undefined in `StartNotCompletedBody` uses S10 with reason `launch-unconfirmed` and
    opens `launch-unconfirmed`; recognized `TIMED_OUT` does the same. `COMPLETED` in
    `StartNotCompletedBody` changes no axis, finding or effect and is `Evidence`.
    Test it as an additional S10 variant. In
    `StopNotCompletedBody`, `REJECTED`, `FAILED`, `TIMED_OUT` and unspecified/undefined
    statuses use S4; `COMPLETED` has no row-specific change and is `Evidence`.
    Test each of these enum variants within S4/S7/S10, including the S10 finding for both
    timed-out results and the watchdog. Harness enum handling is B6b; inventory lifecycle
    handling is B12; delivery enum handling is B22.
B22. **Delivery table.** Final states absorb every input. `SubmittedUnconfirmed` opens
    `delivery-unconfirmed`. `CommandStatus` unspecified or undefined in `DeliveryNotCompleted`
    yields `Unknown` from `Sent`; pending and final states remain as their table cells.
    Undefined or unspecified `DeliveryOutcome` yields `Unknown` from `Sent`; pending and
    final states remain unchanged. DV1's persisted node_instance_id and DV4's persisted
    error reason are outside this pure DeliveryStep surface; it returns state and findings
    only. Other recognized inputs not named in the table change
    nothing and emit no finding.

## Expected outputs: transition, launch-decision and delivery rows

Each row is an output item. Cells below are copied from the spec; B1–B22 and V1 explain
ambiguities and the declared deviations. R10 derivations and additional named variants
are part of the test for that row, not new requirements introduced by a test.

### Session rows

| ID | Input (current launch) | `absent` | `starting` | `present` | `exited` | `unknown` |
|---|---|---|---|---|---|---|
| `S1` | `up` accepted | `starting` | — (no-op, returns launch) | — (no-op) | `starting` | rejected `SEAT_STATE_UNKNOWN` |
| `S2` | `down` accepted (StopSeat dispatched); additionally test from `present` with a launch, then current-launch `StopResult STOPPED`: session `absent` and resolve `orphan-harness` | — (desired only) | — | — | — (StopSeat sent to clean up the pane) | — |
| `S3` | `StopResult` STOPPED / KILLED / NOT_RUNNING | n/a | `absent` | `absent` | `absent` | `absent` |
| `S4` | `StopSeat` FAILED / TIMED_OUT | n/a | `unknown`/stop-failed | `unknown`/stop-failed | — | — |
| `S5` | `LaunchResult READY` | n/a | `present` (no readiness event was seen: +F(sources-disagree), activity `unknown`) | — | `unknown`/sources-disagree +F(sources-disagree) | `present` |
| `S6` | `LaunchResult FAILED` | n/a | `exited` | `unknown`/sources-disagree +F(sources-disagree) | — | `exited` |
| `S7` | `LaunchResult UNKNOWN` | n/a | `unknown`/launch-unconfirmed +F(launch-unconfirmed) | `unknown`/sources-disagree +F(sources-disagree) | — | — |
| `S8` | `StartSeat` REJECTED `SEAT_ALREADY_RUNNING` / `ORPHAN_HARNESS_DETECTED` | n/a | `unknown`/orphan-or-running +F(orphan-harness) | n/a | n/a | n/a |
| `S9` | `StartSeat` REJECTED other reason, or FAILED | n/a | `absent` +F(launch-rejected) | n/a | n/a | n/a |
| `S10` | `StartSeat` TIMED_OUT, or `LaunchWatchdog` fires | n/a | `unknown`/launch-unconfirmed or launch-result-missing | — | — | — |
| `S11` | readiness event (profile) | n/a | `present` | — | `unknown`/sources-disagree +F(sources-disagree) | `present` |
| `S12` | `SESSION_ENDED` after a readiness event in this launch | n/a | n/a (readiness moved it to `present`) | `exited` | — | `exited` |
| `S13` | `SESSION_ENDED` without a readiness event (orphan, R15) | — | — | — | — | — |
| `S14` | `ProcessExited` (any `ExitSource`; M6 adds container sources) | n/a | `exited` | `exited` | — (merge exit code) | `exited` |
| `S15` | `NodeAttached`, new instance, inventory LAUNCHING / RUNNING / EXITED / UNKNOWN (same `launch_id`) | — | `starting` / `present` / `exited` / `unknown` | same mapping | same mapping | same mapping |
| `S16` | `NodeAttached`, new instance, seat missing from inventory or other `launch_id` | — | `unknown`/inventory-missing +F(inventory-mismatch) | same | — | same |

C1. **Earlier test corrections owned by story S12.** Update the committed S5 test
    ActivityTableTests.A7InputResolvedChangesNeedsInputOnlyWhenItMatchesAndMovesUnknownToWorking
    in tests/Aiakos.Orchestrator.Tests/Seats/ActivityTableTests.cs: when known session is
    present and prior known activity is unknown, every INPUT_RESOLVED case expects working
    with null reason regardless of pending-ID match. Preserve needs-input mismatch assertions
    and every other activity column. Update any committed A2 assertion that expects a working
    detail to be cleared: expect the original detail instead under B23, while pending clears.
    Change only assertions for those corrected cells; all other earlier expected results stay.
    The corrected local acceptance A7 expectation follows the same rule.

B23. **Corrections to merged activity rows.** For current-launch INPUT_RESOLVED while known
    session is present and known activity is unknown, A7 always changes known and reported
    activity to working, clears its unknown reason and detail, and emits the ordinary A7
    transition. This holds with no pending request, wildcard, matching or mismatching IDs,
    for both profiles and both request_id/tool_use_id attributes. Clear a pending request
    only on the B10 match; retain a mismatching pending request. No new effect or finding open.
    For A2 from working, the dash preserves ActivityDetail/KnownActivityDetail, including
    tool:<name>, compacting and retrying; B10 still clears PendingInputRequest. Value/reason
    remain unchanged and no activity transition is emitted. Preserve existing pipeline
    LastEventAt and activity-stale resolve behavior; extra resolves remain allowed by B2.
    These corrections are implemented in new story S12, not by retrying S5. S12 becomes
    ready only after PR #129 merges as accepted and this amendment is approved. PR #129 was
    verified merged at 2026-10-04T21:03:23Z. The amended B10/A2/A7 text is the target for S12;
    it does not invalidate or reopen the previously accepted S5 run.

### Activity rows

Use known session `present` unless the row is explicitly about another session; B4 governs
`starting`/`unknown` and `absent`/`exited`. Pending-input and compaction variants are tested
with the row that consumes them. A13 is omitted: its complete outputs are S12/S13.

| ID | Input (`HarnessEventKind`, current launch, not late) | `idle` | `working` | `needs-input` | `unknown` |
|---|---|---|---|---|---|
| `A1` | readiness: `SESSION_STARTED` with a readiness source (profile) | `idle` | `idle` | `idle` | `idle` |
| `A2` | `PROMPT_SUBMITTED` | `working` | — (preserve detail; clear pending request) | `working` (a new prompt means no dialog is open) | `working` |
| `A3` | `ACTIVE` (level; OpenCode `busy`) | `working` | — | — (**sticky**, spike 0004) | `working` |
| `A4` | `TOOL_STARTED` | `working` detail `tool:<name>` | detail `tool:<name>` | — (sticky: a parallel tool does not answer the dialog) | `working` |
| `A5` | `TOOL_FINISHED` | `working` | — (detail cleared) | `working` if it resolves the pending request (below), else — | `working` |
| `A6` | `INPUT_REQUESTED` | `needs-input` | `needs-input` | — (request ID updated) | `needs-input` |
| `A7` | `INPUT_RESOLVED` | — | — | `working` if it matches the pending request, else — | `working` unconditionally; clear unknown reason, even with absent or mismatching pending ID |
| `A8` | `COMPACTION_STARTED` | `working` detail `compacting`, remember `idle` | detail `compacting`, remember `working` | — | `working` detail `compacting`, remember `unknown` |
| `A9` | `COMPACTED` | — | if detail is `compacting`: the remembered value (an `unknown` stays `unknown`/observation-gap); else — | — | — |
| `A10` | `TURN_ENDED` | — | `idle` | `idle` (dialog closed, e.g. denial) | `idle` |
| `A11` | `TURN_FAILED` | — +F(turn-failed) | `idle` +F(turn-failed) | `idle` +F(turn-failed) | `idle` +F(turn-failed) |
| `A12` | `RETRYING` | `working` detail `retrying` | detail `retrying` | — | `working` detail `retrying` |
| `A14` | `TELEMETRY`, `OTHER`; each current-launch nonduplicate variant sets `LastEventAt = now` and resolves `activity-stale` | — (usage updated; quiet timer reset) | — | — | — |
| `A15` | `QuietTimeout` (R19) | n/a | `unknown`/quiet-timeout +F(activity-stale) | n/a | n/a |
| `A16` | `ObservationGap`, sequence gap, new node instance (R18) | `unknown`/observation-gap +F(observation-gap) | same | same | same |

### Resumability rows

Construct the needed current launch and native ID in the fixture; later up/down inputs
are not prerequisites to a row test. Known session `present` is the normal evidence
fixture; U8 additionally exercises the non-present sessions specified in B8.

| ID | Input | `none` | `fresh-only` | `resumable` | `lost` | `unknown` |
|---|---|---|---|---|---|---|
| `U1` | new native session assigned (`up` from `none`, or any `up --fresh`) | `fresh-only` | `fresh-only` (new ID if fresh-explicit) | `fresh-only` (new session) | `fresh-only` (new session) | `fresh-only` (new session) |
| `U2` | conversation evidence for the current native session (profile; applies even when late, R13) | n/a | `resumable` | — | `unknown`/contradicting-evidence +F(sources-disagree) | `resumable` |
| `U3` | `LaunchResult READY` for mode RESUME (verified) | n/a | n/a | — | n/a | `resumable` |
| `U4` | `LaunchResult FAILED` reason `RESUME_SESSION_NOT_FOUND` | n/a | n/a | `lost` +F(resume-lost) | — | `lost` |
| `U5` | `LaunchResult FAILED` for a FRESH relaunch that reused the ID | n/a | `unknown`/fresh-relaunch-failed +F(launch-failed) | n/a | n/a | — |
| `U6` | gap / new node instance (R18) | — | `unknown`/observation-gap | — | — | — |
| `U7` | `SessionObserved.matches_expected = false`, or a readiness event with another native ID that is **not** a rotation (U8) | n/a | `unknown`/session-id-mismatch +F(session-id-mismatch) | same | same | +F only |
| `U8` | session rotation (`profile.IsSessionRotation`; Claude `/clear`, D14): readiness event with a new valid native ID whose `previous_session_id` equals the current one | n/a | `fresh-only` (new `seat_session`, decision `harness-cleared`) | same | same | same |

### Launch decisions

Each row includes both `Fresh` values and both profiles. B14's rejection order still applies.

| ID | Resumability | `up` | `up --fresh` |
|---|---|---|---|
| `LD1` | `none` | FRESH, new native ID — decision `new-session` | same |
| `LD2` | `fresh-only` | FRESH reusing the ID if `profile.FreshRelaunchReusesSessionId`, else a new ID — decision `no-conversation-yet` | FRESH, new ID — `fresh-explicit` |
| `LD3` | `resumable` | RESUME — decision `resume` | FRESH, new ID — `fresh-explicit`, old session abandoned |
| `LD4` | `lost` | **rejected** `RESUME_LOST` (hint: `--fresh`) | FRESH, new ID — `fresh-explicit`, old session abandoned |
| `LD5` | `unknown` | RESUME — decision `resume-unverified` (a missing conversation fails honestly with `RESUME_SESSION_NOT_FOUND`) | FRESH, new ID — `fresh-explicit`, old session abandoned |

### Delivery rows

`pending`/`sent` and all final states are tested for each row; `n/a` means unchanged here
(the pure delivery function has no event disposition). DV3 tests each of its three outcomes.

| ID | Input | `pending` | `sent` | final states |
|---|---|---|---|---|
| `DV1` | committed, dispatched | `sent` (with `node_instance_id`) | — | — |
| `DV2` | `CommandDispatchFailed` | `not-delivered` | n/a | — |
| `DV3` | `DeliveryResult CONFIRMED` / `SUBMITTED_UNCONFIRMED` / `NOT_DELIVERED` | n/a | `confirmed` / `submitted-unconfirmed` (+F(delivery-unconfirmed)) / `not-delivered` | — |
| `DV4` | `CommandResult` REJECTED / FAILED / TIMED_OUT (e.g. `SEAT_NOT_READY`, `SEAT_BUSY`, `SEAT_STOPPING`) | n/a | `failed` with reason | — |
| `DV5` | node reattached with a new instance | n/a | `unknown` | — |

## Tests (`tests/Aiakos.Orchestrator.Tests/Seats/`)

Plain xUnit v3, no fixture, no Docker, no Akka. Every test ends with the shared assertion
`SeatAssert.Invariants(step)`: rule 4 for known and reported values; a reason on every `unknown`;
no overlay ⇒ reported = known. `TestProfiles.cs` holds two test doubles. `ClaudeLike`: readiness =
`SESSION_STARTED` with `source` in `startup`, `resume`, `fork`, `clear`; evidence =
`PROMPT_SUBMITTED`, `TURN_ENDED`, `TURN_FAILED`; rotation = `SESSION_STARTED` with `source=clear`
and `previous_session_id`; reuses the ID; no `INPUT_RESOLVED`; quiet 10 min. `OpenCodeLike`:
readiness and evidence = `SESSION_STARTED`; no rotation; new ID; emits `INPUT_RESOLVED`.

The table classes follow B2: one test per owned row, every column and named variant.

- T4. `LaunchDecisionTests`: every resumability value with and without `Fresh`, for both profiles.
- T5. `SendDecisionTests`: every clause of rule 16.
- T6. `DeliveryTableTests`: the delivery table and rule 22.
- T7. `OverlayTests`: rules 3 and 13.
- T8. `PipelineTests`: each pipeline step (B6c → B6a attribution → B6b orphan → B7 order
  → B6a table application), rule 7 with a `TELEMETRY` overtaking a `TURN_ENDED`, B12 both
  branches, late U2, and late U8 updating native ID/resumability without changing activity.
- T9. `TimerTests`: rule 19.
- T10. `VocabularyTests`: for every value of every enum with a `ToStored` overload above
  (`Enum.GetValues`), returns exact lower-kebab strings; defined non-unspecified values are
  distinct; unspecified and undefined proto values return `unknown`; `Duplicate` throws
  `ArgumentException`. The exact strings in the surface comments hold (V1).

`GoldenScriptTests`: each script is a list of inputs with the expected axes after every step.
Use `ClaudeLike` except GS9 (`OpenCodeLike`). Unless a script says otherwise, its fixture is
present/idle/resumable, Desired Up, a current FRESH launch with StopRequested false, a matching
valid native ID, and no overlay. GS1 starts starting/unknown/not-ready and fresh-only; GS5 starts
working and GS7 starts needs-input. Seq matches the current NextSeq at each input, and SourceSeq
is 0 or increasing. Assert axes after every step, with the row-specific findings/effects named
by the script; do not assert unrelated future resolve requests (B2).

- GS1. *Permission turn:* readiness; `PROMPT_SUBMITTED`; `TOOL_STARTED` (`Bash`, `t1`);
  `INPUT_REQUESTED` (`request_id=t1`); `OTHER`; `TOOL_FINISHED` (`t1`); `TURN_ENDED`;
  `COMPACTION_STARTED`; `COMPACTED` → idle, working, working `tool:Bash`, needs-input, same, working,
  idle, working `compacting`, idle; resumability becomes `resumable` at the prompt.
- GS2. *Kill and resume:* a ready seat with one turn; `ProcessExited(signal 9)` → `exited`,
  `unexpected-exit`; `up` → `RESUME`, decision `resume`; a `SESSION_ENDED` before readiness is
  `Orphan`; readiness → `present`; `LaunchResult READY` changes nothing.
- GS3. *Failed resume:* as 2, then `ProcessExited(exit 1)` and `LaunchResult FAILED
  RESUME_SESSION_NOT_FOUND` → `exited`, `lost`, `resume-lost`, no `StartLaunch`; `up` →
  `RESUME_LOST`; `up` with `Fresh` → `FRESH`, `fresh-explicit`, new ID, previous abandoned.
- GS4. *Turn failure:* prompt → working and resolve `delivery-unconfirmed`; then
  `TURN_FAILED` → idle and `turn-failed`; the next `TURN_ENDED` resolves it.
- GS5. *Exit without `SESSION_ENDED`:* working, then `ProcessExited` → `exited` / `none`.
- GS6. *Rotation:* idle and `resumable`; `OTHER` (`reason=clear`); `SESSION_STARTED` (`source=clear`,
  new ID, `previous_session_id`) → `present`, idle, `fresh-only`, `AdoptRotatedSession`, no
  `session-id-mismatch`; a prompt with the new ID → `resumable`; `down`, `StopResult`, `up` →
  `RESUME` with the new ID. With an invalid new ID the same event is U7.
- GS7. *Escape denial:* needs-input and no event → `send` is `SEAT_NEEDS_INPUT`, with `Force` too;
  the next `PROMPT_SUBMITTED` → working.
- GS8. *Parallel tools:* two `TOOL_STARTED`, `INPUT_REQUESTED` without an ID, `TOOL_FINISHED` of
  the other tool → working (the accepted early resolve); with `request_id` set it stays
  needs-input.
- GS9. *OpenCode:* `ACTIVE`; `INPUT_REQUESTED p1`; `ACTIVE` (still needs-input); `INPUT_RESOLVED
  p1` → working; `TURN_ENDED` → idle.

- T11. `SeatPropertyTests` (CsCheck, at least 1 000 cases each). The generator builds complete Claude
  turns (prompt, zero or more tool calls with an optional permission request, an optional
  compaction, `TURN_ENDED`), statusLine `TELEMETRY` ticks anywhere, increasing `Seq` and
  `SourceSeq`:
  - any arrival order of the harness events gives the same final axes as `SourceSeq` order, and
    no step has activity `none` while session is `present`;
  - repeating events or whole ranges changes neither the final state nor the number of
    non-`Duplicate` dispositions;
  - dropping a run of `Seq` gives activity `unknown/observation-gap` until the next event that
    the activity table's `unknown` column maps to a value, and turns `fresh-only` into `unknown`;
  - switching `NodeInstanceId` mid-stream applies rule 11 once and restarts `NextSeq`;
    include a new epoch with `Seq > 1` and an `ObservationGapBody`: exactly one open
    `observation-gap` request and one `RequestCapture` for that input.
- T12. `KnownLimitationTests`: one pinned case where the guard loses history mid-turn (arrival
  `COMPACTED`, `PROMPT_SUBMITTED`, `COMPACTION_STARTED` for `SourceSeq` 3, 1, 2 ends `idle`,
  sorted order ends `working`). The test asserts the `idle` and its comment says why.

## Definition of done

- `dotnet build -c Release` at the repo root: 0 warnings, 0 errors.
- `dotnet test --project tests/Aiakos.Orchestrator.Tests -c Release --filter-namespace
  Aiakos.Orchestrator.Tests.Seats`: all green. The project's other tests need Docker; do not
  change them or their fixture.
- All files LF, UTF-8 without BOM, final newline.
- One commit on the current branch, subject `feat(orchestrator): <story title> (#13)`, using
  the exact title of that story in `stories.md`. The body lists every place where the story
  followed this brief against the spec lines. Include only the risk sentence for that story
  from this table; another story's checks are not claimed. Do not push, do not open a PR.

| Story | Exact commit-body risk sentence |
|---|---|
| S1 | Risks: checks 0006-RK11 (vocabulary test). |
| S2, S3, S4, S6, S7, S8, S10 | Risks: this story checks none of the slice's listed open risks. |
| S5 | Risks: checks 0005-RK9 and 0006-RK2 (parallel tools script). |
| S9 | Risks: checks 0006-RK4 (permutation property and the pinned limitation). |
| S11 | Risks: checks 0005-RK3 and 0006-RK10 (Escape denial script). |
| S12 | Risks: this story checks none of the slice's listed open risks. |

## Out of scope (do not implement, do not stub)

Actors, `SeatRegion`, stashing, supervision; the database, migrations, repositories and queries;
generating IDs or tokens; building `StartSeat` or any proto message; usage samples; launch
outcome and exit code bookkeeping; timers themselves (only the three `…Fired` inputs); the real
Claude Code profile (#12); findings storage, counts and severities; logging, metrics, spans;
changes to `docs/`, `CLAUDE.md`, CI, or any other project.

### Amendment acceptance outputs

| ID | Input | Exact expected output |
|---|---|---|
| `A7-recovery` | present/unknown observation-gap or quiet-timeout; INPUT_RESOLVED; pending absent, wildcard, matching or mismatching; either ID attribute; both profiles | known/reported activity working, null reason/detail; matching pending cleared, mismatching pending retained; one reported activity transition Rule A7; no effects or finding opens; Applied, LastEventAt now, activity-stale resolve |
| `A2-detail` | present/working with detail tool:build, compacting or retrying and pending p1; PROMPT_SUBMITTED; both profiles | known/reported working and original detail, null reason; pending null; no activity transition, effects or finding opens; Applied, LastEventAt now, activity-stale resolve |

T13. One acceptance test per A7-recovery and A2-detail output covers every named variant.
    Retain all earlier A7 columns, including needs-input mismatch staying needs-input. Correct
    the earlier A7 acceptance test to expect working from unknown per spec 0006; do not run
    its gate on main as author. QA records the missing-behavior baseline for S12.
