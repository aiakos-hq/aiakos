---
id: 15-4
title: "#15 slice 4 — released instance configuration and host lifecycle"
issue: 15
status: draft
route: impl
paths: [src/Aiakos.Cli/, src/Aiakos.Wsl/, src/Aiakos.Hosting.Wsl/, src/Aiakos.Orchestrator/, tests/Aiakos.Cli.Tests/, tests/Aiakos.Wsl.Tests/, tests/Aiakos.AppHost.Tests/, tests/Aiakos.Orchestrator.Tests/, Directory.Packages.props, Aiakos.slnx]
date: 2026-10-07
---

# Brief: #15 slice 4 — released instance configuration and host lifecycle

Part of #15, spec 0007 R6–R21 and ADR0034. Self-contained: implementers do not read specs or
ADRs. Where silent, choose the smallest private implementation and record it in the commit.
Do not add public commands, fallback discovery, seat reconciliation or automatic seat starts.

## Goal and prerequisites

A released Windows instance can initialize isolated configuration, copy its installed runtime,
run the orchestrator in-process, hold and supervise its WSL node, and shut down deliberately.
Pure configuration/runtime/supervisor stories do not depend on API or actors. The final wiring
story requires merged 15-1 S5, 15-2 API hosting/authentication/read stories, and the production
node-link connection registry. Missing dependencies stop that story, never justify substitutes.
The self-contained linux-x64 node payload is supplied by 15-5; live install tests use an owned
fixture payload until packaging merges, and report the live packaged start as pending.

Main base 8d06d44 contains InstanceDefaults, node locking, WSL preflight, node link registry,
SeatQueries, and service defaults. It does not contain shared orchestrator composition or an
Aspire-free WSL library: both are explicit changes below. 15-1 still owns local CLI parsing
and dry run. 15-3 owns the HTTP client and live seat commands. No story here implements them.

Risks: checks configuration separation, runtime replacement, supervision, shared composition and
secret ACLs (0007-RK4/RK5/RK6/RK8). Windows process survival and WSL keepalive (0007-RK2/RK3,
0004-RK5) require the owned Windows demo below; Linux fakes do not close those risks.
Package size, NuGet publishing, seat recovery, delivery and guidance risks remain open.

## Files

| Path | Purpose |
|---|---|
| src/Aiakos.Cli/ | Instances namespace configuration, secrets, runtime, supervisor, host and CLI wiring |
| tests/Aiakos.Cli.Tests/ | xUnit v3 permanent tests; owned temporary instance roots |
| src/Aiakos.Wsl/, tests/Aiakos.Wsl.Tests/ | Aspire-free shared WSL helper library and tests |
| src/Aiakos.Hosting.Wsl/, tests/Aiakos.AppHost.Tests/ | delegate existing preflight/environment/install helpers |
| src/Aiakos.Orchestrator/, tests/Aiakos.Orchestrator.Tests/ | shared composition and host-only admin lifecycle routes |
| Directory.Packages.props, Aiakos.slnx | central packages and project registration |

No PackageReference Version, NoWarn, pragma warning disable or SuppressMessage. No node code,
AppHost executable rewrite, database schema, migrations, actor changes, CI or release publishing.
Tests never initialize an owner home, kill an owner process, update global tools or remove volumes.

## Public surface

Types below are in Aiakos.Cli.Instances, unless another namespace is stated. Immutable public
collections are snapshots. Secret-bearing objects are classes with redacted ToString, never records.
Each story creates only its owned types; no stubs for a later story.

```csharp
public sealed record WslConfiguration(string Distro, string Home, string NodeId);
public sealed record DatabaseConfiguration(string Mode, string? Image, string? ConnectionStringFile);
public sealed record TelemetryConfiguration(bool Dashboard, string? OtlpEndpoint);
public sealed record InstanceConfiguration(string Instance, int PortBase, string Operator,
    WslConfiguration Wsl, DatabaseConfiguration Database, TelemetryConfiguration Telemetry);
public sealed record InstancePaths(string WindowsHome, string WslHome, string Container, string Volume);
public static class InstanceLayout {
    public static InstancePaths Resolve(string userProfile, string instance);
    public static string? Validate(InstanceConfiguration configuration,
        IReadOnlyList<InstanceConfiguration> initialized);
}
public static class InstanceConfigurationJson {
    public static string Serialize(InstanceConfiguration configuration);
    public static InstanceConfiguration Deserialize(string json);
}
public sealed class InstanceInitializer {
    public Task<bool> InitializeAsync(string userProfile, InstanceConfiguration configuration,
        bool force, CancellationToken cancellationToken);
}
public sealed class RuntimeCopy {
    public Task<string> EnsureAsync(string sourceDirectory, string windowsHome, string version,
        string? runningVersion, CancellationToken cancellationToken);
}
public sealed record NodeRestartDecision(bool Retry, TimeSpan Delay);
public static class NodeRestartPolicy {
    public static NodeRestartDecision Decide(int exitCode, int consecutiveFailures, TimeSpan uptime);
}
public interface IOwnedNodeProcess : IAsyncDisposable {
    int Id { get; }
    Task<int> WaitForExitAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
public interface INodeProcessFactory {
    Task<IOwnedNodeProcess> StartAsync(CancellationToken cancellationToken);
}
public sealed class NodeSupervisor(INodeProcessFactory factory, TimeProvider timeProvider) {
    public int? LastExitCode { get; }
    public Task RunAsync(CancellationToken cancellationToken);
}
```

Host composition is in Aiakos.Orchestrator:
`OrchestratorComposition.AddAiakosOrchestrator(WebApplicationBuilder)` returns that builder;
`MapAiakosOrchestrator(WebApplication)` returns that application. Existing Program remains
public for WebApplicationFactory and calls the two methods before RunAsync.

Shared WSL helpers retain existing public preflight/runner/config result signatures but move
implementations into Aiakos.Wsl. Hosting.Wsl retains compatible forwarding facades where its
existing callers require them. New public `WslEnvironment.Compose(string? existing,
IReadOnlyDictionary<string,string> environment)` returns the colon-separated WSLENV value.
`NodeInstall.Script` is a fixed shell script accepting source Windows directory, relative WSL
home and version as positional arguments; no interpolated shell text.

## General rules

G1. No secret value, connection string, node environment or subprocess output is printed in a
    CLI-generated error, serialized status, exception message or structured logging property.
    Read secrets only into process memory/environment or user-only files. Never include them in
    argv. Null public arguments throw ArgumentNullException; anticipated IO/process/configuration
    failures at the command boundary use fixed errors below. Cancellation stops owned work only.
G2. Non-Windows instance init/start/stop/run returns exit2 and stderr
    `Instance management requires Windows.\n` before IO or process launch. Instance status is
    a read command and is not rejected by this platform guard. No tests invoke live Windows/WSL
    operations by default. Permanent tests use temporary directories, fake processes and fake time.

## Changes to earlier behavior

C1. Extract orchestrator service registration, endpoint policy, telemetry, migration ordering,
    options resolution and route mapping from Program into the shared composition methods.
    Preserve every existing registration, health endpoint, startup validation and migration-first
    behavior; tests relying on public Program continue working. API registrations from merged
    15-2 are included, rather than a parallel host-only API implementation.
C2. Extract WSL preflight/runner/config/environment logic into Aiakos.Wsl without Aspire references.
    Hosting.Wsl uses it and keeps existing public caller behavior and diagnostics. Register library
    and Wsl.Tests in solution; reuse existing WSL tests rather than delete their assertions.
C3. The final wiring replaces only 15-1 R7's not-implemented outcome for instance init/start/run/
    stop/status. All other CLI behavior remains owned by 15-1/15-3. CLI gains references to Core,
    Wsl and Orchestrator; never reference Node or AppHost. Final command integration waits on
    merged 15-1 S5 and 15-2 API stories, not a handwritten alternate parser.

## Rules

R1. InstanceLayout.Resolve validates `[a-z][a-z0-9-]{0,62}` before joining paths. release configuration requires PortBase7180; another instance initialization requires
    explicit --port-base (no default from release). release maps to
    userProfile/.aiakos and WSL .aiakos; another slug n maps to .aiakos-n on both sides, container
    aiakos-n-postgres, volume aiakos-n-pgdata. Resolve allows dev for client discovery only.
    Configuration validation refuses Instance=dev, Wsl.Home=.aiakos-dev or PortBase=5180 with
    `The dev instance is reserved for the AppHost.`. It accepts only the exact relative WSL home
    derived from the slug. Invalid configuration returns `Invalid instance configuration.`.
R2. PortBase is1..51535; offsets0,1,2,10,10000,14000 must all lie in1..65535 and must not equal
    any corresponding derived port of another initialized instance. Collision returns
    `Instance ports overlap an initialized instance.`. Compare configurations discovered only
    at userProfile/.aiakos/instance.json and userProfile/.aiakos-*/instance.json; malformed sibling
    files fail with `Invalid instance configuration.` rather than silently skipping. Ignore the
    current instance on force reinitialization. No socket probing substitutes for this check.
R3. Source-generated configuration JSON uses snake_case, exact field order Instance,PortBase,
    Operator,Wsl,Database,Telemetry; nested declaration order above; null properties omitted
    except otlp_endpoint which stays null. Deserialize rejects malformed/missing required fields,
    wrong types, invalid modes and nonabsolute external connection-string paths with FormatException
    message `Invalid instance configuration.`. Mode is exactly container/external. Container
    requires image postgres:18 and no connection file; external requires no image and an absolute
    connection file. Telemetry dashboard and explicit endpoint cannot both be selected; endpoint
    if present is an absolute http/https URI. Config has no token/password/connection-string value.
R4. InstanceInitializer derives paths, validates against siblings, then creates instance.json and
    secrets/api-token,node-token,postgres-password (password only container mode). Tokens and
    password are independent cryptographically random32-byte values encoded base64, UTF-8 no BOM,
    no newline. Before secret content is written Windows ACL disables inheritance and grants only
    the current user's SID read/write; no Everyone/Users/group ACE. ACL failure aborts before secret
    write. Existing instance.json with force=false returns false without writes or random generation;
    force=true is refused while instance.lock is held with `Instance is locked by another host.`
    and otherwise explicitly recreates secrets.
    Configuration is published last via sibling temporary file and rename. Failed init removes only
    newly created owned files, retaining an existing configuration until replacement succeeds.
    The initializer's bool is true when newly initialized/reinitialized and false for existing.
R5. RuntimeCopy copies the entire installed source directory into windowsHome/runtime/version,
    preserving relative names and bytes, including node/linux-x64 payload when present. Version
    is a single safe path segment (ASCII alphanumeric plus dot/dash/plus, neither dot nor dotdot).
    Reject links/reparse points in source or runtime destination, rather than follow them. A sorted
    manifest of relative paths, lengths and SHA-256 content hashes decides missing/different; a
    timestamp-only change does not recopy. Stage in an owned sibling and atomically publish after
    all files match; cancellation/failure removes staging and preserves prior copy. Never replace
    the runningVersion directory; differing bytes there returns `Running runtime copy cannot be replaced.`.
    Retain two most recently successfully published runtime copies; protect runningVersion even
    if it means temporarily retaining a third. Never delete directories outside runtime or links.
R6. Shared WSL preflight preserves mirrored-only success, missing-distro failure, fallback to
    userProfile/.wslconfig when wslinfo unavailable, success-only per-distro caching and existing
    diagnostics. WslEnvironment.Compose preserves existing colon entries and appends absent names
    with prefixes AIAKOS_ or OTEL_ sorted ordinal as NAME/u, never duplicates WSLENV itself or
    overwrites an existing entry. Unknown networking refuses start; no NAT fallback or system edit.
R7. NodeInstall.Script uses wslpath -u on positional source and home=$HOME/<relative home>,
    stages copied files in home/node.staging, chmod +x aiakos-node, writes VERSION only after copy,
    then replaces home/node. The caller checks installed VERSION first and does nothing when it
    matches toolVersion. Do not install while an owned node is running. Paths are positional to
    fixed sh -c script, never interpolated. Source payload must contain aiakos-node or return
    `The packaged WSL node is unavailable.`; do not publish a development build as fallback.
R8. NodeRestartPolicy: exit2/3 => Retry=false,Delay=Zero; every other unexpected exit, including0,
    retries. consecutiveFailures is the number before this exit (>=0); delay is1,2,4,8,16,30,30...
    seconds. Uptime>=5min resets that count to0 before deciding, hence1s. Negative arguments
    throw ArgumentOutOfRangeException. Shutdown cancellation does not count as unexpected exit.
R9. NodeSupervisor starts exactly one child at a time, retains and drains its process until exit,
    records LastExitCode, uses R8 delay through TimeProvider, and never kills by pid/name/global
    process search. Exit2/3 ends supervision but not the host. Cancellation during running asks
    only its owned child StopAsync, waits up to10s then disposes; cancellation during delay starts
    no new child. Start failure becomes fixed `Node process could not be started.` in host status;
    no raw process exception/output escapes. An owned child is stopped on partial startup failure.
R10. Container database preparation first probes docker info. Failure returns
    `Docker Desktop is unavailable.` and launches no orchestrator/node. Use postgres:18, container
    and volume from layout, publish127.0.0.1:base+2:5432, password via docker --env-file containing
    POSTGRES_PASSWORD in a current-user-only temporary file removed in finally. Inspect before
    create/start; existing mismatched image/port/volume fails `Instance database configuration does not match.`
    rather than replace an owner container. Poll readiness before orchestrator starts. External mode
    reads the connection string from configured file, does not invoke Docker, and never logs it.
R11. Host obtains exclusive instance.lock before preparing dependencies, writes pid and version
    there, retains the open exclusive handle, and releases it on every owned shutdown/failure path.
    Concurrent host returns exit3 with `Instance is locked by host <pid> (<version>).` and no
    dependency effects (holder pid/version from lock; unknown when unreadable, never guess).
    Host config is loaded from selected home, not environment fallback to dev. WSL preflight and
    version installation finish before the orchestrator accepts nodes. All listeners bind127.0.0.1:
    base+0 HTTP/2 h2c; base+1 HTTP/1.1 API/health/alive; node hook port base+10 through environment.
    The host builds the orchestrator with C1 composition; no second orchestrator process.
R12. Node factory launches held wsl.exe child argv `-d`,distro,`--cd`,`~`,`--exec`,
    `<WSL home absolute path>/node/aiakos-node`, UseShellExecute=false and redirected stdout/stderr.
    Resolve the Linux user's home by a bounded WSL helper, not a Windows user-name guess. Env is
    AIAKOS_ORCHESTRATOR_URL=http://127.0.0.1:base, AIAKOS_HOME=absolute Linux instance home,
    AIAKOS_NODE_ID=config NodeId, AIAKOS_NODE_TOKEN=secret, AIAKOS_INSTANCE=config Instance;
    OTEL_* when configured; WSLENV composed by R6. No token in argv, status or logs.
R13. Host readiness requires healthy /health and configured node connected in NodeLinkRegistry;
    only then atomically publish connection.json with api_url,pid,version,started_at,otlp_endpoint
    in that order, compact JSON+LF. Discovery uses only that file and a live-pid check; missing,
    malformed or dead-pid => unavailable, never probe guessed port or delete a live owner file.
    Start launches hidden detached Windows runtime copy `aiakos instance run --instance <slug>`;
    use a detached Windows process with no inherited console and independent process lifetime,
    not merely a console child hidden with CreateNoWindow. Timeout default90s/parser bound, then status. Already-live host returns its status exit0 without
    another process; no runtime update behind its back. Timeout/failure stops only the child it
    launched, removes its connection file, releases its lock; message `Instance did not become ready.`.
R14. Host-only GET /v1/admin/status and POST /v1/admin/shutdown share 15-2 bearer auth and changing
    version gate. Status contains api=v1,running,host_pid,host_version,started_at,ports,database,
    nodes,seat_counts,log_directory; no secrets. Shutdown body keep_seats,keep_database; default
    false. Query persisted agent seats only: session starting/present/unknown blocks with409 reason
    SEATS_RUNNING, detail `Run aiakos down --all before stopping the instance.`, retryable=false,
    and ordered seat addresses in a seats field. Humans and absent/exited agents do not block.
    keep_seats overrides with warning `Seats keep running only while WSL stays up and are adopted by the next node.`
    Return202 before stopping acceptance of new API commands; finish outstanding requests, stop
    owned node within10s, stop orchestrator, stop owned instance Postgres unless keep_database,
    delete owned connection file and release lock, in that order. No seat actor messages/state writes.
    Without host integration these routes return503 HOST_UNAVAILABLE with 15-2 exact detail.
R15. Status exit0 when running with healthy database/orchestrator and connected node,1 when running
    unhealthy,3 when unavailable. Human output is stable LF lines: instance:<name>, running:<bool>,
    host_pid:<number or null>, host_version:<version or null>, cli_version:<version>,
    version_match:<bool>, followed by ports, database mode/health, node connection/LastExitCode,
    seat_counts and log_directory; labels followed by one space, strings JSON-quoted. JSON status
    uses R14 fields plus cli_version/version_match. Version comparison is informational for status;
    stop requires matching major.minor, reads work across versions. Init/start/run/stop human
    success prints status except init prints `Instance initialized.\n` or `Instance already initialized.\n`;
    successful stop prints `Instance stopped.\n`. Config error exit2, process outcome failure1,
    lock/unavailable3, incompatible4, auth5. Errors go stderr with final LF and no stdout.
R16. Host logs roll UTC daily logs/host-yyyyMMdd.log and captures node output into node-yyyyMMdd.log;
    delete only these matching log files older than14days. Redact secret values before emitting any
    node output or formatted host event. Use Serilog.Extensions.Hosting10.0.0 and Serilog.Sinks.File7.0.0 in host only, centrally managed
    packages (package availability checked against their NuGet publisher pages); existing node stdout contract remains unchanged. OTLP is off unless configuration
    enables it; dashboard=true ensures aiakos-instance-dashboard container with UI base+10000,
    OTLP base+14000 on loopback and points host/node at it. Explicit endpoint skips dashboard.
    connection.json records chosen endpoint for15-3 CLI spans. Dashboard failure aborts startup
    with `Instance telemetry could not be started.`; no silent exporter fallback. Do not enable
    telemetry from inherited owner OTEL variables when config disables it.

Package pins for R16 were checked against publisher pages:
[Serilog.Extensions.Hosting](https://www.nuget.org/packages/Serilog.Extensions.Hosting/10.0.0) and
[Serilog.Sinks.File](https://www.nuget.org/packages/Serilog.Sinks.File/7.0.0).

## Expected outputs and permanent tests

Base fixture: owned userProfile root, instance release, operator owner, base7180, Ubuntu,
WSL home .aiakos, node wsl-local, container postgres:18, dashboard=false, endpoint=null,
version0.1.0. All subprocess tests use recording ports and contain no authenticated owner state.

| ID | Case | Exact expectation |
|---|---|---|
| `E1` | layout/guard/JSON/overlap matrix | .aiakos and aiakos-release-postgres/aiakos-release-pgdata; named .aiakos-demo; forbidden dev message R1; port collision message R2; exact config field order/types R3; no secret fields; fixture JSON is the following document |
| `E2` | new/existing/force/init failure | true/new three secret files; false/existing zero writes/RNG; force lock refusal; independent base64 values decoding to32bytes; explicit current-user-only ACL before content; no secret in output; config published last |
| `E3` | runtime new/same/changed/running/cancel/prune | bytes preserved; hash-identical zero replacement; safe version directory only; protected running mismatch exact R5 error; last two+protected copy; staging removed on failure; owner files unchanged |
| `E4` | extraction/composition regression | existing orchestrator health/migration-first/token/endpoint tests unchanged green; same registrations/routes in executable and in-process host; public Program remains |
| `E5` | WSL install/version/argv matrix | matching VERSION zero copy; mismatch staging/chmod/VERSION/rename sequence; missing payload exact R7 error; WSLENV entries preserved and appended sorted /u; no injected shell command |
| `E6` | restart/fake time/owned stop | delays1,2,4,8,16,30,30; uptime300s reset1; exit2/3 no retry; unexpected0 retry; cancellation delay zero restart; owned child stop then dispose once; LastExitCode exact; no owner process effect |
| `E7` | database and startup recording ports | Docker unavailable => exact R10 error/no later effects; external => zero Docker calls; env-file ACL/delete/no secret argv; lock collision => exit3/no effects; preflight/install before API before held-node; h2c/HTTP1 loopback ports exact |
| `E8` | ready/discovery/shutdown/status | connection only after healthy+connected; malformed/dead file unavailable3; timeout owned cleanup; live host no second start; agent starting/present/unknown409 SEATS_RUNNING, absent/exited/human allow202; stop order exact R14; status codes/streams exact R15 |
| `E9` | log/telemetry/platform matrix | UTC daily owned filenames,14day retention,secret redaction; disabled zero exporter/dashboard; enabled endpoint or loopback dashboard exact ports; non-Windows guard exact G2 before effects |

E1 configuration golden (compact plus final LF):
```json
{"instance":"release","port_base":7180,"operator":"owner","wsl":{"distro":"Ubuntu","home":".aiakos","node_id":"wsl-local"},"database":{"mode":"container","image":"postgres:18"},"telemetry":{"dashboard":false,"otlp_endpoint":null}}
```

T1. Commit E1 layout/configuration golden and collision tests with exact serialized compact JSON;
    test malformed sibling, mode/URI/port/slug/home bounds and no input mutation.
T2. Commit E2 transactional initializer and Windows ACL test inspecting actual temporary secret
    file ACLs; Linux tests exercise ordering with a security port, never claim Windows ACL proof.
T3. Commit E3 real temporary-directory runtime byte/hash/pruning/cancellation/link refusal tests.
T4. Commit E4 shared composition integration/regression tests; no duplicate
    service registration, no changed migration/health behavior; include existing suites.
T5. Commit E5 shell-script fixture/argv/env/version tests; production script is exercised only in
    an owned opt-in Windows/WSL fixture, not an owner node installation.
T6. Commit E6 fake-time supervisor/policy tests including cancellation while running/delayed,
    start failure, reset boundary299s/300s, and only-owned process cleanup.
T7. Commit E7 recording-process startup/database tests and external Postgres in-process host
    integration using a private test database and fake node; Docker never touches owner containers.
T8. Commit E8 actual HTTP auth/version/status/shutdown tests, persisted session fixture, connection
    lifecycle and CLI application dispatch tests after merged CLI/API dependencies. Windows opt-in
    owned demo must prove detached host survives parent terminal closure and attached WSL child
    remains alive; do not mark these risks closed based on Linux fakes.
T9. Commit E9 redaction/retention/exporter/dashboard/platform tests with sentinel secrets generated
    at test runtime; no committed token-like fixtures. Assert disabled config overrides inherited OTEL.

## Done

Release solution build0warnings/0errors; affected and earlier test suites green. One commit per
story, LF UTF-8 without BOM and final newline. Do not push/open PR. Report Windows-only checks
not run as unknown. Acceptance gates are authored later and run only by QA. Missing dependency
or packaged payload is named with source evidence, never substituted by a working-tree node.

## Out of scope

Seat actor/dispatcher/state changes; live up/down/send/capture/attach handlers; CLI trace export;
NuGet payload publishing/tag pipeline/global tool installation; logs of real owner sessions;
Windows service/logon/reboot recovery; automatic seat restart; remote listeners/TLS/tenants.
