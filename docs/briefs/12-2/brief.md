---
id: 12-2
title: "#12 slice 2 — relay and ingest"
issue: 12
status: draft
route: impl
paths: [src/Aiakos.Orchestrator/Harnesses/ClaudeCode/, src/Aiakos.Orchestrator/Aiakos.Orchestrator.csproj, tests/Aiakos.Orchestrator.Tests/Harnesses/ClaudeCode/, src/Aiakos.Node/Hooks/, tests/Aiakos.Node.Tests/Hooks/]
date: 2026-10-08
---

# Brief: #12 slice 2 — relay and ingest

Part of #12. Self-contained. **Do not read docs/specs/ or docs/adr/.** Where silent, use the
simplest behavior and record the choice in the commit body. Do not create a missing neighbor.

## Goal and baseline

Supply the embedded POSIX relay and a runnable loopback ingest with authenticated launch
attribution, sequence-loss detection and status coalescing. Spec 0005 R16–R19, R24 transport,
R37/R38 ingest observability and AC4/AC5 are covered here. Main already references ASP.NET Core
in Node, but has no ingest, relay or normalizer. Brief 12-1 accepts caller-supplied relay bytes;
its adapter does not need modification. No existing brief owns this work.

The raw consumer seam below deliberately separates this slice from 12-3 normalization and
12-4 driver composition. Tests supply a recording consumer; production driver registration,
token creation/file writing, NodeProgram wiring and normalized TELEMETRY publication are later
work, not invented defaults. A caller builds the ingest with an explicit port and consumer,
registers launches, starts it and stops it. No harness capability is advertised by this slice.
Risks checked: 0005-RK11 relay dependency behavior and token exposure component of AC11;
node capability checks and real-Claude end-to-end remain 12-4/12-5.

## Files to create or touch (nothing else)

| Path | What |
|---|---|
| src/Aiakos.Orchestrator/Harnesses/ClaudeCode/ | Resources/aiakos-hook-relay and ClaudeCodeRelay.cs |
| src/Aiakos.Orchestrator/Aiakos.Orchestrator.csproj | Embed relay with fixed logical name |
| tests/Aiakos.Orchestrator.Tests/Harnesses/ClaudeCode/ | Relay resource contract tests |
| src/Aiakos.Node/Hooks/ | Launch registry, raw contracts, ingest, sequence tracker, telemetry |
| tests/Aiakos.Node.Tests/Hooks/ | Permanent relay shell and real-socket ingest tests |

No new package, Version on PackageReference, NoWarn, pragma warning disable or SuppressMessage.
Do not reference Orchestrator from Node. Linux shell tests obtain the relay resource by reading
its source path from the repository (no Node-to-Orchestrator ProjectReference).

## Public surface (exact names)

```csharp
// Aiakos.Orchestrator.Harnesses.ClaudeCode
public static class ClaudeCodeRelay
{
    public static Aiakos.Contracts.Node.V1.SeatFile BuildFile();
}
// Aiakos.Node.Hooks; use existing generated contract types, not parallel enums.
public sealed record HookLaunch(string SeatId, string SeatInstanceId, string LaunchId,
    string SeatAddress);
public sealed record IngestedPayload(HookLaunch Launch, string Kind, string Name,
    ulong SourceSeq, DateTimeOffset ReceivedAt, byte[] Body);
public interface IHookIngestConsumer
{
    ValueTask ReceiveAsync(IngestedPayload payload, CancellationToken ct);
    ValueTask GapAsync(HookLaunch launch,
        Aiakos.Contracts.Node.V1.ObservationGap gap, CancellationToken ct);
}
public sealed class HookLaunchRegistry
{
    public HookLaunchRegistry(TimeProvider timeProvider);
    public void Register(string token, HookLaunch launch);
    public void End(string launchId);
    public bool TryResolve(string token, out HookLaunch? launch);
}
public sealed class HookSequenceTracker
{
    public HookSequenceTracker(TimeProvider timeProvider);
    public void Receive(HookLaunch launch, ulong sourceSeq);
    public IReadOnlyList<HookLaunch> TakeExpiredGaps();
}
public sealed class HookIngest : IAsyncDisposable
{
    public HookIngest(int port, HookLaunchRegistry registry,
        IHookIngestConsumer consumer, TimeProvider timeProvider,
        Microsoft.Extensions.Logging.ILoggerFactory loggerFactory);
    public Uri BaseUri { get; } // includes /v1/hooks; available after StartAsync
    public Task StartAsync(CancellationToken ct);
    public Task StopAsync(CancellationToken ct);
}
```

The consumer receives raw full bodies, not truncated JSON or speculative HarnessEvents. Later
12-3/12-4 preserve sequence/launch identity while normalizing. Consumer.GapAsync receives exactly
Reason=IngestUnavailable, unset From/To and DroppedEvents=0. No gRPC send or seat inventory
registration is performed here. Registry and tracker are thread-safe, instance-local services.

## General rules

G1. No hook payload, prompt, token, Authorization header or user config in logs/spans/metrics,
    errors, ToString output of public records or exception inner detail. HookLaunch is safe
    metadata; IngestedPayload.ToString must omit Body. No telemetry middleware that records
    request headers/body/URL query. Do not store tokens in public records. Tests use ordinary
    synthetic credential words, never real/token-shaped secrets; sentinel search covers logs,
    spans and child argv. Returned body/file bytes are independent snapshots of caller inputs.

G2. Preserve earlier results; APIs are additive. Use ordinal identity/header-value comparison,
    invariant numeric formatting and TimeProvider timers, not wall-clock sleeps for unit timing.
    Null top-level arguments throw ArgumentNullException(parameter name). Fixed errors omit
    supplied values. Accept arbitrary body bytes, including empty, invalid JSON, NUL/non-UTF8;
    normalization belongs to 12-3. Shut down owned listener/workers/timers on stop/dispose without
    stopping other node services; cancellation is observed. No external port/process on disposal
    is killed. Every story commits permanent tests of its rules and applicable G1/G2 boundaries.

## Changes to earlier behavior

None. Existing adapter consumes supplied relay files; existing node link is unaffected.

## Rules

R1. Embed UTF-8/noBOM/LF/final-LF script at Resources/aiakos-hook-relay with logical name
    Aiakos.ClaudeCode.HookRelay. BuildFile returns a fresh SeatFile: Root=FileRoot.SeatHome,
    Path="aiakos/bin/aiakos-hook-relay", Mode=0755, Expand=false, Content=embedded bytes.
    No file IO/environment reads at runtime and no settings/projection assembly in this API.

R2. Relay uses /bin/sh, arguments hook|status and name. Read stdin completely and POST its bytes
    unchanged, including trailing newlines, to $AIAKOS_HOOK_URL/$kind/$name. Use curl
    --connect-timeout 0.5 and --max-time 2, --data-binary @-, Content-Type application/json,
    -H @"$AIAKOS_SEAT_TOKEN_FILE", and X-Aiakos-Source-Seq. Never read token content into argv
    or environment. Always exit 0, suppress stderr and hook stdout; status prints exactly
    "aiakos <AIAKOS_SEAT>\n" before posting (unset seat uses "seat"). Missing URL, unreadable
    header or dependencies => same stdout/exit and no decision. Let curl --data-binary @- read the relay stdin directly; never use body command
    substitution or temporary payload files. Drain remaining stdin with cat >/dev/null after
    curl exits and on every no-post path, with errors suppressed. No additional mktemp
    dependency; no on-disk payload even if the relay is killed. Tests provide
    finite stdin; a producer that never closes stdin is not a network-timeout test.

R3. Before POST, increment decimal counter $AIAKOS_SEAT_SEQ_FILE under flock on <file>.lock,
    wait at most 1s. Missing/empty/nondecimal counter seeds Unix microseconds then increments;
    existing valid counter increments without reset. Write counter under same lock. Lock timeout
    or counter IO failure => header 0; absent seq path => 0. Never remove/reset counter or lock
    file during cleanup. 200 concurrent invocations yield distinct increasing sequence values
    when sorted; HTTP arrival order is allowed to differ. Lock and HTTP budgets are sequential:
    under 2.6s network-failure bar applies with uncontended lock, not a simultaneous 1s lock wait.

R4. Registry.Register accepts nonempty ASCII token with no whitespace/control and nonempty
    launch metadata without control; otherwise ArgumentException("INVALID_HOOK_LAUNCH").
    Tokens are unique; collision throws InvalidOperationException("HOOK_TOKEN_IN_USE").
    Re-registering a LaunchId throws InvalidOperationException("HOOK_LAUNCH_IN_USE").
    Check argument validity first, then remove expired token entries, then check token collision,
    then LaunchId collision: a collision on both returns HOOK_TOKEN_IN_USE. Expired token may
    be reused for a never-seen LaunchId; every previously registered LaunchId is remembered
    for this registry lifetime and cannot be reused, even after its token expires.
    End records end time once; current launches resolve, ended launches resolve until strictly
    before end+60s, and at/after boundary fail. Unknown End is a no-op. Expired tokens are removed
    on access; no refresh from requests. Distinct old/new launches of same seat coexist. Identity
    always comes from token registration, never session_id or any other payload field.

R5. HookIngest uses WebApplication.CreateSlimBuilder, Kestrel HTTP/1.1 bound ONLY to
    IPAddress.Loopback, supplied port 0..65535 (0 requests an ephemeral test port), no redirect,
    TLS, wildcard, forwarded-header or authentication fallback. Start exposes actual BaseUri;
    occupied port throws InvalidOperationException("HOOK_INGEST_BIND_FAILED") without
    inner exception or alternate-port retry. Constructor out-of-range port throws
    ArgumentOutOfRangeException(nameof(port)) (framework message need not be matched).
    BaseUri before successful start throws InvalidOperationException("HOOK_INGEST_NOT_STARTED").
    Second StartAsync while started throws InvalidOperationException("HOOK_INGEST_ALREADY_STARTED").
    StopAsync before start is a no-op; after start it cancels workers/closes listener and is
    idempotent. A stopped/disposed ingest cannot restart: StartAsync throws
    InvalidOperationException("HOOK_INGEST_STOPPED"). BaseUri remains its last started value
    after stop. Concurrent lifecycle calls are serialized; start cancellation releases partial
    resources and leaves instance stopped. Only POST /v1/hooks/{kind}/{name}, kind
    exactly hook/status, nonempty single ASCII name [A-Za-z0-9_-]+. Unmatched path/invalid kind
    or name => 404; other methods on matched route => 405. No request-triggered launch creation.
    Require exactly one Authorization value "Bearer <registered token>" (case-sensitive bearer
    text); missing/malformed/multiple/unknown/expired => 401 and no consumer call or seq update.

R6. After authentication, accept <=1048576 body bytes and enqueue full bytes/registration/
    source seq/receive time before 204; >limit => 413, no consumer/seq update. Enforce limit for
    Content-Length and streaming/chunked/unknown length; aborted body never publishes a prefix.
    Invalid/missing/multiple/overflow/nondecimal Source-Seq header => 0; valid decimal ulong
    (including 0) retained. No JSON parsing or session-ID check. Enqueue uses an in-memory
    unbounded channel and background consumer, so a delayed consumer or disconnected orchestrator cannot
    delay 204. Use a single ordered worker for consumer calls; handler never awaits consumer.
    Do not introduce persistent spool or publish dummy normalized events. Consumer errors are
    caught at worker boundary, logged through loggerFactory.CreateLogger("Aiakos.Node.Hooks.HookIngest") at Error
    as fixed "HOOK_CONSUMER_FAILED" without exception detail,
    worker continues; no HTTP retry or duplicate consumption. Stop cancels outstanding calls and
    discards local pending bodies; shutdown persistence is outside this component contract.

R7. Tracker receives every accepted seq before status coalescing, scoped by LaunchId. Seq0 is
    ignored. First positive value establishes baseline, never fabricates loss before it. Higher
    values create missing intervals; late values remove themselves, duplicates neither duplicate
    consumption decisions nor create/reset timers (HTTP raw hook posts themselves are retained).
    Missing interval deadline is 2s after the first higher value exposing it. At deadline emit
    once for each contiguous missing run; a filled run emits none. Reported missing values are
    remembered so late fills/repeated higher values never report that run twice. Use interval
    bookkeeping, not one allocation per missing number; huge jump to ulong.MaxValue is supported.
    TakeExpiredGaps returns one launch entry per newly expired run. Ingest polls with a
    TimeProvider-backed 100ms timer and queues GapAsync in the same worker as raw consumption.
    Clock deadlines, not HTTP arrival sorting, decide expiry; tracker never reorders hooks.
    Scope is intentionally per launch as accepted spec0005 R19 requires. Interleaved old/new
    launches sharing a seat counter can cause an apparent per-launch gap even when all seat
    numbers arrived: do not silently substitute seat-scoped accounting. Document this limitation
    in tests and handoff; a later improvement needs a spec amendment, not an inferred fix.

R8. Hook posts enqueue immediately. Status posts coalesce per SeatId, across launch registrations:
    first status emits immediately; within 1s of last emission retain only the newest arrival.
    Emit pending newest at lastEmission+1s even if no further requests arrive. Continuous status
    traffic emits at most once per second, leading/trailing; 50 posts over [0,2s) emit <=3 after
    trailing flush and last carries newest bytes. The pending record retains its own launch,
    sequence and receive time; do not replace them with current seat's identity. Equal sequence
    values do not deduplicate status data; sequence tracks loss independently. Different seats
    have independent clocks; hook-kind posts are never throttled. "TELEMETRY" mapping/null usage
    conversion is deferred to 12-3; this rule supplies the raw emission bar needed for AC5.

R9. ActivitySource name Aiakos.Node.Hooks; span hook_ingest.receive uses only hook.name, source_seq and aiakos.seat.address for
    authenticated accepted requests. Meter Aiakos.Node.Hooks exposes aiakos.node.hooks.received
    counter with name/result tags (result accepted, unauthorized, too-large), aiakos.node.hooks.gaps counter
    on emitted runs and `aiakos.node.hooks.latency` histogram, unit `ms`, measured from request
    received to enqueued and recorded only for accepted (204) requests. Since relay has no
    emission timestamp, never infer a timestamp from sequence (persisted counter != clock). Every401 (missing, malformed, multiple, unknown or expired Authorization) has metric
    name="unknown"; no arbitrary
    unauthenticated names/IDs. Disable default Kestrel request logging that could capture secrets.
    No trust metrics in this slice.

## Expected outputs: exact text

Base: registered token fixture "ingest-fixture", launch L1, seat S1, instance I1, address
impl@aiakos-dev; body bytes UTF-8 "{\"session_id\":\"untrusted\"}\n\n". IDs in pure fixtures
are opaque; no actor or canonical-ID validator is required. Real-socket tests use 127.0.0.1:0.

| ID | Change | Expected |
|---|---|---|
| `E1` | two BuildFile calls | exact embedded bytes; SeatHome, aiakos/bin/aiakos-hook-relay, decimal mode493, Expand=false; independent snapshots with generated FileRoot.SeatHome, Path and Content names |
| `E2` | finite stdin trailing LF/nonUTF8; ingest down/hangs/500; missing env/dependency | exact posted bytes when reachable, Authorization only from header file, exit0; hook stdout empty, status exactly aiakos impl@aiakos-dev LF; uncontended-lock failures <2.6s |
| `E3` | missing counter, 200 calls, persisted counter, held lock | seed above microsecond time sampled before call; sorted 200 seqs strictly increasing; next launch increments persisted value; lock timeout header0 and retained files |
| `E4` | unknown token, end at t0, another launch same seat | unknown false; L1 resolves before t0+60s, false at boundary; new L2 unaffected; both-collision HOOK_TOKEN_IN_USE; expired token/new LaunchId accepted, previously seen LaunchId HOOK_LAUNCH_IN_USE; fixed invalid errors R4 |
| `E5` | hook-kind posts for multi-post cases, status at most once per seat; wrong route/method/token; payload tries alternate identity; lifecycle | exact 404/405/401 as R5; no callbacks for rejected posts; accepted registration remains S1/I1/L1 despite session_id; port invalid ArgumentOutOfRangeException(port), occupied HOOK_INGEST_BIND_FAILED, pre-start BaseUri HOOK_INGEST_NOT_STARTED, repeated start HOOK_INGEST_ALREADY_STARTED, stop before start/repeated stop no-op, restart HOOK_INGEST_STOPPED, port released after stop |
| `E6` | hook-kind posts for multi-post cases, status at most once per seat; exactly1MiB/1MiB+1/chunked/abort; bad seq; blocked consumer | 204/413; no prefix published; malformed seq0; full accepted bytes independent; p99 response <50ms at20req/s over100 requests with consumer blocked |
| `E7` | 1,2,4 then silence; late3; two runs; zero/duplicates/huge jump | one IngestUnavailable at2s after4, none before; late3 before deadline yields0; two contiguous missing runs yield2; seq0/duplicates no extra gap; launch baselines independent; L1(seq10 at0s), L2(seq11/12 at0.1s), L1(seq13 at0.2s) gives exactly one L1 gap at2.2s for11..12, no L2 gap (accepted per-launch scope limitation) |
| `E8` | 50 status posts in2s; concurrent seat; hook alongside | <=3 raw status calls after trailing flush, final newest bytes with original launch metadata; other seat independently emits; every hook delivered |
| `E9` | sentinel payload/header; accepted/401/413/gap requests | no sentinel in logs/spans/metrics/child argv; named fixed telemetry R9 with exact result tags; `aiakos.node.hooks.latency` histogram unit `ms`, elapsed time from request received to enqueued, recorded only for accepted (204) requests; no latency record for 401/413 |

## Tests

Use xUnitv3 like existing Node tests. Timing state tests use fake TimeProvider; socket and shell
tests use real loopback and Linux sh/curl/flock, skipped on non-Linux. Do not launch Claude/tmux.
Shell suite resolves relay source relative to solution root; resource contract tests are in
Orchestrator.Tests. No Node reference to Orchestrator and no acceptance tests committed.

- T1. Permanent embedded-resource/path/mode/byte/snapshot tests E1 and shell tests E2/E3,
    including /proc child cmdline sentinel scan while listener delays response, counter reuse,
    missing tools and absence of payload files (including after forced relay kill). Never print sentinel or raw argv on failure.
- T2. Permanent registry tests E4 and concurrent resolution/end/expiry, all G1 input boundaries.
- T3. Permanent HTTP tests E5/E6 with real TCP, chunked limit/abort, recorded consumer, delayed
    consumer, exact byte preservation and full lifecycle cancellation/port-binding checks.
- T4. Permanent tracker tests E7: explicit t=0,1.999s,2s clock boundaries, late fills after report,
    separate launches, intervals splitting/merging, max ulong and duplicates.
- T5. Permanent coalescing tests E8 with raw recording consumer and clock; prove newest final
    body survives without another post and every accepted seq participates in gap tracking.
- T6. Permanent telemetry/no-secret tests E9; latency/load timing uses real socket and reports
    distribution (no live orchestrator prerequisite). Existing earlier Node tests stay green.

## Definition of done

Release build at root: 0 warnings/errors; affected Node/Orchestrator tests and acceptance pass,
earlier tests stay green. LF/UTF8/noBOM/final LF. One local commit feat(claude): <story title>
(#12), body names silent choices and checked risks. No push/PR. Follow story.sh gate/retry.

## Out of scope (do not implement, do not stub)

12-1 settings/launch builders;12-3 JSON normalizer/tool pairing/usage;12-4 driver, trust, token
creation/writing, dependency capability checks and production composition;12-5 live Claude/E2E;
NodeProgram/gRPC/buffer integrations, actor/lifecycle inventory, personal user files, guidance,
CI, new packages/protos and native Aiakos.HookRelay project. No no-op default consumer.
