---
id: 11-4
title: "#11 slice 4 — stop, process tree and liveness"
issue: 11
status: draft
route: impl
paths: [src/Aiakos.Node/Sessions/, tests/Aiakos.Node.Testing/, tests/Aiakos.Node.Tests/Sessions/]
date: 2026-10-08
---

# Brief: #11 slice 4 — stop, process tree and liveness

Part of #11. Self-contained. **Do not read docs/specs/ or docs/adr/.** Read SessionHost.cs,
TmuxClient.cs, ProcessSnapshot.cs, SessionRegistry.cs and the session tests. Where this brief
is silent, choose the simplest behavior and record it in the commit body. Do not add fallback
input, restart, adoption, listing classification or another session-host implementation.

## Goal

Provide real-tmux status observations, once-per-launch exit events, recovery of a deleted
private socket, and signal-based stop of the harness and its tracked descendants. These are
components for the complete ISessionHost facade in 11-5; no placeholder facade is created here.
Dead panes remain available as evidence until an explicit start or stop removes them.

### Verified baseline and prerequisites

The analysis branch starts at 28902f7 (main observed 2026-10-08). 11-1 and 11-2 stories
1–4 and 9 are merged: existing contracts, validation, TmuxClient, ProcessRunner, registry
(including recovery records), IProcessSnapshot and ProcessSnapshot are available. 11-3 input
and capture have a component TmuxSessionInput; this slice does not depend on 11-3 or alter it.
11-2-5 (#215, direct launch commands) is open; TmuxLaunchCommands and TmuxSessionStarter are
absent on this baseline. 11-2-6 (start lifecycle), 7 (attach), 8 (diagnostics) are described
in the approved 11-2 brief but absent. Their contracts may be assumed only after their stories
merge. S1–S6 below can proceed on the baseline with scripted handles/registry. S7 and S8 MUST wait
for 11-2-5 and 11-2-6 to merge; do not implement their missing components or create stubs.
11-2-7/8 are not prerequisites; preserve their contracts if they merge before S7.

11-2's starter accepts an Action<SessionHostEvent> callback; its existing dead-replacement
path persists RegistryExit.Reported, calls the callback, then marks Reported=true. That contract
is retained. The normal running entry has complete handle fields; an exceptional failed starting
entry has none and must never be converted to a SessionHandle. 11-5 later registers adopted
handles and wires the components into DI/Hello. This slice's caller explicitly registers a
returned handle and runs the watcher. No node startup/DI changes are authorized.

This slice checks 0004-RK2 (socket recovery), 0004-RK4 (optional signal) and the automated
part of 0004-RK8 (setsid descendants, PID reuse). The /proc check-to-signal race remains until
pidfd in M7; detached children missed between snapshots remain an acknowledged limit. No
sandbox claim or global orphan search is added. Manual down/pgrep evidence is outside this slice.

## Files to create or touch (nothing else)

| Path | What |
|---|---|
| `src/Aiakos.Node/Sessions/*.cs` | ProcessTree, LinuxProcessSignals, TmuxPaneObserver, SessionLifecycle, PaneWatcher, TmuxSessionStopper; later integrate starter locking |
| `tests/Aiakos.Node.Testing/*.cs` | Scripted snapshots/signals and permanent reusable test helpers |
| `tests/Aiakos.Node.Tests/Sessions/*.cs` | Permanent unit tests and isolated opt-in real tmux tests |

No new package or project reference, Version=, reflection in production, NoWarn, pragma warning
disable or SuppressMessage. Existing ISessionHost/records, fake and input signatures stay unchanged.

## Public surface (exact names)

All production types below are in Aiakos.Node.Sessions. Constructors listed are available to
acceptance tests. Existing records are reused; private helpers are implementation choices.
The later 11-5 facade delegates its corresponding operations to these components.

```csharp
public static class ProcessTree
{
    public static IReadOnlyList<ProcessInfo> Expand(IReadOnlyList<ProcessInfo> snapshot,
        SessionHandle root, IReadOnlyList<ProcessInfo> tracked);
}
public enum SignalResult { Sent, Gone, Reused, Failed }
public interface IProcessSignals
{
    Task<SignalResult> SendAsync(int pid, ulong startTime, int signal, CancellationToken ct);
}
public sealed class LinuxProcessSignals : IProcessSignals
{
    public LinuxProcessSignals(IProcessSnapshot processes);
    public Task<SignalResult> SendAsync(int pid, ulong startTime, int signal, CancellationToken ct);
}
public sealed record PaneObservation(SessionHandle Session, PaneStatus Status);
public sealed record PanePoll(IReadOnlyList<PaneObservation> Observations, bool NoServer,
    SessionHostError? Error);
public sealed class TmuxPaneObserver
{
    public TmuxPaneObserver(TmuxClient client, IProcessSnapshot processes);
    public Task<PaneStatus> GetStatusAsync(SessionHandle session, CancellationToken ct);
    public Task<PanePoll> PollAsync(IReadOnlyList<SessionHandle> sessions, CancellationToken ct);
    public Task<ProcessInfo?> GetServerAsync(CancellationToken ct);
}
public sealed class SessionLifecycle : IAsyncDisposable
{
    public SessionLifecycle(ISessionRegistry registry, TimeProvider? timeProvider = null);
    public void Register(SessionHandle session);
    public IReadOnlyList<SessionHandle> Sessions { get; }
    public void Unregister(SessionHandle session);
    public Task<IAsyncDisposable> EnterAsync(string sessionName, CancellationToken ct);
    public Task ObserveAsync(SessionHandle session, PaneStatus status, CancellationToken ct);
    public void Publish(SessionHostEvent observation);
    public IAsyncEnumerable<SessionHostEvent> WatchAsync(CancellationToken ct);
    public ValueTask DisposeAsync();
}
public sealed class PaneWatcher
{
    public PaneWatcher(TmuxPaneObserver observer, SessionLifecycle lifecycle,
        IProcessSignals signals, Microsoft.Extensions.Logging.ILogger<PaneWatcher> logger,
        TimeSpan? pollInterval = null, TimeProvider? timeProvider = null);
    public Task TickAsync(CancellationToken ct);
    public Task RunAsync(CancellationToken ct);
}
public sealed class TmuxSessionStopper
{
    public TmuxSessionStopper(TmuxClient client, TmuxPaneObserver observer,
        IProcessSnapshot processes, IProcessSignals signals, ISessionRegistry registry,
        SessionLifecycle lifecycle, TimeProvider? timeProvider = null);
    public Task<StopReport> StopAsync(SessionHandle session, StopRequest request, CancellationToken ct);
}
```

S7 adds an optional trailing `SessionLifecycle? lifecycle = null` to the existing
TmuxSessionStarter constructor. Old callers compile with unchanged behavior. It is only a
shared start/stop lock and registration seam, not a replacement for the existing publish callback.

## General rules

G1. Never send text or keys, stop on a watcher finding, infer harness state from text, kill-server,
    use a shell/pgrep or signal an unverified process. Use existing TmuxClient argv/environment
    and error classification. Diagnostics contain only seat/launch/tmux IDs, PID, signal,
    operation and error class: no argv, environment values, attributes, stderr or pane text.
    No server/session mutation during disposal or watcher cancellation.

G2. All waits use injected TimeProvider (System by default) and cancellation; no real-time sleeps
    in unit tests. PollInterval defaults to 1 second and must be positive and at most
    4294967294 milliseconds, otherwise ArgumentOutOfRangeException before IO. Stop durations
    have the same upper bound and accept zero. Non-Linux signalling throws Unavailable,
    "Process signals require Linux.", false before a syscall. Preserve earlier tests/results;
    only the additive starter wiring in C1 changes an earlier component.

## Changes to earlier behavior

C1. When the optional lifecycle is supplied, TmuxSessionStarter.StartAsync holds
    lifecycle.EnterAsync(derived session name,ct) from before registry read/list until it returns
    or throws, and Register(handle) only after the running entry is committed and before return.
    Keep the starter's existing serialization as well; acquire lifecycle first. Its dead-replacement
    event callback remains supplied by caller (use lifecycle.Publish in integrated tests).
    Without lifecycle its existing behavior is unchanged. TmuxSessionStopper uses the same
    lifecycle lock; it does not wait for any input gate. A start in progress completes before stop
    observes its result. Concurrent stops for one name serialize; different names run independently.

C2. Explicit proposed narrowing of spec 0004 R30, requiring maintainer approval of this analysis:
    socket recovery signals only a server identity learned from an earlier reachable private socket;
    do not discover a server by argv/socket-name heuristics after restart. With no remembered server
    and a matching live root, remain degraded without signals or vanish, per R8. Automatic recovery
    of a socket deleted before the watcher first runs is deferred; never claim R30 fully implemented.

## Rules

R1. ProcessTree.Expand is pure. Keep tracked identities (PID,StartTime) even after reparenting.
    Add the root only when its PID/start time match. Add every current process with SessionId
    equal to root.PanePid only while that matching root is present, and recursively add descendants
    of any currently matching tracked identity. Continue to a fixed point, including descendants
    which create a new session. Replaced PIDs never become ancestry roots and never overwrite a
    saved identity; omit a new identity with a PID already tracked. Return distinct identities
    sorted ascending by PID. Only same-UID snapshots from IProcessSnapshot are consumed; no
    argv predicate, process-group search or scan of another user's processes.

R2. LinuxProcessSignals accepts pid>0, startTime>0 and signal 1–64; otherwise throw
    InvalidArgument,"Invalid process signal request.",false before IO. Immediately before each
    signal read a fresh same-user snapshot: absent PID -> Gone; changed start time -> Reused;
    neither case calls kill. Call kill(pid,signal) through LibraryImport("libc"), SetLastError=true;
    success -> Sent, ESRCH -> Gone, any other errno -> Failed, without native error text.
    Snapshot failures propagate the existing fixed error. Production uses the Linux binding;
    unit tests may inject an internal syscall delegate to prove errno handling without signalling
    a real process. Never signal a negative PID/group.
    pidfd is out of scope; this check reduces but cannot eliminate the race after the snapshot.

R3. Observer GetStatus uses exactly display-message,-p,-t,PaneId followed by the single format
    "#{pane_pid}\t#{@aiakos-launch}\t#{pane_dead}\t#{pane_dead_status}\t#{pane_dead_signal}".
    Fields are separated by actual TAB, not backslash+t. Trim a single final LF/CRLF only.
    Matching PID/launch, dead=0 and matching process start time -> Alive,null,null,null.
    Matching dead=1 -> Exited with nullable status/signal; do not require an exited PID in /proc.
    Empty status/signal -> null; integer status >=0 and signal 1–64 accepted; signal 0 -> null.
    No server, NotFound or identity mismatch -> Missing,null,null,null, never signal or remove.
    Timeout -> Unknown,null,null,"tmux invocation timed out.". Other tmux/parser/snapshot errors
    -> Unknown with safe fixed reason "Pane status could not be read.". Unavailable client throws
    its existing Unavailable error. Invalid handle fields -> NotFound,"Session was not found.",false.
    Invalid fields mean empty SeatId/LaunchId, SessionId not $<digits>, PaneId not %<digits>,
    PID<=0, start time=0, or session name not <valid rig>_<valid seat> (one underscore;
    reverse into seat@rig and use TmuxNames.TryParseAddress). ReadOnly handles are accepted
    for status and stop. Cancellation propagates.

R4. Poll uses one list-panes,-a,-F invocation with format
    "#{session_name}\t#{session_id}\t#{pane_id}\t#{pane_pid}\t#{pane_dead}\t#{pane_dead_status}\t#{pane_dead_signal}\t#{@aiakos-launch}".
    Parse all rows before acting; malformed/truncated output -> empty Observations and
    TmuxFailed,"Pane listing could not be read.",true. An ordinary invocation failure yields
    empty Observations and TmuxClient.GetError(result), never Missing for each handle. Recognized
    no-server yields NoServer=true, Error=null and Missing for each supplied handle. Successful
    listing yields one observation per supplied handle, in supplied order; match session name/ID,
    pane ID/PID and launch, and verify live start time via one snapshot. Unmatched -> Missing.
    Unrelated rows are not registered or mutated. Parse status/signal as R3; errors produce no
    partial events. Empty supplied handles still permit a listing. No listing classification here.

R5. SessionLifecycle retains registered handles keyed by session name with ordinal equality.
    Register is idempotent for identical handles; a new launch replaces the previous handle.
    Sessions returns a name-sorted snapshot. Unregister removes only an identical current handle.
    EnterAsync supplies per-name exclusion and a lease whose async disposal releases it once.
    ObserveAsync acquires that same lease, then ignores stale/unregistered handles; Alive/Unknown
    do nothing. Exited: require a matching complete registry handle (or no entry for a read-only
    handle), preserve all other fields, write Exit(code,signal,now,false), Publish(PaneExited),
    then write Exit.Reported=true. Existing matching Reported=true emits nothing. Retry of a
    matching Reported=false exit uses its recorded time/status, not a new timestamp.
    A writable handle with no registry entry -> NotFound,"Session was not found.",false
    without persistence or event.
    A mismatching registry -> NotFound,"Session was not found.",false without overwrite/event.
    Missing -> SessionVanished once per registered launch in this process, without deleting
    registry or inventing exit status. After a terminal event, suppress subsequent terminal events
    for that launch; explicit stop unregisters it. Persisted Reported=true also suppresses a later
    vanish. Read-only/no-registry dedupe is in memory. Starting entries without handle fields are
    never registered or published. Reconciliation and restart vanish dedupe belong to 11-5.

R6. Publish synchronously accepts an event into an in-memory FIFO, deduplicating accepted events
    by SeatId/LaunchId across starter and watcher publication. First event wins; no payload replay
    for a second subscriber. WatchAsync has one active reader; second reader throws
    InvalidOperationException("A session event reader is already active."). Buffered observations
    survive reader cancellation and are drained in acceptance order; cancellation releases reader
    ownership, it does not dispose the lifecycle. Disposed Publish throws
    InvalidOperationException("Session lifecycle is disposed.") before acceptance. Dispose completes
    the event stream and never touches registry/panes. A publication throw leaves Reported=false
    and permits retry. As in 11-2 there is no transactional outbox: enqueue and Reported persistence
    can be interrupted by a crash; do not promise exactly-once external delivery across that window.

R7. Watcher TickAsync serializes calls with a cancellation-aware gate; a concurrent caller
    waits, then runs a complete tick (it does not skip polling); RunAsync ticks immediately, then waits
    PollInterval after each completed tick. On successful poll call ObserveAsync for each observation.
    Poll/observation failure logs Warning "Session host watcher degraded: {ErrorCode}" and marks
    an internal degraded flag. An observation failure does not stop the remaining observations:
    attempt each in supplied order, and classify the whole tick as degraded if any failed.
    A failed poll has no observations to process. Continue next tick, no fabricated events. On first successful full
    tick after degradation log Information "Session host watcher recovered.". Cancellation exits
    promptly, not degradation. Watcher is an explicitly run component, not NodeProgram registration.
    Events use TimeProvider UTC. Kept dead panes and repeated ticks publish only once through R5/R6.

R8. On a successful reachable poll with registered live handles, remember the private server's
    identity by display-message,-p,"#{pid}" through this same TmuxClient and matching that positive
    PID in a same-user snapshot (GetServerAsync returns ProcessInfo or null). Run GetServerAsync
    exactly once on every successful reachable poll containing at least one Alive observation,
    before observing any terminal rows; otherwise do not call it. No discovery by
    argv pattern. This binds the remembered PID/start time to the private socket that answered.
    On no-server with registered handles, fresh snapshots must still contain that server identity
    and at least one registered live root identity before attempting signals.SendAsync(server PID,
    recorded start time,10). Attempt SIGUSR1 at most once per remembered server identity per
    unreachable episode. Log Information "Recovering tmux socket for server {Pid}." only on Sent.
    Defer all Missing events while the verified server still exists; mark degraded and poll next
    tick. Do not kill the server, delete a socket, start a server or mutate pane environment.
    After a successful poll reset the episode, preserve handles and publish no vanish for recovered
    panes. If no remembered server, take a fresh snapshot: while any registered root identity
    still matches, send no signal and defer all Missing events as degraded; if none matches,
    Missing observations may publish vanish. If the remembered server is gone/reused, send no
    signal and permit Missing observations even when a root remains. C2 names the R30 narrowing. An unreachable server still present after the attempt remains
    degraded/unknown (not vanished); status independently remains Missing per R3. Identification
    failures emit no events and retry next tick. Record no server identity on disk in this slice.

R9. Stop validates handle as R3, session name using existing TmuxNames address rules, and Grace/
    ChildGrace as G2; invalid duration/signal/callback-null -> InvalidArgument,"Invalid stop request.",false
    before pane mutation. Default Graceful is Signal(15), ChildGrace is 2 seconds. Acquire the
    lifecycle name lease and recheck status/identity before any action. Exited/Missing -> NotRunning,
    recorded exit status from matching registry if absent in observation, ChildrenSignalled=0,
    Leftovers=[], Error=null. Exited still follows R12 dead cleanup/publication; Missing unregisters
    and deletes only a matching entry, without kill-session. Unknown throws TmuxTimeout or
    TmuxFailed with R3 safe reason before any change. A stale handle/registry mismatch throws
    NotFound and must not unregister/delete a different launch. Missing with no entry is idempotent.

R10. For Alive, snapshot/Expand before graceful action; require the matching root or throw
    NotFound before signals. Default/explicit signal is sent only to root via IProcessSignals;
    Custom invokes the callback once with handle/ct and never sends automatic graceful input.
    Wait for Exited/Missing by polling status every min(100 ms, remaining Grace), starting with an
    immediate check; on every grace poll take a snapshot and Expand before querying status,
    retaining discovered identities even if the root exits on that poll. Grace zero skips delay.
    Unknown is not proof of exit. Rescan/Expand before
    each signal round, retaining every prior identity. If harness still Alive/Unknown at deadline,
    SIGKILL every surviving tracked identity -> Killed. If Exited/Missing during grace, SIGTERM
    every surviving tracked non-root identity, wait ChildGrace with snapshots every <=100 ms,
    then SIGKILL survivors -> Stopped. Round order is ascending PID. Count distinct non-root
    identities whose TERM or KILL returned Sent, once regardless of rounds, as ChildrenSignalled.
    Reused/Gone identities are not counted or retried. Do not select the node or tmux server
    unless actually descendants/session members (tests explicitly ensure unrelated processes survive).

R11. After final kill, check snapshots every <=100 ms for up to 1 second; a tracked identity is
    leftover only when both PID/start time match. Return sorted unique leftover PIDs, error
    LeftoverProcesses,"Processes survived stop.",false, Metadata["pids"]=comma-separated PIDs.
    A reused PID is not a leftover. A Failed signal does not itself prove a leftover; final
    verification decides. After graceful callback or any Sent signal, cancellation, callback
    exception, snapshot failure or tmux failure returns StopReport with progress instead of
    throwing: fixed TmuxFailed,"Stop did not complete.",false (timeout retains TmuxTimeout and
    "tmux invocation timed out.",false). Never expose callback/native exception messages.
    Until R10 determines Stopped/Killed, partial reports use Killed conservatively with non-null
    Error (it is not a success claim). Before that boundary errors throw SessionHostException;
    caller cancellation propagates.
    On partial failure retain session and registry, do not run cleanup, report current tracked
    survivors (last safe snapshot, no guessed death). Callback invocation crosses the boundary
    even if it throws. No automatic retry or completion after cancellation is required.

R12. Final cleanup occurs only if there are no leftovers, no earlier error and a fresh observation
    is Exited or Missing; Unknown/Alive returns Error=TmuxFailed,"Stop did not complete.",false
    and keeps evidence. Exited: persist/publish exit through the R5 rules while already holding
    the lease (use an internal non-locking helper, never recursively acquire it). Reverify exact
    session/ID, pane ID/PID, launch and dead=1 immediately before kill-session,-t,"="+SessionName;
    never require a dead PID still present. Only after successful removal delete matching registry
    and unregister. Missing: use matching RegistryExit status if any, no fabricated status/event,
    delete matching registry and unregister. Preserve recovery sidecars; no recovery deletion by
    stop. For a live stop Outcome stays R10 Stopped/Killed, optional exit fields reflect actual
    tmux/registry evidence; no synthetic 0 or 9. Errors after a change are returned with the
    established outcome and no evidence deletion. Tmux failures before mutation throw normally.

R13. Add permanent isolated real-tmux verification of the merged components using T7. This story
    changes tests only, not production behavior. A failure is reported with the exact scenario and
    evidence to lead; do not repair production rules or create missing prerequisites in this story.

## Expected outputs: exact text

In fixtures use handle ("seat-id","launch-a","demo_worker","$1","%2",100,10,false).
Baseline snapshot is root (100,parent=1,session=100,start=10), child (101,100,100,11),
setsid grandchild (102,101,102,12), unrelated (200,1,200,20). Registry matches that handle.
Exact error strings and logs below are fixed; tests do not match arbitrary native stderr.

| ID | Change | Expected |
|---|---|---|
| `E1` | Expand baseline; root gone and 102 reparented; root PID reused | identities [100/10,101/11,102/12]; tracked 102/12 retained after reparent; reused 100 is never an ancestry root; 200 never selected |
| `E2` | Send to matching, absent, reused, errno ESRCH/EPERM identities | Sent, Gone, Reused, Gone, Failed respectively; no syscall for absent/reused; invalid -> InvalidArgument,"Invalid process signal request.",false |
| `E3` | Alive; dead status 7/empty signal; dead empty/9; empty/0; timeout; no server | Alive/null/null; Exited/7/null; Exited/null/9; Exited/null/null; Unknown reason "tmux invocation timed out."; Missing/null/null |
| `E4` | Poll two handles and unrelated row; malformed row; failed invocation | observations match supplied handles only; malformed -> TmuxFailed,"Pane listing could not be read.",true and zero observations; failure has zero observations/events |
| `E5` | Exited repeated; Reported=true; vanished repeated; stale registry; writable no-entry | one PaneExited(7,null) and persisted Reported=true; no duplicate; one SessionVanished; mismatch -> NotFound,"Session was not found.",false without overwrite; writable no-entry -> same NotFound and no event |
| `E6` | Publish A twice, B once; cancel reader; second concurrent reader; dispose | FIFO A,B exactly once; new reader drains buffered events; "A session event reader is already active."; disposed publish "Session lifecycle is disposed." and no acceptance |
| `E7` | Failed tick then success; repeated dead tick | Warning "Session host watcher degraded: TmuxTimeout"; Information "Session host watcher recovered."; one exit event, pane retained; first handle NotFound logs Warning "Session host watcher degraded: NotFound" and does not prevent second handle exit; concurrent tick waits then polls |
| `E8` | Known server 300/start30; no-server, roots alive; repeated unreachable; recovery; reused 300 | one SIGUSR1(300,30,10) per episode; Information "Recovering tmux socket for server 300."; zero vanish; repeated unreachable zero additional signals; reused server zero signal and Missing observations permitted; unremembered server/live root -> zero signal, zero vanish, degraded; no matching roots -> vanish permitted; exactly one GetServer call per reachable tick with Alive, none otherwise |
| `E9` | Initially dead/missing, read-only handle, invalid duration, wrong launch | NotRunning with recorded nullable status and zero children; read-only permitted; InvalidArgument,"Invalid stop request.",false; wrong launch NotFound without deletion/signal |
| `E10` | TERM exits root; TERM ignored; child setsid; late fork | Stopped versus Killed; initial and rescanned descendants signalled; root initially alone, child appears on a grace poll, root then exits -> child retained and signalled to completion; unrelated 200 survives; children counted distinctly; no kill-session while live |
| `E11` | Tracked 102/12 remains after verification; PID reused; callback throws sentinel | Leftovers=[102], LeftoverProcesses,"Processes survived stop.",false,pids="102"; reused PID omitted; callback failure returns TmuxFailed,"Stop did not complete.",false, no sentinel/no cleanup |
| `E12` | Stop observes exit7/empty signal then removal; dead reverify becomes alive or launch changes | PaneExited(7,null) before kill-session and registry deletion; stale/alive reverify prevents removal and returns safe partial error |
| `E13` | Start held in creation, stop arrives; concurrent input gate held | stop waits for committed handle then signals it; input gate does not delay stop; old starter callers unchanged; new handle registered once |

## Tests

Use xUnit v3 in Aiakos.Node.Tests, imitate ProcessSnapshotTests and TmuxSessionInputTests.
Scripted runner tests assert complete argv arrays (actual TAB), no stdin, no unexpected command.
Unit tests run without tmux on all OS; Linux syscall tests and real tests dynamically skip elsewhere.

- T1. Permanent ProcessTree tests cover transitive ancestry, same-session non-child, detached and
    reparented tracked children, forks from tracked surviving children, absent/reused root and child,
    duplicate snapshots and stable order; scripted signal tests and Linux subprocess tests prove
    R2 sends to exact identity and never to reused/absent PID. No fixtures resembling tokens.
- T2. Permanent observer goldens cover every E3/E4 case, malformed/truncated UTF-8/fields/numbers,
    stale session/launch/PID/start time, no-server versus timeout and parser failure, empty exit
    fields and signal 0/9. GetServer verifies recorded private server via exact #{pid} command.
- T3. Permanent lifecycle tests cover matching persistence before enqueue/after enqueue, retry of
    Reported=false, persisted true, read-only/no-entry, writable/no-entry NotFound without event, registry mismatch, starter callback dedupe,
    reader ownership/cancellation/FIFO, disposed publication and per-name locking. State explicitly
    in test comments that process-crash outbox atomicity is not claimed.
- T4. Permanent watcher tests use fake time and scripted observer IO: immediate tick/interval,
    concurrent call waits then polls, first observation NotFound still processes second exit
    and marks tick degraded, poll errors produce no events, recovery logging, one exit/vanish, remembered server
    missing/reused, SIGUSR1 once, unremembered/live-root defers vanish without signals,
    unremembered/no-root permits vanish, exact GetServer call schedule, no input/kill-server.
- T5. Permanent not-running stopper helper tests cover shared request validation before mutation,
    read-only handles, stale handles, dead cleanup order/reverify, matching entry deletion only,
    unknown status, nullable recorded exit fields, name-lock release on every exit and failures
    before/after removal. Use an internal not-running cleanup helper called under an existing
    lifecycle lease, with no recursive acquisition; S6 builds the public StopAsync around it.
- T8. Permanent live stopper tests cover graceful success/ignored TERM/custom callback, zero grace,
    ChildGrace default/zero, root initially alone then forks on a grace poll and exits leaving the
    tracked child, reparent/setsid, reused PIDs, leftovers, signal failures, safe partial reports,
    cancellation before/after boundary, no cleanup on unknown/failed verification, outcome/counts
    and lease release. S6 reuses S5 validation and cleanup without changing their tested behavior.
- T6. Permanent integrated start/stop tests use the actual starter only after prerequisites merge,
    shared lifecycle and blocking scripted runner. Start holds creation, stop waits, different seats
    proceed, input operations remain independent, cancelled waiter mutates nothing, constructor
    compatibility and no accidental reentrant lifecycle deadlock.
- T7. Opt-in real tests (Category=Tmux, AIAKOS_TEST_TMUX=1, Linux tmux>=3.4) construct actual
    client/starter/lifecycle/watcher/stopper after prerequisite merge. Unique instance test-<guid N>,
    home, TMUX_TMPDIR and HOME per test; user's .tmux.conf intentionally sets remain-on-exit off
    and base-index 1. Exit7 yields retained dead pane and exactly one exit7 event; external SIGKILL
    yields signal9 or null (never zero). Stop TERM-handling and TERM-ignoring harnesses with a
    recorded setsid grandchild: Stopped/Killed, no PID/start-time survivors, removed session.
    Start watcher while pane alive to learn server PID; externally delete only its test socket,
    verify recovered listing, one SIGUSR1 attempt and no vanish. Externally kill only that test
    server, verify vanish and status Missing within 2 seconds with default poll interval (allow
    bounded assertion timeout for scheduling, record observed latency). Teardown may kill-server
    only the unique test socket; retain recorded process identities to clean failed test children.
    Never inspect or signal owner sessions. Record tmux version; do not claim unrun 3.4 coverage.
    Also start a watcher only after deleting its test socket: with a live recorded root it must
    stay degraded, send no signal and emit no vanish (C2); this is not proof of restart recovery.

## Definition of done

- `dotnet build -c Release` in the story worktree: zero warnings/errors.
- `dotnet test --project tests/Aiakos.Node.Tests -c Release`: existing and new unit tests green.
- For S8 run opt-in real tests with AIAKOS_TEST_TMUX=1; skipped tests are not proof of real behavior.
- LF, UTF-8 without BOM, final newline; one commit on the story branch, subject
  `feat(node): <story title> (<story ID>, #11)`. Body names silent choices and scoped risk evidence.
- Do not push or open a PR. Required prerequisites absent -> report the missing capability; do
  not implement around it. Acceptance tests are supplied separately and never read by implementer.

## Out of scope (do not implement, do not stub)

11-3 delivery/capture changes, 11-5 facade/list/adopt/reconcile/DI/Hello/capability and full
telemetry catalogue, Claude hooks/readiness/screens, secret files, CLI, Docker/custom stop
implementation (the callback seam itself is supported), cgroups, pidfd, automatic restart,
node shutdown seat termination, CI changes and a transactional event outbox.
