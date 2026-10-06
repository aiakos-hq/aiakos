---
id: 10-4
title: "#10 slice 4 — buffered node events"
issue: 10
status: draft
route: impl
paths: [src/Aiakos.Data/Link/, src/Aiakos.Node/Link/, src/Aiakos.Node/OrchestratorConnection.cs, src/Aiakos.Node/NodeProgram.cs, src/Aiakos.Orchestrator/Link/, src/Aiakos.Orchestrator/Program.cs, tests/Aiakos.Node.Tests/Link/, tests/Aiakos.Orchestrator.Tests/Link/]
date: 2026-10-06
---

# Brief: #10 slice 4 — buffered node events

Part of #10. Self-contained. **Do not read docs/specs/ or docs/adr/.** Read the current generated
node contract, `Contracts/Node`, both link implementations and their tests, and repository conventions first. Where silent, choose the simplest behavior and record it in the
commit body; do not add a fallback, another operation, or a guessed state.

## Goal and baseline

Main `06d4930` contains authenticated/traced reconnecting streams (10-3), the v1 envelopes,
`seat_event`/seat model tables, read-only SeatQueries and a pure SeatStateMachine. It has no event
buffer, event persistence writer, SeatActor shell, harness driver, hook ingest, assigned-seat
runtime routing, or command execution. The current link defaults intentionally produce empty
inventory and perform no application work. This slice supplies bounded node events, a commit
interface, pure cursor planning, and replay/ack transport. Production orchestrator integration
is deferred until 13-3 merges its committed actor ports described below.

Maintainer decision qitem-20261006073947-480c6379: no durable transport inbox, no new tables,
no second writer of a seat's state/findings. 13-3 owns the single transaction and actor restart/reload; 10-4 owns NodeProxy re-forwarding.
Node buffer, normalization/bounds, cursor/interface and node source/stream stories can finish
without 13-3; only the final orchestrator integration story waits for that external prerequisite.
This changes no spec 0002 behavior; its durable-commit acceptance is exercised after 13-3, not
claimed by an earlier transport-only story. The analysis PR must be marked **read this one**: per lead decision
qitem-20261006074916-a1895c92 it includes a one-sentence spec 0006 clarification that the node
link writes node-scoped findings (seat_id null, node_name set); ADR 0032 governs a seat's state.
The final story writes only those node findings in the existing table, never a seat finding,
state, transition or event row. The 13-3 author has been told not to duplicate this writer.

10-5 **can be briefed independently** against the published producer/consumer seams here: it owns
command persistence, execution, CommandAck, command-result production and inflight command limits.
Its implementation integration must depend on this slice's node producer/stream stories when
it emits CommandResult. It need not depend on overflow mechanics or the actor persistence implementation.
13-3/13-5 owns the SeatActor transaction and state/effect/resync consumer; #12 owns normalization,
source_seq stamping, SessionObserved attribution and actual harness callbacks; 11-5 owns discovery
of surviving sessions and feeds recovered seat inventory. No inferred implementation is available.

Risks checked: 0002-RK1 buffer bounds/telemetry timing and 0002-RK2 retained authenticated loopback
transport. Live statusLine volume and the M1 demonstration remain open. 0006-RK6 readiness-result
ordering and 0006-RK9 re-forwarding after a real SeatActor restart are exercised by 13-3 and the final orchestrator integration story; full readiness remains 13-5.

## Files and public surface

Create node event types under `Aiakos.Node.Link`; modify existing connection/source/composition
only in the node integration story. Orchestrator link owns the interface, pure cursor helper,
and transport adapter. No schema changes. No new package/project reference,
proto changes, warning suppression, or changes to the pure seat state machine. Do not touch
node options/token validation or manufacture session-host/harness capabilities.

Node producers (#11/#12/10-5) compile against NodeEventBuffer. 13-3 publishes its own actor
ports independently; no dependency on this slice is required. Acceptance compiles against
the named public classes.
The final integration story supplies the INodeEventCommitter facade using the committed
ISeatEventCommitter port from 13-3; do not create a default successful or in-memory provider. Existing INodeLinkApplication remains usable unchanged.

```csharp
namespace Aiakos.Node.Link;
public sealed class NodeEventBufferOptions
{
    public int MaxEventsPerSeat { get; set; } = 2000;
    public int MaxEvents { get; set; } = 10000;
    public long MaxBytes { get; set; } = 67108864;
}
public sealed class NodeEventBuffer : IDisposable
{
    public NodeEventBuffer(NodeEventBufferOptions? options = null, TimeProvider? timeProvider = null);
    public void RegisterSeat(Aiakos.Contracts.Node.V1.SeatInventory inventory, bool recovered = false);
    public void Enqueue(Aiakos.Contracts.Node.V1.SeatEvent value);
    public IReadOnlyList<Aiakos.Contracts.Node.V1.SeatInventory> Inventory();
    public IReadOnlyList<Aiakos.Contracts.Node.V1.ConnectRequest> Snapshot(string seatId);
    public int BufferedCount { get; }
    public long BufferedBytes { get; }
    public void Acknowledge(Aiakos.Contracts.Node.V1.EventAck ack);
    public void ApplyReplay(IReadOnlyList<Aiakos.Contracts.Node.V1.ReplayFrom> replay);
    public IAsyncEnumerable<Aiakos.Contracts.Node.V1.ConnectRequest> ReadAllAsync(CancellationToken ct);
}
public interface INodeEventSource
{
    IAsyncEnumerable<Aiakos.Contracts.Node.V1.ConnectRequest> ReadEventsAsync(CancellationToken ct);
}
// Existing INodeLinkSource remains unchanged; EventNodeLinkSource implements both interfaces.
public sealed class EventNodeLinkSource : INodeLinkSource, INodeEventSource
{
    public EventNodeLinkSource(Microsoft.Extensions.Options.IOptions<Aiakos.Node.NodeOptions> options,
        NodeEventBuffer buffer);
}
// The following declarations are in namespace Aiakos.Orchestrator.Link, not Node.Link.
public interface INodeEventCommitter
{
    Task<IReadOnlyList<Aiakos.Contracts.Node.V1.ReplayFrom>> GetReplayAsync(
        NodeIdentity identity, Aiakos.Contracts.Node.V1.Hello hello, CancellationToken ct);
    Task<Aiakos.Contracts.Node.V1.EventAck?> CommitAsync(NodeIdentity identity,
        string nodeInstanceId, Aiakos.Contracts.Node.V1.ConnectRequest request, CancellationToken ct);
    Task StateChangedAsync(NodeIdentity identity, NodeLinkState state, CancellationToken ct);
}
public sealed record NodeEventCursorResult(bool Duplicate, bool Gap, Guid NodeInstanceId, ulong NextSeq);
public static class NodeEventCursor
{
    public static NodeEventCursorResult Evaluate(Guid? committedEpoch, ulong nextSeq,
        Guid incomingEpoch, ulong seq);
}
public interface INodeEventApplication
{
    Task<Aiakos.Contracts.Node.V1.EventAck?> ReceiveEventAsync(NodeIdentity identity,
        string nodeInstanceId, Aiakos.Contracts.Node.V1.ConnectRequest request, CancellationToken ct);
}
public sealed class EventNodeLinkApplication : INodeLinkApplication, INodeEventApplication
{
    public EventNodeLinkApplication(INodeEventCommitter committer);
}
// Final integration only; uses actor interfaces published by 13-3.
public sealed class SeatNodeEventCommitter : INodeEventCommitter
{
    public SeatNodeEventCommitter(Npgsql.NpgsqlDataSource dataSource,
        Aiakos.Orchestrator.Seats.ISeatInputCommitter inputs,
        Aiakos.Orchestrator.Seats.ISeatEventCommitter events,
        Aiakos.Orchestrator.Seats.ISeatActorLifecycle lifecycle);
}
```

13-3 publishes the following in `Aiakos.Orchestrator.Seats`; these are prerequisites, not
classes to create in any 10-4 story:

```csharp
public interface ISeatEventCommitter
{
    Task<SeatEventsCommitted> CommitAsync(Guid tenantId, Guid seatId, string nodeName,
        Guid nodeInstanceId, IReadOnlyList<Aiakos.Contracts.Node.V1.SeatEvent> events,
        string? traceParent, CancellationToken ct);
}
public sealed record SeatEventsCommitted(Guid SeatId, Guid NodeInstanceId, long ThroughSeq);
public sealed record SeatInputCommitted(Guid SeatId, long Version, SeatState State,
    IReadOnlyList<SeatEffect> Effects);
public interface ISeatInputCommitter
{
    Task<SeatInputCommitted> ApplyAsync(Guid tenantId, Guid seatId, SeatInput input, CancellationToken ct);
}
public sealed record SeatActorReloaded(Guid TenantId, Guid SeatId, long Version);
public interface ISeatActorLifecycle
{
    IDisposable Subscribe(Action<SeatActorReloaded> observer);
}
```

## General rules

G1. Never log/trace tokens, raw bytes, message bodies, Attributes, environment/argv, peer exception
    detail or projected content. Database evidence is not a diagnostic log. No process, tmux,
    driver, command execution, pane input or automatic restart is introduced. Use identity from
    authenticated NodeIdentity only. Cancellation throws OperationCanceledException; after
    cancellation start no write/ack or retry. An already committed transaction may remain.

G2. Input validation happens before mutation. No message-text parsing or culture-sensitive
    ordering. Test TimeProvider controls all added timing. Buffer methods are thread-safe and
    snapshot mutable arguments/results (including protobuf unknown fields and original trace).
    Fixed errors below omit supplied values and have no arbitrary inner exception. Production
    exceptions at the stream boundary become a fixed protocol failure, never silent success.

## Changes to earlier behavior

C1. 10-3 stamps ambient trace on every outgoing write. Buffered SeatEvent envelopes now keep the
    W3C trace captured when first enqueued, including during replay under another ambient activity;
    the connection must not overwrite it. Hello/heartbeat/Goodbye and ordinary responses retain
    10-3 sender behavior and Consumer receive activity/callback lifetime. Existing fake sources
    lacking INodeEventSource still behave exactly as before. Update only affected link tests.

## Rules

R1. RegisterSeat requires a nonempty UUID SeatId; preserve its canonical lowercase D spelling,
    launch/native/lifecycle data, but compute FirstBufferedSeq and LastSeq from the buffer.
    Updating existing inventory never resets its counter. Enqueue requires a registered seat,
    a valid Timestamp ObservedAt, SourceSeq <= long.MaxValue, and either empty or a nonempty UUID
    LaunchId. Replace input Seq with the next per-seat integer starting at 1, no reuse after
    ack/drop/coalescing. Maximum seq is long.MaxValue-1; attempting exhaustion throws
    InvalidOperationException("Event sequence exhausted.") without mutation. Invalid input throws
    ArgumentException("Invalid seat event.") without mutation. Unknown event oneof is accepted
    opaquely. ObservedAt and SourceSeq never reorder arrival. Preserve optional exit/signal fields.
    Record a ConnectRequest clone with original trace from Activity.Current only if W3C; otherwise
    both trace strings empty. Preserve all original event bytes/unknown fields. Input Seq is not
    trusted. Snapshot returns clones in seq order. Inventory returns seat-ID ordinal order with
    FirstBufferedSeq=0 when empty; LastSeq never decreases and starts at 0. Counts are unacked
    retained envelopes; byte count is ConnectRequest.CalculateSize(). No disk spool.

R2. Acknowledge removes retained envelopes through ThroughSeq cumulatively for that seat; stale
    or repeated acknowledgements are no-ops. Unknown seat, zero ThroughSeq or ThroughSeq greater
    than LastSeq throws ArgumentException("Invalid event acknowledgement.") without dropping
    anything. ApplyReplay validates its entire list before mutation: unique registered SeatIds,
    NextSeq in 1..LastSeq+1, otherwise ArgumentException("Invalid event replay checkpoint.").
    For each listed seat delete below NextSeq and mark eligible for this connection; omitted
    seats remain buffered but ineligible. Reapply after every valid Welcome. Never renumber.

R3. ReadAllAsync has one reader per connection; it keeps events retained until ack, and yields
    each eligible retained envelope once per ApplyReplay generation. Choose globally oldest
    enqueue ordinal among eligible seats' lowest unsent seq. Newly enqueued events for a seat
    cannot overtake its retained replay. A later ApplyReplay resets send cursors and wakes the
    reader; cancellation stops only that enumerator, never clears data. A slow/failing writer
    leaves every unacked event eligible for the next connection. Dispose closes/wakes readers
    after caller operations have ended, without touching sessions. Inventory registration with
    recovered=true queues one ObservationGap NodeRestarted (DroppedEvents=0, ObservedAt=current
    provider UTC, LaunchId empty), before subsequently
    enqueued events, once per seat; ordinary first registration queues no restart gap.

R4. Before admission, clone HarnessEvent and truncate Raw above 262144 bytes, setting
    RawTruncated=true and RawSize to the original count; at/below cap preserve supplied fields.
    Normalized fields/Usage are retained. Attribute values above 1024 UTF-8 bytes are rejected
    with ArgumentException("Invalid seat event.") before sequence allocation; other known event
    fields are not silently truncated. Validate envelope size <=4194304 after normalization.
    The proto bytes are the evidence: arbitrary NUL/noncharacters in body strings or Raw are
    accepted; lone UTF-16 surrogates are normalized to U+FFFD through UTF-8 replacement, never a
    formatter/encoder exception. Normalize all body string fields before measuring/snapshotting;
    UUID/timestamp/control fields still follow R1. Do not use JSON to move node events.

R5. Snapshot options at construction. Require MaxEventsPerSeat>=2, MaxEvents>=2, MaxBytes>=1024;
    otherwise ArgumentException("Invalid event buffer options."). After every operation the
    retained counts/serialized bytes satisfy all three limits. When exceeded, first remove all
    but the newest retained Telemetry per seat (never another harness kind). If still exceeded,
    drop globally oldest retained envelope until bounds hold. Count every removed unacked
    envelope, including coalesced telemetry, by its seat; never change its allocated seq.
    Add that count to a per-seat arbitrary-precision nonnegative pending overflow accumulator; it is scalar bookkeeping, not
    a retained envelope and has no seq. If a dropped envelope is a BufferOverflow gap, carry its
    DroppedEvents forward rather than counting the gap itself as a lost harness observation.
    After eviction or ack, materialize pending gaps in seat-ID ordinal order only when the
    envelope fits all limits without further eviction: allocate a fresh seq and enqueue
    ObservationGap(BufferOverflow,DroppedEvents=min(count,ulong.MaxValue)), subtracting that emitted
    count from the accumulator, with current provider UTC and empty
    LaunchId. Leave accumulators pending until room exists. Retained data precedes that later
    gap by seq. A producer may continue; its drops accumulate. No recursive eviction/gap loop.
    Expose no pending-gap payload as an acknowledged event; no pending evidence is discarded.

R6. While a reader is connected, it sends at most one Telemetry event per seat per provider
    second; first is immediately eligible, subsequent is eligible at elapsed >=1s from that
    seat's last Telemetry send. A held Telemetry head blocks later seq only for its own seat;
    other seats may proceed. Rate timing is provider-backed and resets for each new connection.
    It never sleeps the producer or consumes a seq itself. Overflow coalescing is R5, not a
    silent rate-limit drop. While disconnected no send clock is armed. Unknown/Other/SessionEnd
    kinds are forwarded through the same buffer without filtering.

R7. EventNodeLinkSource retains DefaultNodeLinkSource's Hello platform/protocol/name/version and
    limits, supplies buffer.Inventory(), and supplies BufferedCount in Heartbeat. No capability
    is added. WelcomeAsync applies replay before releasing event enumeration; EventAck invokes
    Acknowledge. Other responses retain the existing no-op callbacks until 10-5 provides command
    consumers. Unknown seat inventory is retained, never killed, silently adopted or discarded.

R8. OrchestratorConnection detects optional INodeEventSource after valid Welcome and its awaited
    callback; starts exactly one event enumeration alongside heartbeat/response receive. Every
    event write uses the existing single serialized writer and preserves C1 trace. Cancel and
    await enumeration before reconnect, including blocked producer/read and failed write. Never
    complete the request stream while an event write is active. Host cancellation disposes
    the call to interrupt a stuck write, bounded by existing host shutdown budget, keeps the
    buffer, and never retries. Fixed source/protocol validation failures reconnect using
    FailedPrecondition with no peer/input text in logs; existing backoff/metric/auth behavior
    remains. No event is sent before Welcome completes; heartbeat may run while events wait.

R9. Define INodeEventCommitter with exactly the public signatures above, no storage/default
    implementation. Its facade contract for the final integration story: validate authenticated tenant/NodeName and
    assigned non-retired seat; unknown inventory produces a health finding and no replay entry;
    unassigned/foreign/retired event produces a security finding and null ack. Never adopt/stop a
    seat from inventory. GetReplay returns committed NextSeq for that epoch, or 1 for a new epoch,
    in seat-ID ordinal order. CommitAsync atomically persists original event/unknown bytes,
    deduplicates (tenant,epoch,seat,seq), applies state/findings and records gaps/resync effects;
    only then returns cumulative EventAck. Duplicate is successful without a second transition.
    SourceSeq controls harness ordering if present; ObservedAt never orders. A gap is evidence
    and a resync request, never a fabricated missing event. Provider crash/reload/re-forwarding
    faults the uncommitted task and exposes reload completion for NodeProxy re-forwarding;
    transport cannot acknowledge mere receipt.
    StateChangedAsync forwards overlay input; provider alone writes its derived state/findings.
    The facade is not implemented in the interface story. Its actor persistence port is supplied
    by 13-3. Tests use clearly named fakes.

R10. NodeEventCursor is pure, owns no durable/live storage and does not apply SeatStateMachine.
    Require nonempty incoming epoch, nonempty committed epoch if present, nextSeq in
    1..long.MaxValue, seq in 1..long.MaxValue-1; otherwise
    ArgumentException("Invalid event cursor."). On absent epoch set incoming epoch; Gap=false
    for seq1, true for seq>1; NextSeq=seq+1, Duplicate=false. On same epoch seq<nextSeq returns
    Duplicate=true, Gap=false, unchanged nextSeq. On same epoch seq>=nextSeq returns
    Duplicate=false, Gap=(seq>nextSeq), NextSeq=seq+1. Changed epoch always returns Gap=true,
    Duplicate=false, incoming epoch, NextSeq=seq+1. No sorting, invented observations or I/O.
    This helper plans cursor effects; the actor still checks DB duplicate keys for old epochs.

R11. EventNodeLinkApplication delegates replay/state to the committer with authenticated identity,
    awaited cancellation and cloned mutable proto arguments/results. ReceiveEventAsync accepts
    only SeatEvent with nonempty UUID epoch/seat, seq1..long.MaxValue-1, valid ObservedAt,
    SourceSeq<=long.MaxValue, empty/UUID launch; invalid throws
    ArgumentException("Invalid seat event.") before provider invocation. Accept unknown oneof.
    It does not mutate the event or original Trace, normalize/truncate received evidence, or
    derive state. Validate returned ack names the same seat and ThroughSeq>=received Seq and
    <=long.MaxValue-1, otherwise InvalidOperationException("Invalid committed event acknowledgement.").
    Null is preserved without ack. ReceiveAsync routes non-events to existing no-op behavior;
    direct SeatEvent passed to ReceiveAsync throws
    InvalidOperationException("Seat events require committed acknowledgement.") without invoking
    the provider. The final integration story uses ReceiveEventAsync, never discards its ack.
    Replay entries require unique valid UUID seats and NextSeq1..long.MaxValue, otherwise
    InvalidOperationException("Invalid committed event replay."). No fallback checkpoint.

R12. Final orchestrator integration is externally blocked until 13-3 is merged and supplies its
    ISeatEventCommitter postcommit/failure/reload port. Implement the R9 facade using that port;
    authenticated assignment and replay reads use existing seat/seat_state rows, never a new table. Register SeatNodeEventCommitter as singleton INodeEventCommitter and
    EventNodeLinkApplication as the production
    INodeLinkApplication; preserve explicit test/custom registrations with TryAdd conventions.
    NodeProxyActor/registry detect optional INodeEventApplication for SeatEvent and propagate
    its nullable EventAck after the awaited commit callback to NodeLinkService. Other messages,
    ordinary applications and 10-3 Consumer activity lifetime remain unchanged. NodeLinkService
    emits returned ack through its existing single serialized response writer, with current
    sender trace; null/failed/canceled callbacks emit none. Immediate cumulative ack satisfies
    the 1s/100-event maximum wait. On callback/protocol failure use fixed FailedPrecondition
    without exception/input detail; retain node unacked data for reconnect. Await each inbound
    callback before reading another application envelope: at most one pending event per node,
    backpressure via gRPC; heartbeats/liveness still governed by existing 10-3 budgets. Awaiting
    provider restart recovery never means successful receipt. Supersession/cancellation stops
    old writes and preserves existing link replacement behavior. No transport seat-state or event writer.

R13. In the independent node integration story register singleton NodeEventBuffer and
    EventNodeLinkSource by existing TryAdd conventions; source and hosted connection share
    that singleton, not a per-reconnect buffer. Producer APIs have no fabricated harness.

R14. The final orchestrator story exercises the real 13-3 provider and Postgres transaction:
    1000 events with controlled lost acks and two orchestrator restarts produce exactly 1000
    unique event rows and monotonic per-seat commit order, no gap if all data retained; epoch
    change with unavailable old events records gap/resync and no missing-event rows. Test
    unauthorized seat and unknown inventory, unknown event evidence, duplicate commit, canceled
    pending commit, real actor failure/reload/re-forwarding and original trace replay. No real
    Claude/tmux dependency. If 13-3 lacks this seam, stop/report; do not fill it in here.

R15. Final facade implementation lives in Orchestrator.Link; Data.Link supplies only a read query
    and node-finding writer, with NpgsqlDataSource constructor injection and parameterized SQL.
    Validate the entire Hello before writes: nonempty UUID epoch, unique nonempty UUID inventory
    SeatIds, empty/nonempty UUID LaunchIds, FirstBufferedSeq<=LastSeq<=long.MaxValue-1 (First=0
    allowed), and valid strings serialized as protobuf evidence; otherwise
    ArgumentException("Invalid seat inventory."). Convert UUIDs to canonical D spelling for
    keys; compare parsed GUIDs, not alternate spellings. Malformed input never partially attaches
    seats or records findings. Normalize lone UTF-16 surrogates to U+FFFD before evidence encoding.
    Assignment reads join existing seat/seat_state by authenticated TenantId and exact ordinal
    NodeName, seat.retired_at IS NULL; missing state means initial epoch null/NextSeq1. Read every
    assigned seat, including those omitted from inventory, in UUID-string ordinal order. For each
    assigned inventory seat await actor ApplyAsync(NodeAttached(epoch, mapped inventory)), then
    re-read committed position and return NextSeq for that epoch or 1 when epoch differs.
    Map empty LaunchId to null; preserve Lifecycle/LastSeq in SeatInventoryEntry. Connected needs
    no extra synthetic input; Unknown/Disconnected await NodeLinkLost for all assigned seats.
    Unknown/unassigned/foreign/retired Hello inventory yields a node finding, no replay entry.
    Per unknown entry insert one open warning kind `unknown-node-seat`, summary
    `Node reported an unassigned seat.`; evidence JSON stores original inventory protobuf bytes
    as base64 under `inventory_proto`. Per rejected event insert one open error kind
    `unassigned-seat-event`, summary `Node sent an event for an unassigned seat.`; evidence JSON
    contains only canonical `seat_id`. These rows have authenticated tenant/node_name, seat_id
    and launch_id null, fresh UUID finding_id, occurrences1, database UTC timestamps. Separate
    notifications create separate rows; no invented uniqueness/migration. On canceled/failed
    finding write no ack/replay success; no raw content in summary/logs. Authorized CommitAsync
    passes a one-event cloned batch plus original Trace.Traceparent (empty becomes null) to
    ISeatEventCommitter and maps the returned trusted SeatId/epoch/ThroughSeq to EventAck; validate
    returned identity/epoch and through bounds before success. Unknown body remains opaque.
    No SQL event/state/finding-for-seat write, and no guessed actor for an unknown ID.

R16. Before the first event commit subscribe to ISeatActorLifecycle and retain one immutable
    request until success/null/cancellation; dispose subscription when callback completes or session ends. Snapshot
    matching tenant/seat reload notification generation before each attempt, so a notification
    racing with the fault is retained. Only InvalidOperationException with exact message
    `SEAT_COMMIT_FAILED` and no inner exception triggers re-forwarding: await a matching later
    SeatActorReloaded, then send the same event/seq/trace again while session is current. Other
    exceptions terminate via R12 fixed failure without ack/retry. No busy loop/timer polling,
    unrelated/initial-load notices do not release retry, canceled/superseded sessions never retry.
    Actor notification is post-reload, not proof of commit; eventual receipt alone releases ack.
    Ambiguous postcommit failure re-forwarding is safe through actor DB dedupe. The facade uses
    no unbounded pending batch collection; the existing one-event per-node backpressure holds.

## Expected outputs: exact text

Base fixture: two registered UUID seats A=11111111-1111-1111-1111-111111111111 and
B=22222222-2222-2222-2222-222222222222; no harness exists. Provider UTC begins
2026-10-06T00:00:00Z. A new buffer starts with no inventory until registration.

| ID | Change | Expected |
|---|---|---|
| `E1` | A events arrive SourceSeq 9 then 1, optional exit absent then zero; input/snapshot mutated | seq 1,2 in arrival order; original observed/source/unknown data and optional presence retained; snapshot mutation cannot change retained evidence; exact inventory range and counts; invalid UUID/timestamp/source/sequence exhaustion has fixed R1 error and no mutation |
| `E2` | ack 1 twice; replay next 2; invalid cumulative ack and duplicate/out-of-range replay | only seq 2 remains; LastSeq unchanged; invalid checkpoint/ack fixed R2 ArgumentException with no partial deletion; omitted seats retained/ineligible |
| `E3` | retained A1,A2,B1; Welcome then A3 arrives mid-replay; cancellation/reconnect; recovered registration | A1 before A2 before A3; B preserves its order; replay repeats unacked data; no pre-Welcome send; cancellation preserves data; recovered A has NodeRestarted seq1 before live seq2, no guessed process/input |
| `E4` | raw at cap/cap+1; 1024/1025-byte attribute; NUL,U+FFFE,lone surrogate body values; unknown oneof | cap retained unchanged, cap+1 raw bytes clipped with true flag/original RawSize; normalized fields retained; 1025-byte attribute rejected before allocation; NUL/U+FFFE preserved in protobuf bytes, surrogate becomes U+FFFD without exception; oversized envelope fixed Invalid seat event.; unknown bytes retained |
| `E5` | small limits, telemetry coalescing, oldest drops, many affected seats | retained count/bytes stay within configured bounds; allocated numbers never reused; lost count per seat eventually emitted as BufferOverflow when capacity returns; coalesced telemetry counted; no recursive gap storm; non-telemetry kinds not coalesced; invalid options fixed R5 error |
| `E6` | Telemetry A1,A2 plus B event; advance 999ms then 1ms; disconnect | A1 immediately, A2 only at 1s; B may send while A is held; no producer delay or hidden rate-drop; provider-only timing; reader cancellation leaves retained data |
| `E7` | source creates Hello/heartbeat, handles Welcome/EventAck; unknown inventory omitted from replay | real inventory/ranges and BufferedCount; replay releases only listed seats; ack drops committed range; unknown inventory retained; no invented capabilities or command/session effects |
| `E8` | held Welcome; blocked/failing event writer, heartbeat, host stop and reconnect under another ambient trace | no event before Welcome callback completes; single writer, no overlapping sends; unacked replay preserves original trace; reader/write canceled and awaited before replacement; shutdown finishes within existing host budget with no retry or data clear; earlier 10-3 tests stay green |
| `E9` | compile a fake committer and inspect interface | exact R9 signatures; no transport storage/provider registration; fake can delay/null/fail commit, and no ack is implied by receipt |
| `E10` | absent/same/changed epoch, duplicate, skip, invalid cursor | exact R10 Duplicate/Gap/epoch/NextSeq tuples; no mutation/I/O; fixed Invalid event cursor. for invalid bounds/UUID |
| `E11` | fake provider delayed/null/invalid ack, mutable args/results, invalid event/replay | awaiting completion; null remains null; clones preserve bytes/trace; exact R11 fixed errors including direct ReceiveAsync rejection before invalid event provider call; no successful ack on failure/cancellation |
| `E12` | blocked commit, supersession, repeated event, ordinary application, failed callback | no ack before commit; one serialized ack after commit; null/failure/cancel produces no ack; no reads ahead of pending callback; old application and Consumer lifetime preserved; fixed FailedPrecondition without peer detail |
| `E13` | production node composition | one buffer/source singleton shared by connection and producer; reconnect retains events; explicit source test registration preserved |
| `E14` | 1000 events, two orchestrator restarts/lost acks; epoch change; real actor failure | node singleton survives reconnect; exactly 1000 unique committed rows, per-seat seq order and no false gap; original trace preserved; actor restart re-forwards pending data before ack; unavailable old-epoch data yields gap/resync, no invented rows; unauthorized event has finding/no ack and unknown inventory finding/no stop |

| `E15` | assigned/missing-state/unknown/foreign/retired inventory and event; overlay input | only authenticated active bindings replay; actor NodeAttached committed before checkpoint read; unknown inventory/open warning and rejected event/open error have exact R15 summary and seat_id null; no event/seat-state write; failed finding write never becomes successful ack |
| `E16` | controlled commit failure; reload before/after fault; unrelated notice; cancellation; postcommit ambiguity | one retained identical event re-forwarded only after matching later reload; no ack on fault/reload; one ack after receipt; duplicate row/transition suppressed; unrelated notice does not retry; cancellation disposes subscription and prevents retry; generic exception fixed failure |

## Tests

Use xUnit v3 and current test cancellation conventions. Unit tests run on all OS; real protocol
fixtures use isolated loopback Kestrel and scripted callbacks, no tmux/harness/owner node.

- T0. In each story commit permanent no-secret/cancellation/odd-input regression tests for G1/G2;
    existing architecture/protocol tests remain unchanged unless C1 explicitly changes the result.
- T1. Commit buffer sequence/inventory/snapshot/ack/replay tests E1–E3. Drive sequence exhaustion
    through an internal counter-seeding test constructor/helper, accessible via the existing test
    friend assembly; no production toggle. Reader assertions use barriers, not arbitrary sleeps.
- T2. Commit E4–E6 normalization/budget/rate tests; use small supplied options and injected provider,
    not 10000-event sleeps. Test pending gaps with more affected seats than capacity and release
    capacity via cumulative acks; no lost gap bookkeeping or recursive materialization loop.
- T3. Commit source/stream E7/E8 and E13 node composition tests with a scripted peer, blocked Welcome and cancellable writer,
    full loopback trace replay and controlled dropped acks. Test old sources lacking the optional
    interface so the prior connection contract remains usable by 10-5 and existing consumers.

- T4. Commit E9–E11 interface/cursor/adapter tests with delayed/cancelable fake committer and
    immutable snapshots. No SQL, actor or storage substitute is needed by this story.
- T5. Commit E12 and E14–E16 real-provider integration tests using existing isolated
    Testcontainers/Postgres fixtures, loopback/scripted node and deterministic provider fault
    seam supplied by 13-3; no production fault toggle. Lost ack/restart tests use barriers.

## Definition of done

Acceptance passes and prior tests stay green; Release build has zero warnings/errors. Node,
Orchestrator and Data tests appropriate to the story pass (DB tests use existing Testcontainers).
LF/UTF-8/no BOM/final newline. One local commit `feat(link): <story title> (#10)`, body records
silent choices and risks checked/remaining. No push/PR by implementer; use story.sh gate/retry.

## Out of scope

Actual hook/SSE ingest, harness normalization or mechanics, turn-to-command correlation before
10-5 supplies it, command execution/dedupe/dispatch, tmux discovery, tokens, automatic restart,
SeatActor state/transitions/command outcomes, real resync/capture execution, spool/retention/GC,
CLI, AppHost, remote TLS, proto/CI changes, real Claude/M1 demo. No empty stubs for these.
