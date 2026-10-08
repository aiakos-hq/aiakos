---
id: 10-5
title: "#10 slice 5 — commands"
issue: 10
status: approved
route: impl
paths: [src/Aiakos.Node/, src/Aiakos.Orchestrator/Link/, tests/Aiakos.Node.Tests/, tests/Aiakos.Orchestrator.Tests/]
date: 2026-10-08
---

# Brief: #10 slice 5 — commands

Part of #10. Self-contained. Do not read specs or ADRs while implementing. Read the named
existing code and the dependency stories. Where silent, use the simplest behavior and record
it in the commit body. This does not authorize additional features or production substitutes.

## Goal

Commands travel through the authenticated link, receive immediate acceptance acknowledgements,
execute through a harness-neutral node port, and produce sequenced CommandResult events.
Duplicate execution and unsafe resend across a node restart are prevented. SeatActor owns
command persistence and meaning; this slice owns transport and execution coordination.

## Prerequisites and ownership

The repository currently contains NodeEventBuffer, INodeLinkSource, OrchestratorConnection,
NodeLinkService, NodeLinkRegistry, NodeProxyActor and CommandValidator. Read those sources.
10-4 publishes EventNodeLinkSource/INodeEventSource and the optional postcommit event application;
its source/stream story must be merged before the node composition story. This is a published
seam, not permission to implement missing 10-4 code here.

13-4 publishes ISeatCommandPort and ISeatNodeConnectionSource in Aiakos.Orchestrator.Seats:
SendAsync(SeatKey key, string nodeName, Guid nodeInstanceId, Command command, CancellationToken ct)
returns Task<SeatCommandDispatch>; Get(SeatKey key, string nodeName) returns SeatNodeConnection.
SeatKey carries TenantId and SeatId; the records are SeatCommandDispatch(bool Sent) and
SeatNodeConnection(bool Connected, Guid? NodeInstanceId). That protocol story must be merged
before the final adapter story. It is not needed by the transport stories. 13-4 persists commands
before invoking this port and owns sent/final/unknown updates. Never write seat_command or
seat state directly from link code. A wire CommandAck is acceptance only.

This brief publishes INodeCommandDriver for future 12-4/11-3/11-4 integration. The driver owns
resolved launch preparation (worktree, executable substitution, two allowed placeholders,
projected files, mode-0600 secret file and AIAKOS_SEAT_TOKEN_FILE), readiness/resume verification,
delivery/confirmation, capture and graceful stop mechanics. This slice forwards exact resolved
messages; it does not implement these mechanics. Scripted drivers prove this slice; production
cannot advertise a driver until that driver exists. No successful fallback driver is allowed.

## Files to create or touch

| Path | Purpose |
|---|---|
| src/Aiakos.Node/Commands/ | Protocol, executor, dedupe and driver registry |
| src/Aiakos.Node/Link/ | Compose command callbacks with the existing event source |
| src/Aiakos.Node/OrchestratorConnection.cs | One serialized request writer and outgoing command acks |
| src/Aiakos.Node/NodeProgram.cs | Explicit opt-in command service registration |
| src/Aiakos.Orchestrator/Link/ | Command sender, connection registry integration, actor-port adapter |
| tests/Aiakos.Node.Tests/ | Permanent command and source tests |
| tests/Aiakos.Orchestrator.Tests/ | Permanent sender, reconnect and adapter tests |

No proto/schema/package changes, Version on PackageReference, warning suppression, or modifications
to seat state machines, actor stores, session host implementations or harness adapters.

## Public surface

Types below are owned only by the rules that name them. Existing constructors/interfaces remain
usable. Proto arguments and results are independently cloned at ownership boundaries.

```csharp
// Aiakos.Node.Commands (R1)
public interface INodeCommandDriver
{
    string Harness { get; }
    IReadOnlyCollection<string> Capabilities { get; }
    Task<Aiakos.Contracts.Node.V1.LaunchResult> StartAsync(
        string seatId, Aiakos.Contracts.Node.V1.StartSeat start, CancellationToken ct);
    Task<Aiakos.Contracts.Node.V1.DeliveryResult> DeliverAsync(
        string seatId, Aiakos.Contracts.Node.V1.DeliverInput input, CancellationToken ct);
    Task SendKeysAsync(string seatId, Aiakos.Contracts.Node.V1.SendKeys keys, CancellationToken ct);
    Task<Aiakos.Contracts.Node.V1.PaneCapture> CaptureAsync(
        string seatId, Aiakos.Contracts.Node.V1.CapturePane capture, CancellationToken ct);
    Task<Aiakos.Contracts.Node.V1.StopResult> StopAsync(
        string seatId, Aiakos.Contracts.Node.V1.StopSeat stop, CancellationToken ct);
}
public sealed record NodeCommandSeat(string SeatId, string LaunchId, string Harness, bool Ready);
// R2-R4 implement this class incrementally. R2 admission supports a scripted internal
// execution callback; R3 adds scheduling; R4 dispatches INodeCommandDriver.
public sealed class NodeCommandExecutor : IAsyncDisposable
{
    public NodeCommandExecutor(Aiakos.Node.Link.NodeEventBuffer buffer,
        IReadOnlyList<INodeCommandDriver> drivers, TimeProvider? timeProvider = null);
    public void RegisterSeat(NodeCommandSeat seat);
    public Aiakos.Contracts.Node.V1.CommandAck Receive(Aiakos.Contracts.Node.V1.Command command);
    public void Acknowledge(Aiakos.Contracts.Node.V1.EventAck ack);
    public int InflightCount { get; }
    public IAsyncEnumerable<Aiakos.Contracts.Node.V1.ConnectRequest> ReadResponsesAsync(CancellationToken ct);
}
// Aiakos.Node.Link (R5). Optional; legacy sources remain supported.
public interface INodeCommandSource
{
    IAsyncEnumerable<Aiakos.Contracts.Node.V1.ConnectRequest> ReadCommandResponsesAsync(CancellationToken ct);
}
// Aiakos.Orchestrator.Link (R6-R7)
public sealed record NodeCommandTarget(Guid TenantId, string NodeName, Guid NodeInstanceId);
public sealed class NodeCommandSender
{
    public NodeCommandSender(NodeLinkRegistry registry, TimeProvider? timeProvider = null);
    public Task<bool> SendAsync(NodeCommandTarget target,
        Aiakos.Contracts.Node.V1.Command command, CancellationToken ct);
}
// R8, Aiakos.Orchestrator.Link
public sealed class LinkSeatCommandPort : Aiakos.Orchestrator.Seats.ISeatCommandPort,
    Aiakos.Orchestrator.Seats.ISeatNodeConnectionSource
{
    public LinkSeatCommandPort(NodeCommandSender sender, NodeLinkRegistry registry);
}
```

Internal queue/registry messages, injected writer hooks and constructors may be added for tests;
no production fault flags. The driver returns observations, never actor state. Its StartAsync
honors ReadyTimeout with UNKNOWN/READY_TIMEOUT plus evidence; DeliverAsync honors ConfirmTimeout
with SubmittedUnconfirmed. Cancellation must stop underlying work. Drivers forward raw harness
observations through the same NodeEventBuffer independently of command results. Correlation of
confirmed turn IDs to the command activity belongs to the driver; this executor keeps the original
command activity alive through driver completion so that it can be captured without payload tags.

## General rules

G1. Never log/trace/stringify commands, env, argv, projected files, secret bytes, bodies, captures,
    driver exception messages or inner exceptions. Generated error metadata is empty. Use only
    command/seat/launch IDs, kind, reason, status, counts and timing as telemetry. All times use
    the injected TimeProvider; barriers and fake time are used in tests, not sleeps.
G2. Defensive public APIs reject null with ArgumentNullException; malformed local registrations
    with ArgumentException("Invalid command seat."). Local cancellation returns a canceled task.
    Disconnection never cancels an accepted node execution; host disposal does. All background
    faults are observed and late completion cannot emit a second result. Existing tests remain
    unchanged except the explicitly named C items. Retain LF/UTF-8/no BOM/final newline.

## Changes to earlier behavior

C1. Node source Command callback changes from no-op to admission plus an outgoing CommandAck
    only when command services are installed. EventAck also notifies executor retention. Hello
    keeps platform/protocol fields and gets installed driver capabilities; Heartbeat gets the
    executor InflightCount. Existing sources without INodeCommandSource keep their behavior.
C3. Preserve registry ownership, replacement and removal keyed by authenticated NodeId. Add a
    secondary lookup by authenticated tenant and ordinal node name over Welcome-complete sessions.
    Exactly one match returns that session; zero or multiple matches return no target. Two distinct
    NodeIds sharing tenant/name remain connected independently and neither supersedes the other;
    sender returns false and adapter Get returns (false,null) while ambiguous. Removal restores
    unique lookup. Same NodeId replacement retains existing supersession semantics.

C2. Orchestrator response stream can send commands after Welcome, alongside existing EventAck
    and Goodbye, using the same writer gate. Existing ReceiveAsync/application and event commit
    paths retain their behavior; CommandAck is additionally routed to the sender's tracker.

## Rules

R1. Publish driver/seat protocol. Harness and capabilities are ordinal strings, drivers immutable
    registrations. Duplicate/empty harness registrations fail ArgumentException("Invalid command
    drivers."). An empty driver collection is legal and advertises no capabilities. RegisterSeat
    requires nonempty UUID seat/launch and installed harness; canonicalizes IDs lowercase D and
    clones inventory into NodeEventBuffer. For a new RegisterSeat inventory set SeatId and
    LaunchId to canonical IDs, Lifecycle=Running when Ready else Unknown, NativeSessionId="",
    FirstBufferedSeq=0 and LastSeq=0. There is no inventory harness field; keep harness only in
    executor state. For existing inventory clone its snapshot, replace LaunchId and Lifecycle
    as above, preserve NativeSessionId and sequence fields; buffer remains sequence authority.
    RegisterSeat represents an occupied recovered seat even when Ready=false. Existing recovered
    seat registration belongs to the adoption producer. Active means occupied or pending start;
    a successful terminal stop clears occupancy. For an admitted supported start on a new or
    stopped seat register requested LaunchId, Lifecycle=Launching before execution, preserving
    existing native/sequence fields (new fields empty/zero). Failed/Unknown/TimedOut retains that
    requested LaunchId and sets Lifecycle=Unknown; Ready sets Running and NativeSessionId to
    the driver's ObservedSessionId. Terminal successful stop sets Exited, retains launch/native.
    A rejected start never replaces an occupied launch. Driver outcomes never establish actor state.

R2. Receive synchronously admits and creates CommandAck before scheduling driver work.
    It returns without awaiting execution; ReadResponsesAsync supplies receipt acks and exact
    retained result replays through a process-lived single-reader response queue. Valid UUID
    command/seat IDs are canonicalized; malformed IDs throw ArgumentException("Invalid command
    envelope.") locally. Stream translation belongs to R5.
    Deduplicate by command UUID across the node process, before validation/preconditions; even
    altered payload or seat with an existing ID never executes and returns Duplicate=true for
    the original ID. New entry returns Duplicate=false. Each Receive also queues its ack before
    scheduling work; the source must not enqueue a second copy of that ack. Snapshot original command/trace and
    store no secret-bearing command after execution finishes. For a known unfinished entry do
    nothing else; for finished entry replay its original retained SeatEvent with identical seq,
    body/trace if still buffered. Do not allocate a second event. If its event was already acked
    (or evicted) there is nothing to replay; an acked duplicate does not create new evidence.
    Keep finished IDs/results until at least 10 minutes after their cumulative result EventAck.
    ApplyReplay counts as ack through NextSeq-1; source notifies executor of this too. Unacked
    entries never expire; expiry at >=10 minutes is lazy on admission/ack. Retain safe terminal
    result metadata after overflow without retaining discarded command secrets. InflightCount
    counts accepted unfinished entries, not duplicates/retained finished entries.
    Each terminal result enters NodeEventBuffer once with canonical seat, inventory LaunchId,
    provider ObservedAt, SourceSeq=0 and original receive activity. Seq/trace belong to buffer.
    Ensure inventory exists before any result, including validation, stop and timeout results.
    If no R1 start registration applies and seat is unknown, create minimal SeatInventory:
    SeatId=canonical command seat, LaunchId="", Lifecycle=Unknown, NativeSessionId="",
    FirstBufferedSeq=0, LastSeq=0. Never replace existing inventory with this minimal record.
    A rejected supported start on an unknown seat uses this minimal record, not ready inventory.
    Noncompleted errors use ErrorReasons.Create with message "Command rejected." unless
    another rule explicitly fixes the message; never copy exception details.

R3. Per seat StartSeat/DeliverInput/SendKeys execute FIFO; seats run independently. CapturePane
    and StopSeat bypass FIFO. A pending launch cannot block capture. Stop atomically marks seat
    stopping before any driver call, cancels current StartSeat and emits one Completed Launch
    Unknown reason STOPPED; queued DeliverInput/SendKeys complete Rejected/SEAT_STOPPING.
    An active delivery is canceled and completes Failed/SEAT_STOPPING with NotDelivered; never
    claim no input was sent merely because of cancellation. Queued starts also reject
    SEAT_STOPPING. Same-pending-launch waiters are not queued starts: R4 owns their result.
    While stopping, newly received otherwise-valid StartSeat/DeliverInput/SendKeys return
    Rejected/SEAT_STOPPING without driver calls. A second otherwise-valid StopSeat under a new
    command ID joins the pending stop and copies its terminal status/Error/Stop body into its
    own result without a second driver call. Each waiter retains its own receipt deadline;
    timeout wins only its own terminal gate. Capture still bypasses FIFO while stopping.
    Stopping ends when the original stop completes. A racing late driver result is ignored.
    FIFO fixtures use SendKeys behind held SendKeys on a registered seat, and StartSeat behind
    held SendKeys on a registered stopped seat. These remain admissible under R4/R9; do not
    use a different-launch start behind a pending start or two unfinished deliveries as FIFO
    fixtures. Every S2/S3 StartSeat fixture uses a seat registered with RegisterSeat and then
    stopped by a successful StopSeat before the start; S4 adds start registration. S2/S3
    do not assert the launch id or lifecycle of a start; E4 owns those assertions.
    Every new command uses Timeout measured from receipt, including queue time; absent, invalid
    or nonpositive Duration gives Rejected/INVALID_LAUNCH with fixed message "Invalid command
    timeout."; driver never invoked. At timeout emit TimedOut, Error{Code=DeadlineExceeded,
    Reason="COMMAND_TIMEOUT", Message="Command timed out.", Retryable=false}; for StartSeat
    also Launch Unknown/COMMAND_TIMEOUT, for DeliverInput Delivery NotDelivered. Readiness and
    confirmation sub-deadlines are driver-owned and can finish before the outer timeout.
    Host disposal cancels and awaits workers with a five-second provider-based bound; a driver
    ignoring cancellation is detached with fault observation, no late event or secret logging.

R4. Launch/stop dispatch uses this order for every new command: R3 timeout validity check,
    first CommandValidator.Validate(command, installed capabilities) error (safe fixed message),
    then kind-specific seat preconditions, all evaluated atomically at receipt before FIFO
    admission. Do not defer or repeat these checks at the FIFO head. Stopping checks in R3
    precede other seat preconditions. Dedupe R2 precedes all three. Invalid timeout wins
    even when validator also fails. Shared dispatch forwards exact clones with the correct seat.
    Success is Completed with matching typed driver result; driver throws, null result or
    invalid/unspecified outcome gives Failed/SESSION_HOST_ERROR, "Command execution failed.".
    Missing driver gives Rejected/UNSUPPORTED. R2 owns shared noncompleted Error construction.
    Start selects Harness. Different LaunchId on occupied (including Failed/Unknown) or pending
    seat returns Rejected/SEAT_ALREADY_RUNNING, no Launch body, no driver call. Same pending
    launch under another command ID waits for original and clones its complete terminal status,
    Error and Launch body into its own result; never launches twice. When StopSeat cancels the
    original, its same-launch waiters copy Completed with no Error and Launch Unknown/STOPPED
    rather than queued-start Rejected/SEAT_STOPPING, unless their own deadline already won.
    Same occupied Ready launch
    returns Completed with Launch{Outcome=Ready, Reason="", ObservedSessionId="", ExitCode absent,
    Evidence absent}, no Error and no driver call. Same occupied non-Ready launch with retained
    terminal start metadata copies that status/Error/Launch exactly (including TimedOut); retain
    this launch metadata until successful stop even after command-ID retention expires. Same
    occupied non-Ready launch without terminal metadata (recovered registration after restart)
    returns Rejected/SEAT_NOT_READY with Launch{Outcome=Unknown, Reason="SEAT_NOT_READY",
    ObservedSessionId="", ExitCode absent, Evidence absent}; no driver call. This is the answer
    to R7 StartSeat resend after changed instance; do not invent a second launch.
    New LaunchId on stopped seat may start; its result event and inventory use requested LaunchId
    even if driver returns Failed/Unknown or outer deadline produces TimedOut. Ready=true only
    after driver Ready. Failed/Unknown/TimedOut keep occupied/not ready and launch evidence;
    never fresh-fallback. Stop requires registered seat or Rejected/SEAT_NOT_FOUND. Successful
    Stopped/Killed/NotRunning clears occupancy/readiness, retains inventory/home; unsuccessful
    stop preserves occupancy. R2 owns result envelopes and R1 owns inventory values.

R9. Input/capture dispatch uses R4 validation order and shared safe driver mapping. Non-Start
    missing registered seat returns Rejected/SEAT_NOT_FOUND. DeliverInput requires Ready or
    Rejected/SEAT_NOT_READY; already admitted unfinished delivery gives Rejected/SEAT_BUSY
    rather than queued second delivery. Lead containing CR/LF returns Rejected/INPUT_NOT_ALLOWED.
    SendKeys uses existing exact allowlist/capability. Forward exact clones with correct seat.
    Capture text over 1048576 UTF-8 bytes is cut at a scalar boundary and Truncated=true;
    preserve supplied flags/timestamp/size. Completed uses driver's Delivery/Capture body;
    SendKeys success has no typed result. R4 shared failure mappings and R2 envelopes apply.

R5. Translate executor malformed-envelope ArgumentException("Invalid command envelope.")
    from ReceiveAsync to gRPC FailedPrecondition with that exact fixed text, not generic Unavailable.
    Add a composed CommandNodeLinkSource implementing existing INodeLinkSource/INodeEventSource
    and new INodeCommandSource, wrapping EventNodeLinkSource and executor. Receive Command
    immediately calls executor.Receive; ReadCommandResponsesAsync delegates executor.ReadResponsesAsync; it never awaits execution. EventAck
    and Welcome replay notify executor and delegate to event source. Forward unrelated responses
    unchanged. Response queue is process-lived, single-reader, contains cloned ack/trace or retained result
    replay envelopes. Original results still leave through buffer event enumeration; duplicate
    replay envelopes leave through this response reader without a new buffer sequence. Replays
    obey Welcome seat eligibility and are dropped if already cumulatively acked before write;
    a disconnected send keeps the current unsent ack for reconnect. A sent/lost ack is recovered
    when the orchestrator resends the command. Heartbeat, ack and event sends share one writer;
    no message before Welcome callback finishes. Receive callbacks use stream cancellation;
    command workers use executor lifetime. Reconnect does not dispose executor or event buffer.
    Acks carry receipt trace, events original completion trace. Post-Hello new seat inventory
    uses 10-4's reconnect mechanism, including command-created unknown inventory. No ack reader
    or event reader from an old stream survives into a replacement. Provide an explicit
    AddNodeCommands(IServiceCollection) extension; register only when real drivers were supplied,
    preserve test-source override registration. No default driver/capability or startup of seats.

R6. NodeCommandSender uses only authenticated NodeLinkRegistry sessions. Use the C3 tenant/name lookup (do not trust client-provided IDs), and check expected NodeInstanceId. New
    sender SendAsync clones command/trace, checks seat UUID, command UUID, positive timeout,
    available session and installed advertised capabilities using CommandValidator, and returns
    false without write for invalid target/command/unsupported/full/disconnected. No automatic
    capability invention. The maximum unfinished entries is Hello.Limits.MaxInflightCommands;
    zero uses 64. Reserve slot atomically per authenticated node; duplicate active ID with same
    target reuses its acceptance task, never consumes another slot. An ID with a different seat
    or different serialized command returns false. Write only after Welcome; success returns
    true only on matching CommandAck for that authenticated session. There is one deadline: command Timeout from sender admission for both ack wait and
    tracker lifetime. Before it, sent-but-unacked work stays tracked for result/reconnect. At
    expiry resolve uncompleted acceptance false, remove tracker and release its slot atomically;
    an already true acceptance stays true. No second execution/ack deadline exists. Unknown/stale/foreign ack is ignored. Duplicate flag does
    not alter acceptance meaning. A correlated CommandResult releases a slot only after event
    application returned its successful postcommit ack; failed/null/pending event application
    never releases it. After outer command timeout slot is released; do not fabricate an event
    or SQL outcome. Registry removal/supersession closes the old writer but preserves unfinished
    trackers and pending acceptance waits until a matching ack on a later authenticated session
    or the original deadline; removal alone does not return false. R7 adds resend and its
    explicit changed-instance input removal; S7 need not resend to prove a pending wait.

R7. Preserve unfinished cloned commands in sender across reconnect within the orchestrator
    process. At new authenticated Welcome, resend StartSeat/CapturePane/StopSeat with same ID
    and attempt incremented, unchanged body/trace, including across changed node instance. Start
    keeps LaunchId. DeliverInput/SendKeys resend only if current instance equals original send
    instance. Otherwise remove tracker, resolve acceptance false, never write those commands;
    13-4's link-loss transaction owns recording unknown. Original deadline never resets.
    Reconnect/supersession and concurrent SendAsync cannot write a command twice on the same
    session. Registry current session becomes available only after Welcome written. Persisted
    actor command creation precedes initial SendAsync; link holds no token-bearing durable
    payload. Orchestrator restart does not recover this in-memory tracker or invent a StartSeat;
    13-4's restart policy owns durable recovery/unknown and explicit subsequent dispatch.
    A completed command never resends; receipt trace on replay derives from original command
    trace, not ambient reconnect trace. An older callback may not remove a replacement session.

R8. Implement LinkSeatCommandPort against published 13-4 protocol. Get uses key TenantId and
    nodeName to select the authenticated current connected registry session; absent returns
    SeatNodeConnection(false,null). SendAsync verifies key seat equals command SeatId, target
    tenant/name/instance matches current session, then delegates sender and returns
    SeatCommandDispatch(bool). Cancellation propagates; all other transport failures become
    Sent=false without leaking details. Register one singleton for both ports, same sender and
    registry as live NodeLinkService. This story requires 13-4 protocol and 10-4 postcommit event
    application. No actor/store port is redefined. No direct actor call for resend or synthetic
    result; existing event path is sole result consumer. Add an isolated loopback proof with
    real sender/source/executor/buffer, fake driver and delayed postcommit application. Hold
    commit to prove results cannot release inflight capacity early.

R10. A separate real actor test
    uses 13-3/13-4 production stores and isolated Postgres, explicit real immutable test profile:
    persisted command before wire send, result replay creates one command update/event only,
    readiness event before Ready result produces no sources-disagree finding. Gate must wait
    for those external stories; no fake production actor/store is allowed.

## Expected outputs: exact text

| ID | Input | Expected |
|---|---|---|
| `E1` | protocol/driver collection/registration | exact R1 signatures; duplicate driver => Invalid command drivers.; malformed seat => Invalid command seat.; exact new/existing RegisterSeat R1 inventory fields; preserved native/sequence fields on existing registration; no drivers advertises nothing |
| `E2` | pending/completed/acked/expired duplicate, altered payload | first Duplicate=false; all known Duplicate=true; one execution/one seq; buffered replay same bytes/seq/trace; no new acked event; retention expires only at >=10min after cumulative ack/replay; correct InflightCount; exact minimal R2 inventory and result envelope fields |
| `E3` | held start, capture, stop, FIFO, fake deadline | capture runs while start held; FIFO SendKeys behind SendKeys and start behind SendKeys on registered stopped seat; independent seats; launch Unknown/STOPPED; queued delivery/keys Rejected/SEAT_STOPPING; new start/delivery/keys while stopping Rejected/SEAT_STOPPING; second stop copies first terminal status/Error/Stop with one driver call; timeout TimedOut/COMMAND_TIMEOUT; no late second result; Invalid command timeout. without driver |
| `E4` | start/stop; invalid timeout plus validator error; same Ready/pending/finished/recovered launch; same pending launch waiter when original stopped; different Failed/Unknown launch; failed start on stopped seat | exact R4 status/Error/Launch fields; stopped original and same-launch waiter Completed Launch Unknown/STOPPED without Error; invalid timeout wins; no double launch; occupied different ID SEAT_ALREADY_RUNNING; recovered same ID SEAT_NOT_READY/Unknown; requested launch on failed/timed-out stopped-seat start result and inventory; exception text absent |
| `E5` | production opt-in with fake supplied driver, blocked Welcome/writer/reconnect | ack admission does not await driver work; no pre-Welcome/overlapping writes; heartbeat actual unfinished count; executor survives reconnect; stale stream readers canceled/awaited; event/ack original trace; legacy source unchanged; malformed command IDs => FailedPrecondition/Invalid command envelope. |
| `E6` | authenticated/missing/foreign/full target, delayed ack/commit | only current target writes; true only after matching CommandAck; false for unsupported/full/invalid; one reserved duplicate slot; successful postcommit final result alone releases capacity early; stale/unknown ack does nothing; two authenticated NodeIds with same tenant/name neither supersede nor route; removing one restores routing; removal/supersession before ack keeps acceptance pending until later matching ack or original deadline; one deadline removes tracker/releases slot and returns false for unacked |
| `E7` | same/changed instance reconnect and supersession | same instance resends all unfinished kinds once; changed resends Start/Capture/Stop only; IDs/launch/body/trace unchanged, Attempt incremented; old deadline preserved; no completed resend/old-session removal; no cold-start replay |
| `E8` | adapter and loopback | matching trusted tenant/name/instance/seat only; ambiguous lookup disconnected; same singleton ports; no early capacity release |
| `E9` | input/keys/capture and driver errors; delivery received while start pending | exact R9 mappings; pending-start delivery immediately Rejected/SEAT_NOT_READY and never queued; readiness/busy/lead/key restrictions; scalar-safe capture limit; exception text absent |
| `E10` | real actor replay | command persisted before send; one durable result/update on replay; readiness-before-Ready no sources-disagree |

## Tests

Use xUnit v3 and repository cancellation conventions. Fixtures contain unmistakably synthetic
sentinels, never token-shaped credentials. No real owner harness/tmux/node/seat or sleep polling.

T0. Every story commits permanent tests for G1/G2: cancellation/null/bad inputs as appropriate,
    original-sensitive-string absence from logs/errors/traces, mutation isolation and existing
    regression suites. No test changes solely to hide an earlier failing contract.
T1. Commit E1 protocol/registration tests against scripted drivers; no real harness.
T2. Commit E2 retention tests with controlled result completion and fake TimeProvider; exercise
    result overflow, cumulative EventAck/Welcome replay, altered duplicate payload, and secrets
    released from completed entries. Use existing buffer snapshots as oracle for original seq.
T3. Commit E3 barrier-driven scheduling/deadline/stop tests, including cancellation-ignoring
    driver cleanup and result race; fake provider five-second disposal bound.
T4. Commit E4 launch/stop tests for every same/different occupied launch case, recovered non-Ready
    resend, exact Ready body, timeout-before-validator, stopped-seat failed/unknown/timed-out new
    launch inventory and event IDs, and safe driver faults.
T5. Commit E5 source/composition/loopback tests; run earlier 10-3/10-4 stream tests unchanged.
T6. Commit E6 isolated loopback registry/sender tests with token registry authentication,
    backpressure limit=1, blocked commit, ack ordering, duplicate concurrent sends and timeout.
T7. Commit E7 controlled reconnect tests for every kind, changed instance, lost ack, supersession,
    command deadline, ambient trace changes and late old callbacks. No network sleep delays.
T8. Commit E8 adapter/complete loopback proof with scripted driver and delayed application.
T9. Commit E9 input/capture mapping, readiness/busy/lead/key and scalar-boundary capture tests.
T10. Commit E10 isolated Testcontainers real actor proof;
    no SQL substitute in the actor proof. Synthetic normalized readiness observations only,
    no real Claude demo. If production prerequisite is absent, report it; do not implement it.

## Definition of done

Story acceptance and earlier tests pass; Release build zero warnings/errors. Run relevant Node
and Orchestrator tests; actor proof requires Docker/Postgres. One local commit
`feat(link): <story title> (#10)` with silent choices and risk evidence in body; no push/PR.
Risks: checks secret-safe command path (0002-RK2) but does not close remote TLS; E10 checks normal
readiness/result ordering (0006-RK6). Event volume (0002-RK1) and actor restart re-forwarding
(0006-RK9, 10-4) remain with their owning integration paths.

## Out of scope

CLI commands, launch-material resolution, token generation/storage/rotation, node configuration,
file/worktree/session-host/harness mechanics, hook normalization and turn-correlation implementation,
SeatActor state/persistence/restart/resync policies, TLS/remote nodes, spool, proto changes and M1
real harness demo. This analysis does not claim #10 completed or any external prerequisite merged.
