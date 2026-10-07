---
id: 13-4
title: "#13 slice 4 — SeatActor lifecycle and delivery"
issue: 13
status: draft
route: impl
paths: [src/Aiakos.Core/, src/Aiakos.Orchestrator/, tests/Aiakos.Orchestrator.Tests/]
date: 2026-10-07
---

# Brief: #13 slice 4 — SeatActor lifecycle and delivery

Part of #13, spec 0006 R21–R32, R39 and R44–R46. Self-contained: implementers do not read the
specs or ADRs. This replaces the entire earlier draft, including its tables and split. It adds
commands to the existing serialized SeatActor; a second actor or independent dispatcher writer
would violate the sole-writer design. The local API in 15-2 resolves the seat UUID and supplies
CallerContext. This slice owns command creation, token generation, actor command handling,
transactional command persistence, and send-only ports; it does not implement HTTP or node I/O.
Where silent, preserve the existing pure machines, choose the simplest private implementation,
and describe that choice in the commit. Never substitute a fresh conversation or fake success.

## Prerequisites and baseline

Read existing SeatProtocol.cs, SeatSurface.cs, SeatStateMachine.cs, DeliveryStateMachine.cs,
SeatWireInput.cs, PostgresSeatActorReader.cs and PostgresSeatActorWriter.cs. Schema reference is
src/Aiakos.Data/Migrations/0002_seat_model.sql; do not change it. Read the merged 13-3 brief as
context for the actor's transaction/reload contract. At analysis base origin/main 8d06d44 the
protocol/reader/event-writer foundation exists; actor/region and finding/result writer stages
are not all merged. The following must exist before their consuming story is ready:

- Serialized seat apply and commit actor: SeatActor with the 13-3 safe failures and mailbox gate.
- Seat region routing, gateway and reload notification: SeatRegion, SeatActorGateway and
  ISeatActorLifecycle publishing SeatActorReloaded before releasing queued requests.
- Atomic finding and session rotation persistence and received command/launch outcomes:
  PostgresSeatActorWriter's R7/R8 transaction stages, with postcommit snapshot refresh.
- Claude orchestrator adapter: IHarnessAdapter.BuildLaunch/BuildDelivery and its profile;
  production launch material must contain the finalized projection files from 14-5 and the
  real relay bundle from 12-2. Fake supplied files/adapter are sufficient for isolated tests.

10-5 supplies the production ISeatCommandPort/connection view. A provider of the resolved
launch material is an integration prerequisite, not invented data in this slice. Production
registration is permitted only when these services exist; the core stories can use injected
fakes to prove their own behavior. Lead must keep production activation pending until then.
No story builds a missing transport or relay inside this slice.

## Files and public surface

Core contains only the following API seam. Command replies and all internal collaboration types
live in Orchestrator.Seats. Later 15-2 follows these exact names; it must replace its older
Orchestrator ISeatCommandDispatcher/SeatEnvelope reference with the Core pair below.

```csharp
namespace Aiakos.Core;
public sealed record CallerContext(Guid TenantId, string User, string Sender);
public sealed record SeatCommandEnvelope(Guid TenantId, Guid SeatId, object Message);
public interface ISeatCommandDispatcher
{
    Task<object> DispatchAsync(SeatCommandEnvelope command, CallerContext caller, CancellationToken ct);
}
public sealed record SeatUp(bool Fresh, string? Note);
public sealed record SeatDown;
public sealed record SeatSend(string Body, bool Force);
public sealed record SeatCapture(int HistoryLines);

namespace Aiakos.Orchestrator.Seats;
public sealed record SeatCommandAccepted(Guid? LaunchId, Guid CommandId);
public sealed record SeatAlreadyUp(Guid LaunchId);
public sealed record SeatAlreadyDown;
public sealed record SeatCommandRejected(string Reason);
public sealed record SeatCaptureCompleted(Aiakos.Contracts.Node.V1.PaneCapture Capture);
public sealed record SeatCommandTimedOut(string Reason);
public sealed record SeatNodeConnection(bool Connected, Guid? NodeInstanceId);
public interface ISeatNodeConnectionSource
{
    SeatNodeConnection Get(SeatKey key, string nodeName);
}
public sealed record SeatCommandDispatch(bool Sent);
public interface ISeatCommandPort
{
    Task<SeatCommandDispatch> SendAsync(SeatKey key, string nodeName, Guid nodeInstanceId,
        Aiakos.Contracts.Node.V1.Command command, CancellationToken ct);
}
public interface ISeatTokenGenerator { byte[] Create(); }
public sealed record SeatLaunchMaterial(string Address, string SpecHash, string BindingHash,
    Aiakos.Spec.ResolvedSeatParameters Parameters,
    IReadOnlyList<Aiakos.Contracts.Node.V1.SeatFile> SuppliedFiles);
public interface ISeatLaunchMaterialSource
{
    Task<SeatLaunchMaterial?> LoadAsync(SeatKey key, CancellationToken ct);
}
public sealed record SeatSessionInsert(Guid SessionId, string Harness, string NativeSessionId,
    string Decision);
public sealed record SeatLaunchInsert(Guid LaunchId, Guid SessionId, string Mode, string Decision,
    string DecidedBy, string? DecisionNote, Guid CommandId, string NodeName, string SpecHash,
    string BindingHash, byte[] SeatTokenHash);
public sealed record SeatCommandInsert(Guid CommandId, Guid? LaunchId, SeatCommandKind Kind,
    System.Text.Json.JsonElement Payload, bool Forced, string RequestedBy, string? TraceParent);
public sealed record SeatCommandUpdate(Guid CommandId, string Status, string? Outcome,
    string? ErrorReason, Guid? NodeInstanceId, int Attempts, DateTimeOffset? SentAt,
    DateTimeOffset? CompletedAt);
public sealed record SeatCommandTransaction(SeatActorSnapshot Before,
    IReadOnlyList<SeatAppliedInput> Inputs, SeatSessionInsert? Session, SeatLaunchInsert? Launch,
    SeatCommandInsert? Command, IReadOnlyList<SeatCommandUpdate> Updates,
    Guid? AbandonSessionId, SeatDesired? Desired, string? DesiredBy, DateTimeOffset At);
public interface ISeatCommandStore
{
    Task<SeatStoreReceipt> CommitAsync(SeatCommandTransaction transaction, CancellationToken ct);
}
public sealed record SeatPreparedLaunch(SeatSessionInsert? Session, SeatLaunchInsert Launch,
    SeatCommandInsert Command, Aiakos.Contracts.Node.V1.Command WireCommand);
public interface ISeatLaunchFactory
{
    SeatPreparedLaunch Prepare(SeatActorSnapshot before, StartLaunch effect,
        SeatLaunchMaterial material, CallerContext caller, string? note, DateTimeOffset at);
}
```

R1 owns Core records, replies, connection/port/token/material types. R2 owns the insert/update/
transaction records and store port. R5 owns PreparedLaunch/factory. Public types listed here
are implemented only in their owning story. Keep all existing constructors and interfaces
usable by earlier tests; new optional service bundles or overloads may compose command services
without changing the 13-3 ports. Internal requests are keyed by SeatKey and use asynchronous
TaskCompletionSource; their private names are not an API. Cancellation is caller-wait cancellation.
Concrete names are SeatCommandDispatcher, PostgresSeatCommandStore, SeatTokenGenerator and
SeatLaunchFactory. Extend existing SeatActor/SeatRegion/SeatActorGateway rather than duplicate them.
No new packages, Version attributes, NoWarn, pragma warning suppression or migration. Tests live
in tests/Aiakos.Orchestrator.Tests; existing TestKit/xUnit-v3 patterns remain the standard.

## General rules

G1. Trusted identity is CallerContext.TenantId/User/Sender; envelope tenant must match caller and
    SeatId must be a nonempty UUID. Never infer identity from command/body/address text. Clone
    mutable protobufs, byte arrays and JsonElement values at asynchronous boundaries. Bodies,
    tokens, raw evidence and arbitrary exception messages never enter logs, spans or metrics.
    Before admission cancellation queues nothing; after admission it cancels only the wait and
    never a commit or an already accepted send. No retry of an admitted public command. A missing
    service returns fixed SeatCommandRejected("SEAT_COMMAND_SERVICE_UNAVAILABLE"); never a dummy.

G2. All mutations and node sends are decisions of the one seat actor. No SQL from dispatcher,
    lifecycle observer, node port or timer callbacks. The dispatcher forwards through the
    existing region envelope with tenant and seat, preserving mailbox serialization. Every
    send, outcome notification and successful reply follows its corresponding committed state.
    No database/port I/O in the pure machines. Earlier event/state-machine outputs stay exact.

## Rules

R1. Publish R1's records/ports exactly above. SeatCommandDispatcher implements the Core interface
    and resolves no addresses. Dispatch validates caller/envelope match, nonempty seat and
    supported message, returning SeatCommandRejected("INVALID_SEAT_COMMAND") without actor/store/
    port work otherwise. Null command/caller returns a safe faulted task ArgumentNullException
    with parameter name command/caller; null inner Message is INVALID_SEAT_COMMAND. User/Sender
    must be nonempty strings without CR/LF/NUL or isolated surrogate; Note permits newlines but
    not NUL/isolated surrogate. Capture HistoryLines 0..10000 inclusive; body validation uses
    InputValidator.CheckBody; require Problem==None and pass its Normalized body to the adapter. Bad note/body/history yields INVALID_SEAT_COMMAND without value
    echo. Region eligibility failures map exact SEAT_NOT_FOUND/HUMAN_SEAT/SEAT_RETIRED/profile
    reason from 13-3 to SeatCommandRejected. Returned accepted/up/down/capture/timeout records
    have the properties shown, never a second CallerContext or ambiguous Core.SeatEnvelope.

R2. Publish R2's transaction records/store port exactly above. Inputs hold the actor's already
    computed SeatStep values. Transaction.Before is the loaded snapshot/version; At is trusted
    TimeProvider time. Session/Launch/Command are optional inserts, Updates is empty when absent;
    Desired is null unless up/down changes desired configuration; AbandonSessionId is an existing
    session to mark abandoned. Commit returns the actual version and current session pointer.
    The actor alone calls it; after success it refreshes via reader before any reply/send.
    A no-op returns unchanged version; any persisted mutation increments version exactly once.

R3. PostgresSeatCommandStore executes one transaction sharing 13-3's writer helpers/transaction
    stages rather than invoking a separately committing ISeatActorWriter. Factor a common
    transaction helper if required, preserving existing CommitAsync behavior. Insert order is
    session, launch, command, applied evidence/transitions/findings, then version-checked state;
    update desired configuration and abandonment in that same transaction. All WHERE clauses
    use tenant_id/seat_id, plus version for state. New ids are Guid.CreateVersion7; no fixed ids.
    The actual insert columns/values are exactly:

    | Table | Values |
    |---|---|
    | seat_session | tenant_id=Before.Key.TenantId; session_id=Session.SessionId; seat_id=Before.Key.SeatId; harness=Session.Harness; native_session_id=Session.NativeSessionId; decision=Session.Decision; previous_session_id,conversation_at,lost_at,abandoned_at,abandoned_reason=NULL; created_at=At |
    | seat_launch | tenant_id/seat_id=Before.Key; launch_id,session_id,mode,decision,decided_by,decision_note,command_id,node_name,spec_hash,binding_hash,seat_token_hash=Launch properties; requested_at=At; outcome,outcome_reason,observed_session_id,exit_code,exit_signal,evidence,outcome_at,ended_at,end_reason=NULL |
    | seat_command | tenant_id/seat_id=Before.Key; command_id,launch_id=Command; kind=SeatVocabulary.ToStored(Command.Kind); status='pending'; payload=independent Command.Payload; forced=Command.Forced; requested_by=Command.RequestedBy; traceparent=validated trace or NULL using 13-3 validation; attempts=0; created_at=At; outcome,error_reason,node_instance_id,turn_id,result,sent_at,completed_at=NULL |

    Columns state/status/user/sender/payload_hash on these respective tables do not exist and
    must not be introduced. Payload never contains the token or token hash. Up session pointer
    is the inserted session or reused Before.CurrentSessionId; current_launch_id=Launch.LaunchId.
    Reuse never inserts/rewrites a session. AbandonSessionId sets abandoned_at=At and
    abandoned_reason='fresh-explicit', keeping all history; null does nothing. Desired updates
    seat.desired to up/down, desired_at=At, desired_by=DesiredBy, updated_at=At; state reader
    obtains Desired from seat, not a nonexistent seat_state.desired column. Each applied
    transition uses cause_type='command', cause_command_id=Command.CommandId when creating a
    command; timer transitions use cause_type='timer', NULL cause_command_id. Findings and all
    mutable state columns follow 13-3's transaction rules. Nothing deletes prior evidence.
    Version conflict throws InvalidOperationException("SEAT_VERSION_CONFLICT") without inner;
    check/not-null/FK violations give SeatStoreRejectedException("SEAT_COMMIT_REJECTED").
    All failures roll back inserts, desired, abandonment, findings and state together.

R4. Store Updates mutate only the same seat's existing command: status,outcome,error_reason,
    node_instance_id,attempts,sent_at,completed_at, using the supplied properties. Missing or
    foreign command is SEAT_COMMIT_REJECTED with rollback. A final status (completed/rejected/
    failed/timed-out/unknown) never gets overwritten by a later send acknowledgement, deadline
    or node-loss update. For accepted send acknowledgement status='sent', attempts=1,
    node_instance_id=the connection used, sent_at=At; completed_at/outcome/error_reason=NULL.
    For send failure status='failed', attempts=0, sent_at/node_instance_id=NULL,
    completed_at=At,error_reason='COMMAND_DISPATCH_FAILED'; deliver outcome='not-delivered',
    other kinds NULL. Apply CommandDispatchFailed(kind) in the same failure transaction for
    start/stop; deliver uses DeliveryStateMachine Pending + DeliveryDispatchFailed. No token,
    result, native identity or launch outcome is manufactured from a successful send ack.

R5. SeatLaunchFactory accepts the StartLaunch effect produced by the pure up decision; it does
    not choose the launch mode. Select the matching IHarnessAdapter/Profile by Before.Harness,
    call BuildLaunch(material.Parameters,effect.Mode,new NativeSession(effect.NativeSessionId),
    cloned SuppliedFiles). Validate material node/harness match Before, nonempty address/hashes,
    and native id through Profile; missing/invalid material yields fixed
    InvalidOperationException("LAUNCH_MATERIAL_UNAVAILABLE"), no inner or values. For a
    new-session launch the actor generates native id via Profile.NewNativeSessionId; resume
    passes the stored id verbatim, never a new one. Material has real finalized projection and
    relay files in production; tests inject immutable fixtures. BuildLaunch errors are fixed
    INVALID_LAUNCH rejections, never echo builder exceptions. Factory creates a new command UUID,
    and a session UUID only if effect.NewSession; otherwise requires Before.CurrentSessionId.
    New session decision='new-session' if effect.Decision is new-session/no-conversation-yet,
    otherwise 'fresh-explicit'. Launch mode is fresh/resume, decision exactly effect.Decision;
    decided_by=caller.User,note=request.Note,node/hashes=material,requested_by=caller.User.
    Command payload JSON is exactly {"mode":<stored mode>,"native_session_id":<id>}.
    Token generator returns 32 cryptographically random bytes per launch, independent each
    time; injectable test generator uses ordinary opaque fixture bytes. Store only SHA-256(token)
    in SeatLaunchInsert.SeatTokenHash. Wire StartSeat.Secrets has exactly one seat_token with
    these bytes and FilePath="secrets/seat-token"; never EnvVar, argv/env or projected file bytes.
    Add non-secret Env AIAKOS_SEAT=material.Address and
    AIAKOS_SEAT_TOKEN_FILE="${AIAKOS_SEAT_HOME}/secrets/seat-token"; node writes mode0600.
    StartSeat contains launch/seat/native ids, material address/harness, effect mode, BuildLaunch
    argv/env/files/terminal/ReadyTimeout. Workspace.Path=Parameters.Workdir; for seat-worktree
    its matching workdir checkout supplies Worktree.RepoPath=SourcePath, Branch, BaseRef;
    shared checkout has no Worktree. Command.SeatId=Before.Key.SeatId lowercase D;
    Command.CommandId=the new UUID lowercase D; Attempt=1; Timeout=30s. Never serialize or log
    PreparedLaunch/WireCommand; no token retained in a row or exception.

R6. Actor up handling calls pure Apply with UpRequested(Fresh,connection.Connected,newLaunchId,
    profileGeneratedNativeId) and uses that sole decision. Profile id may be generated but no
    token/row is created for rejection/no-op. Precedence stays pure-machine exact: disconnected
    NODE_NOT_CONNECTED; connected starting/present AlreadyUp; connected unknown
    SEAT_STATE_UNKNOWN; connected absent/exited with resumability Lost and !Fresh RESUME_LOST.
    For AlreadyUp commit desired-up only when it changes and return SeatAlreadyUp(current id).
    For StartLaunch prepare R5, then commit R3 rows, state starting/readiness false and desired-up
    before sending; new session resets resumability FreshOnly per pure Apply. Explicit Fresh
    that replaces a previous session marks it abandoned as R3, closes resume-loss/mismatch
    findings per pure Apply and preserves caller.Note in launch decision_note. Do not fall back
    when a native id/previous-session pointer is missing: LAUNCH_MATERIAL_UNAVAILABLE.
    After precommit/read refresh send once via port using the exact current connection instance,
    then commit R4 send acknowledgement/failure. Sent=true returns
    SeatCommandAccepted(newLaunchId,commandId); false/port exception returns
    SeatCommandRejected("COMMAND_DISPATCH_FAILED") after its safe failure commit. No up reply
    means launch-ready: final launch outcomes arrive exclusively in committed CommandResult.

R7. Commands/result integration runs within the existing SeatActor mailbox. Successful send
    acknowledgment is transport acceptance only, not LaunchReceipt/PaneCapture/DeliveryReceipt.
    Existing 13-3 mapping/writer owns CommandResult launch/stop/delivery/capture results, launch
    metadata, resumability changes and event/axis/finding transaction. Refresh the committed
    command after event commit before completing an outcome wait. Waits are keyed by CommandId;
    capture registers its completion before send so a fast event cannot race registration.
    Validate same tenant/seat, stored command kind and launch; duplicate/stale/foreign/missing
    results cannot complete another wait. A result received while port send is outstanding is
    enqueued and handled after that mailbox operation; never wait for a CommandResult inside
    the actor's send handler. Continue processing mailbox while capture caller awaits. No port
    result ever bypasses the event transaction. Already-final command metadata from reload may
    satisfy a matching existing wait; prospective events may not. Public send/down/up return
    admission accepted, as 15-2 specifies; they do not wait for terminal outcomes. Any internal
    waiter/subscriber for a delivery outcome follows this same committed-event rule. Pending
    replies use TrySetResult/Exception so event/timeout/node loss/cancellation completes at most
    once. Store/refresh failure uses 13-3 SeatCommitFailedException and supervision; no success.

R8. Down applies DownRequested and commits desired-down (caller.User) plus the pure step. If
    reported session is absent or no current launch exists, do not insert/send a command;
    return SeatAlreadyDown after desired commit. Otherwise one pending stop command has payload
    {"grace_seconds":10}; Wire StopSeat.Grace=10s, Command.Timeout=30s, Attempt=1, seat/id as R5.
    Commit it and StopRequested before send. Send=true is SeatCommandAccepted(launchId,commandId);
    failure follows R4 COMMAND_DISPATCH_FAILED. Existing committed StopResult STOPPED/KILLED/
    NOT_RUNNING makes session Absent/activity None; FAILED/TIMED_OUT result makes session
    Unknown with reason stop-failed and activity Unknown/stop-failed. Resumability never changes
    for stop alone. Node loss still uses the existing link overlay, never an invented stopped.

R9. Capture requires a current launch, including exited/unknown; no launch returns
    SeatCommandRejected("SEAT_NOT_PRESENT"). Disconnected returns NODE_NOT_CONNECTED. Commit a
    capture command with payload {"history_lines":HistoryLines}, forced=false and requested_by
    caller.User before sending; Timeout=5s, Attempt=1, CapturePane.HistoryLines=(uint)value.
    Capture command insertion/send acknowledgment changes no axis. Send failure follows R4.
    After committed matching Completed CommandResult with Capture, return
    SeatCaptureCompleted(Capture.Clone()). Failed/rejected/noncapture terminal result gives
    SeatCommandRejected("CAPTURE_FAILED"); timed-out gives SeatCommandTimedOut("CAPTURE_TIMEOUT").
    At local 5s deadline from successful send, commit status='timed-out',error_reason=
    'CAPTURE_TIMEOUT',completed_at=At before that reply; leave outcome NULL. Final event beats
    timeout if its commit occurred first; no synthetic wire event. PaneDead is evidence only:
    use existing 13-3 CaptureBody behavior/finding, never infer a healthy axis from a capture.

R10. Send runs InputValidator before admission and pure SendRequested(Force,connected,
    DeliveryInFlight). In-flight means the same seat has an existing deliver status pending/sent.
    Rejection order is disconnected NODE_NOT_CONNECTED; unknown session SEAT_STATE_UNKNOWN;
    session not Present SEAT_NOT_PRESENT; in-flight DELIVERY_IN_FLIGHT; Working SEAT_WORKING;
    NeedsInput SEAT_NEEDS_INPUT; Unknown activity without Force SEAT_ACTIVITY_UNKNOWN.
    Force permits Unknown activity only, never an overlay/working/input dialog. Use matching
    adapter.BuildDelivery(caller.Sender,InputValidator.CheckBody(Body).Normalized,commandId) to build lead/body/expect_confirmation/
    ConfirmTimeout; request carries no sender/lead override. Adapter failure is fixed
    INVALID_DELIVERY rejection with no rows/send. Commit pending deliver with JSON payload
    {"lead":built.Lead,"body":built.Body,"expect_confirmation":built.ExpectConfirmation},
    forced=pure Accepted.Forced,requested_by=caller.User. Wire DeliverInput matches all built
    fields; Command.Timeout=30s,Attempt=1; ConfirmTimeout comes from adapter/profile, not15s.
    After precommit send once and commit R4; return SeatCommandAccepted(launchId,commandId).
    Admission accepted carries no final delivery outcome and changes no activity by itself.

R11. Only the existing committed result writer/DeliveryStateMachine decides a sent delivery's
    final outcome: CONFIRMED→confirmed; SUBMITTED_UNCONFIRMED→submitted-unconfirmed plus open
    delivery-unconfirmed; NOT_DELIVERED→not-delivered; unspecified→unknown; FAILED/REJECTED/
    TIMED_OUT status→failed. A final outcome is immutable. On node instance replacement or
    NodeLinkLost, any sent nonfinal delivery becomes status='unknown',outcome='unknown',
    completed_at=At; pending not yet dispatched becomes failed/not-delivered instead. Persist
    these changes with the link input step in one R3 transaction, then notify any waiters.
    Never reissue deliver from seat actor, timer, restart or dispatcher. Same-instance transport
    reconnection resend remains NodeProxy/10-5's responsibility; this actor does not schedule it.
    Delivery confirmation has no direct activity effect. On local 30s command deadline persist
    status='timed-out',outcome='failed',error_reason='COMMAND_TIMEOUT',completed_at=At; no retry.

R12. After a committed confirmed delivery with nonempty turn_id, compare the next successful
    event/input commit against persisted plus currently received seat events of current launch:
    a Harness PROMPT_SUBMITTED with matching attributes.turn_id satisfies it, even if it arrived
    before the result. If missing at that next commit, append sources-disagree to that input's
    SeatStep.Findings before its writer commit. Remember checked command ids during this actor
    lifetime; after reload conservatively recheck unresolved confirmed deliveries at the next
    commit, skipping those whose turn already has matching prompt evidence. No fabricated prompt
    or activity change.
    Do not check stale launches or confirmation without turn_id. Do not parse raw payloads.

R13. Actor timers use TimeProvider, injected fake time and per-arm generation/current launch;
    stale queued timer messages no-op. Timer callbacks only enqueue actor requests. Timer rules:

    | Timer | Arm/rearm and cancel | Enqueued input/action |
    |---|---|---|
    | quiet | known Present/Working with LastEventAt: due LastEventAt+Profile.QuietTimeout; rearm on new LastEventAt; cancel when not Present/Working, overlay or actor stop | QuietTimeoutFired, existing pure Apply and writer; stale deadline no-op |
    | launch watchdog | after launch precommit; due launch requested At+Profile.ReadyTimeout+30s command timeout+30s; cancel on terminal launch result, absent/exited or actor stop | LaunchWatchdogFired; starting→Unknown/launch-result-missing and launch-unconfirmed finding via pure machine |
    | unknown prolonged | reported Unknown: due SessionSince+5min; retain deadline across same unknown reason; cancel when no longer Unknown or actor stop | UnknownProlongedFired; existing state-unknown-prolonged finding, no command |
    | capture | after successful capture send: SentAt+5s; cancel on final result/wait cancellation/actor stop | R9 committed timeout, reply CAPTURE_TIMEOUT |
    | command | after successful start/stop/deliver send: SentAt+30s; cancel on final committed result/actor stop | commit timed-out/error_reason COMMAND_TIMEOUT; start uses StartNotCompletedBody(TimedOut,COMMAND_TIMEOUT), stop StopNotCompletedBody(TimedOut), deliver DeliveryNotCompleted(TimedOut); no invented SeatEvent |

    Timer-derived pure steps and command updates share one store transaction. Command timeout
    does not delete launch-watchdog: watchdog may later establish missing-result diagnosis.
    ReadyTimeout/QuietTimeout/ConfirmTimeout always come from the selected harness; fixed5s
    capture,30s command,10s stop grace and5min unknown are orchestration policy, not profile values.
    Reload uses stored requested_at/sent_at/last_event_at/session_since, not a fresh full interval;
    overdue timers enqueue once when resumed. Do not arm a second timer per unrelated input.

R14. Extend the existing actor restart reload: no relaunch or new delivery. Keep the 13-3
    OrchestratorRestarted/NodeLinkLost overlays and notification-before-release handshake.
    On successful reload, persisted pending/sent command metadata is the truth; outcomes not
    established by results remain unknown rather than success. Await SeatActorReloaded region
    publication before newly queued public requests can decide. For a current launch whose
    reported session is Unknown, create one evidence capture (history_lines=0) through R9's
    precommit/send path, after notification; no launch leaves none. Suppress duplicate capture
    if a pending/sent capture already exists; do not block other commands awaiting its result.
    Restore timers per R13. Reload does not reset an already final outcome or re-send a start/
    deliver command. Actor stop/restart faults pending capture waits with
    SeatCommandRejected("SEAT_ACTOR_UNAVAILABLE"); link loss completes them with
    SeatCommandRejected("NODE_NOT_CONNECTED") after the link commit. Wait cancellation removes
    the wait/timer but neither undoes command rows nor stops processing later committed results.

R15. Add observability at committed transitions/findings and command boundaries only. ActivitySource
    name="Aiakos.Seats"; activities seat.apply,seat.up,seat.down,seat.send,seat.capture; tags only
    aiakos.seat.address,aiakos.seat.launch_id,aiakos.command.id plus kind/outcome/reason.
    On each committed transition Information log template exactly
    "Seat {SeatAddress} {Axis} {From} -> {To} ({Reason}) cause {CauseType} launch {LaunchId}";
    increment Counter<long> aiakos.seat.transitions with axis/from/to/cause_type; add span event
    seat.transition with axis/from/to/reason/cause. Finding changes emit Warning
    "Seat {SeatAddress} finding {Kind} {Status}" with status open/resolved. Meter name Aiakos.Seats;
    counters aiakos.seat.launches(mode,decision,outcome), aiakos.seat.deliveries(outcome,forced),
    aiakos_seat_commands_total(kind,outcome), one increment per committed respective change.
    Do not count rollback/prospective steps or replay duplicates. No body/token/raw/capture text,
    note, secret path, CallerContext.Sender or exception detail in any telemetry. Traceparent
    validation/linking follows 13-3; no second logging of the same commit by gateway/dispatcher.

R16. Add command service registration to the existing shell only when its named dependencies
    are supplied. Core ISeatCommandDispatcher singleton delegates to the same SeatActorGateway/region
    that exposes 13-3's event/input/lifecycle ports. Register store/factory/token generator as
    services for the existing actor; don't start a second region or actor system. Preserve old
    actor constructors/event-only configuration and health/shutdown tests. Missing command
    services reject SEAT_COMMAND_SERVICE_UNAVAILABLE while 13-3 event/input paths remain valid.
    Production port/connection/material providers remain prerequisites; expose registration
    extension class SeatCommandServiceCollectionExtensions with
    AddSeatCommands(this Microsoft.Extensions.DependencyInjection.IServiceCollection services),
    returning that same IServiceCollection, to consume registered providers, not stub them.

## Changes to earlier behavior

C1. The 13-3 actor's unsupported public command branch now admits this slice's typed command
    request through region/gateway, while unsupported generic ApplyAsync inputs still fail
    SEAT_INPUT_NOT_SUPPORTED. Update only tests that explicitly asserted new public commands
    were unsupported; no pure-machine or existing event/ingest expectation changes. New store
    factoring must preserve every existing reader/writer/actor test. The 15-2 API bridge consumes
    Core.ISeatCommandDispatcher and Core.SeatCommandEnvelope; it owns its matching bridge edit.

## Expected outputs: exact text

Each row is one item/test case family. IDs below are stable only after approval. Use fake port,
controlled completions/time and real Postgres for SQL; no Claude/process/transport required.
Base actor fixture: tenant/seat UUIDs T/S, agent harness fixture, node n with live instance N,
version7, current session J, launch L, Present/Idle/Resumable, desired Up; caller(T,"owner","lead").
Symbols L2/C/J2 mean fresh UUIDs asserted consistent across rows/wire/replies, never pinned values.
All actor cases assert commit and metadata refresh occur before sends/replies.

| ID | Change | Exact output |
|---|---|---|
| `PROTOCOL` | reflect seam and all reply records; invalid tenant/UUID/body/note/history | exact properties/signatures above; invalid returns SeatCommandRejected("INVALID_SEAT_COMMAND"), zero queued requests/rows/sends |
| `STORE-shape` | reflect transaction records and store interface | every R2 property/signature exactly above; immutable insert/update/transaction data; no implementation or SQL required here |
| `STORE-inserts` | prepared fresh launch with session J2/L2/C and step Starting | every R3 column exact; session decision new-session, launch fresh/new-session, command pending/start/attempts0; version8, pointers J2/L2; desired_up attributed owner; no secret bytes; unchanged transaction with no mutation returns SeatStoreReceipt(7,J)/no writes |
| `STORE-rollback` | state version mismatch or invalid NOT NULL/check/FK after insert | SEAT_VERSION_CONFLICT or SEAT_COMMIT_REJECTED/no inner; zero new session/launch/command/transitions/findings; old desired/pointers/abandonment unchanged |
| `STORE-status` | send success/failure then late success after final event | R4 exact sent/failed fields; late send ack does not overwrite final outcome; foreign/missing update SEAT_COMMIT_REJECTED/no partial writes |
| `LAUNCH-build` | fresh/resume and shared/seat-worktree, fake adapter/files/profile | StartSeat matches R5 fields, timeout30s, native id chosen solely by effect/profile; no new id on resume; token only Secrets[0] seat_token/FilePath secrets/seat-token, SHA256 only in launch row |
| `LAUNCH-invalid` | missing source/mismatched node/invalid native id/build error | LAUNCH_MATERIAL_UNAVAILABLE or INVALID_LAUNCH, no token in exception, zero commit/send |
| `UP-matrix` | connected/disconnected, each Session and Resumability, Fresh flag | pure-machine precedence in R6; starting/present SeatAlreadyUp(L), unknown SEAT_STATE_UNKNOWN, disconnected NODE_NOT_CONNECTED, lost without Fresh RESUME_LOST; no-op/rejection no launch/command/token |
| `UP-start` | absent/exited, None/FreshOnly/Resumable/Unknown/Lost plus explicit Fresh | L2/session/native/mode/decision exactly pure StartLaunch; accepted SeatCommandAccepted(L2,C) after commit/send ack; explicit Fresh abandons old row and records owner/note; no fallback |
| `RESULT-order` | capture/delivery result blocked at writer, duplicate/foreign/stale result | no final reply before commit+refresh; matching committed result completes exactly once; no direct port outcome; mailbox serves other requests while capture waits |
| `DOWN-stop` | absent; live launch; completed stop and failed/timed-out stop | absent→SeatAlreadyDown; live→SeatCommandAccepted(L,C), desired Down and stop payload grace10 before send; completed stop→Absent/None; failed/timed-out→Unknown/stop-failed; original resumability retained |
| `CAPTURE-result` | launch in each state; missing launch; successful/failed result or5s timeout | no launch SEAT_NOT_PRESENT; completed returns SeatCaptureCompleted(exact cloned PaneCapture); failure CAPTURE_FAILED; timeout SeatCommandTimedOut("CAPTURE_TIMEOUT") after timed-out row commit; capture admission no axis change |
| `SEND-reject` | each invalid admission state and Force | R10 exact rejection precedence; zero deliver row/send; Force only unknown activity, sets forced=true; present idle Force=true records forced=false |
| `SEND-accepted` | adapter derives delivery from trusted lead and body; ack success/failure | accepted SeatCommandAccepted(L,C); exact lead/body/expect_confirmation payload; profile ConfirmTimeout wire; ack failure failed/not-delivered, COMMAND_DISPATCH_FAILED; no activity change |
| `SEND-outcome` | all delivery results/noncompleted status/node loss/deadline/replay | R11 exact immutable outcomes; node loss unknown; pending no send not-delivered; deadline failed/COMMAND_TIMEOUT; one original send, no actor resend |
| `SEND-evidence` | confirmed turn with/without matching prompt after next commit | missing→one open sources-disagree; matching→no such new finding; stale launch ignored; activity still only pure harness conclusions |
| `TIMERS` | varied profiles, fake time just before/at due; stale generation | exact R13 timer/input/deadlines, no action before due, one after; quiet uses profile, watchdog ready+60s, unknown5min, no reissue; overdue reload does not restart interval |
| `RELOAD` | restart/load handshake blocked; final/pending commands; no/live launch | no public request/send before SeatActorReloaded; live unknown→one capture C/history0 after notification; no launch→none; no start/deliver resend; final outcomes preserved |
| `CANCEL` | cancellation pre/post admission, capture waiter canceled, link lost, actor stop | pre: canceled Task/no rows/send; post: canceled wait/commit may complete; link after commit NODE_NOT_CONNECTED, stop SEAT_ACTOR_UNAVAILABLE; no rollback or resend |
| `OBSERVE` | committed transition/finding and duplicate/rollback with sentinel body/token/note/raw | exact R15 templates/names/tag allowlist; one committed transition counter/event; Warning finding; zero telemetry occurrences of sensitive sentinels; duplicate/rollback no increments |
| `HOST-command` | DI with fake real seams; services absent; dispose | same existing gateway/region for all ports; supplied dispatcher works; absent rejects SEAT_COMMAND_SERVICE_UNAVAILABLE; old health/event tests unchanged, termination awaited |

## Permanent tests

T1. Commit tests for PROTOCOL, immutability and G1 validation, including valid non-BMP/U+FFFE,
    newline body/note versus invalid isolated surrogate/NUL; null top-level parameter names.
T2. Commit transaction/insert/rollback/status Postgres tests for R2–R4 with tenant isolation,
    existing history preservation, exact finite fields and not-null schema constraints.
T3. Commit launch factory/token tests with fake adapter and injected opaque token bytes, hash
    equality, new token each launch, mutable input cloning, no secret in row/argv/env/telemetry.
T4. Commit up matrix/atomic-order tests including failed resume result then RESUME_LOST and
    explicit Fresh; return no success before refresh; store failures use existing supervision.
T5. Commit pending outcome tests with controlled event writer and fast-result race, malformed/
    unknown/duplicate/foreign/stale results, bounded cancellation and no two outcome sources.
T6. Commit down tests with all StopOutcome/status variants, unchanged resumability, desired
    attribution and safe send failure.
T7. Commit capture tests for all launch states/history bounds, successful cloned bytes, timeout
    versus event commit ordering, dead-pane evidence and actor continuing while caller waits.
T8. Commit send tests for every R10 admission and R11 outcome, at-most-once/node replacement/
    timeout; prompt-evidence confirmation finding in R12 and activity never set by receipts.
T9. Commit fake-time timer/reload/cancel tests for each R13 arm/cancel rule, varied profiles,
    overdue deadlines, duplicate capture suppression and queued-request publication order.
T10. Commit MeterListener/ActivityListener/captured-log tests pinning R15 names and secret/body/
    raw/note sentinel absence.
T11. Commit same-gateway DI/health/shutdown regression tests for R16 with missing command services
    rejecting safely and the original event-only configuration still functioning.

## Done and out of scope

Build Release has zero warnings/errors; the orchestrator tests and all earlier affected tests
pass. Gate judges the committed story only. UTF-8 without BOM, LF and final newline. No push/PR
by author/implementer; lead owns those and maintainer merges. No schema/proto/pure-machine
change, terminal/harness execution, token-file writing, transport reconnection, API/CLI query
or HTTP routes, 13-5 integration beyond the named seams, remoting/clustering, or automatic
relaunch/fresh fallback. Risks checked: transactional sole writer, lost resume and at-most-once
0006-RK3/RK4/RK8/RK11; live node/Claude/restart demonstration belongs to later integration.
