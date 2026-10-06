---
id: 13-3
title: "#13 slice 3 — transactional SeatActor shell"
issue: 13
status: draft
route: impl
paths: [src/Aiakos.Orchestrator/, tests/Aiakos.Orchestrator.Tests/, Directory.Packages.props]
date: 2026-10-06
---

# Brief: #13 slice 3 — transactional SeatActor shell

Part of #13. Self-contained. **Do not read specs or ADRs**, except the existing SQL definitions
in `src/Aiakos.Data/Migrations/0002_seat_model.sql` (read only). Read the existing state machine,
vocabulary and repository/test style. Where silent, preserve existing behavior, choose the
smallest implementation and record the choice in the commit body.

## Goal

Host one local actor per agent seat and commit each accepted input's evidence and conclusions
atomically before reporting success. Provide the exact actor commit/reload seam that 10-4's
node events integration consumes. This slice handles existing normalized event bodies, link
inputs and explicit timeout inputs; it does not create launches, commands or scheduled timers.

Current main has 13-1's pure machine, 13-2's schema/read queries and 10-3's transport. It has no
SeatActor, transactional seat writer or production IHarnessStateProfile. The actual profile is
owned by #12: inject profiles in tests and reject a missing profile explicitly in production.
No default Claude profile, always-successful writer or Empty actor substitute is added here.
10-4's node facade/security/replay/re-forwarder is not yet merged and is not a prerequisite for
this slice. Its integration waits for this slice's public interfaces and implementation.

Issue #13 lists 0001-RK4, 0005-RK3/RK9/RK12 and 0006-RK1/RK3/RK4/RK8/RK11. Here check
TestKit/v3 compatibility (0001-RK4/0006-RK1), persisted enum vocabulary (0006-RK11), atomic
ordering/dedupe and bounded evidence growth per replay. The earlier pure tests guard stale
ordering and hook limitations. Actual apply-duration measurements, retention and real hook/
node restart demonstrations remain with integration/stage B; do not claim those risks closed.

## Files to create or touch (nothing else)

| Path | What |
|---|---|
| `src/Aiakos.Orchestrator/Seats/` | Protocol/ports, wire mapper, separate SQL reader/writer, actor, region and facade |
| `src/Aiakos.Orchestrator/Program.cs` | Register shell after migrations without replacing node application |
| `tests/Aiakos.Orchestrator.Tests/` | xUnit v3 TestKit adapter, actor probes/fakes, database tests and compatibility record |
| `Directory.Packages.props` | Only central Akka.TestKit 1.5.71 entry |
| `tests/Aiakos.Orchestrator.Tests/Aiakos.Orchestrator.Tests.csproj` | Only Akka.TestKit reference without Version |

No new project, other package, Version on PackageReference, NoWarn, pragma warning disable or
SuppressMessage. Never change migrations, the pure machine/vocabulary, Data, Contracts, node
transport, existing node application registration or existing state-machine test expectations.

## Public surface (exact names)

Namespace `Aiakos.Orchestrator.Seats`. R2 owns these ports/records and no implementation:

```csharp
public sealed record SeatKey(Guid TenantId, Guid SeatId);
public sealed record SeatEnvelope(Guid TenantId, Guid SeatId, object Message);
public sealed record SeatEventsCommitted(Guid SeatId, Guid NodeInstanceId, long ThroughSeq);
public sealed record SeatInputCommitted(Guid SeatId, long Version, SeatState State,
    IReadOnlyList<SeatEffect> Effects);
public sealed record SeatActorReloaded(Guid TenantId, Guid SeatId, long Version);
public sealed class SeatCommitFailedException : InvalidOperationException
{
    public SeatCommitFailedException();
    public SeatCommitFailedException(string? message);
    public SeatCommitFailedException(string? message, Exception? innerException);
}
public interface ISeatEventCommitter
{
    Task<SeatEventsCommitted> CommitAsync(Guid tenantId, Guid seatId, string nodeName,
        Guid nodeInstanceId, IReadOnlyList<Aiakos.Contracts.Node.V1.SeatEvent> events,
        string? traceParent, CancellationToken ct);
}
public interface ISeatInputCommitter
{
    Task<SeatInputCommitted> ApplyAsync(Guid tenantId, Guid seatId, SeatInput input,
        CancellationToken ct);
}
public interface ISeatActorLifecycle
{
    IDisposable Subscribe(Action<SeatActorReloaded> observer);
}
public sealed record SeatStoredCommand(Guid CommandId, Guid? LaunchId, SeatCommandKind Kind,
    string Status, string? Outcome);
public sealed record SeatActorSnapshot(SeatKey Key, string Kind, string? Harness, string? Node,
    bool Retired, long Version, Guid? CurrentSessionId, SeatState State,
    IReadOnlyDictionary<Guid, SeatStoredCommand> Commands);
public sealed record SeatAppliedInput(SeatInput Input, SeatStep Step, DateTimeOffset At,
    Aiakos.Contracts.Node.V1.SeatEvent? Event, string? TraceParent);
public sealed record SeatStoreReceipt(long Version, Guid? CurrentSessionId);
public sealed record SeatNativeSession(Guid SeatId, Guid SessionId);
internal sealed class SeatStoreRejectedException : InvalidOperationException
{
    internal SeatStoreRejectedException(); // fixed SEAT_COMMIT_REJECTED, no inner
}
public interface ISeatActorReader
{
    Task<SeatActorSnapshot?> LoadAsync(SeatKey key, CancellationToken ct);
    Task<IReadOnlyList<SeatKey>> GetStartupSeatsAsync(CancellationToken ct);
    Task<SeatNativeSession?> FindNativeSessionAsync(Guid tenantId, string harness,
        string nativeSessionId, CancellationToken ct);
    Task<IReadOnlySet<long>> GetCommittedSequencesAsync(SeatKey key, Guid nodeInstanceId,
        IReadOnlyList<long> sequences, CancellationToken ct);
}
public interface ISeatActorWriter
{
    Task<SeatStoreReceipt> CommitAsync(SeatActorSnapshot before,
        IReadOnlyList<SeatAppliedInput> inputs, CancellationToken ct);
    Task RecordActorStoppedAsync(SeatKey key, DateTimeOffset at, CancellationToken ct);
}
// R2 internal actor messages, visible to the existing friend test assembly:
internal sealed record SeatEventRequest(string NodeName, Guid NodeInstanceId,
    IReadOnlyList<Aiakos.Contracts.Node.V1.SeatEvent> Events, string? TraceParent,
    TaskCompletionSource<SeatEventsCommitted> Completion);
internal sealed record SeatInputRequest(SeatInput Input,
    TaskCompletionSource<SeatInputCommitted> Completion);
internal sealed record SeatChildLoaded(SeatKey Key, long Version, TaskCompletionSource Ready);
// R3 mapper; previously merged proto and state-machine types only.
internal static class SeatWireInput
{
    internal static EventReceived Map(Guid nodeInstanceId,
        Aiakos.Contracts.Node.V1.SeatEvent value,
        IReadOnlyDictionary<Guid, SeatStoredCommand> commands);
}
// R4/R5–R8: independent concrete implementations.
public sealed class PostgresSeatActorReader(Npgsql.NpgsqlDataSource dataSource) : ISeatActorReader;
public sealed class PostgresSeatActorWriter(Npgsql.NpgsqlDataSource dataSource) : ISeatActorWriter;
// R9/R10: real Akka ReceiveActor, with fakeable dependencies.
public sealed class SeatActor : Akka.Actor.ReceiveActor
{
    public SeatActor(SeatKey key, ISeatActorReader reader, ISeatActorWriter writer,
        IReadOnlyList<IHarnessStateProfile> profiles, TimeProvider timeProvider);
}
// R11–R13: real Akka region and same singleton facade exposed through all three ports.
public sealed class SeatRegion : Akka.Actor.ReceiveActor
{
    public SeatRegion(ISeatActorReader reader, ISeatActorWriter writer,
        IReadOnlyList<IHarnessStateProfile> profiles, TimeProvider timeProvider);
}
public sealed class SeatActorGateway : ISeatEventCommitter, ISeatInputCommitter, ISeatActorLifecycle
{
    public SeatActorGateway(Akka.Actor.IActorRef region);
}
```

The class outlines above state constructors/interfaces, not empty implementations. Use separate
reader and writer files/classes so their stories can run in parallel. Each class owns its private
SQL/enum conversion helpers; no competing edits to a shared mapper file. New message types
between gateway/region/child other than the three named requests/notices may be internal.
Actor-only tests use the exact named requests/notices through a test supervisor, without
requiring the later gateway story; region/host tests additionally use the public ports.

## General rules

G1. Tenant/seat identity comes from trusted routing arguments, never protobuf body identity.
    Every SQL predicate/join includes tenant; all statements parameterized. Never log/trace
    input objects, raw, attribute values, native session IDs, command bodies, tokens, exception
    messages/inner exceptions or SQL parameter values. Constants, GUID ids, kinds and versions
    are safe. Existing profile is immutable per request; use Harness ordinal equality to select
    a supplied profile, with no harness-name special cases. No raw harness JSON parsing.
    Persist strings exactly, with no NFC/Normalize/platform path calls. Unicode scalars including
    U+FFFE/U+FFFF and supplementary characters are preserved. Body text containing NUL becomes terminal opaque evidence, per R3/R6/R9, not unacked
    rejection. Isolated UTF-16 surrogates in fabricated in-memory inputs are rejected before
    serialization (valid received protobuf strings cannot contain them). Binary raw permits
    any bytes. Never pass NUL-bearing text to PostgreSQL text/jsonb.
    Use TimeProvider.GetUtcNow for actor apply/record times, never node observed_at for ordering.
    Commit each owned expected-output test here, not in a later integration story.

G2. Inputs and effects are plain existing types. Supported ApplyAsync inputs are exactly
    NodeAttached, NodeLinkLost, OrchestratorRestarted, QuietTimeoutFired, LaunchWatchdogFired and
    UnknownProlongedFired. Commands/up/down/send/capture requests and automatic timer scheduling
    belong to 13-4; unsupported inputs receive R9's fixed failure without commit. Returned
    RequestCapture effects are not executed or turned into command rows here. AdoptRotatedSession
    is a persistence effect and is consumed atomically by R7, never dispatched to a node. Acks/
    other external effect execution belong to 10-4/13-4; a receipt is not a wire EventAck.
    No new requirement is inferred from an unsupported capability: report it, do not stub it.
    Writer stages are consecutive: R5/R6 first, R7 second, R8 third. Earlier fixtures omit later
    finding/rotation/result behavior. Such input reaching the incomplete writer fails with
    SeatStoreRejectedException, never silently ignores metadata or uses a placeholder. Remove
    that stage guard when the owner lands. Production hosting waits for all writer stages.

## Changes to earlier behavior

C1. Program now hosts `/user/seats` and registers concrete seat reader/writer/gateway ports
    after migration ordering is established. Keep ActorSystem name `aiakos`, shutdown and all
    health tests, node application/stream behavior and 13-1 pure-machine results unchanged.
    No existing test is weakened; the skeleton's old no-actors source comment may be updated.

## Rules

R1. Before other actor tests are added, use Akka.TestKit 1.5.71 with TestKitBase and a small
    ITestKitAssertions adapter over the already selected xUnit v3 Assert APIs. Add only that
    centrally pinned package and one reference. Akka.TestKit.Xunit2 1.5.71's upstream nuspec
    depends on xunit 2.8.1, not xunit.v3; do not install it or create a v2 project. Record that
    inspected dependency and the adapter smoke-test command/result in
    tests/Aiakos.Orchestrator.Tests/Seats/TestKitCompatibility.md. The adapter covers every
    ITestKitAssertions method of the selected release and delegates failures to xUnit v3.
    Smoke test sends literal `ping` to a real echo actor and receives exactly `ping` through a
    TestProbe, then shuts down the system. Test synchronization uses probes, controlled Task
    completion and bounded WaitAsync, not sleep/poll loops. System disposal awaits termination.

R2. Add the public ports/records exactly above without changing existing positional types.
    This is the durable 10-4 seam: its INodeEventCommitter facade performs authenticated
    assignment/replay reads and node-scoped security findings, then calls these actor ports.
    SeatCommitFailedException is a typed retry signal: every constructor ignores supplied
    message/innerException and sets Message exactly SEAT_COMMIT_FAILED, InnerException null.
    Standard overloads preserve exception analyzer conventions without carrying secrets. Actor
    failure uses the parameterless constructor; 10-4 catches the type, never parses Message.
    SeatStoreRejectedException has fixed SEAT_COMMIT_REJECTED/no inner.
    Only this actor shell writes seat-scoped state/evidence/conclusions. It never writes a
    finding with seat_id null. Ports are asynchronous request/receipt contracts; failed or
    canceled requests are never successful receipts. Actor failure/reload uses R10/R13.

R3. Map wire SeatEvent into existing EventReceived without mutating it: NodeInstanceId from
    trusted argument, Seq/SourceSeq checked long conversion, LaunchId null for empty string
    otherwise parsed UUID. Body mappings: Harness -> HarnessBody with Kind, NativeSessionId
    and an independent ordinal attributes dictionary; SessionObserved -> SessionObservedBody;
    ProcessExited -> ProcessExitedBody preserving optional int null versus zero; Gap ->
    ObservationGapBody; no known oneof -> UnknownBody. CommandResult with status Completed and
    Launch/Stop/Capture typed result maps respectively LaunchResultBody, StopResultBody and
    CaptureBody(PaneDead), regardless of command dictionary. Other completed results map
    OtherCommandResultBody. Non-completed result uses stored command kind by parsed CommandId:
    Start -> StartNotCompletedBody(status,error?.Reason or empty), Stop -> StopNotCompletedBody;
    missing/invalid command UUID or other kind -> OtherCommandResultBody. Unknown proto enum
    values pass through to existing honest state-machine fallbacks. Never use NativeName as kind,
    never parse raw; late/stale/duplicate/gap handling remains the existing pure machine.
    Any NUL in a known BODY string/map (recursively including nested results and map keys)
    instead maps ObservationGapBody(GapReason.IngestUnavailable), keeping trusted epoch/seq/
    source_seq/launch attribution. Cursor advances, observation loss becomes honest state and
    an observation-gap finding via pure Apply. This is not INVALID_SEAT_EVENT or retryable.

R4. Reader LoadAsync uses the existing tables, returns null for a missing/wrong-tenant seat,
    includes metadata for human/retired seats so the region can reject them, and does not insert
    anything. Agent without seat_state is corrupt: throw InvalidOperationException with message
    `INVALID_SEAT_SNAPSHOT` and no inner exception. Hydrate every SeatState property from its
    same-named snake_case seat_state column; Desired comes from seat.desired, NativeSessionId
    from current seat_session, Launch from current seat_launch. Parse enums through exact stored
    vocabulary, not culture-sensitive parsing. Launch.Mode comes from mode; ReusedNativeSessionId
    is false for decisions new-session/fresh-explicit and true otherwise; StopRequested equals
    Desired==Down when a current launch exists. CurrentSessionId comes from state. Human without
    state returns SeatState.Initial(time from seat.created_at), version 0, empty Commands.
    Commands contains all command metadata for this tenant/seat, not payloads or tokens.
    Optional fields retain null, counters/timestamps retain exact values (UTC offsets on read).
    Unknown stored enum strings/inconsistent current launch/session joins produce the same
    INVALID_SEAT_SNAPSHOT, with no values echoed. Read in a repeatable-read read-only transaction
    so state/session/launch/commands form one snapshot. No event-log replay or hook parsing.
    FindNativeSessionAsync returns only owner SeatId/SessionId for exact tenant/harness/native
    id, null when absent. No history or cross-tenant values, no Normalize.
    GetCommittedSequencesAsync reads only the unique event keys matching tenant/seat/epoch
    and supplied sequences, returning a set (empty input -> empty without SQL); never loads raw
    or body. This is a duplicate-key lookup, not event-log replay.
    GetStartupSeatsAsync selects only nonretired agent seats with current_launch_id not null OR
    desired='up', across tenants, ordered tenant_id then seat_id. It never selects humans.

R5. Writer CommitAsync processes the supplied already-applied steps in order in ONE Postgres
    transaction. Write evidence/transitions/findings/session/result rows first, state update last.
    Skip steps with Disposition Duplicate entirely. If no nonduplicate event, state
    change, transition, finding or persistence effect remains, return original version/session id
    without writing. Otherwise use final Step.State, update all mutable seat_state columns with
    WHERE tenant_id/seat_id/version=before.Version, increment version once per request, and update
    updated_at to final applied At. Zero matched rows throws InvalidOperationException message
    `SEAT_VERSION_CONFLICT`, no inner exception; nothing commits. seat.desired is not changed by
    shell inputs. No insert/replace of seat_state, no delete of events/transitions, no event log
    replay. All evidence, transitions, findings, current session pointer and applicable result
    metadata either commit together or roll back. A store failure must not expose exception
    detail to port callers; R10 supplies the safe public failure. Existing immutable ids/FKs
    remain intact. All new row ids may use Guid.CreateVersion7; test outcomes never pin them.
    PostgreSQL check/not-null/foreign-key failures (23514/23502/23503) roll back and become
    SeatStoreRejectedException, not the retry signal. Unique-key/version races are retryable
    after rollback/reload; native id races follow R7/R9. No partially committed subset.

R6. For each nonduplicate wire event insert one seat_event with a fresh event_id, trusted ids,
    seq, SourceSeq null when zero, valid nullable launch UUID, observed_at (null if absent),
    received_at=At and TraceParent. Event body_type is harness/command-result/session-observed/
    process-exited/gap or unknown. Harness kind uses SeatVocabulary.ToStored; persist native_name,
    native_session_id, attributes JSON object, usage JSON, raw byte-exact, raw_content_type,
    raw_truncated/raw_size, origin live/resync or null for other enum values.
    A known BODY containing NUL uses UnknownBody opaque storage below: body_type unknown, no
    text/kind/native/attribute/usage fields, original protobuf bytes as raw. Step still records
    R3's gap. TraceParent is persisted only when ActivityContext.TryParse(traceParent, null,
    isRemote:true, out _) succeeds; otherwise null, including NUL/malformed trace. Never reject
    an otherwise valid event for trace metadata or log the malformed value. Non-harness known
    body is Google.Protobuf JsonFormatter JSON; raw=null. Unknown body has body=null and raw equal
    the SeatEvent.ToByteArray bytes (including unknown fields), raw_content_type
    `application/x-protobuf`, raw_truncated=false, raw_size=raw.Length. Disposition is the pure
    step's stored string; never store Duplicate. Any unexpected existing unique event key for a
    nonduplicate step fails the entire transaction, not ON CONFLICT silently skipping state.
    Insert every Step.Transition once in order: cause_type event when Event nonnull, otherwise
    timer for timeout inputs, restart for OrchestratorRestarted, link otherwise; cause_event_id
    links to that inserted row, cause_command_id null, launch_id=Step.State.Launch?.LaunchId,
    at=At, reason=transition.Reason (do not substitute Rule). Axis/reported/from/to exact from
    machine. JSON uses explicit serializer escaping, no manually interpolated JSON strings.
    usage keys are context_used_percent/context_window_tokens/context_used_tokens/cost_usd/model_id;
    absent optional fields and empty model are omitted, finite cost only; unknown unused fields
    are not invented. seat_state.usage updates to latest nonduplicate Harness Usage whose launch
    matches the current launch, even for late telemetry, otherwise keeps prior usage.

R7. Apply finding changes in input order. An open creates an open finding or increments existing
    open occurrences by one, preserving first_seen_at, refreshing last_seen_at=At and launch_id
    from that step; summary exact `Seat finding: {kind}.`, evidence null. Severity error for
    launch-rejected, launch-failed, resume-lost, session-id-mismatch, turn-failed, actor-stopped;
    warning for every other existing SeatVocabulary.Finding constant. A close resolves only an
    existing open row at At, resolved_reason `state-changed`; closing nonexistent is a no-op.
    New recurrence after resolved makes a new row. Include only seat-scoped findings. Clear any
    open actor-stopped finding on the next successful non-no-op commit.
    For AdoptRotatedSession, find the previous current session id by its exact NativeSessionId;
    create new seat_session only if no tenant/harness/native-id row exists, with harness from
    before.Harness, decision harness-cleared, previous_session_id old id, created_at=At. If an
    existing row belongs to this seat, reuse its SessionId; preserve decision/previous/history/
    timestamps and make no new row. Pure rotation remains conservative FreshOnly until evidence.
    If row belongs to a different seat, R9 substitutes a gap before accepting the step and no
    adoption reaches writer. For a race where another seat inserts after R9 lookup: INSERT ON
    CONFLICT DO NOTHING, then owner lookup; foreign owner rolls back with safe
    InvalidOperationException SEAT_SESSION_CONFLICT/no inner. R10 reload/re-forward rechecks
    ownership and commits terminal R9 gap instead of repeatedly inserting the collision. Preserve old
    conversation history; update current_session_id to new id atomically, return it in receipt.
    Current launch's session_id continues to identify its original launch conversation.
    When a step changes resumability to Resumable, set current session conversation_at if null;
    when it changes to Lost set lost_at if null; other session history is unchanged. A late
    rotation is still consumed; duplicate rotation is not. No new launch/session for other inputs.

R8. A nonduplicate CommandResult updates an EXISTING command with matching tenant/seat and UUID
    only (never manufacture one). Set status via SeatVocabulary, completed_at=At, error_reason
    to error.Reason or null, result to JsonFormatter JSON of the full CommandResult. Kind-specific
    outcome: deliver typed DeliveryResult uses DeliveryStateMachine from stored Pending/Sent/
    terminal state and DeliveryResultArrived; noncompleted Deliver uses DeliveryNotCompleted;
    persist resulting lower-kebab outcome and returned finding changes through R7. Stop typed
    result uses SeatVocabulary.ToStored, start typed LaunchResult uses LaunchOutcome stored text;
    other kind has null outcome. Typed DeliveryResult turn_id empty -> null. A typed LaunchResult
    for command.Kind Start and command.LaunchId matching event.LaunchId updates that seat_launch:
    outcome/reason/observed_session_id/optional exit_code, outcome_at=At, evidence=Evidence.Text
    or null. Do not change launch token hash/session_id, retry/dispatch a command or fill omitted
    optional values with zero. ProcessExited for current launch sets ended_at=At, end_reason
    `exited`, optional exit_code/exit_signal from body; a completed StopResult for current launch
    sets ended_at=At, end_reason to stored stop outcome. Stale-launch/Orphan/Evidence result rows
    still retain event evidence but may update result metadata only on the named EXISTING command/
    launch; they never change the current pointer. Missing command/foreign id means no command/
    launch mutation. Duplicate means no mutation. Command creation, sent marking, retry and
    node-replacement delivery handling remain 13-4; this rule only records received outcomes.

R9. SeatActor loads its snapshot before processing requests (asynchronously, mailbox held) and
    reloads on every restart. For valid events request: select a matching injected profile once,
    query committed sequences for the trusted key/epoch once per batch, then apply events in
    supplied order using SeatWireInput and SeatStateMachine.Apply, At from provider
    once for the entire batch. An already persisted event key becomes an unchanged Duplicate
    step without applying or adopting its epoch, even if the current snapshot has another epoch.
    Before accepting a proposed AdoptRotatedSession step, query FindNativeSessionAsync. Same-seat
    existing row is reusable under R7. Foreign owner replaces the proposed step by pure Apply on
    original state with ObservationGapBody(IngestUnavailable), plus one open session-id-mismatch
    FindingChange, Disposition Evidence. Native/current session ids stay unchanged; activity becomes
    Unknown/observation-gap, FreshOnly resumability becomes Unknown/observation-gap. Keep original
    wire evidence, advance cursor and send normal committed receipt. No collision retry loop.
    Empty events allowed: no write, receipt ThroughSeq=max(0,NextSeq-1).
    Nonempty ThroughSeq=max seq in the supplied batch (duplicates successful), never an invented
    higher ack; NodeInstanceId equals trusted epoch. Accumulate all inputs/steps and call writer
    once, even if the batch is all duplicates (writer no-ops). Advance in-memory snapshot only
    after successful write receipt; returned effects reflect committed steps only. ApplyAsync
    similarly calls pure Apply once with existing supported input and commits before returning
    State/Version/Effects; filter consumed AdoptRotatedSession from external Effects. Do not
    execute external effects in actor. Refresh entire snapshot by reader LoadAsync after successful write and before replying;
    use its actual State/Version/CurrentSessionId/Commands, not prospective pointers or stale
    command outcomes. This metadata refresh sends no SeatChildLoaded/reload notification.
    Validate before apply: empty tenant/seat/epoch UUID, null/empty nodeName, null events/entry,
    event SeatId not the trusted seat UUID, seq outside 1..long.MaxValue-1, SourceSeq>long.MaxValue,
    nonempty invalid LaunchId, invalid observed Timestamp or isolated-surrogate text anywhere
    in a known protobuf string/map causes a faulted Task with InvalidOperationException message
    `INVALID_SEAT_EVENT`, no inner exception, commit, receipt or restart. Valid U+FFFE/U+FFFF/
    supplementary scalars pass unchanged. Clone all mutable proto events/maps/byte buffers at the
    gateway boundary in R11, so caller mutation after submitting cannot change what is committed.
    Missing/wrong tenant -> `SEAT_NOT_FOUND`; human -> `HUMAN_SEAT`; retired -> `SEAT_RETIRED`;
    events NodeName != registered Node ordinal -> `SEAT_NODE_MISMATCH`; unavailable profile ->
    `HARNESS_PROFILE_UNAVAILABLE`; null/unsupported generic input -> `SEAT_INPUT_NOT_SUPPORTED`.
    These are safe faulted completions with no commit/restart and no value echo. Missing profile is not stubbed or retried.

R10. Only one request may apply/commit per seat at a time. Hold the actor mailbox while load/
    commit is awaiting; queued requests keep mailbox order. Different seats may commit concurrently.
    A failed writer commit or failed postcommit metadata refresh faults the current request
    with a parameterless SeatCommitFailedException (Message SEAT_COMMIT_FAILED, no inner),
    sends no receipt/effects, and throws a fresh exception of that same type to trigger
    supervision. Validation failures never use this retry type. SeatStoreRejectedException faults
    caller with ordinary InvalidOperationException SEAT_COMMIT_REJECTED/no inner, no receipt and
    no restart. Unique/version races and SQL/IO availability failures remain retryable; do not
    relabel deterministic check/FK violations transient. Do not publish prospective
    snapshot or catch failure and continue from it. After reload, queued requests apply to the
    persisted state. On every successful initial/restart load send SeatChildLoaded to Parent
    with RunContinuationsAsynchronously Ready completion, and await Ready before releasing held
    mailbox work. Region acknowledges after filtering initial load/publishing R13; a test
    supervisor does the same ack. This handshake makes notification-before-queued-work exact.
    The failed request is never internally replayed: 10-4 owns retained batches
    and re-forwarding. A transaction that committed but whose caller saw failure is replay-safe
    via persisted NextSeq/event key. A failed snapshot reload faults waiting requests with
    `SEAT_ACTOR_UNAVAILABLE` and is supervised; no fabricated initial state.


R11. SeatRegion lives at /user/seats and forwards SeatEnvelope to child named SeatId.ToString("D")
    with original sender/request completion preserved. Before child creation reader checks tenant,
    agent Kind, Retired and presence of state; missing/human/retired rejected per R9 with no child.
    Never accept identity from inner message. Seat UUID is globally primary-key unique; a child
    first created for one tenant may never serve another tenant. Existing actor is reused for
    the same key; no passivation/RigActor/remoting/persistence. Before accepting external work,
    eager-create GetStartupSeatsAsync keys. Other eligible agent seats create lazily on first
    supported request. For per-key startup metadata/load failure, create no child (or stop the
    failed not-ready child), best-effort RecordActorStoppedAsync at provider time, Error `Seat actor
    unavailable` with tenant/seat ids, continue other keys and finish startup. Missing-seat FK
    failure logs R12 safe finding-write failure only. A later request retries that seat's load;
    never fabricate state. Child's own initial load is required even if region read metadata
    for eligibility. No event log replay. Once created, forward messages without waiting on that
    child's commit, so other seats are not serialized behind it.
    Gateway clones all mutable proto values/maps/byte buffers before enqueuing named requests,
    and uses RunContinuationsAsynchronously completion sources. Never synchronously throw for
    R9 invalid inputs: return its safe faulted task. Forward correct trusted ids through envelope.
    ct canceled before admission produces a canceled task and no queued work. After admission,
    caller cancellation cancels its wait only, not the commit; commit may still finish and future
    replay dedupes. Unread/closed node-side completion does not roll back a committed input.
    Gateway request wait has a 30 second bound, yielding SEAT_ACTOR_UNAVAILABLE without exposing
    a prospective receipt; timeout doesn't reissue the input. Host/actor stop cancels repository
    work, fails outstanding waiters safely and never hangs shutdown. Tests control task barriers
    and cancellation rather than depending on a process/peer continuing to read.

R12. Region uses OneForOneStrategy(10 retries within 1 minute), any child exception -> Restart;
    exhausted child is stopped, removed from routing, safely fails pending request waits and
    writes actor-stopped finding best effort through RecordActorStoppedAsync. That method uses
    existing R7 finding upsert semantics, severity error, supplied At, launch id null, never
    modifies state/version. Emit Error message `Seat actor stopped` with tenant/seat GUIDs only.
    The next eligible request recreates the child. No automatic replay, invented success or
    unbounded retry. Region best-effort failure is safe Error `Seat actor finding write failed`
    with ids only; do not throw an underlying SQL exception into telemetry.

R13. Publish SeatActorReloaded through the singleton gateway lifecycle subscription only after
    a restarted child's committed snapshot has loaded successfully, before releasing queued work.
    Initial successful child load emits none. Include tenant/seat/version from loaded snapshot;
    no event bodies/profile values. Subscribe returns an idempotently disposable registration;
    future notifications exclude disposed subscribers. Invoke each active observer once per
    successful reload, then acknowledge SeatChildLoaded.Ready so held child work can resume;
    initial load is acknowledged without publishing. One observer throwing does not stop others or restart actor, only emits
    Error `Seat actor observer failed` with ids. A subscription made before commit sees the
    subsequent restart reload. 10-4 uses this to re-forward its cloned uncommitted batch; it
    subscribes before sending and matches tenant/seat. No consumer is registered in this slice.

R14. Program registers Postgres reader/writer, creates SeatRegion in ActorSystem aiakos at
    /user/seats through Akka.Hosting after migrations, and registers SeatActorGateway as the SAME
    singleton for ISeatEventCommitter/ISeatInputCommitter/ISeatActorLifecycle. Supply registered
    IEnumerable<IHarnessStateProfile> as an immutable list (possibly empty, then R9 rejects).
    Use existing TimeProvider singleton. Startup waits region eager loads; a missing/corrupt
    eligible seat gets no child, best-effort RecordActorStoppedAsync and safe Error `Seat actor
    unavailable` with tenant/seat ids, then loading continues and host starts. Missing-seat FK
    failure uses R12 safe finding-failure log only. Later request retries that seat's load, never
    fabricates state. Startup-list query failure (global database unavailable) fails startup
    safely with SEAT_ACTOR_UNAVAILABLE. No single corrupt seat blocks other tenants or host.
    Host termination stops actors and cancels/faults outstanding work before disposing data source.
    Keep EmptyNodeLinkApplication unchanged: 10-4 later registers its real facade. No circular DI
    dependency on node facade or profile implementation. Health remains existing migration health.

## Expected outputs: exact text

Tests use fixed UTC time t=2026-10-06T00:00:00Z, nonempty fixed UUID tenant/seat/epoch/launch/session,
a legal seeded agent snapshot version=7, present/idle/resumable with matching native UUID and
NextSeq=1, and an immutable test profile that regards SessionStarted(source=startup) as readiness,
PromptSubmitted/TurnEnded as conversation evidence and valid matching previous_session_id as rotation.
Fixtures must explicitly fill every existing required SeatState property and FK via plain test SQL.
No secret-shaped fixture strings. H below means human kind, not a hash.

| ID | Change | Expected |
|---|---|---|
| `KIT-v3` | R1 smoke and assertion adapter | TestProbe receives exactly `ping`; v3 assertion failure is reported by v3; system terminates; compatibility record states Xunit2 dependency xunit 2.8.1 and actual passing command |
| `PORT-shape` | reflection of R2 surface | Exact public records/interfaces/signatures above; three ports distinct, no node-scoped writer, no reference/dependency to 10-4 facade; immutable record properties |
| `MAP-bodies` | each body/command subtype and optional exit absent/zero | exact existing SeatInput types/fields from R3; absent int null, present zero 0; unknown oneof UnknownBody; unknown enums retained; attributes copied; no raw parsing |
| `READ-snapshot` | seeded snapshot with overlay/pending/compaction/launch/session/counters/commands; foreign tenant/missing/human/corrupt | every property and metadata exactly R4; foreign/missing null, human version0 Initial, corrupt safe INVALID_SEAT_SNAPSHOT; no INSERT/UPDATE/event read |
| `READ-startup` | nonretired agent A desired up/no launch, B desired down/current launch, C down/no launch; H, retired, second tenant | keys A/B plus eligible second-tenant key ordered tenant/seat; omit C/H/retired; no duplicate or event replay |
| `WRITE-atomic` | two legal EventReceived steps plus transitions, no findings/rotation/results | one transaction; two unique event rows, exact final state, version8, transition links to their own evidence; no partial state/evidence on forced failure after event inserts |
| `WRITE-conflict` | before version7, database version8 | throws SEAT_VERSION_CONFLICT with no inner/value; event/transition counts and state unchanged; all-duplicate request returns old version and no new rows |
| `WRITE-evidence` | harness raw binary/attributes/usage, known non-harness, protobuf unknown fields, duplicate | exact R6 body_type/vocabulary/nullable fields; unknown raw bytes exactly ToByteArray, content type application/x-protobuf; duplicate adds nothing; native Unicode U+FFFE preserved; NUL body opaque raw fields exact, malformed/NUL trace persisted null |
| `WRITE-findings` | open observation-gap twice, close twice, open again; actor-stopped then successful work | first occurrence counts1 then2, only one open row; resolved_reason state-changed; close nonexistent no row; recurrence creates new open1; actor-stopped resolved on successful commit, no node-scoped rows |
| `WRITE-session` | current session old -> valid late rotation new -> replay, then conversation and lost transition | new session decision harness-cleared, previous points old, old row unchanged, state pointer new, launch pointer old; replay no second session; conversation_at/lost_at set once, receipt new pointer; same-seat existing native row reused unchanged; foreign-owner race rolls back SEAT_SESSION_CONFLICT/no mutation; terminal actor handling is ACTOR-reject |
| `WRITE-results` | command result each kind/status; absent/zero exit; foreign/missing ids; process exit and stop | exact R8 metadata and existing delivery machine outcome/finding; foreign/missing no row manufacture; duplicate no mutation; no command send or token change |
| `ACTOR-commit` | blocked writer, two inputs to same seat; release first then second | second not applied while first awaits; no receipt/effect before release; first version8 then second9, pure sequential final state; valid unicode no exception, applied proto attributes copied; gateway clone is REGION-route |
| `ACTOR-reject` | each R9 malformed input/routing/profile/unsupported case; NUL body then valid event | malformed cases faulted Task with exactly corresponding R9 fixed InvalidOperationException message and no inner; zero writes/receipts/restarts; NUL then valid event commits 2 rows, gap/finding once, NextSeq3 and ThroughSeq2 without restart or poison replay; foreign session owner terminal gap/mismatch receipt preserves native pointer; human creation exclusion is REGION-route |
| `ACTOR-fail` | writer throws once, then reader reload barrier; resend only after lifecycle notification | first request faults with SeatCommitFailedException, exactly SEAT_COMMIT_FAILED/no inner, no success; no prospective state; one SeatChildLoaded notice after restart load barrier; caller replay commits once, duplicate replay no additional rows |
| `ACTOR-cancel` | pre-cancel, after-admission cancel, closed/unread response, actor/host stop | pre-cancel no writer; admitted canceled wait may commit once; closed response doesn't undo commit; all waits/shutdown bounded; no internal resend or fabricated ack |
| `REGION-route` | two seats with writer A blocked, B succeeds; duplicate A key; wrong tenant/H/retired/missing; startup eligible list | /user/seats/<uuid-D> once per eligible seat; B independent, same A reuses actor, rejected ids no new child; startup creates only R4 eligible keys |
| `REGION-limit` | 11 child failures inside minute with injected writer, finding write failure separately | at most10 restarts then child stops; actor-stopped open error summary Seat finding: actor-stopped.; safe Seat actor stopped log; next request recreates; best-effort finding failure safe log, no leaked detail |
| `REGION-reload` | observer before failure; throwing observer plus healthy; dispose twice | none at initial load; exactly one matching SeatActorReloaded at successful restart load before queued commit; healthy still called when other throws; disposed never called again; fixed safe observer log |
| `HOST-shell` | real migrated host, no real profile, then test-injected profile and one corrupt startup seat | three DI ports same singleton, /user/seats exists, eager eligible children; no profile gives HARNESS_PROFILE_UNAVAILABLE/no ack; injected profile real transaction succeeds; corrupt key has no child and best-effort actor-stopped, other host/seats continue; shutdown terminates; prior health/node app tests unchanged |

## Tests

Committed xUnit tests use existing Postgres fixture (postgres:18) and legal plain SQL seeds.
Keep TestKit fakes/adapter in tests, never production. Each expected output remains protected
by a test in its owning story. Do not use reflection as the sole shape oracle once types exist.

T1. Commit R1 adapter and echo smoke; record the actual result. This compatibility check precedes
    the actor story's tests; framework-level assert tests verify adapter failures are real.
T2. Commit R2 public shape test compiling against every port/record and all typed failure constructors (fixed text/null inner, only definitions,
    no fake production class). Commit R3 table of every current protobuf BodyCase and CommandStatus,
    unknown enum integers, optional ints, source sequences and copied attributes in mapper story.
T3. Commit R4 database round-trip and startup selection with two tenants and every nullable
    field, existing event keys from both old/current epochs (keys only, no replay), existing FK joins, retired/human exclusion and safe corrupt data.
T4. Commit R5/R6 real database atomicity for evidence/transitions/state only. Force SQL failure
    after event inserts with forged final state (unknown session missing its required reason),
    assert rollback and SeatStoreRejectedException. Race two version7 commits: exactly one
    version8 succeeds. Include duplicate-only/mixed new, opaque unknown/NUL bodies and invalid
    trace metadata. Independent row goldens; no production failure hook/schema change. Later
    writer-stage fixtures excluded until owner lands, per G2.

T5. Commit real actor probe tests with controlled reader/writer tasks, immutable fake profile
    and injected TimeProvider: serial commit, cross-seat concurrency, failure/reload/replay,
    invalid input, missing profile and no precommit effects. Include foreign native-id ownership
    and safe typed-versus-permanent store failure classification.
    Model expected State through existing pure Apply in actor tests but pin independent receipts/
    row counts/order/failure messages; never derive both sides through the actor under test.
T6. Commit region/gateway tests for tenant isolation, routing/startup/human checks, supervision
    threshold, observer disposal/exceptions and shutdown with unread requests. Use deterministic
    barriers/recorded restart notifications; TestKit deadlines cap failure, no sleeps.
T7. Commit real-host integration with singleton-port resolution, eager startup and real Postgres
    commit through ISeatEventCommitter, plus a failure that emits no receipt. Test NUL body then
    valid event: 2 committed rows, through2, no reconnect retry; malformed/NUL trace null; foreign
    native owner terminal gap/mismatch; corrupt startup seat isolation. Test missing profile
    explicitly; no gRPC peer dependency. Retain all earlier tests unchanged.

T8. Commit R7 finding/session tests and expand rollback coverage to finding/session pointer/history.
    New, same-seat existing, late/duplicate rotations; foreign-owner race rolls back without
    mutation, actor re-forward then terminal gap (T5/T7). Exact fixed messages and row goldens.
T9. Commit R8 command/delivery/launch tests; expand rollback coverage to results. Existing writer
    tests stay unchanged. No row manufacture/dispatch; optional absence null, foreign ids safe.

## Definition of done

- `dotnet build -c Release`: 0 warnings, 0 errors.
- `dotnet test --project tests/Aiakos.Orchestrator.Tests -c Release`: all green; Docker required.
- Second failed acceptance gate stops the story per workflow; no third attempt.
- All files LF, UTF-8 without BOM, final newline.
- One commit on story branch, subject `feat(orchestrator): <story title> (#13)`. Body lists
  choices where silent, compatibility evidence where applicable and risks checked; no push/PR.

## Out of scope (do not implement, do not stub)

13-4 command creation/dispatch/automatic timers/actual capture, 13-5 complete restart/resync and
observability/demo, #12 real harness profile, 10-4 authentication/replay/EventAck/node-scoped
findings/retained re-forwarding, node/harness I/O, API/CLI, migrations, event sourcing,
cluster/sharding/RigActor/passivation. The commit/reload seam is production code, not a stub;
missing consumers remain explicit missing consumers.
