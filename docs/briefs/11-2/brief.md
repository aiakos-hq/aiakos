---
id: 11-2
title: "#11 slice 2 — tmux runner and start"
issue: 11
status: approved
route: impl
paths: [src/Aiakos.Node/Sessions/, src/Aiakos.Core/TmuxNames.cs, tests/Aiakos.Node.Testing/, tests/Aiakos.Node.Tests/Sessions/]
date: 2026-10-06
---

# Brief: #11 slice 2 — tmux runner and start

Part of #11. Self-contained. **Do not read docs/specs/ or docs/adr/.** Read the current
`SessionHost.cs`, `LaunchValidator.cs`, `TmuxNames.cs`, `FakeSessionHost.cs` and the session
validator tests first. Where this brief is silent, choose the simplest behavior and record it
in the commit body; do not add another operation or a fallback.

## Goal

Add a bounded process runner, private tmux client initialization, durable launch registry,
process snapshot reader and a real start component. A successful start runs the supplied
executable directly, has labels and a durable handle, and has sent no input. Existing live
sessions survive initialization, failed starts and component disposal.

The predecessor 11-1 is merged on main at `2c968eef41ee6d3e81bda89f66926ed63c270cc0`:
`ISessionHost`, its records, `ILaunchFiles`, `LaunchValidator`, `TmuxNames` and the fake exist.
There is no real runner, client, registry store, process scanner or watcher on that baseline.
This slice creates the first four. The watcher/event transport and the complete `ISessionHost`
facade do **not** exist yet. `TmuxSessionStarter` is the start component that later slices will
compose into that facade; it does not implement `ISessionHost` or supply placeholder methods.
Its supplied event callback is an actual consumer seam, not an empty watcher implementation.
Node startup/DI, reconciliation before Hello, capability advertisement and configuration binding
belong to 11-5. Here callers construct options, call InitializeAsync once, then call StartAsync.
No change to NodeProgram, NodeOptions, OrchestratorConnection or the fake is needed.

The issue's risk row lists 0003-RK1, 0004-RK1/RK2/RK3/RK4/RK8, 0005-RK14 and 0004-FU1.
This slice checks the start half of 0004-RK3 (direct exec and tmux argument boundaries), and
retains 11-1's 0004-FU1 validator. Paste, socket recovery, watcher signal reporting, stop trees
and sandbox isolation remain with 11-3/11-4/11-5/M6. Do not claim those risks closed.

## Files to create or touch (nothing else)

| Path | What |
|---|---|
| `src/Aiakos.Node/Sessions/*.cs` | New runner/client/options/version/config, registry, process snapshot and start components; source-generated JSON metadata |
| `src/Aiakos.Core/TmuxNames.cs` | Add socket/config/attach helpers without changing address/session validation |
| `tests/Aiakos.Node.Testing/*.cs` | Recording/scripted `FakeProcessRunner` implementing the new runner seam |
| `tests/Aiakos.Node.Tests/Sessions/*.cs` | Permanent unit tests and isolated opt-in real tmux start tests |

No new package, project reference, reflection in src, warning suppression or Version= reference.
Preserve all existing interface signatures, validator results and fake tests. Unix-only operations
check the OS before Unix APIs; unit tests for parsing/building commands run on every OS.

## Public surface (exact names)

All new node types are in `Aiakos.Node.Sessions`. Existing records in `SessionHost.cs` are reused.
Constructor wiring not shown is internal choice; the constructors below are available to tests.

```csharp
public sealed record ProcessRequest(string FileName, IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string,string> Environment, ReadOnlyMemory<byte>? Stdin, TimeSpan Timeout);
public enum ProcessOutcome { Exited, TimedOut, StartFailed }
public sealed record ProcessResult(ProcessOutcome Outcome, int? ExitCode, byte[] Stdout, byte[] Stderr,
    bool StdoutTruncated, bool StderrTruncated);
public interface IProcessRunner { Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct); }
public sealed class ProcessRunner : IProcessRunner, IDisposable
{
    public ProcessRunner(int maxConcurrency = 8, TimeProvider? timeProvider = null);
    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken ct);
    public void Dispose();
}
public sealed class TmuxHostOptions
{
    public string Instance { get; set; } // caller supplies; example "test-<guid N>"
    public string Home { get; set; } // caller supplies absolute node home
    public string? TmuxPath { get; set; } // null -> first executable tmux on supplied PATH
    public TimeSpan InvocationTimeout { get; set; } // 5 seconds
}
public static class TmuxVersion { public static bool TryParse(string text, out int major, out int minor, out string? version); }
public sealed class TmuxClient
{
    public TmuxClient(TmuxHostOptions options, IProcessRunner runner,
        IReadOnlyDictionary<string,string> nodeEnvironment, Microsoft.Extensions.Logging.ILogger<TmuxClient> logger,
        System.Diagnostics.Metrics.IMeterFactory meterFactory);
    public SessionHostInfo Info { get; }
    public Task InitializeAsync(CancellationToken ct);
    public Task<ProcessResult> RunAsync(IReadOnlyList<string> command, ReadOnlyMemory<byte>? stdin, CancellationToken ct);
    public static bool IsNoServer(ProcessResult result);
    public static SessionHostError? GetError(ProcessResult result);
}
public interface ISessionRegistry
{
    Task<RegistryEntry?> ReadAsync(string sessionName, CancellationToken ct);
    Task<IReadOnlyList<RegistryEntry>> ReadAllAsync(CancellationToken ct);
    Task WriteAsync(RegistryEntry entry, CancellationToken ct);
    Task DeleteAsync(string sessionName, CancellationToken ct);
    Task<IReadOnlyList<RegistryEntry>> ReadRecoveryAsync(string sessionName, CancellationToken ct);
    Task WriteRecoveryAsync(RegistryEntry entry, CancellationToken ct);
    Task DeleteRecoveryAsync(string sessionName, CancellationToken ct);
}
public sealed class SessionRegistry : ISessionRegistry { public SessionRegistry(string home); }
public interface IProcessSnapshot
{
    Task<IReadOnlyList<ProcessInfo>> ReadAsync(CancellationToken ct);
}
public sealed class ProcessSnapshot : IProcessSnapshot
{
    public ProcessSnapshot(string procRoot = "/proc", uint? effectiveUid = null);
    public Task<IReadOnlyList<ProcessInfo>> ReadAsync(CancellationToken ct);
    public static void CheckOrphans(IReadOnlyList<ProcessInfo> snapshot, IReadOnlyList<int> livePanePids, IOrphanProbe? probe);
    public static ulong RequireStartTime(IReadOnlyList<ProcessInfo> snapshot, int panePid);
}
public sealed class TmuxLaunchCommands
{
    public TmuxLaunchCommands(TmuxClient client, IProcessSnapshot processes);
    public Task<SessionHandle> CreateAsync(SessionSpec spec, CancellationToken ct);
}
public sealed class TmuxSessionStarter
{
    public TmuxSessionStarter(TmuxClient client, TmuxLaunchCommands commands,
        ISessionRegistry registry, ILaunchFiles files, IProcessSnapshot processes,
        Action<SessionHostEvent> publish, System.Diagnostics.Metrics.IMeterFactory meterFactory,
        Microsoft.Extensions.Logging.ILogger<TmuxSessionStarter> logger, TimeProvider? timeProvider = null);
    public SessionHostInfo Info { get; }
    public Task<SessionHandle> StartAsync(SessionSpec spec, CancellationToken ct);
    public IReadOnlyList<string> GetAttachCommand(SessionHandle session, bool readOnlyMode = true);
}
public sealed class LaunchFiles : ILaunchFiles { }

namespace Aiakos.Core;
public static class TmuxNames // add members to existing class
{
    public static string SocketName(string instance); // "aiakos-" + instance
    public static string ConfigPath(string home); // <home>/tmux/tmux.conf
    public static IReadOnlyList<string> AttachCommand(string instance, string seatAddress, bool readOnlyMode = true);
}

namespace Aiakos.Node.Testing;
public sealed class FakeProcessRunner : Aiakos.Node.Sessions.IProcessRunner
{
    public IReadOnlyList<Aiakos.Node.Sessions.ProcessRequest> Requests { get; }
    public void Enqueue(Aiakos.Node.Sessions.ProcessResult result);
    public Task<Aiakos.Node.Sessions.ProcessResult> RunAsync(Aiakos.Node.Sessions.ProcessRequest request, CancellationToken ct);
}
```

## General rules

G1. Never log or trace full argv, stdin, stdout, stderr, environment values or Attributes; never
    put Environment or Argv in a registry entry. SessionSpec contains caller-provided non-secret
    values only; this component does not mint/read/write seat tokens. Preserve the *_FILE validator
    exception. Never send keys, input, an Enter or any readiness workaround during initialization,
    start, errors or disposal. Never run kill-server in production. Only a verified dead managed
    session may be removed by this slice. The tests' teardown can kill only their unique test socket.

G2. Failures never silently start another process/session or retry a mutating tmux command.
    Caller cancellation propagates OperationCanceledException, releases locks/permits and stops
    further commands; it does not kill a pane or roll back a running harness. ProcessRunner kills
    only its own client on timeout/cancellation (not the tmux server/process tree). Preserve any
    registry entry once new-session may have run, so 11-5 can reconcile an uncertain start. Every
    production exception conversion uses a fixed message below; do not attach arbitrary exception
    messages or process stderr as Exception.InnerException. No exceptions are swallowed into success.

## Changes to earlier behavior

None. The fake and 11-1 validation stay unchanged. A runtime label-encoding helper in this slice
preserves rather than rejects valid opaque labels and environment values ending in semicolons.

## Rules

R1. ProcessRunner launches Process with UseShellExecute=false, ArgumentList (one string per
    element), redirects all three streams, clears ProcessStartInfo.Environment then copies exactly
    request.Environment. Never use Arguments, a command shell or inherited environment. Stdin is
    raw bytes, written in chunks of at most 65536 bytes then closed; null closes immediately.
    Concurrently drain stdout and stderr through EOF, retain at most 1048576 bytes **each**, set
    that stream's truncation flag only if extra bytes were read, and discard surplus without
    stopping the drain. Return exact retained bytes and exit code. A missing/unstartable executable
    returns StartFailed with null exit code and empty streams, without exposing OS exception text.
    FakeProcessRunner records snapshots of requests in call order (copy args/env/stdin), returns
    the next queued result, checks cancellation first and is thread-safe. An empty scripted
    queue throws InvalidOperationException("No scripted process result."); it starts no process.

R2. The runner admits at most maxConcurrency active clients (default 8). Waiters use caller
    cancellation; the request timeout begins only after acquiring the permit and starting the
    client. Timeout includes stdin writes and output drains, not just process exit; keep it armed
    until the invocation is finished. It uses the supplied TimeProvider (System by default).
    On timeout kill/reap that client if still alive, cancel IO and close redirected pipes (a child
    retaining a pipe must not hang teardown); await canceled IO tasks and return TimedOut/null
    ExitCode with bytes already retained. EOF/full-stdin guarantees apply to normal completion,
    not a canceled/timed-out invocation. Caller cancellation similarly kills/reaps the client,
    cancels IO and closes pipes, but throws cancellation. Dispose releases runner resources after
    its operations have completed; it never sends anything to a server or pane. maxConcurrency <1
    or timeout <=0 or timeout > TimeSpan.FromMilliseconds(4294967294) is
    ArgumentOutOfRangeException before launching anything.

R23. After a child has started successfully, treat any IOException raised by writing,
    flushing or closing its redirected stdin as the child having stopped accepting input.
    This includes broken-pipe/EPIPE; use the exception type, without inspecting OS message text,
    HResult or errno. Drop the unwritten remainder, close the runner's stdin stream (an
    IOException from this close is treated the same way), keep draining stdout and stderr,
    and await that child's exit. Return Exited with its actual exit code and exact retained
    streams/truncation flags under R1; do not throw the stdin IOException, expose its OS
    message, kill the child, retry or start another process. This early-closure case is the
    exception to R1/R2's normal full-stdin guarantee. This classification applies only to
    stdin write/flush/close IOExceptions, not stdout/stderr IO or process-start failures.
    R2 timeout and caller cancellation remain authoritative while waiting/draining:
    return TimedOut on timeout or throw cancellation, respectively, even after stdin closed.

R3. TmuxClient snapshots nodeEnvironment and options at construction. Client environment contains only present
    HOME, USER, LOGNAME, SHELL, PATH, TMUX_TMPDIR, XDG_RUNTIME_DIR and the UTF-8 locale selection.
    If LC_ALL is nonempty and names a UTF-8 locale (case-insensitive UTF-8 or UTF8 suffix), pass it;
    otherwise pass LANG when it names UTF-8 and LC_ALL is empty; in every other case use LANG=C.UTF-8
    and omit LC_ALL. Omit all other variables, including TMUX/TMUX_PANE, AIAKOS_NODE_TOKEN, OTEL_*,
    DOTNET_*. Later mutations of the caller dictionary cannot change client requests.

R4. TmuxClient.GetError performs the following classification and returns null only on success.
    IsNoServer is true only for the recognized nonzero-exit no-server cases below. RunAsync returns
    raw ProcessResult (apart from availability/argument/cancellation failures); its consumer
    either explicitly accepts IsNoServer or throws SessionHostException(GetError(result)).
    Classify in this order: TimedOut ->
    (TmuxTimeout, "tmux invocation timed out.", true); StartFailed -> (Unavailable,
    "tmux executable could not be started; requires tmux >= 3.4.", false); exit 0 -> success;
    nonzero stderr containing ordinal-ignore-case "no server running", or "error connecting to"
    together with "(No such file or directory)" or "(Connection refused)" -> no-server; an error
    connecting with "Permission denied" is an ordinary failure, not an absent server. Next, "can't find session" or "can't find pane" -> NotFound, "duplicate session"
    -> AlreadyRunning; otherwise TmuxFailed. NotFound's message is "Session was not found.";
    AlreadyRunning's is "A live session already exists for SeatId."; no-server outside explicit
    initialization/list handling is TmuxFailed with "tmux server is not running."; unclassified
    is TmuxFailed with "tmux invocation failed.". Only TmuxTimeout/TmuxFailed are retryable.
    Keep up to 1024 bytes of unclassified stderr as UTF-8 with replacement in Error.Metadata
    under "stderr" for the **returned error only**; never log/trace it. Recognized errors have
    no stderr metadata. The runner itself does not classify tmux-specific text.

R5. SocketName returns aiakos-<instance>; ConfigPath returns Path.Combine(home,"tmux","tmux.conf").
    Instance/Home default to empty, TmuxPath to null, InvocationTimeout to 5 seconds.
    Instance must match ^[a-z][a-z0-9-]{0,63}$; Home is absolute and contains no NUL;
    InvocationTimeout must be >0 and <=4294967294 milliseconds. Options errors throw
    ArgumentException("Invalid tmux host options.") from the TmuxClient constructor before any
    process/files are touched. SocketName rejects an invalid instance and ConfigPath an invalid
    home with the same fixed ArgumentException; they perform no IO.
    Null TmuxPath resolves the first existing executable named tmux among supplied PATH's entries
    (ignore empty/relative entries); an explicit relative path is an options error; an absolute missing/non-executable path is
    unavailable with the missing-path reason below. Resolve
    once at InitializeAsync; log only "tmux executable: {Path}". Missing path makes Info
    ("tmux",null,Unavailable,"tmux executable could not be started; requires tmux >= 3.4.",[]).
    Linux LaunchFiles checks File.Exists plus any executable mode bit; DirectoryExists uses
    Directory.Exists. Unsupported OS makes the client unavailable with reason
    "tmux session host requires Linux." and no process or filesystem mutation.

R6. InitializeAsync runs the resolved binary with arguments ["-V"] only. TmuxVersion accepts
    exactly tmux <digits>.<digits><optional ASCII-letter suffix> with optional final CR/LF,
    no other prefix/trailing text; parsed Version omits "tmux ". Compare numeric major/minor,
    requiring >=3.4 (4.0 passes; 3.3a fails). Initial Info before initialization is
    ("tmux",null,Unavailable,"tmux session host is not initialized.",[]). Lower version reason
    is "tmux <version> < 3.4"; unparseable reason is "Unrecognized tmux version; requires tmux >= 3.4.";
    retain null Version for unparseable text, never its raw value. Available Info has parsed Version,
    null Reason and capabilities ["session-host.tmux"]. Repeated/concurrent InitializeAsync calls
    share one initialization; unavailable results do not throw or shut down the node. Subsequent
    client/start operations throw Unavailable with Info.Reason and Retryable=false.

R7. With a valid version, rewrite <Home>/tmux/tmux.conf, UTF-8 without BOM and LF/final newline,
    with exactly the configuration block below. Every subsequent client RunAsync prefixes
    ["-u","-f",ConfigPath,"-L",SocketName] to command; InvocationTimeout defaults to 5 seconds.
    Null stdin is required for every command other than load-buffer; for load-buffer use
    5 seconds + 2 seconds * (stdin byte count / 1048576.0), regardless of default timeout.
    The client does not join, shell-quote or evaluate arguments; fixed command names/formats
    are component-generated. For **data elements** ending with ';', insert one '\\' immediately
    before that final ';' before passing to the runner; tmux removes that inserted character.
    This also preserves a literal data suffix '\\;'. RunAsync takes one command, never a compound command sequence, so every element after the
    fixed command name is data for this escaping rule (a literal standalone ';' is escaped too).
    Do not double-escape in callers. Prefix flags/path/socket are component-owned, not user options.
    Config write failures yield unavailable reason "tmux configuration could not be prepared."
    without changing sessions. No new-session is issued during initialization.
    Configuration block (the inline comments are part of the output):

```tmux
# Generated by aiakos-node. Do not edit; rewritten at every node start.
set-option -g remain-on-exit on # keep dead panes: exit status and evidence
set-option -g history-limit 10000 # scrollback for CapturePane
set-option -g window-size manual # attaching humans do not resize seats
set-option -g update-environment "" # attaching clients do not inject variables
set-option -g allow-rename off # preserve managed names
set-option -g automatic-rename off # preserve managed names
```

R8. Before marking Info available, run show-environment -g on the private socket. A recognized
    no-server result means no existing server and is success: stop initialization commands
    immediately, mark Available, and do not issue any set-environment or set-option calls.
    R7's generated config will apply when the first explicit new-session starts the server.
    Thus no-server initialization has exactly -V then show-environment -g, and no other tmux call.
    Otherwise
    require success, parse lines NAME=value or -NAME without echoing values, and unset every name
    outside R3's final key allowlist using separate ["set-environment","-g","-u",NAME] calls.
    Log "Removed tmux global environment variable {Name}." for each removed name. Also unset
    inherited unsafe LC_ALL when the selected locale omits it. Then apply the six R7 settings
    with separate ["set-option","-g",name,value] calls, in R7 order (update-environment value
    is an empty string). No killing, input or session recreation. If any hygiene invocation
    fails, Info becomes Unavailable with the applicable R4 fixed error and no capabilities;
    do not continue initialization. Registry/reconciliation is not part of initialization here.
    Initialization uses an internal invocation path which does not require Available; public
    RunAsync does require it. No public early-available interval is introduced.

R9. SessionRegistry rejects a non-absolute Home with ArgumentException("Invalid tmux host options.").
    It uses <Home>/sessions (create/chmod 0700 on Linux), one <SessionName>.json per
    entry. Validate the name equals TmuxNames.SessionName(entry.SeatAddress) before writing;
    read/delete names must match ^[a-z][a-z0-9-]{1,39}_[a-z][a-z0-9-]{0,23}$, otherwise throw
    ArgumentException("Invalid registry session name.") before IO. Never include Argv/Environment.
    Serialize every existing RegistryEntry field via source-generated System.Text.Json metadata,
    snake_case property names, compact UTF-8 without BOM/final LF. The exact root names are:
    schema, instance, state, seat_id, seat_address, launch_id, harness, socket, session_name,
    session_id, pane_id, pane_pid, pane_start_time, created_at, exit, attributes. Exit object names:
    code, signal, observed_at, reported. Attributes keys are caller-owned and are not renamed. Attributes are preserved verbatim.
    Write to a unique same-directory temporary file created with mode 0600; flush bytes to disk
    (FileStream.Flush(true)), then File.Move(overwrite:true). Final file stays 0600. The older file
    remains readable until rename. On failure remove that invocation's temporary file if possible,
    keep the previous committed file; never delete the previous entry as recovery.
    Recovery methods use the same validation/atomic-write/mode/serialization rules at
    <SessionName>.recovery (JSON array of full RegistryEntry objects). ReadRecoveryAsync returns
    the ordered entries, or [] only when absent. WriteRecoveryAsync atomically appends an entry
    unless that LaunchId is already recorded; never overwrite earlier recovery evidence on a
    failed replacement's next attempt. Use the same per-session write lock for primary/recovery
    operations. This sidecar retains failed launches' identity/exit evidence while the primary
    .json is replaced by new attempts. ReadAllAsync
    ignores .recovery files; deleting a primary entry does not delete its recovery sidecar.

R10. ReadAsync returns null only for an absent file; malformed JSON or IO errors throw
    SessionHostException(TmuxFailed,"Session registry operation failed.",true), never return
    null/an empty registry. ReadAllAsync reads only *.json in ordinal filename order; ignore .tmp
    leftovers and preserve exit.reported. DeleteAsync is idempotent for missing files. Serialize
    writes/deletes for the same session name. Caller cancellation before rename leaves old bytes
    intact; after rename it may leave the newly committed entry. Crash tests simulate a failure
    after temporary flush and before rename by a test seam (internal choice, no production toggle).

R11. New launch entry is Schema=1, Instance=options.Instance, State="starting", SeatId,
    SeatAddress, LaunchId, Harness and Attributes from spec, Socket=SocketName, SessionName from
    TmuxNames, CreatedAt=injected TimeProvider.GetUtcNow(), Exit=null and four nullable handle
    fields null. Commit it before issuing new-session. Once creation/labels succeed, commit the
    same entry with State="running" and SessionId/PaneId/PanePid/PaneStartTime from the handle;
    preserve CreatedAt and Attributes. No token or Environment is stored. Failure after writing
    starting leaves that entry; failure committing running does not remove/stop the existing pane.

R12. ProcessSnapshot reads only numeric PID directories under procRoot; effectiveUid defaults to
    Linux geteuid via LibraryImport, with a supplied UID enabling fake-proc tests on any OS.
    Read status Uid line (second numeric field = effective UID); include only matching UID.
    Parse stat after its **last closing parenthesis**: ppid field 4, session ID field 6, starttime
    field 22; handle spaces and parentheses in comm, use invariant numeric parsing. Read cmdline
    as NUL-separated UTF-8 argv, remove only the final empty segment, replace invalid UTF-8.
    Do not read environ or use pgrep/process-name matching. Return ProcessInfo sorted by PID.
    A process disappearing during reads is skipped; permission/parse errors for matching-user
    entries throw TmuxFailed,"Process snapshot could not be read.",true. No process is signalled.

R13. ProcessSnapshot.CheckOrphans receives a snapshot plus live managed pane PID roots; the
    caller extracts roots from R18's listing with matching schema/instance and pane_dead=0.
    The helper does not call tmux, inspect the registry or identify processes by command name.
    Exclude each root,
    recursive descendants via ParentPid and any same-session-ID processes (including detached
    children with no remaining parent link). Invoke the supplied IOrphanProbe on each remaining
    own-UID process, once in ascending PID order. Do not call the probe for null OrphanProbe.
    Any matches -> SessionHostException(OrphanDetected,"Orphan harness processes detected.",false,
    metadata {"pids":comma-separated ascending decimal PIDs}); no start/removal/registry write.
    Never signal matching processes. A probe exception is TmuxFailed,"Orphan probe failed.",true
    without the probe's message/inner exception. Run the probe only as part of an explicit start.

R14. ProcessSnapshot.RequireStartTime finds the supplied pane PID in its snapshot and returns
    its StartTime. Creation uses this helper on the same snapshot seam to get new pane PID's start time. Snapshot absence after a
    successful creation yields TmuxFailed,"Created pane process could not be identified.",true;
    leave the pane/starting entry intact, with no guessed start time or default zero. PID and
    start time come from the actual pane process, not the tmux server or an argv match.

R15. TmuxLaunchCommands.CreateAsync is the low-level creation step; its caller must have performed
    R18–R20 checks and written R11 starting. It issues one command, with this exact ArgumentList
    after the R7 prefix: ["new-session","-d","-P","-F","#{session_id}\t#{pane_id}\t#{pane_pid}",
    "-s",SessionName,"-x",columns,"-y",rows,"-c",WorkingDirectory], then each environment pair
    in ordinal name order as ["-e","NAME=value"], then ["--"] and each argv element separately.
    If Argv has just one element prepend /usr/bin/env **after --** so the harness runs directly,
    never via sh -c. Quotes, spaces, backticks, dollars and internal ';' bytes stay data. Trim
    only final CR/LF from stdout; require exactly three TAB-separated fields: $<decimal>,
    %<decimal>, positive decimal PID; malformed successful output throws TmuxFailed,
    "Invalid tmux pane response.",true with no raw output. Do not infer successful creation
    from stderr or retry duplicate creation.

R16. After parsing creation, apply six session labels, in order @aiakos-schema=1,
    @aiakos-instance=Instance, @aiakos-seat-id=SeatId, @aiakos-seat=SeatAddress,
    @aiakos-launch=LaunchId, @aiakos-harness=Harness, using separate
    ["set-option","-t",SessionId,label,value] commands, then
    ["set-option","-p","-t",PaneId,"remain-on-exit","on"]. Each of those seven commands is
    immediately preceded by its own R17 display-message verification. Full command order after
    new-session is verify, schema, verify, instance, verify, seat-id, verify, seat-address,
    verify, launch, verify, harness, verify, remain-on-exit (14 invocations). Verification expects
    an empty or matching launch label through the launch write, then the exact launch label.
    R7's server default already
    protects immediate exit. Check every command's result before proceeding. Obtain R14 start
    time and return SessionHandle with supplied SeatId/LaunchId, derived SessionName, parsed IDs/PID,
    actual start time and ReadOnly=false. Return as soon as the pane is labelled; no readiness wait.
    Label failure leaves the pane/starting entry, throws its R4 error, and sends no cleanup/input.

R17. Immediately before any post-creation mutation (label/options calls) verify the pane using
    ["display-message","-p","-t",PaneId,"#{pane_pid}\t#{@aiakos-launch}\t#{pane_dead}"].
    PID must equal creation PID; until launch label is set allow empty launch, then require its
    exact value. Initial label writes are the sole exception to requiring an already-labelled
    handle. A different PID or unexpected nonempty launch -> NotFound,"Session was not found.",false,
    with no subsequent mutation. Dead=1 is allowed when labelling the new pane (immediate-exit
    evidence); no need to wait for process liveness.

R18. TmuxSessionStarter checks availability, caller cancellation, LaunchValidator with LaunchFiles,
    then reject TAB/LF/CR/NUL in SeatId, LaunchId or Harness with
    InvalidArgument,"Launch labels contain a forbidden character.",false before any registry/tmux
    mutation. SeatAddress is already validated by LaunchValidator. This runtime check leaves
    LaunchValidator and the fake unchanged. Serialize StartAsync by derived
    SessionName across its callers; a second same-seat start waits, validates and then sees the
    first pane (AlreadyRunning), never kills or queues input. Different seat starts overlap under
    the runner cap. Within the lock, list panes via ["list-panes","-a","-F",the fixed format below].
    Recognized no-server means empty list; every other failure stops. Treat malformed listing as
    TmuxFailed,"Invalid tmux pane response.",true. Multiple panes under the target name are
    NotFound,"Session was not found.",false before the alive/dead checks. For a single existing
    target session with pane_dead=0,
    throw AlreadyRunning,"A live session already exists for SeatId.",false and metadata launch_id
    from its label (empty when unlabelled); never remove it. A dead target without matching schema/instance/seat-address labels -> NotFound,
    "Session was not found.",false; foreign sessions are never claimed/deleted.
    Fixed format (actual TAB characters separate fields):
    #{session_name}\t#{session_id}\t#{pane_id}\t#{pane_pid}\t#{pane_dead}\t#{pane_dead_status}\t#{pane_dead_signal}\t#{@aiakos-schema}\t#{@aiakos-instance}\t#{@aiakos-seat-id}\t#{@aiakos-seat}\t#{@aiakos-launch}\t#{@aiakos-harness}

R19. After listing and the alive/foreign checks, perform R13 orphan checking. A managed dead
    target needs a matching registry entry (same SessionId/PaneId/PanePid/launch label) before
    removal; absence/mismatch -> NotFound,"Session was not found.",false, no removal/new entry.
    Exception: a matching State="starting" entry with SessionId/PaneId/PanePid/PaneStartTime all
    null proves no handle was returned (R20 commits running before returning). Match its Schema,
    Instance, SeatId, SeatAddress, LaunchId, Harness and SessionName to the dead target's labels/name;
    do not require its absent handle fields or invent a start time. This case is recoverable.
    For an ordinary entry with a recorded handle, read its exit code/signal as nullable integers:
    empty stays null, never zero. If Exit.Reported
    is not already true, write that exit with injected ObservedAt and Reported=false, then call
    publish(PaneExited(old handle,ObservedAt,code,signal)) before removal. Only after publish returns
    successfully write the same exit with Reported=true. If publish throws, convert to
    TmuxFailed,"Pane exit publication failed.",true, leave Reported=false, do not remove the pane;
    a later retry must attempt publication again. The supplied callback synchronously enqueues
    before returning, and a throw means it did not accept the event. Persisted reported=true
    deduplicates successful publication on a normal retry/process restart;
    atomic persistence plus external event delivery is not a transactional outbox in this slice.
    For the exceptional matching starting entry with no returned handle, write its Exit code/signal/
    ObservedAt with Reported=false, and do not call publish or construct a SessionHandle. Retain this
    full failed-launch entry using WriteRecoveryAsync before any removal. Then reverify and remove
    only the matching dead pane, as below. The failed StartAsync already reported TmuxFailed.
    Preserve the recovery sidecar across primary deletion/new starting writes and every failed
    replacement; delete it only after the replacement's running entry is durably committed.
    Maintainer decision 2026-10-06: this narrowly exempts such failed starting launches from
    spec 0004 R13's PaneExited requirement. SessionHandle.PaneStartTime remains non-nullable and
    no merged 11-1 code changes. The spec amendment is included in this analysis PR.
    Reverify PID equals the just-listed target PID, launch equals the matched registry LaunchId,
    and dead=1 using R17's display command immediately before
    ["kill-session","-t","="+SessionName]; refusal leaves the existing registry/session untouched.
    On successful removal delete that registry entry, then R11 starts the new launch.

R20. On an absent/removed target, write R11 starting -> R15–R17 CreateAsync -> R11 running,
    in order, then delete any recovery sidecar for this session name and return the handle.
    If recovery cleanup fails, propagate the registry error and keep the new running pane/entry;
    never delete/kill the new pane. Recovery entries are evidence, not other managed panes.
    A failure stops this sequence at that point and releases
    the lifecycle lock; never remove a possibly live pane to make retry succeed. A second start
    finds a live pane even after a prior label/registry failure and refuses it. Production caller
    supplies publish, not an implicit noop. No fake delivery, stop, list/adopt/watch APIs are added.

R21. GetAttachCommand uses TmuxNames.AttachCommand(Instance, seat address reconstructed from the
    validated SessionName, readOnlyMode), returns exactly ["tmux","-L",SocketName,
    "attach-session","-r","="+SessionName] by default, without -r for false. No shell quoting in
    these elements, no process/input/file mutation. Use R5's existing SocketName validation;
    AttachCommand validates its address through the existing SessionName helper, retaining that
    helper's ArgumentException. Starter requires Available;
    handle SessionName must parse under the existing seat/rig patterns, otherwise NotFound.

R22. Use ActivitySource/Meter name Aiakos.Node (reuse NodeTelemetry.ActivitySource; meter through
    IMeterFactory), no payload/error-detail tags. Start span session_host.start: aiakos.seat.address,
    aiakos.launch_id, aiakos.harness, argv.count, env.names (ordinal names), outcome (ok or error code);
    tmux.session/tmux.pane_id when known. Child span "tmux <command>": tmux.command,
    tmux.exit_code when present, tmux.error_class (ok/timeout/failed/no_server/not_found).
    Counter aiakos.node.tmux.invocations tags command/result with those values; duplicate session
    maps to failed. Histogram aiakos.node.tmux.invocation.duration, milliseconds, command tag.
    Observable gauge aiakos.node.session_host.available returns 1 or 0 with tmux.version (empty
    when unknown). Instrument only these current operations, no placeholder delivery/stop metrics.

## Expected outputs: exact text

Base scripted fixture: Instance=test, Home=/tmp/node-fixture, valid spec SeatId=seat-1,
SeatAddress=impl@demo, LaunchId=launch-1, Harness=test-harness, Argv=[/usr/bin/test-harness,arg],
WorkingDirectory=/tmp/work, SeatHome=/tmp/seats/demo/impl, Environment={TEST_MODE:plain}, Size=160x45,
Attributes={native_session_id:native-1}; process snapshot PID 1234, starttime 9876, own UID 1000.
ConfigPath=/tmp/node-fixture/tmux/tmux.conf, SocketName=aiakos-test. Real tests substitute isolated
paths/socket and a harmless test executable. Exact error triples below are (Code,Message,Retryable).

| ID | Input/case | Expected |
|---|---|---|
| `E1` | executable echoes argv/env/stdin, output at cap and cap+1; nonexistent executable | ArgumentList and exact explicit environment, exact stdin bytes, null stdin EOF; each stream independently capped at 1048576 with truncation false at cap and true at cap+1, both drains reach EOF; StartFailed/null exit/empty streams for nonexistent executable; no shell/secret inheritance |
| `E2` | 9 blocked clients at default cap; injected-clock timeout; waiting-client and active-client cancellation | at most 8 active; queued cancellation starts nothing; timeout kills/reaps only client and returns TimedOut/null exit; caller cancellation throws and releases permit; blocked stdin or inherited pipes cannot hold teardown; later request succeeds; server/pane unaffected; maxConcurrency=0 and timeout=0 throw ArgumentOutOfRangeException before launch |
| `E3` | node dictionary holds allowlisted values, AIAKOS_NODE_TOKEN/OTEL/DOTNET/TMUX sentinels; LANG/LC_ALL non-UTF8, UTF8 and absent | only R3 keys reach the request; caller dictionary mutation has no effect; LC_ALL=UTF-8 retained when valid, otherwise valid LANG with empty LC_ALL retained, otherwise LANG=C.UTF-8 and LC_ALL absent; no sentinel inherited |
| `E4` | timeout/start-failed/no-server/not-found/duplicate/other stderr plus >1024 stderr bytes | exact R4 triples; successful exit remains success regardless of stderr; unclassified metadata stderr contains only first 1024 bytes decoded with replacement; stderr never in logs/traces, recognized errors have no stderr metadata |
| `E5` | naming helpers, PATH with two executables, explicit path, unsupported OS, invalid instance/home | socket aiakos-test, config /tmp/node-fixture/tmux/tmux.conf; first executable/explicit path resolved once; only resolved path logged; invalid options ArgumentException("Invalid tmux host options.") from TmuxClient constructor and no IO; invalid SocketName/ConfigPath inputs throw the same fixed error; unsupported OS Unavailable/"tmux session host requires Linux."/[] |
| `E6` | tmux 3.4, 3.6, 4.0, 3.3a, next-3.7, master, garbage, missing binary; two Initialize calls | accepted 3.4/3.6/4.0 have Available/session-host.tmux; 3.3a has Unavailable/"tmux 3.3a < 3.4"/[]; unparseable has null Version and fixed R6 reason; missing has fixed R5 reason; only one -V invocation, no socket/config flags in version command; no new-session/input; subsequent client RunAsync throws Unavailable |
| `E7` | initialization and scripted RunAsync; semicolon data and load-buffer stdin | exact R7 config bytes/prefix; no user config; every data suffix ';' gets one inserted backslash and round-trips including literal '\\;'; standalone ';' data is escaped; separate args unchanged otherwise; non-load stdin refused with ArgumentException; load stdin uses 5s+2s/MiB; configuration failure Unavailable with fixed R7 reason |
| `E8` | no server; existing server with allowed variables plus AIAKOS_NODE_TOKEN, OTEL_X, DOTNET_X and unsafe LC_ALL; failing unset | no-server sequence exactly -V then prefixed show-environment -g, no other calls or server creation; forbidden globals removed with names-only logs; six options applied in order; prior sessions untouched; failed unset stops initialization and Available is not advertised |
| `E9` | registry write/read/update; malformed name; simulated crash before rename | directory 0700/file 0600, exact R9 snake_case schema and Attributes round-trip; recovery array round-trips full old entries with identical modes/atomicity; repeated same LaunchId is not appended, different failed attempts preserve earlier entries, absent ReadRecoveryAsync returns []; it is excluded from ReadAllAsync; primary delete does not delete recovery; null handle fields starting then real fields running; no argv/env; invalid name ArgumentException("Invalid registry session name."); failed write preserves previous committed bytes |
| `E10` | absent/corrupt registry file; .tmp leftover; repeated delete; concurrent writes and cancellation before rename | absent read null; corrupt read TmuxFailed/"Session registry operation failed."/true; sorted .json entries only; delete absent succeeds; no torn committed file; canceled pre-rename write preserves older entry |
| `E11` | successful/failed start with recording registry | starting committed before new-session, running committed only after labels; CreatedAt/Attributes preserved; failures after starting retain it and do not stop/remove panes |
| `E12` | fake proc with foreign UID, vanished pid, comm containing parentheses/spaces, invalid cmdline UTF8 and matching-UID malformed stat | only own effective UID; exact fields 4/6/22; sorted PIDs; argv NUL-split with U+FFFD; no environ read; transient disappearance skipped; matching-user parse failure TmuxFailed/"Process snapshot could not be read."/true |
| `E13` | roots/descendants/session members, two unmatched probe hits 42 and 99; null/throwing probe | managed live tree/session members excluded; probe called once per remaining own-UID process in PID order; hits OrphanDetected/"Orphan harness processes detected."/false with pids="42,99"; null probe not invoked; throwing probe fixed R13 failure; no signals or start mutations |
| `E14` | created pane PID present/absent from snapshot | present handle uses 9876; absent TmuxFailed/"Created pane process could not be identified."/true; no guessed PID/starttime, no cleanup |
| `E15` | base launch, one-element argv, quote/dollar/backtick/internal-semicolon data and env value ending ';' | exact R15 new-session args and env order, stdin null; single argv uses -- /usr/bin/env executable; no shell evaluation or readiness/input; malformed successful response fixed R15 failure, no second new-session |
| `E16` | response $1 TAB %2 TAB 1234; failure in each label write | exact sequence verify/schema, verify/instance, verify/seat-id, verify/seat-address, verify/launch, verify/harness, verify/remain-on-exit (14 calls); handle (seat-1,launch-1,demo_impl,$1,%2,1234,9876,false); failure stops subsequent commands and preserves pane/starting |
| `E17` | post-create verification mismatch, immediate dead new pane | mismatched PID/nonempty launch NotFound/"Session was not found."/false before mutation; matching PID and allowed empty launch permit labels; immediate dead pane stays as evidence |
| `E18` | invalid spec; SeatId/LaunchId/Harness containing TAB/LF/CR/NUL; live existing target; malformed listing; foreign dead target; two same-seat/different-seat starts | validator's existing first error unchanged and no mutation; after valid validator result, those named label fields reject TAB/LF/CR/NUL with InvalidArgument/"Launch labels contain a forbidden character."/false before mutation; live target AlreadyRunning/"A live session already exists for SeatId."/false and running launch_id, no kill; malformed listing fixed R18 failure; foreign dead target NotFound; same seat serializes, different seats overlap |
| `E19` | matching managed dead target with status 7, empty signal, exit.reported=false/true; matching starting entry with all handle fields null/no returned handle; registry mismatch; failed publication; stale reverify | PaneExited(7,null) published once before dead removal (none if reported); old registry deleted after successful remove; no false 0 for unknown exit; mismatched registry/stale PID or launch/dead refuses deletion; publication failure fixed R19 error, Reported=false and no removal; retry publishes, then persists Reported=true before removal; matching unreturned starting launch stores exit (7,null) and full old identity in .recovery before verified dead removal, emits no PaneExited and invents no PaneStartTime; new launch succeeds; recovery evidence remains across failed replacement and is removed only after replacement running commit; mismatched starting labels refuse removal |
| `E20` | empty-target start, injected failure/cancellation at registry/create/labels/running-write stages, retry after uncertain start | ordered R20 stages; no subsequent action on failure; retained uncertain entry/pane; locks released, retry refuses an existing live pane; no automatic input/kill/retry; supplied event callback used |
| `E21` | default/true/false attach for demo_impl, invalid name, unavailable client | exactly [tmux,-L,aiakos-test,attach-session,-r,=demo_impl], without -r for false; invalid name NotFound; unavailable throws Unavailable; no process/input/mutation |
| `E22` | ActivityListener/MeterListener and log capture with argv/env/attributes/stdout/stderr sentinels; success/failure/version cases | exact R22 spans/instruments and bounded tags; current outcomes/availability observed; no sentinel values, raw commands or error messages in diagnostics; registry has Attributes but no argv/environment; disposing runner/client components leaves existing harness alive |

| `E23` | child exits without reading 8 MiB stdin; child closes stdin, stays alive briefly, writes stdout/stderr and exits 3 | early-exit child returns Exited with its actual exit code; closed-stdin child returns Exited/3 with exact stdout/stderr and false truncation flags; unwritten stdin is dropped, stdin write/flush/close IOException is treated as early closure without inspecting message/HResult/errno, no stdin IOException or OS text, no child kill/retry; a subsequent request succeeds |

## Analysis decision

context-gap amendment requested by lead 2026-10-05 22:37Z: R23/E23/T9 defines early stdin
closure for S1 after review of #181. Existing R2 caller-cancellation behavior is unchanged;
its inherited-pipe failure is a judgment-gap for the retry, not an added requirement.

context-gap resolved by maintainer decision 2026-10-06 (lead handoff
qitem-20261005215420-064ab307): a dead matching starting launch for which no handle was ever
returned may be replaced without PaneExited. R14 still fails honestly when /proc start time is
unobservable; R19/E19 now allow recovery, retaining old identity/exit evidence in the registry
until verified removal and durable replacement. The non-nullable 11-1 handle is unchanged.
Spec 0004 R13 and Changes after acceptance are amended in this same analysis PR.

The data-suffix escape in R7 follows tmux's argv parser (it removes the single backslash before
an ending semicolon): [tmux 3.4 cmd_parse_from_arguments](https://github.com/tmux/tmux/blob/3.4/cmd-parse.y#L1015).
No caller data is reparsed through a shell.

## Tests

Sentence-style xUnit v3 tests with TestContext.Current.CancellationToken, as in existing Sessions
unit tests. Each permanent T item keeps **all** its listed outputs true after local acceptance
tests are removed. Do not mirror code branches without asserting the caller-visible result.

- T0. In each story's permanent tests, assert G1/G2 for its operations: no inherited sentinel
    credentials or diagnostic payload, no unsolicited pane input/server kill, and cancellation
    has no later mutation. Keep existing ArchitectureTests and 11-1 tests unchanged.
- T1. Commit process-runner tests for E1–E2, plus recording FakeProcessRunner. Use harmless
    child fixtures for stream/drain/timeout/cancellation tests, and a virtual clock for timeout.
    Never launch tmux or touch an owner socket in these tests.
- T9. Commit an early-stdin-closure regression test for E23: exercise both an immediate exit
    without reading a large stdin and closure while the child remains alive, then assert its
    exit code/retained output and successful permit reuse. The latter must fail if the runner
    kills the child instead of allowing its exit 3.
- T2. Commit initialization/client/naming tests for E3–E8. Script outputs and assert complete
    requests/config bytes. No actual tmux needed. Pure version/command/env tests run on every OS.
- T3. Commit filesystem registry tests for E9–E10 with private temp directories and controlled
    flush-before-rename failure. Test entry transitions using direct writes.
- T4. Commit fake-proc/snapshot and orphan exclusion tests for E12–E14. E13's starter sequencing
    assertion is added by the final start story; this story implements/tests the CheckOrphans and RequireStartTime helpers
    consumed there, without launching tmux.
- T5. Commit command-creation tests for E15–E17 with scripted runner and snapshots. Add opt-in
    Linux real-tmux tests [Trait("Category","Tmux")], dynamic skip unless AIAKOS_TEST_TMUX=1 and
    tmux >=3.4. Use Instance=test-<unique guid>, private temp Home/SeatHome/TMUX_TMPDIR. Prove
    single-element argv has no sh -c in its process tree, argv/env are unchanged, malicious user
    .tmux.conf is not loaded, immediate exit leaves a dead pane, and no startup input is sent.
    Test teardown only may kill-server on that unique test socket and removes its temp data.
    No CI edit here: full shared ISessionHost contract tests and the CI switch belong to 11-5.
- T6. Commit lifecycle tests for E18–E20 and integrated ordering assertions for
    E11/E13/E14. Script failures at each boundary, assert no forbidden later commands. Add opt-in
    real-tmux alive refusal/dead replacement and server-hygiene tests using T5 isolation. Check
    previous session remains alive after initializing another component and disposal.
- T7. Commit exact attach-command and validation/availability/no-mutation tests for E21.
- T8. Commit ActivityListener/MeterListener/log-capture tests for E22 over initialization,
    command creation and integrated start success/failure. Observe the existing host remains alive
    across component disposal; use only T5's isolated test socket in real opt-in checks.

## Definition of done

- Acceptance tests pass, earlier tests stay green; `dotnet build -c Release` has 0 warnings/errors.
- `dotnet test --project tests/Aiakos.Node.Tests -c Release` passes; for real start stories also
  run with AIAKOS_TEST_TMUX=1 on Linux tmux >=3.4. No full database suite needed for these node changes.
- All files LF, UTF-8 without BOM, final newline. One local commit per story with subject
  `feat(node): <story title> (#11)`; body records silent choices and says
  "Risks: checks the start half of 0004-RK3; remaining #11 risks stay with delivery, stop, adoption and sandboxing."
- No push/PR by implementers. `tools/story.sh` controls the gate and one retry; no manual replacement.

## Out of scope (do not implement, do not stub)

Delivery/keys/capture, full status/watch/stop/adopt/list facade, signal-based tree termination,
socket recovery, token creation, hook readiness, Claude-specific behavior, gRPC command execution,
SeatActor, node startup/Hello integration, reconciliation, full shared real-host contract suite,
CI changes, ownership marker for seat homes, new packages and changes to specs/ADRs/docs by implementers.
