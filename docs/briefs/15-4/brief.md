---
id: 15-4
title: "#15 slice 4 — released instance configuration and host lifecycle"
issue: 15
status: approved
route: impl
paths: [src/Aiakos.Cli/, src/Aiakos.Wsl/, src/Aiakos.Hosting.Wsl/, src/Aiakos.Orchestrator/, src/Aiakos.Api.Contracts/, src/Aiakos.AppHost/, tests/Aiakos.Hosting.Wsl.Tests/, docs/specs/0007-cli-and-released-instance.md, tests/Aiakos.Cli.Tests/, tests/Aiakos.Wsl.Tests/, tests/Aiakos.AppHost.Tests/, tests/Aiakos.Orchestrator.Tests/, Directory.Packages.props, Aiakos.slnx]
date: 2026-10-07
---

# Brief: #15 slice 4 — released instance configuration and host lifecycle

Part of #15, spec 0007 R6–R21 and ADR0034. Self-contained: implementers do not read specs or
ADRs. Where silent, choose the smallest private implementation and record it in the commit.
Do not add public commands, fallback discovery, seat reconciliation or automatic seat starts.

## Goal and prerequisites

A released Windows instance can initialize isolated configuration, copy its installed runtime,
run the orchestrator in-process, hold and supervise its WSL node, and shut down deliberately.
Pure configuration/runtime/supervisor stories do not depend on API or actors. All CLI stories
    require merged15-1 S1 project/test infrastructure; project-reference smoke additionally waits
    on merged15-1 S5. The final wiring
story requires merged15-1 S5 and15-2 S2/S3/S5 (contracts/authentication/reads/version policy), and the production
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
| src/Aiakos.Hosting.Wsl/, tests/Aiakos.Hosting.Wsl.Tests/ | compatibility wrappers and delegate shared WSL helpers |
| src/Aiakos.AppHost/NodeDeployment.cs, tests/Aiakos.AppHost.Tests/ | delegate existing install script to shared NodeInstall.Script |
| src/Aiakos.Api.Contracts/, docs/specs/0007-cli-and-released-instance.md | admin DTOs and explicitly documented local admin API amendment |
| src/Aiakos.Orchestrator/, tests/Aiakos.Orchestrator.Tests/ | shared composition and host-only admin lifecycle routes |
| Directory.Packages.props, Aiakos.slnx | central packages and project registration |

No PackageReference Version, NoWarn, pragma warning disable or SuppressMessage. No node code,
AppHost composition rewrite (only NodeDeployment install delegation), database schema, migrations, actor changes, CI or release publishing.
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
public interface IInstanceRandom { byte[] CreateBytes(int count); }
public interface ISecretFileSecurity { Task ApplyUserOnlyAsync(string path, CancellationToken ct); }
public sealed class InstanceInitializer(IInstanceRandom random, ISecretFileSecurity security) {
    public Task<bool> InitializeAsync(string userProfile, InstanceConfiguration configuration,
        bool force, CancellationToken cancellationToken);
}
public sealed class RuntimeCopy(TimeProvider timeProvider) {
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
    public string? LastError { get; }
    public Task RunAsync(CancellationToken cancellationToken);
}
```

Host composition is in Aiakos.Orchestrator:
`OrchestratorComposition.AddAiakosOrchestrator(WebApplicationBuilder)` returns that builder;
`MapAiakosOrchestrator(WebApplication)` returns that application. Existing Program remains
public for WebApplicationFactory and calls the two methods before RunAsync.

Shared implementations live in namespace Aiakos.Wsl: IWslProcessRunner, WslProcessResult,
WslProcessStartException, IWslConfigFile, UserProfileWslConfigFile, WslConfigParser, WslOutput,
WslPreflightResult, WslPreflightException and WslPreflight. Their existing signatures are retained.
Hosting.Wsl keeps its existing namespace/types as thin compatibility wrappers/adapters; its runner
and config adapters delegate to shared implementations and convert result/exception types while
preserving Message/ExitCode/output. It does not duplicate preflight algorithms. This is the one
compatibility mechanism; no assembly type forwarding or breaking caller namespace migration.
`Aiakos.Wsl.WslEnvironment.Compose(string? existing,IReadOnlyDictionary<string,string> environment,
IReadOnlyList<string> prefixes)` accepts the caller's prefixes; ordinal prefix match, existing
variable-name comparison OrdinalIgnoreCase. Hosting.Wsl passes its existing configurable prefixes
(defaults OTEL_, AIAKOS_, DOTNET_); the released node factory passes AIAKOS_, OTEL_.
`Aiakos.Wsl.NodeInstall.Script` is the fixed development copy/install script from NodeDeployment
with no behavior change. `NodeInstaller(IWslProcessRunner runner)` adds released-version checking:
`Task<bool> InstallAsync(string distro,string sourceDirectory,string relativeHome,string version,
CancellationToken ct)` returns false for same installed VERSION, true after successful install.

Additional public ports in Aiakos.Cli.Instances (each owned only by its corresponding rule):

```csharp
public sealed class InstanceProcessRequest(string fileName, IReadOnlyList<string> arguments,
    IReadOnlyDictionary<string,string> environment) {
    public string FileName { get; }
    public IReadOnlyList<string> Arguments { get; }
    public IReadOnlyDictionary<string,string> Environment { get; }
} // redacted ToString; snapshots, no generated record printer
public sealed record InstanceProcessResult(int ExitCode,string Stdout,string Stderr);
public sealed class InstanceProcessRunner : IInstanceProcessRunner;
public interface IInstanceProcessRunner {
    Task<InstanceProcessResult> RunAsync(InstanceProcessRequest request,TimeSpan timeout,CancellationToken ct);
}
public interface IDatabaseManager {
    Task<InstanceDatabaseLease> PrepareAsync(InstanceConfiguration config,string home,CancellationToken ct);
    Task StopAsync(InstanceDatabaseLease lease,CancellationToken ct);
}
public sealed class InstanceDatabaseLease(string connectionString,bool containerManaged) {
    public string ConnectionString { get; }
    public bool ContainerManaged { get; }
} // redacted ToString
public sealed class InstanceDatabaseManager(IInstanceProcessRunner runner,ISecretFileSecurity security,
    TimeProvider timeProvider) : IDatabaseManager;
public interface IInstanceLockLease : IAsyncDisposable { }
public sealed class InstanceLockFactory {
    public IInstanceLockLease Acquire(string home,int pid,string version);
    public bool IsHeld(string home);
}
public interface IOwnedNodeLauncher {
    Task<IOwnedNodeProcess> StartAsync(InstanceProcessRequest request,IHostLogs logs,CancellationToken ct);
}
public sealed class ReleasedNodeProcessFactory(InstanceConfiguration config,string linuxHome,
    string nodeToken,string? otlpEndpoint,IHostLogs logs,IOwnedNodeLauncher launcher) : INodeProcessFactory;
public interface IHostLogs : IAsyncDisposable {
    Microsoft.Extensions.Logging.ILoggerProvider CreateLoggerProvider();
    Task WriteHostAsync(string message,CancellationToken ct);
    Task WriteNodeAsync(string text,CancellationToken ct);
}
public sealed class HostLogs(string home,IReadOnlyList<string> secrets,TimeProvider timeProvider) : IHostLogs;
public interface IHostTelemetry : IAsyncDisposable {
    Task<string?> PrepareAsync(InstanceConfiguration config,CancellationToken ct);
}
public sealed class HostTelemetry(IInstanceProcessRunner runner) : IHostTelemetry;
public sealed record ConnectionInfo(string ApiUrl,int Pid,string Version,DateTimeOffset StartedAt,
    string? OtlpEndpoint);
public interface IInstanceLiveness {
    bool IsProcessAlive(int pid);
    bool IsLockHeld(string windowsHome);
}
public sealed class ConnectionStore(IInstanceLiveness liveness) {
    public Task<ConnectionInfo?> ReadAsync(string home,CancellationToken ct);
    public Task<ConnectionInfo?> ReadDevAsync(string home,CancellationToken ct);
    public Task PublishAsync(string home,ConnectionInfo info,CancellationToken ct);
    public Task RemoveOwnedAsync(string home,int pid,CancellationToken ct);
}
public interface IInstanceOrchestrator : IAsyncDisposable {
    Task StartAsync(CancellationToken ct);
    Task<bool> IsHealthyAsync(CancellationToken ct);
    Task<bool> IsNodeConnectedAsync(string nodeId,CancellationToken ct);
    Task BeginShutdownAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
}
public interface IInstanceOrchestratorFactory {
    IInstanceOrchestrator Create(InstanceConfiguration config,string home,InstanceDatabaseLease database,
        string nodeToken,string apiToken,IInstanceHostControl control);
}
public interface IInstanceWslPreparation {
    Task<string> PrepareAsync(InstanceConfiguration config,string payloadDirectory,string version,CancellationToken ct);
}
public sealed class InstanceWslPreparation(Aiakos.Wsl.WslPreflight preflight,
    Aiakos.Wsl.NodeInstaller installer,IInstanceProcessRunner runner) : IInstanceWslPreparation;
public interface IReleasedNodeProcessFactoryProvider {
    INodeProcessFactory Create(InstanceConfiguration config,string linuxHome,string nodeToken,
        string? otlpEndpoint,IHostLogs logs);
}
public sealed class InstanceHost(InstanceLockFactory locks,IDatabaseManager database,
    IInstanceWslPreparation wsl,IInstanceOrchestratorFactory orchestrator,TimeProvider timeProvider) {
    public Task RunAsync(InstanceHostRequest request,CancellationToken ct);
}
public sealed class InstanceHostRequest(InstanceConfiguration config,string home,int pid,string version,
    string nodeToken,string apiToken,string payloadDirectory,IReleasedNodeProcessFactoryProvider nodeFactory,ConnectionStore connections,
    IHostLogs logs,IHostTelemetry telemetry,IInstanceHostControl control,InstanceStopSignal stopSignal) {
    public InstanceConfiguration Config { get; }
    public string Home { get; }
    public int Pid { get; }
    public string Version { get; }
    public string NodeToken { get; }
    public string ApiToken { get; }
    public string PayloadDirectory { get; }
    public IReleasedNodeProcessFactoryProvider NodeFactory { get; }
    public ConnectionStore Connections { get; }
    public IHostLogs Logs { get; }
    public IHostTelemetry Telemetry { get; }
    public IInstanceHostControl Control { get; }
    public InstanceStopSignal StopSignal { get; }
} // class properties initialized from constructor; redacted ToString
public sealed class WindowsDetachedInstanceLauncher : IDetachedInstanceLauncher;
public interface IDetachedInstanceLauncher {
    Task<int> LaunchAsync(string dotnetPath,IReadOnlyList<string> arguments,CancellationToken ct);
    Task StopOwnedAsync(int pid,CancellationToken ct);
}
public sealed class InstanceStarter(RuntimeCopy runtime,ConnectionStore connections,
    IDetachedInstanceLauncher launcher,InstanceAdminClient client,TimeProvider timeProvider) {
    public Task<Aiakos.Api.Contracts.HostStatusResponse> StartAsync(string sourceDirectory,string home,
        string instance,string version,TimeSpan timeout,CancellationToken ct);
}
public sealed class InstanceAdminClient(HttpClient http) {
    public Task<Aiakos.Api.Contracts.HostStatusResponse> StatusAsync(ConnectionInfo connection,
        string tokenFile,CancellationToken ct);
    public Task<Aiakos.Api.Contracts.HostShutdownResponse> StopAsync(ConnectionInfo connection,string tokenFile,string clientVersion,
        bool keepSeats,bool keepDatabase,CancellationToken ct);
}
public interface IInstancePlatform { bool IsWindows { get; } string UserName { get; } string UserProfile { get; } }
public interface IInstanceCommands {
    Task<int> RunAsync(Aiakos.Cli.CliRequest request,string currentDirectory,string cliVersion,
        TextWriter stdout,TextWriter stderr,CancellationToken ct);
}
public interface IInstanceHostRunner {
    Task RunAsync(InstanceConfiguration config,string home,string version,CancellationToken ct);
}
public sealed class InstanceCommands(IInstancePlatform platform,IInstanceRandom random,
    ISecretFileSecurity security,InstanceStarter starter,InstanceAdminClient client,
    ConnectionStore connections,IInstanceHostRunner host,TimeProvider timeProvider) : IInstanceCommands;
public static class InstanceStatusRenderer {
    public static string Text(Aiakos.Api.Contracts.HostStatusResponse status,string cliVersion);
    public static string Json(Aiakos.Api.Contracts.HostStatusResponse status,string cliVersion);
}
```

Host API control port is in Aiakos.Orchestrator.Instances, avoiding an Orchestrator-to-CLI cycle:
`IInstanceHostControl.GetStatusAsync(CancellationToken)` returns `Task<HostStatusResponse>`;
`GetBlockingAgentAddressesAsync(CancellationToken)` returns `Task<IReadOnlyList<string>>`;
`RequestStopAsync(bool keepSeats,bool keepDatabase,CancellationToken)` returns `Task` when queued.
CLI classes consuming it import that namespace; production control reads SeatQueries/NodeLinkRegistry.
R24 owns IInstanceOrchestrator, IInstanceOrchestratorFactory and the concrete `InstanceOrchestratorFactory(IHostLogs logs) : IInstanceOrchestratorFactory`.
R24 owns `InstanceStopSignal`: `Task RequestAsync(bool keepSeats,bool keepDatabase,CancellationToken ct)`
queues the first request only; `Task<HostShutdownRequest> WaitAsync(CancellationToken ct)` awaits it.
R24 owns `ProductionInstanceHostControl(InstanceConfiguration config,string home,int pid,string version,
DateTimeOffset startedAt,InstanceStopSignal stopSignal,Func<HostDatabase> databaseSnapshot,
Func<IReadOnlyList<HostNode>> nodeSnapshots)` implementing IInstanceHostControl.
Its `void Bind(IServiceProvider services)` binds the built application's service provider once.
Before binding, status/blocker reads throw the existing HOST_UNAVAILABLE admin exception; stop queues
through the supplied signal. Config/home metadata is immutable; snapshots reflect current host state.
Construction performs no query. Bind happens before listeners start. No Orchestrator reference to CLI.

`InstanceAdminException(int exitCode,string message,IReadOnlyList<string>? seats,string? reason=null)` is a sealed
exception with public int ExitCode and IReadOnlyList<string>? Seats immutable properties; no raw
response body or bearer token in Message/ToString. Public string? Reason retains only a recognized
problem reason; network failures have null Reason.
`Aiakos.Orchestrator.Instances.AdminHostEndpoints.Map(WebApplication)` returns that application
and is called by shared MapAiakosOrchestrator after merged15-2 routes. Its no-control default is503.

Admin DTOs in Aiakos.Api.Contracts, immutable with generated snake_case serialization:
`HostPorts(int Grpc,int Api,int Postgres,int Hooks,int Dashboard,int Otlp)`;
`HostDatabase(string Mode,bool Healthy)`;
`HostNode(string Node,bool Connected,int? LastExitCode,string? LastError)`;
`HostSeatCounts(int Absent,int Starting,int Present,int Exited,int Unknown)`;
`HostStatusResponse(string Api,string Instance,bool Running,bool Healthy,int HostPid,string HostVersion,DateTimeOffset StartedAt,
HostPorts Ports,HostDatabase Database,IReadOnlyList<HostNode> Nodes,HostSeatCounts SeatCounts,
string LogDirectory)`; `HostShutdownRequest(bool KeepSeats,bool KeepDatabase)`;
`HostShutdownResponse(string Api,string? Warning)`;
`HostProblem(string Reason,string Detail,bool Retryable,IReadOnlyList<string>? Seats)`.

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

C1. Extract current-main orchestrator service registration, endpoint policy, telemetry, migration ordering,
    options resolution and route mapping from Program into the shared composition methods.
    No external15-2 prerequisite for extraction: preserve current Program as it stands. Later
    merged15-2 stories register through these composition methods (move any late Program-only
    additions into composition in S18, the admin lifecycle routes story). Preserve every existing registration, health endpoint, startup validation and migration-first
    behavior; tests relying on public Program continue working. API registrations from merged
    15-2 are included, rather than a parallel host-only API implementation.
C2. Extract WSL preflight/runner/config/environment logic into Aiakos.Wsl without Aspire references.
    Hosting.Wsl uses it and keeps existing public caller behavior and diagnostics. Register library
    and Wsl.Tests in solution; reuse existing WSL tests rather than delete their assertions.
C3. Preserve the existing public CliApplication(GitEnvChecker) constructor and add overload
    CliApplication(GitEnvChecker,IInstanceCommands) for dependency injection. The original
    constructor supplies real InstanceCommands lazily; Program uses real platform/services, while
    tests inject fake Windows platform/instance services. No instance IO is performed by either
    constructor or by non-instance commands. The final wiring replaces only 15-1 R7's not-implemented outcome for instance init/start/run/
    stop/status. All other CLI behavior remains owned by 15-1/15-3. Project references belong to C4, not this wiring story; never reference Node or AppHost. Final command integration waits on
    merged 15-1 S5 and 15-2 API stories, not a handwritten alternate parser.

C4. Add CLI references Core, Wsl, Orchestrator, Api.Contracts and FrameworkReference
    Microsoft.AspNetCore.App before host adapter stories compile; project-only change, no Program
    wiring. Node and AppHost are not CLI references. A temporary local dotnet pack/install smoke
    uses exactly 15-1 R9 isolation and asserts --help still starts with the ASP.NET runtime.
C5. Extend only the admin API policy beyond15-2: add the above admin DTOs/context entries;
    route registration belongs to R14; this item creates DTOs and policy documentation only.
    Existing routes retain15-2's problem shape/reasons unchanged. Admin SEATS_RUNNING alone
    adds seats; omit seats on other admin problems. Amend spec0007 API table with status route,
    shapes and this explicit admin problem exception; no other spec or brief change.

## Rules

R1. InstanceLayout.Resolve validates `[a-z][a-z0-9-]{0,62}` before joining paths. release configuration requires PortBase7180; another instance initialization requires
    explicit --port-base (no default from release). release maps to
    userProfile/.aiakos and WSL .aiakos; another slug n maps to .aiakos-n on both sides, container
    aiakos-n-postgres, volume aiakos-n-pgdata. Resolve allows dev for client discovery only.
    Resolve invalid slug throws ArgumentException with message `Invalid instance name.`.
    Configuration validation refuses Instance=dev, Wsl.Home=.aiakos-dev or PortBase=5180 with
    `The dev instance is reserved for the AppHost.`. It accepts only the exact relative WSL home
    derived from the slug. Invalid configuration returns `Invalid instance configuration.`.
R2. PortBase is1..51535; offsets0,1,2,10,10000,14000 must all lie in1..65535 and must not equal
    any of the six derived ports of another initialized instance (set intersection must be empty,
    not just matching offsets); reserve dev six-port set5180,5181,5182,5190,15180,19180. Collision returns
    `Instance ports overlap an initialized instance.`. Compare configurations discovered only
    by R4 only at userProfile/.aiakos/instance.json and userProfile/.aiakos-*/instance.json; malformed sibling
    files fail with `Invalid instance configuration.` rather than silently skipping under R4. Ignore the
    current instance on force reinitialization. No socket probing substitutes for this check. Reserved dev configuration is not deserialized
    as a released configuration; its port set is a constant. R4 discovers released siblings only.
R3. Source-generated configuration JSON uses snake_case, exact field order Instance,PortBase,
    Operator,Wsl,Database,Telemetry; nested declaration order above; null properties omitted
    except otlp_endpoint which stays null. Deserialize rejects malformed/missing required fields,
    wrong types, invalid modes and external connection-string paths not matching Windows drive-rooted form
    `[A-Za-z]:[\\/].+` (no UNC, root-relative or Linux path) with FormatException
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
    Initialization validation errors throw InvalidOperationException with Validate's exact message.
    Discover sibling JSON here, including malformed-sibling failure under R2; S1 validation is pure.
    Lock file is home/instance.lock: held means opening it for read/write with FileShare.Read on Windows,
    FileShare.None otherwise, bufferSize=0, fails due
    to sharing/locking (other IO failures abort as `Instance initialization failed.`).
    force=true is refused while instance.lock is held with `Instance is locked by another host.`
    and otherwise rotates api-token and node-token. Preserve an existing postgres-password byte-for-byte
    with no RNG call or replacement: its retained database volume still uses that password. Generate
    a password only in container mode when absent. External mode leaves an existing password untouched.
    Stage secret files with ACLs before publication. Force replacement retains user-only backups
    of existing secret files until config publication succeeds and restores them on any failure.
    Configuration is published last via sibling temporary file and rename. Failed init removes only
    newly created owned files, retaining an existing configuration until replacement succeeds.
    ACL/random/write failure throws InvalidOperationException `Instance initialization failed.`
    without an inner exception containing credentials. Tests inject random/security ports; default
    production adapters use cryptographic RNG and Windows current-user-only ACLs.
    The initializer's bool is true when newly initialized/reinitialized and false for existing.
R5. RuntimeCopy copies the entire installed source directory into windowsHome/runtime/version,
    preserving relative names and bytes, including node/linux-x64 payload when present. Version
    is a single safe path segment (ASCII alphanumeric plus dot/dash/plus, neither dot nor dotdot).
    Reject links/reparse points in source or runtime destination, rather than follow them. A sorted
    manifest of relative paths, lengths and SHA-256 content hashes decides missing/different; a
    timestamp-only change does not recopy. Stage in an owned sibling and atomically publish after
    all files match; cancellation/failure removes staging and preserves prior copy. Never replace
    the runningVersion directory; differing bytes there returns `Running runtime copy cannot be replaced.`.
    EnsureAsync returns the absolute runtime version directory. Unsafe version/link returns
    InvalidOperationException `Invalid runtime copy.`; other anticipated IO returns
    `Runtime copy could not be created.`; running mismatch is InvalidOperationException with R5's
    exact message. Cancellation propagates OperationCanceledException. Publication writes
    runtime/<version>/.published-at as UTC round-trip TimeProvider time; prune by that time
    descending, ties by version ordinal descending. Exclude this marker from content comparison.
    Retain two most recently successfully published runtime copies; protect runningVersion even
    if it means temporarily retaining a third. Never delete directories outside runtime or links.
R6. Shared WSL preflight preserves mirrored-only success, missing-distro failure, fallback to
    userProfile/.wslconfig when wslinfo unavailable, success-only per-distro caching and existing
    diagnostics. WslEnvironment.Compose preserves existing colon entries and appends absent names
    matching supplied prefixes sorted ordinal as NAME/u; compare existing names OrdinalIgnoreCase,
    never duplicates WSLENV itself or overwrites an existing entry. Unknown networking refuses start; no NAT fallback or system edit.
R7. NodeInstaller first verifies local sourceDirectory/aiakos-node exists, otherwise throws
    InvalidOperationException `The packaged WSL node is unavailable.`. Commands use runner with
    fileName=wsl.exe and argv `-d`,distro,`--exec`,`/bin/sh`,`-c`,fixedScript,`sh`, positional values.
    A VERSION read script `cat "$HOME/$1/node/VERSION"` receives relativeHome; matching version
    (trim final newline only) returns false and runs no install. Missing VERSION permits install.
    Copy uses unchanged NodeInstall.Script with sourceDirectory,relativeHome positional arguments;
    then a fixed `printf '%s\n' "$2" > "$HOME/$1/node/VERSION"` writes relativeHome,version. No
    node process is running at this stage; host R11 enforces that ordering. Install/copy failure
    throws InvalidOperationException `WSL node installation failed.` and never writes VERSION.
    Existing dev NodeDeployment.WslInstallScript delegates to unchanged shared Script; release-only
    VERSION behavior lives in installer, not the AppHost helper. Never publish a development node.
R8. NodeRestartPolicy: exit2/3 => Retry=false,Delay=Zero; every other unexpected exit, including0,
    retries. consecutiveFailures is the number before this exit (>=0); delay is1,2,4,8,16,30,30...
    seconds. Uptime>=5min resets that count to0 before deciding, hence1s. Negative arguments
    throw ArgumentOutOfRangeException. Shutdown cancellation does not count as unexpected exit.
R9. NodeSupervisor starts exactly one child at a time, retains and drains its process until exit,
    records LastExitCode, uses R8 delay through TimeProvider, and never kills by pid/name/global
    process search. Exit2/3 ends supervision but not the host. Cancellation during running asks
    only its owned child StopAsync, waits up to10s then disposes; cancellation during delay starts
    no new child. RunAsync returns normally after exit2/3 with LastExitCode and LastError exactly
    `Node exited with code 2.` or `Node exited with code 3.`. Cancellation completes owned cleanup
    then throws OperationCanceledException. A start failure ends supervision with no retry,
    LastExitCode=null and LastError=`Node process could not be started.`, returns normally, and
    host continues unhealthy. No raw process exception/output escapes. Host status consumes
    these members; an owned child is stopped on partial startup failure.
R10. Container database preparation first probes docker info. Failure returns
    `Docker Desktop is unavailable.` and launches no orchestrator/node. Use postgres:18, container
    and volume from layout, publish127.0.0.1:base+2:5432, password via docker --env-file containing
    POSTGRES_PASSWORD in a current-user-only temporary file removed in finally. Inspect before
    create/start; existing mismatched image/port/volume fails `Instance database configuration does not match.`
    rather than replace an owner container. Poll readiness before orchestrator starts. External mode
    reads the connection string from configured file, does not invoke Docker, and never logs it.
    Exact container-mode argv (fileName=docker, runner timeout10s except pull): info;
    inspect,--type,container,name; for missing container only: image,inspect,postgres:18; if absent,
    pull,postgres:18 with timeout180s; then volume,create,volumeName; then
    create,--name,name,--env-file,tempFile,--env,POSTGRES_USER=aiakos,--env,POSTGRES_DB=aiakos,
    --publish,127.0.0.1:<base+2>:5432,--mount,type=volume,source=<volume>,target=/var/lib/postgresql,
    postgres:18; then start,name when not running. Inspect Config.Image, State.Running,
    HostConfig.PortBindings[5432/tcp] exactly loopback/derived host port, and Mounts volume name
    and Destination=/var/lib/postgresql. Missing indicated by inspect error only after docker info
    succeeded; do not treat permission/daemon failure as absence. Readiness argv exec,name,
    pg_isready,-U,aiakos,-d,aiakos, repeat every250ms up to30s using TimeProvider; timeout throws
    InvalidOperationException `Instance database did not become ready.`. Connection string uses
    Host=127.0.0.1,Port=base+2,Database=aiakos,Username=aiakos,Password=secret via Npgsql builder.
    Stop argv stop,--time,10,name; never rm/volume rm. All fixed failure messages in R10 are
    InvalidOperationException. External file read/connect failure => `Instance database is unavailable.`.
    Pull nonzero exit/timeout throws `Instance database image could not be pulled.`; create never
    implicitly pulls because image availability is established first. Inspect-image absence requires
    a missing-image diagnostic, not permission/daemon failure. Caller cancellation propagates and
    removes the temporary env-file; the starter's overall timeout can cancel a slow first start.
    PostgreSQL18 volume destination is pinned to the image layout, not the older /data mount.
R11. InstanceHost obtains the R17 lock, prepares database, runs shared WSL preflight and node
    installation, prepares telemetry/logs, creates and starts the orchestrator, then runs supervisor.
    PrepareAsync returns the absolute Linux instance home: resolved Linux $HOME joined with the
    validated relative config.Wsl.Home. Pass that returned path into NodeFactory.Create.
    Publish connection only after orchestrator /health is healthy AND configured node connected;
    ConnectionStore itself only writes the supplied document. HostRequest is redacted.
    Await StopSignal.WaitAsync concurrently with cancellation. A terminal supervisor exit leaves
    the host running unhealthy, as R9 requires; it does not request host shutdown.
    API control and host share the same signal. A stop request carries keep_seats and keep_database;
    keep_seats changes admission/warning only, never leaves the owned node alive. Cancellation/failure
    defaults keep_database=false. After a successful response has completed, BeginShutdownAsync
    closes new API command admission and drains outstanding requests, then stop owned node within10s,
    stop orchestrator, stop acquired instance Postgres unless keep_database, stop acquired dashboard,
    remove owned connection, flush/dispose logs and release lock. Cleanup executes remaining steps
    even when an earlier step fails; preserve the first sanitized failure. Partial startup cleans
    only acquired resources. Runtime/instance paths never fall back to dev.
    Backlog #12 (12-2) owns the node hook-port configuration/consumer. This slice does not introduce
    or forward a hook-port environment variable; reserved port reporting remains unchanged.
R24. InstanceOrchestratorFactory creates an in-process WebApplication through C1/C4 and registers
    the supplied control instance. After Build, bind ProductionInstanceHostControl to app.Services;
    fake controls need no binding. StartAsync starts listeners; no child orchestrator process.
    Listen127.0.0.1:base HTTP/2 h2c and127.0.0.1:base+1 HTTP/1.1. Configure keys
    ConnectionStrings:aiakos, Aiakos:Instance, Aiakos:Orchestrator:GrpcPort,
    Aiakos:Nodes:0:Id/Name/TenantId/Token, Aiakos:Api:TokenFile=<home>/secrets/api-token,
    Aiakos:Api:Operator and Aiakos:Api:Port consistently with15-2; tenant is TenantIds.Default.
    Secrets stay in memory. Register logs.CreateLoggerProvider() with builder.Logging so orchestrator
    ILogger output passes the same R16 redaction before disk; remove other disk/console providers
    for this released host to avoid duplicate secret-bearing output. Existing executable logging
    is unchanged. BeginShutdownAsync rejects new changing API requests with503 HOST_UNAVAILABLE,
    drains outstanding API requests while retaining node gRPC until StopAsync. StopAsync stops app.
    Production control creates/disposes a fresh DI scope for each persisted SeatQueries read and
    reads NodeLinkRegistry connectivity. Blockers are agent seats in starting/present/unknown,
    addresses sorted ordinal; humans and absent/exited do not block. Status uses declared metadata,
    current database/node snapshots, registry connectivity, persisted seat counts and /health;
    Healthy is database healthy AND /health healthy AND configured node connected. RequestStopAsync
    delegates to the shared signal without querying services. Dispose releases only its application.
R12. Node factory launches held wsl.exe child argv `-d`,distro,`--cd`,`~`,`--exec`,
    `<WSL home absolute path>/node/aiakos-node`, UseShellExecute=false and redirected stdout/stderr.
    Resolve Linux home with wsl.exe argv `-d`,distro,`--exec`,`/bin/sh`,`-c`,
    `printf '%s\n' "$HOME"`; timeout10s; result must be an absolute slash path, trim final LF,
    otherwise InvalidOperationException `WSL home could not be resolved.`. No Windows user guess. Env is
    AIAKOS_ORCHESTRATOR_URL=http://127.0.0.1:base, AIAKOS_HOME=absolute Linux instance home,
    AIAKOS_NODE_ID=config NodeId, AIAKOS_NODE_TOKEN=secret, AIAKOS_INSTANCE=config Instance;
    Remove inherited OTEL_* and add only configured
    OTEL_EXPORTER_OTLP_ENDPOINT plus OTEL_SERVICE_NAME=aiakos-node when endpoint present; WSLENV composed by R6. No token in argv, status or logs.
R13. ConnectionStore atomically publishes the supplied connection.json; the host owns readiness, fields api_url,pid,version,started_at,otlp_endpoint in that order compact+LF.
    ReadAsync returns null for missing/malformed/dead pid OR unheld instance.lock; loopback http API URL
    required. It never probes guessed ports or deletes stale files during read. If pid reused but
    lock unheld, return null. A held lock with unreachable authenticated API is unavailable, not
    proof this host can be replaced. RemoveOwnedAsync removes only a connection with the given pid.
    ReadDevAsync uses the same schema, loopback and live-pid checks but skips the lock check only
    for the .aiakos-dev home; another basename throws ArgumentException `Invalid dev instance home.`.
    Serialize StartedAt with source-generated System.Text.Json DateTimeOffset formatting: UTC
    offset +00:00, no fractional component for the whole-second fixture in E8.
    File publication failure throws InvalidOperationException `Instance connection could not be published.`.
R14. Host-only GET /v1/admin/status and POST /v1/admin/shutdown share 15-2 bearer auth and changing
    version gate. Status contains api=v1,instance,running,healthy,host_pid,host_version,started_at,ports,database,
    nodes,seat_counts,log_directory; no secrets. Shutdown body keep_seats,keep_database; default
    false. Query persisted agent seats only: session starting/present/unknown blocks with409 reason
    SEATS_RUNNING, detail `Run aiakos down --all before stopping the instance.`, retryable=false,
    and ordered seat addresses in a seats field. Humans and absent/exited agents do not block.
    keep_seats overrides with warning `Seats keep running only while WSL stays up and are adopted by the next node.`
    Response202 is HostShutdownResponse(api=v1,warning=null or the keep-seats warning).
    Register an HttpResponse.OnCompleted callback that queues RequestStopAsync with the request flags
    and CancellationToken.None. Return202 before that callback executes. Host shutdown sequence
    belongs to R11, not the route. S18 moves any merged15-2 Program-only service/route additions into
    AddAiakosOrchestrator/MapAiakosOrchestrator, including /v1/version; no alternate route copy.
    Without host integration these routes return503 HOST_UNAVAILABLE with 15-2 exact detail.
R15. Status exit0 when running with healthy database/orchestrator and connected node,1 when running
    unhealthy,3 when unavailable. Human output is stable LF lines: instance:<name>, running:<bool>,
    host_pid:<number or null>, host_version:<version or null>, cli_version:<version>,
    version_match:<bool>, followed by ports, database mode/health, node connection/LastExitCode,
    seat_counts and log_directory; labels followed by one space, strings JSON-quoted. JSON status
    uses R14 fields plus cli_version/version_match. Version comparison is informational for status;
    stop requires matching major.minor, reads work across versions; differing versions print
    stderr `CLI and instance versions differ.\n` for successful status reads. Init/start/run/stop human
    success prints status except init prints `Instance initialized.\n` or `Instance already initialized.\n`;
    successful stop prints `Instance stopped.\n`. Config error exit2, process outcome failure1,
    lock/unavailable3, incompatible4, auth5. Errors go stderr with final LF and no stdout.
R16. HostLogs rolls UTC daily logs/host-yyyyMMdd.log and node-yyyyMMdd.log, UTF-8 LF;
    delete only matching plain files strictly older than14days by parsed UTC filename date.
    Replace each configured secret occurrence with [REDACTED] before rendering to disk, including
    property text and node streams; do not log exception objects or process environment. Use
    Serilog.Sinks.File7.0.0 centrally in CLI host only; do not add Serilog.Extensions.Hosting.
    CreateLoggerProvider returns an ILoggerProvider adapter: format message/properties, redact,
    then WriteHostAsync; never render exception objects. Provider disposal does not dispose the
    shared HostLogs; the host owns the sink lifetime. Logging scopes/properties obey redaction too.
    Dispose flushes sinks. Logging failure throws InvalidOperationException `Instance logging failed.`.
R17. InstanceLockFactory creates home/instance.lock and holds FileAccess.ReadWrite, bufferSize=0,
    FileShare.Read on Windows, FileShare.None elsewhere. Another writer cannot acquire on either OS.
    Holder read on Windows uses FileAccess.Read/FileShare.ReadWrite|Delete; on Linux use libc open
    O_RDONLY + SafeFileHandle + RandomAccess.Read, avoiding FileStream's flock. Follow the existing
    src/Aiakos.Node/InstanceLock.cs pattern in CLI, without a Node project reference. Raw read must
    return the pid/version fixture on Linux; only an actual unreadable/malformed holder is unknown. Write compact JSON pid,version plus LF,
    flush before dependency effects. Sharing collision throws InvalidOperationException
    `Instance is locked by host <pid> (<version>).`; unparseable/read error holder =>
    `Instance is locked by host unknown (unknown).`. Other IO errors => `Instance lock could not be acquired.`.
    IsHeld tests acquiring the same writer handle and immediately disposing it; false if success,
    true only sharing collision. Use the existing Node pattern: an exact IOException from handle
    open is the sharing/locking collision; IOException subclasses and access failures are real IO
    errors. IsHeld missing file returns false without creating it; other non-collision IO errors
    propagate the fixed R17 acquisition message. Disposal releases handle; lock file may remain (not connection).
R18. InstanceStarter first discovers live connection and calls authenticated status. Responding live
    host (healthy or unhealthy) returns it without copying/launching; unreachable API throws `Instance is unavailable.`
    and never stops owner host. Otherwise RuntimeCopy.EnsureAsync(sourceDirectory=the passed directory (production caller supplies AppContext.BaseDirectory),
    home,CLI informational version,runningVersion=null) precedes detached launch. Exact file dotnet
    (PATH-resolved current tool host), argv absoluteRuntime/aiakos.dll,`instance`,`run`,`--instance`,
    slug. Launch uses Windows detached no inherited console, independent lifetime. Poll every250ms
    through TimeProvider for published connection and healthy+connected status up to passed timeout
    (default90s). Timeout stops only returned owned launcher pid, waits max10s, removes owned
    connection and throws `Instance did not become ready.`. Do not delete a lock held by another pid.
R19. HostTelemetry clears inherited owner OTEL exporter config; disabled returns null with zero
    container/exporter activity. Explicit endpoint returns it without Docker. Dashboard=true
    ensures container aiakos-<slug>-dashboard, image mcr.microsoft.com/dotnet/aspire-dashboard:13.5.2,
    ports127.0.0.1:base+10000:18888 and127.0.0.1:base+14000:18889; env
    DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true, no exposed non-loopback port. Docker runner
    timeout10s except pull; argv inspect,--type,container,name; when missing image,inspect,image;
    if image absent pull,image with timeout180s, then create,--name,name,--publish,
    127.0.0.1:<base+10000>:18888,--publish,127.0.0.1:<base+14000>:18889,--env,
    DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS=true,image; start,name when stopped. Dispose
    uses stop,--time,10,name only, never rm. Compare Config.Image and HostConfig.PortBindings
    before start. Selected image13.5.2 manifest exists (registry HEAD200;13.5.4 does not).
    Existing image/
    port mismatch is refused without replacement. Return http://127.0.0.1:base+14000. Host/node
    use chosen endpoint; connection publishes it for15-3 spans. Dispose stops only dashboard
    started/acquired for this host, after orchestrator/database stop, even keep_database=true.
    Pull nonzero exit/timeout throws InvalidOperationException `Instance telemetry image could not be pulled.`.
    Caller cancellation propagates; no create follows failed pull. Other failures throw
    InvalidOperationException `Instance telemetry could not be started.`.
R20. InstanceAdminClient owns only status/shutdown HTTP, not15-3 seat commands. Read tokenFile
    to Bearer header, GET connection.ApiUrl+/v1/admin/status; deserialize HostStatusResponse.
    Stop sends X-Aiakos-Client-Version and body keep_seats,keep_database to admin/shutdown;
    retain HostShutdownResponse.Warning for command renderer. StopAsync returns this response;
    all failures throw InstanceAdminException with ExitCode3/4/5/1 corresponding to unavailable/
    incompatible/auth/seat refusal. HTTP401 => `Instance authentication failed.`;
    409 VERSION_INCOMPATIBLE => `CLI and instance versions are incompatible.`;503 HOST_UNAVAILABLE
    (Reason=HOST_UNAVAILABLE) or refused/network/10s request timeout (Reason=null) => `Instance is unavailable.`;409 SEATS_RUNNING => fixed
    detail plus ordered addresses (JSON quoted, comma-space separated) for stop command. No token/body
    logging. Admin responses may be injected through HttpMessageHandler; no unbriefed15-3 prerequisite.
R23. InstanceProcessRunner implements IInstanceProcessRunner with ProcessStartInfo.ArgumentList,
    UseShellExecute=false, no inherited console, concurrently drained stdout/stderr, specified
    per-call timeout, owned-process-only cleanup. Captured streams stay in returned result only;
    never log raw stdout/stderr/environment/exception. Timeout throws TimeoutException
    `Instance process timed out.`; anticipated start failure throws InvalidOperationException
    `Instance process could not be started.`; cancellation stops its owned process then propagates.
    InstanceProcessRequest uses immutable argv/environment snapshots and redacted ToString.
    The runner is reused by database/dashboard/preparation; long-lived owned node launch is R12.
R22. Create admin DTOs and IInstanceHostControl in the projects/namespaces declared above,
    with fixed source-generated declaration order; no host routes, application wiring or new
    reason on non-admin routes. All secret-bearing public request/process/lease classes use
    redacted ToString. Ports/interfaces are owned by their corresponding behavior rule, not
    created as empty stubs by this DTO story.
R21. InstanceCommands guards Windows mutations through IInstancePlatform before any real services;
    status permits dev. Init maps config from request instance, explicit port-base (release7180),
    distro defaultUbuntu,node-id defaultwsl-local, operator Environment.UserName (in tests supplied
    by IInstancePlatform.UserName/UserProfile), home from layout, database external iff database-url-file present,
    telemetry false/null. Force only from flag. Run loads editable config and enters foreground
    host; production host runner supplies PayloadDirectory=Path.Combine(AppContext.BaseDirectory,
    "node","linux-x64"), including when executing from the versioned runtime copy. Never use cwd
    or a repository path. Start passes current installed source/version to starter; stop uses admin client and
    polls connection/lock every250ms until released,30s maximum, then prints stopped. Timeout=>
    exit1 stderr `Instance did not stop.\n`. dev uses ReadDevAsync; a live dev API returning503
    HOST_UNAVAILABLE=>exit3 stderr `Dev stack has no released instance host status.\n`. Missing/dead
    dev connection or network failure retains `Instance is unavailable.\n`. Never apply dev exemption
    to released discovery.
    Exit3 line `Instance is unavailable.\n` except lock errors retain R17 exact text; exit4 line
    `CLI and instance versions are incompatible.\n`; exit5 `Instance authentication failed.\n`.
    Other anticipated host failures exit1 exact exception message+LF; config errors exit2 fixed
    configuration messages. keep-seats warning from202 goes stderr+LF once; success prints only
    after stop completion. Blocked seats exit1,stderr detail+LF then `Seats: <quoted addresses>.\n`.

Package pins for R16 were checked against publisher pages:
[Serilog.Sinks.File](https://www.nuget.org/packages/Serilog.Sinks.File/7.0.0).

## Expected outputs and permanent tests

Base fixture: owned userProfile root, instance release, operator owner, base7180, Ubuntu,
WSL home .aiakos, node wsl-local, container postgres:18, dashboard=false, endpoint=null,
version0.1.0. All subprocess tests use recording ports and contain no authenticated owner state.

| ID | Case | Exact expectation |
|---|---|---|
| `E1` | layout/guard/JSON/overlap matrix | .aiakos and aiakos-release-postgres/aiakos-release-pgdata; named .aiakos-demo; forbidden dev message R1; port collision message R2; exact config field order/types R3; no secret fields; fixture JSON is the following document |
| `E2` | new/existing/force/init failure | true/new three secret files; false/existing zero writes/RNG; force lock refusal on Linux/Windows; force retains existing postgres password bytes; only two RNG calls when password exists; independent base64 values decoding to32bytes; explicit current-user-only ACL before content; no secret in output; config published last |
| `E3` | runtime new/same/changed/running/cancel/prune | bytes preserved; hash-identical zero replacement; safe version directory only; protected running mismatch exact R5 error; last two+protected copy; staging removed on failure; owner files unchanged |
| `E4` | extraction/composition regression | existing orchestrator health/migration-first/token/endpoint tests unchanged green; same registrations/routes in executable and in-process host; public Program remains |
| `E5` | shared WSL extraction/environment matrix | compatibility messages/signatures/cache unchanged; configurable prefixes and case rules R6; existing dev script byte-equivalent |
| `E10` | WSL install/version/argv matrix | matching VERSION zero copy; mismatch staging/chmod/rename then separate VERSION command; missing payload exact R7 error; no injected shell command |
| `E6` | restart/fake time/owned stop | delays1,2,4,8,16,30,30; uptime300s reset1; exit2/3 no retry; unexpected0 retry; cancellation delay zero restart; owned child stop then dispose once; LastExitCode exact; no owner process effect |
| `E7` | database recording ports | Docker unavailable => exact R10 error/no later effects; external => zero Docker calls; env-file ACL/delete/no secret argv; existing volume uses preserved password in its connection string; database timeout fixed error and zero later startup; missing image pull180s before create, failed pull exact R10 text, zero create |
| `E11` | lock sharing fixture | second writer refused with pid1234 version0.1.0 => Instance is locked by host 1234 (0.1.0).; unknown holder exact R17 text; holder JSON readable without flock on Linux and by shared read on Windows; disposal permits second writer |
| `E12` | host recording ports | lock, database, preflight, install, telemetry/logs, orchestrator, node, readiness publication; publication only after healthy+connected; stop signal flags/drain/node/orchestrator/database/dashboard/connection/lock exact R11; keep_database skips only database stop; partial startup owned cleanup |
| `E13` | held node process factory | exact argv/env/home helper bound R12; secret omitted from argv; streams drained into redacted logs; only owned child stopped |
| `E8` | readiness/connection discovery | supplied connection publication bytes match E8 golden; malformed/dead/unheld returns null; ReadDevAsync exempts only .aiakos-dev lock; owned removal exact R13 |
| `E14` | detached start | live healthy zero copy/launch; held/unreachable API fixed unavailable error; timeout owned cleanup; exact dotnet dll argv and parent lifetime R18 |
| `E15` | admin lifecycle routes | agent starting/present/unknown409 SEATS_RUNNING, absent/exited/human allow202; ordered addresses; warning in202; OnCompleted queues exact flags after response; /v1/version answers with shared composition alone |
| `E16` | status renderers | golden documents below; exact declaration order and LF R15 |
| `E17` | admin client | only status/shutdown routes, bearer/version/body exact;401/409/503/network fixed errors R20 |
| `E18` | command wiring | original CliApplication constructor remains; injected Windows platform can test on Linux; config defaults exact R21; G2 mutations fail before effects; stop waits for lock release/30s timeout; live dev503 HOST_UNAVAILABLE=>exit3 exact R21 dev line; packaged PayloadDirectory independent of cwd |
| `E9` | log matrix | UTC daily owned filenames,14day retention,secret redaction; owned sinks and ILogger properties/scopes redacted through R16 provider |
| `E19` | telemetry matrix | disabled zero exporter/dashboard; explicit endpoint zero Docker; named dashboard image/ports exact R19; mismatch refused, pull180s before create/failed pull exact R19 text; owned stop after database |
| `E22` | short-process adapter | exact argv/env snapshots, timeout/start fixed exceptions, cancellation/drain/only-owned cleanup R23 |
| `E21` | admin contract/policy goldens | exact E16 API document and E15 SEATS_RUNNING body serialize through generated context; existing15-2 ordinary problem retains three fields |
| `E23` | production composition/control | two loopback protocols/config keys R24; bind before reads; scoped persisted status/blockers; ILogger redaction; admission drain and app stop |
| `E20` | host project references | CLI Core/Wsl/Orchestrator/Api.Contracts references and ASP.NET framework; isolated pack/help starts per C4 |

E1 configuration golden (compact plus final LF):
```json
{"instance":"release","port_base":7180,"operator":"owner","wsl":{"distro":"Ubuntu","home":".aiakos","node_id":"wsl-local"},"database":{"mode":"container","image":"postgres:18"},"telemetry":{"dashboard":false,"otlp_endpoint":null}}
```

E1 extra cases: demo base7181 against initialized release7180 => `Instance ports overlap an initialized instance.`;
    demo base5170 against reserved dev => same message; external path `C:\secrets\db` accepted,
    `db.txt` and `/secrets/db` rejected with R3 FormatException. Sibling errors are tested by T2 only.
E3 copies A,B,C published at distinct UTC t1<t2<t3 => retain B,C; when runningVersion=A retain A,B,C.

E8 connection golden (compact+LF, otlp_endpoint retained null):
```json
{"api_url":"http://127.0.0.1:7181","pid":1234,"version":"0.1.0","started_at":"2026-10-20T08:15:00+00:00","otlp_endpoint":null}
```

Status fixture: release,pid1234,version0.1.0,start2026-10-20T08:15:00Z, healthy database/node,
zero seats, log_directory `/owned/.aiakos/logs`. E15 successful stop response is exactly
`{"api":"v1","warning":null}`; keep_seats warning is the exact R14 string. A blocking fixture
with impl@demo and lead@demo returns409 `{"reason":"SEATS_RUNNING","detail":"Run aiakos down --all before stopping the instance.","retryable":false,"seats":["impl@demo","lead@demo"]}`.
E16 API status golden (compact+LF), then renderer appends cli_version/version_match in that order:
```json
{"api":"v1","instance":"release","running":true,"healthy":true,"host_pid":1234,"host_version":"0.1.0","started_at":"2026-10-20T08:15:00+00:00","ports":{"grpc":7180,"api":7181,"postgres":7182,"hooks":7190,"dashboard":17180,"otlp":21180},"database":{"mode":"container","healthy":true},"nodes":[{"node":"wsl-local","connected":true,"last_exit_code":null,"last_error":null}],"seat_counts":{"absent":0,"starting":0,"present":0,"exited":0,"unknown":0},"log_directory":"/owned/.aiakos/logs"}
```
E16 text golden with matching CLI0.1.0 (UTF-8+LF, booleans lower-case, nodes supplied order):
```text
instance: "release"
running: true
host_pid: 1234
host_version: "0.1.0"
cli_version: "0.1.0"
version_match: true
ports: grpc=7180 api=7181 postgres=7182 hooks=7190 dashboard=17180 otlp=21180
database: mode="container" healthy=true
node: "wsl-local" connected=true last_exit_code=null last_error=null
seat_counts: absent=0 starting=0 present=0 exited=0 unknown=0
log_directory: "/owned/.aiakos/logs"
```
    Changed CLI version changes cli_version/version_match only and emits R15 warning. JSON renderer
    retains all status fields in golden order followed by cli_version,version_match, final LF.
    Host Healthy is /health healthy AND database healthy AND configured node connected; unhealthy
    status still renders full document/text and exits1. Empty nodes emits zero node text lines.

T1. Commit E1 layout/configuration golden and collision tests with exact serialized compact JSON;
    test mode/URI/port/slug/home bounds and no input mutation.
T2. Commit E2 transactional initializer and Windows ACL test inspecting actual temporary secret
    file ACLs; Linux tests exercise ordering with a security port, never claim Windows ACL proof.
T3. Commit E3 real temporary-directory runtime byte/hash/pruning/cancellation/link refusal tests.
T4. Commit E4 shared composition integration/regression tests; no duplicate
    service registration, no changed migration/health behavior; include existing suites.
T5. Commit E5 extraction, compatibility wrapper and configurable-prefix/case tests.
T10. Commit E10 shell-script fixture/argv/env/version tests; production script is exercised only in
    an owned opt-in Windows/WSL fixture, not an owner node installation.
T6. Commit E6 fake-time supervisor/policy tests including cancellation while running/delayed,
    start failure, reset boundary299s/300s, and only-owned process cleanup.
T7. Commit E7 recording-process database/argv/timeout/env-file cleanup tests; no owner containers.
T11. Commit E11 actual temporary lock sharing/writer/reader/release tests.
T12. Commit E12 host fake-port readiness/shutdown/flags/partial-cleanup sequencing tests.
T23. Commit E23 external private Postgres in-process composition/control tests, fake node,
    bound services, scoped query disposal, ILogger redaction and admission drain; no owner database.
T13. Commit E13 recording node factory/home/env/drain/only-owned stop tests.
T8. Commit E8 connection bytes/liveness/pid-reuse/publication/removal tests.
T14. Commit E14 starter fake-time/copy/launch/timeout tests. Windows opt-in
    owned demo must prove detached host survives parent terminal closure and attached WSL child
    remains alive; do not mark these risks closed based on Linux fakes.
T15. Commit E15 HTTP admin/auth/version/rejection/OnCompleted callback tests, with persisted seat fixture.
T16. Commit E16 exact status text/JSON bytes and version mismatch warning tests.
T17. Commit E17 HttpMessageHandler client request/error mapping tests.
T18. Commit E18 CLI injection/platform/init/default/error/stop wait tests.
T19. Commit E19 dashboard/explicit/disabled/mismatch/cleanup tests.
T22. Commit E22 process adapter/recording-snapshot/owned cancellation tests.
T21. Commit E21 DTO/context/admin-exception compatibility goldens.
T20. Commit E20 isolated pack/install/help using15-1 R9 temporary-only configuration.
T9. Commit E9 redaction/retention tests with sentinel secrets generated
    at test runtime; no committed token-like fixtures. T19 asserts disabled config overrides inherited OTEL.

## Author resolution of round-1 review

The public surfaces above now provide independent acceptance entry points for every story.
S7–S9 of the first split are replaced by separate process/lock/database/node/log/telemetry/
connection/admin/client/renderer/application concerns; see new split for ownership. No production
code or acceptance gate was changed/run. Node hook-port consuming configuration is not present
in current NodeOptions: lead assigns it to backlog #12 (12-2); no new variable is forwarded here. Architect review must judge these proposed resolutions before approval.

## Done

Release solution build0warnings/0errors; affected and earlier test suites green. One commit per
story, LF UTF-8 without BOM and final newline. Do not push/open PR. Report Windows-only checks
not run as unknown. Acceptance gates are authored later and run only by QA. Missing dependency
or packaged payload is named with source evidence, never substituted by a working-tree node.

## Out of scope

Seat actor/dispatcher/state changes; live up/down/send/capture/attach handlers; CLI trace export;
NuGet payload publishing/tag pipeline/global tool installation; logs of real owner sessions;
Windows service/logon/reboot recovery; automatic seat restart; remote listeners/TLS/tenants.
