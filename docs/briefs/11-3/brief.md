---
id: 11-3
title: "#11 slice 3 — tmux delivery, keys and capture"
issue: 11
status: draft
route: impl
paths: [src/Aiakos.Node/Sessions/, tests/Aiakos.Node.Testing/, tests/Aiakos.Node.Tests/Sessions/]
date: 2026-10-08
---

# Brief: #11 slice 3 — tmux delivery, keys and capture

Part of #11. Self-contained. Do not read specs or ADRs to fill in requirements.
Where silent, use the simplest behavior and record the choice in the commit body.

## Goal and overlap

Implement the real tmux input/capture component for spec 0004 R14–R24, with reusable
cancellation for 11-4's stop operation. 11-1 already owns InputValidator, ISessionHost records,
FakeSessionHost and its contract tests. 11-2 already owns ProcessRunner/TmuxClient initialization,
registry, snapshots, start and attach. Reuse those contracts; do not reimplement them.
The 11-2 start contracts are prerequisites for real tests even where code is not merged yet;
readiness of those stories is a router gate, not permission to create substitutes.

The missing component is TmuxSessionInput, not a partial ISessionHost implementation. 11-4
owns status/watch/stop; 11-5 owns the composing host, reconciliation/adoption and node wiring.
The public component below lets those later slices delegate delivery/keys/capture and cancel
an active delivery before terminating its process. No background input or readiness detection.

This slice checks 0004-RK1 (1 MiB byte-exact delivery) and the delivery half of 0004-RK3
(tmux argument boundaries, including a lead ending in semicolon). It does not close RK2 socket
recovery, RK4 watcher signals, RK8 process trees, 0003-RK1 or 0005-RK14 isolation.

## Files to create or touch (nothing else)

| Path | What |
|---|---|
| src/Aiakos.Node/Sessions/ | Input/capture component and helpers; bounded tail stdout extension to ProcessRunner/TmuxClient; DeliveryReport optional resubmit reason |
| tests/Aiakos.Node.Testing/ | FakeProcessRunner snapshot of the new request option; no replacement fake-host semantics |
| tests/Aiakos.Node.Tests/Sessions/ | Permanent scripted unit tests and isolated opt-in tmux delivery/capture tests |

No new packages, reflection in src, warning suppression, project changes or CI edits.
All original overloads/signatures and existing tests remain valid. Null reference arguments use
ArgumentNullException. Unix-specific tests skip on other OSes; argv/parsing tests run everywhere.

## Public surface (exact names)

Namespace Aiakos.Node.Sessions; reuse all existing ISessionHost records and enums.

```csharp
// Add an optional final positional parameter to the existing record:
public sealed record ProcessRequest(string FileName, IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string,string> Environment, ReadOnlyMemory<byte>? Stdin,
    TimeSpan Timeout, bool RetainStdoutTail = false);
// Existing TmuxClient.RunAsync remains unchanged; add:
public Task<ProcessResult> RunCaptureAsync(IReadOnlyList<string> command, CancellationToken ct);
// Add optional final parameter to existing DeliveryReport:
// string? ResubmitReason = null
public sealed class TmuxSessionInput
{
    public TmuxSessionInput(TmuxClient client, IProcessSnapshot processes,
        Microsoft.Extensions.Logging.ILogger<TmuxSessionInput> logger,
        System.Diagnostics.Metrics.IMeterFactory meterFactory, TimeProvider? timeProvider = null);
    public Task<DeliveryReport> DeliverAsync(SessionHandle session, DeliveryRequest request, CancellationToken ct);
    public Task SendKeysAsync(SessionHandle session, IReadOnlyList<NamedKey> keys, CancellationToken ct);
    public Task<PaneSnapshot> CaptureAsync(SessionHandle session, CaptureRequest request, CancellationToken ct);
    public void CancelDelivery(SessionHandle session);
    internal Task VerifyMutableHandleAsync(SessionHandle session, CancellationToken ct);
}
```

The optional report property preserves old constructor calls and records the driver's accepted
resubmit reason in the returned report, rather than putting arbitrary driver text into logs.
One component instance is shared by the future composing host. It holds no registry, watcher,
process ownership or background task. Existing fake host reports may leave the new property null.

## General rules

G1. No automatic input, retries, repairs or fallback sessions. Tests and cleanup use only unique
    aiakos-test-<guid> sockets, disposable temp homes and fake harnesses; never touch authenticated
    owner tmux, native harness config, real instance homes or Claude. Do not run tests against a
    development build's live team. Real tests use Trait("Category","Tmux") and skip unless Linux,
    AIAKOS_TEST_TMUX=1 and tmux >=3.4; no global environment mutation across parallel tests.
G2. No lead/body/captured text, environment values, driver reason, exception text or raw stderr
    in logs/spans/metrics/registry. Errors may carry existing classified metadata to the caller,
    but diagnostic tags contain only the listed safe fields. No new persisted input/capture state.

## Changes to earlier behavior

C1. ProcessRunner's default first-MiB stdout retention remains unchanged. Capture explicitly
    opts into last-MiB retention so large output cannot discard the newest screen. Stderr still
    retains its first MiB. Existing runner tests keep their expected results. FakeProcessRunner
    must copy RetainStdoutTail when snapshotting a ProcessRequest.
C2. DeliveryReport gains optional ResubmitReason; all existing construction remains source
    compatible. Only TmuxSessionInput's accepted resubmit sets it; no requirement to change fake
    host behavior or old expected results.

## Rules

R1. Tail retention. When RetainStdoutTail=true, ProcessRunner concurrently drains through EOF as before, retaining the last at most 1048576 stdout bytes in bounded memory, with StdoutTruncated true exactly when more bytes were read. It does not stop draining on overflow. Timeouts/cancellation preserve existing outcomes and retained tail; stderr behavior is unchanged. TmuxClient.RunCaptureAsync accepts only a command beginning capture-pane, otherwise ArgumentException("A capture-pane command is required."); it uses the existing sanitized environment/private prefix/timeouts with null stdin and RetainStdoutTail=true. Other RunAsync requests use false.

R2. Handle boundary. Require client Available first, otherwise throw its existing Unavailable error. Validate pane ID ^%[0-9]+$, positive PanePid, nonempty LaunchId and nonzero PaneStartTime, otherwise NotFound/"Session was not found."/false without a tmux call. Before every pane-mutating invocation verify with ["display-message","-p","-t",paneId,"#{pane_pid}\t#{@aiakos-launch}\t#{pane_dead}"], parsing exactly three tab-separated fields, PID and LaunchId equal to handle, dead=0. Verify matching PID/start time in the same-user IProcessSnapshot; a missing/reused PID is NotFound. Reject ReadOnly for delivery and keys with the same NotFound error before tmux. Capture may use ReadOnly/dead handles and verifies PID/launch via display-message but need not find a dead process in /proc. Never target a pane by seat/session name. The exact internal VerifyMutableHandleAsync helper performs the mutation boundary checks above without a mutation; it is directly testable through the existing friend test assembly in S1 and reused by subsequent input callers.

R3. Capture validation and metadata. HistoryLines must be 0..10000, MaxBytes 1..1048576; otherwise throw InvalidArgument/"Invalid capture request."/false before tmux. Run metadata display-message first with format "#{pane_pid}\t#{@aiakos-launch}\t#{pane_width}\t#{pane_height}\t#{cursor_x}\t#{cursor_y}\t#{pane_dead}". Require PID/launch match, positive size, cursor x in 0..width inclusive (pending wrap), y in 0..height-1, and dead 0 or 1; missing/malformed/identity mismatch is NotFound/"Session was not found."/false. No input gate, registry write, process signal or pane mutation. Capture time is TimeProvider.GetUtcNow after successful capture.

R4. Capture bytes. RunCaptureAsync ["capture-pane","-p","-t",paneId] when history=0, otherwise append ["-S","-"+HistoryLines invariant decimal]. Never -e or -J. Decode stdout with UTF-8 replacement, split LF, remove C0 except TAB and remove DEL/C1; trim trailing whitespace on each line and drop trailing empty lines. Preserve wrapped lines. On tail truncation discard the first partial line through its LF before decoding (if no LF, no text); propagate Truncated. Return text joined by LF without a final LF, Lines equal to remaining line count, Size/Cursor/PaneDead from metadata. Classified tmux failure throws SessionHostException without including captured text.

R5. Capture cap. The last Size.Rows physical output lines before normalization form the protected visible screen, including blank lines. Remove oldest history lines until UTF-8 bytes of normalized text <=MaxBytes; never remove a protected visible line. If visible text alone exceeds MaxBytes, return it whole and Truncated=true (visible-screen priority, matching 11-1). Any omitted requested history due to runner truncation or cap sets Truncated. A capture with empty output returns Text="", Lines=0. Capture remains callable while confirmation holds the input gate.

R6. One input gate per pane/launch, shared by DeliverAsync and SendKeysAsync, acquired nonblocking after pure request validation and before any tmux/process call. A second operation throws Busy/"A delivery is already in progress for this session."/true, with no calls. Distinct pane/launch pairs may run concurrently. Hold the gate until confirmation and cleanup finish; release in finally on every exit. CancelDelivery marks only that identity's current delivery cancelled, does not acquire the gate, send input or make tmux calls, and is a no-op when no delivery is active. Capture takes no gate.

R7. Delivery request validation. Before any tmux call use existing InputValidator.CheckLead/CheckBody; preserve its exact messages and normalized bytes. Lead/body NotAllowed -> InputNotAllowed with Reason and false; body TooLarge -> PayloadTooLarge with Reason and false; both normalized body and lead empty -> InputNotAllowed/"Lead and body cannot both be empty."/false. Undefined Submit, negative SubmitDelay, or delay >4294967294 milliseconds -> InvalidArgument/"Invalid delivery request."/false. Valid TAB bytes are unchanged. Generate DeliveryId as Guid N; buffer name aiakos-<first eight DeliveryId chars>-1, unique per delivery. BodyBytes reports normalized UTF-8 bytes.

R8. Delivery commands, in order. After gate admission and before any buffer load, call VerifyMutableHandleAsync(session, CancellationToken.None) once; a stale/missing/reused/dead/read-only handle throws NotFound/"Session was not found."/false, with no load or cleanup. This bounded initial verification runs even when the linked delivery token is already cancelled. Then load body via ["load-buffer","-b",buffer,"-"] with exact normalized UTF-8 stdin; completed load sets BufferLoaded, even for empty body. The initial bounded buffer load is attempted regardless of an already-cancelled caller/CancelDelivery token; check cancellation immediately after it and at every subsequent boundary, before any pane input. If lead nonempty, verified ["send-keys","-t",paneId,"-l","--",lead] then LeadTyped; if empty skip without promoting that stage. If body nonempty, verified ["paste-buffer","-p","-r","-d","-b",buffer,"-t",paneId] then BodyPasted, exactly one paste; if empty skip. Wait SubmitDelay via TimeProvider, then for CtrlM verified ["send-keys","-t",paneId,"C-m"] then Submitted and SubmittedAt=clock now. Submit None skips submit and leaves last completed stage. The existing TmuxClient escapes a trailing semicolon exactly once; do not escape it again. Existing runner already writes stdin in <=65536 byte chunks.

R9. Failure reports and cleanup. Pure validation/Busy failures and the initial post-gate VerifyMutableHandleAsync identity failure throw SessionHostException before any buffer load. Initial identity failure makes no load/delete-buffer call and releases the gate. A classified load failure also throws. Once BufferLoaded, any tmux/identity failure returns DeliveryReport with last successful stage, Unconfirmed unless confirmer returned, accepted resubmit count/reason, body size/normalization and error; force Retryable=false. Never send cleanup keys or repeat a failed command. Finally attempt ["delete-buffer","-b",buffer] if load succeeded but paste did not remove it, with CancellationToken.None; cleanup errors never mask the primary report/exception, and no-buffer is harmless. Successful delivery Error=null and default confirmation NotRequested without a confirmer. Command stages advance only on exit0.

R10. Cancellation preserves the existing 11-1 contract: after ordinary request/availability/handle validation, gate admission and successful initial VerifyMutableHandleAsync with CancellationToken.None (R8), attempt the initial bounded buffer load even if caller cancellation was already requested. On successful load report BufferLoaded, Error TmuxFailed/"Delivery was cancelled."/false, Unconfirmed and no pane input, then cleanup the buffer. CancelDelivery arriving after gate acquisition but before/during load has the same result after successful load; a classified load failure still throws its real error rather than inventing BufferLoaded. After later successful stages, caller/CancelDelivery cancellation returns the last completed stage with that cancellation error and Unconfirmed unless already confirmed. Do not cancel a tmux invocation midway: use CancellationToken.None for the bounded client invocation, check the linked delivery token after load and before/after every later invocation, keeping the completed stage. SubmitDelay and confirmer receive the linked token and their waits must use WaitAsync(linked token) so cancellation returns promptly even if a callback ignores it. Buffer cleanup still runs. No stop process/registry work in CancelDelivery.

R11. Confirmation context. After optional submit, call Confirmer exactly once with delivery ID, capture delegated to this component and SubmittedAt (TimeProvider time at submit, or time entering confirmation for Submit None). No confirmer -> NotRequested/null TurnId. Preserve the returned Confirmation, including TurnId. Context is valid only during that callback. ResubmitAsync requires CtrlM, a completed submit and active context, otherwise InvalidOperationException("Resubmit is not available."). At most one accepted call, including concurrent calls; second throws InvalidOperationException("Only one resubmit is allowed.") without tmux. Validate reason nonempty, otherwise ArgumentException("A resubmit reason is required."); verify identity and send ["send-keys","-t",paneId,"C-m"]. On success increment Resubmits and record exact reason in ResubmitReason; a failed attempt consumes the one-attempt allowance but does not count as a completed resubmit. On identity mismatch or classified tmux failure, ResubmitAsync throws SessionHostException carrying that classified code/message with Retryable=false; record the same error in the delivery state, so even if the confirmer catches it and returns Confirmed the report remains Unconfirmed with that error. No separate gate acquisition. No automatic retry on Unconfirmed. OperationCanceledException while the linked delivery token is not cancelled is treated as a confirmer failure, never caller cancellation. Any other non-cancellation exception from confirmer becomes TmuxFailed/"Delivery confirmation failed."/false report; never echo exception text.

R12. Named keys. Snapshot the supplied list; count >32 or undefined enum throws InvalidArgument/"Invalid named keys."/false before tmux. Empty list is a no-op after availability/syntactic-handle/read-only validation; it takes no gate and makes no process/tmux call, even if delivery holds the gate. Under shared gate verify identity, then one invocation ["send-keys","-t",paneId,<keys...>] mapping Enter/Escape/Tab/Up/Down/Left/Right/CtrlC/CtrlD to Enter/Escape/Tab/Up/Down/Left/Right/C-c/C-d. No free text, key syntax or automatic Enter. Classify failed invocation via existing TmuxClient.GetError and throw. Caller cancellation before invocation throws; never leave the gate held.

R13. Diagnostics. Reuse NodeTelemetry.ActivitySource and Meter Aiakos.Node through IMeterFactory. Spans session_host.deliver/session_host.send_keys/session_host.capture include aiakos.launch_id, tmux.session, tmux.pane_id and aiakos.seat.address reconstructed from validated SessionName when possible. Deliver tags delivery.id, lead.bytes, body.bytes, body.sha256_prefix (first eight lowercase SHA256 hex of normalized body), delivery.stage, delivery.confirmation, delivery.resubmits; keys tag fixed enum names only; capture tags capture.history_lines, capture.bytes, capture.truncated. Counter aiakos.node.session_host.deliveries has stage/confirmation tags once per returned report; histogram aiakos.node.session_host.delivery.bytes records BodyBytes once per report. Reuse existing child tmux spans/counters, no duplicate instrumentation and no payload/error-text tags.

R14. Real-tmux evidence. Add permanent tests using the 11-2 starter, client and runner with a temp instance, explicit test environment and disposable executable harness. The harness stty raw -echo, enables bracketed paste, writes a readiness marker, records raw bytes to a temp file, and may emit scripted output; wait for readiness by marker without sending input. Test four bodies: one line, quotes/backticks/literal $HOME/literal backslash-n/backslashes/Greek/✓/TAB/blank line, 400 lines, exactly 1048576 ASCII bytes. Expected file equals UTF8(lead)+ESC[200~+body+ESC[201~+CR. Also lead ending semicolon is exact. Invalid ESC body/newline lead yield InputNotAllowed and zero input bytes. Do not lower the limit without a maintainer decision; report measured failure if 1 MiB cannot pass.

R15. Real capture/concurrency evidence. Print 500 numbered ANSI-coloured lines with a short final screen; CaptureAsync(500) has no ESC, with MaxBytes=4096 retains the whole final visible screen, excludes oldest lines and Truncated=true. With a waiting confirmer, a second delivery and keys throw Busy without mutations, capture completes, first ResubmitAsync produces one extra CR and second fails. CancelDelivery while confirmer waits returns Submitted and releases gate without a key. Keep tests bounded with explicit cancellation/deadlines and cleanup only their private socket. Full real ISessionHost contract suite, watcher/stop and CI enablement wait for the composing host in 11-5; this slice tests the component, not stubs for absent members.

## Expected outputs: exact values

| ID | Expected |
|---|---|
| `E1` | Runner emits 1048576 A bytes followed by END: default retains A bytes; tail mode ends END, both flag truncated. FakeProcessRunner snapshots true; RunCaptureAsync sets it only on capture-pane. |
| `E2` | Direct VerifyMutableHandleAsync: PID/label/start-time mismatch or read-only handle -> NotFound; helper itself never mutates. Delivery uses this helper once after gate admission and before load-buffer, then again before lead/paste/submit/resubmit; keys verify before their mutation. |
| `E3` | HistoryLines=-1/10001 or MaxBytes=0/1048577 -> InvalidArgument, zero tmux calls; malformed metadata -> NotFound. Cursor x=width/y=height-1 is valid pending wrap; x>width or y>=height -> NotFound. Dead/read-only capture succeeds. |
| `E4` | Bytes for "one  \n\x1btwo\t \n\xff\n\n" -> Text="one\ntwo\n�", Lines=3; capture command has -p, no -e/-J. Tail first partial line is dropped. |
| `E5` | MaxBytes=4, size rows=2, physical lines old/A/B -> Text="A\nB", Truncated=true; rows=2 with visible abc/def and MaxBytes=1 -> Text="abc\ndef", Truncated=true. |
| `E6` | Held gate -> competing delivery Busy before tmux/process calls; capture works. Other pane can deliver. CancelDelivery touches no tmux call. |
| `E7` | ESC or unpaired surrogate -> InputNotAllowed; normalized CRLF/CR -> LF and LineEndingsNormalized=true; >1MiB -> PayloadTooLarge; exact limits accepted; undefined submit/delay invalid -> InvalidArgument. |
| `E8` | Nonempty lead/body CtrlM -> initial verify, load(stdin), verify+lead, verify+paste, delay, verify+C-m; report Submitted. Empty lead skips lead; empty body skips paste; Submit None sends no C-m. |
| `E9` | Stale handle (missing pane, changed label, mismatched/reused PID/start time or dead pane) -> initial verification throws NotFound/"Session was not found."/false, no load/delete-buffer/pane input, gate released; repeat with already-cancelled caller and get the same NotFound. Injected load failure after successful initial verification throws. Fail lead/paste/submit -> report BufferLoaded/LeadTyped/BodyPasted respectively, Retryable=false; no later input. Cleanup only retained buffer; no cleanup keys. |
| `E10` | Valid handle: already-cancelled caller or CancelDelivery after gate/before load -> initial verification succeeds before load, then BufferLoaded after successful load, zero pane input; load failure throws actual error. Cancel after successful load/lead/paste/submit -> respective completed stage and TmuxFailed Delivery was cancelled.; cancellation during invocation waits for that invocation boundary, does not kill it. |
| `E11` | Confirmer returns Confirmed/t1 -> same result; first resubmit success -> Resubmits=1 and exact ResubmitReason; second attempt has no call. Submit None/context expired rejects resubmit; failed resubmit throws classified SessionHostException and yields report Unconfirmed/error even if caught; uncancelled-token callback OperationCanceledException -> TmuxFailed/Delivery confirmation failed., as any other callback failure. |
| `E12` | Enter,Up,CtrlC -> one send-keys argument list ending Enter,Up,C-c; 33 keys or enum99 -> InvalidArgument/no tmux. Empty list -> no gate/no calls even while busy; nonempty competing keys -> Busy/no calls. |
| `E13` | Listeners see specified names/safe tags/counts, but sentinel lead/body/capture/reason/environment/exception text appears nowhere in diagnostic values or registry. |
| `E14` | Private real pane records exact bracketed-paste framing for all four bodies, including 1MiB and trailing semicolon; invalid content records zero bytes. |
| `E15` | Opt-in real tests demonstrate sanitized newest-screen capture, Busy, exactly one resubmit, and stage-preserving cancellation; otherwise report skipped. |

## Tests

Scripted tests belong in tests/Aiakos.Node.Tests/Sessions. Reuse FakeProcessRunner, the existing
InputValidator tests and starter fixture patterns. Do not read author acceptance trials.

T1. Commit runner/client tail-retention unit tests including output larger than two MiB, multibyte split, timeout and EOF drain; unchanged default retention and snapshot flag.

T2. Commit scripted capture/identity tests with FakeProcessRunner and fake process snapshots, malformed/truncated output, dead/read-only pane, UTF-8 replacement/control trimming, history/cap boundaries and oversize-visible policy.

T3. Commit exact argv/stdin/stage tests, initial stale-handle NotFound/no-load/gate-release tests (also with already-cancelled caller), and injected failure/cancellation at each step; all rejected input has zero tmux calls, competing input zero calls, cleanup failures do not replace primary errors. Use deterministic barriers/TimeProvider, never timing sleeps for unit races.

T4. Commit confirmation/key tests for outcome/turn ID, callback failure, lifetime, Submit None, accepted reason, concurrent resubmit limit, identity change on resubmit, bounds and gate release.

T5. Commit logger/ActivityListener/MeterListener sentinel exclusion tests with positive assertions that expected spans/measurements were observed; include a failed delivery and capture so absence is not vacuous.

T6. Commit isolated opt-in real tmux tests covering all prescribed cases. Reuse starter contracts only after predecessor stories merge; no real harness login or owner socket. Record actual tmux version/OS in test output; do not assert Windows/WSL/manual-demo coverage from a Linux run.

T0. Every story commits permanent tests for its rules. Test diagnostics/teardown cannot contain secrets or address owner state. Build/test existing runner/fake contract tests unchanged.

## Definition of done

- dotnet build -c Release: zero warnings/errors; dotnet test green for all earlier tests.
- Commit only the story's required implementation and permanent tests; never push/open a PR.
- Files LF, UTF-8 without BOM, final newline. Conventional subject with story ID and #11.
- Commit body lists silent choices, tested risk IDs, actual opt-in/version evidence and unrun
  environments. After three failures of the same test stop and report, never widen the scope.

## Out of scope

No full ISessionHost facade, DI/node command registration, harness driver/readiness hooks, queue,
registry changes, status/watch/stop/process signalling, reconciliation/adoption, live Claude,
Windows programs, CI or manual demos. The default runner and fake host contracts are preserved.
