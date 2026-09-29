# Spike 0003: Aspire AppHost launching a WSL node

Issue: [#3](https://github.com/aiakos-hq/aiakos/issues/3) · ADRs: [0004](../adr/0004-orchestrator-node-split.md),
[0011](../adr/0011-aspire-for-local-development.md) · Feeds: M1 solution skeleton (#9)

## Question

Can an Aspire AppHost on Windows start a process inside WSL (`wsl.exe -d Ubuntu -- …`), stop it
cleanly, and have it export OpenTelemetry to the Aspire dashboard and open a gRPC stream to a
Windows-hosted service?

**Short answer: yes.** `AddExecutable("node-wsl", "wsl.exe", …)` works. Stopping the resource or
the whole AppHost (Ctrl+C, hard kill of the AppHost, hard kill of DCP) always took the Linux
process down, so we saw no orphans. Traces, logs and metrics from the Linux node show up in the
dashboard, and the bidirectional gRPC stream to a Windows-hosted orchestrator works. There are
three conditions:

1. Environment variables do not cross into WSL unless they are listed in `WSLENV`.
2. The dashboard's OTLP URL that Aspire injects (`localhost`) has to be rewritten for WSL.
3. In the current **NAT** networking mode, Windows Firewall blocks WSL → Windows by default.
   Each listening Windows executable needs an inbound allow rule. Mirrored networking avoids this,
   which is why ADR 0011 asks for it. Mirrored mode was **verified** afterwards: everything runs
   over `127.0.0.1` with no firewall rules involved (see
   [Mirrored networking verification](#mirrored-networking-verification)).

## Environment

| | |
|---|---|
| Host | Windows 11 Home 10.0.26200 |
| WSL | 2.7.10.0, kernel 6.18.33.2-microsoft-standard-WSL2, distro `Ubuntu` (Ubuntu 26.04 LTS) |
| WSL networking | **NAT** (default; no `%USERPROFILE%\.wslconfig`). `ip route`: `default via 172.25.48.1 dev eth0`; eth0 `172.25.48.184/20`; Windows side is `vEthernet (WSL (Hyper-V firewall))` = `172.25.48.1` |
| .NET | SDK 10.0.401 on Windows (runtime 10.0.12). **No .NET in WSL.** The node is published self-contained for `linux-x64` on Windows and copied to `~/aiakos-spikes/0003/node` |
| Aspire | `Aspire.AppHost.Sdk/13.5.4`, `Aspire.Hosting.PostgreSQL` 13.5.4 (no templates or `aspire` CLI installed; plain `dotnet build`) |
| gRPC / OTel | Grpc.AspNetCore / Grpc.Net.Client / Grpc.Tools 2.84.0, Google.Protobuf 3.36.2, OpenTelemetry 1.19.1 (+ instrumentation 1.19.0) |
| Docker | Docker Desktop 29.8.0 (running), `postgres:18.3` pulled by Aspire |

## Steps

The spike code was a throwaway solution outside the repo, in the session scratch directory
`…\scratchpad\0003\` (it is not merged). The essential snippets are below.

```
0003/
  Protos/node.proto            NodeLink.Connect(stream NodeEnvelope) returns (stream OrchestratorEnvelope)
  Orchestrator/                ASP.NET Core gRPC server (bidi stream), OTel -> UseOtlpExporter()
  Node/                        Worker: dials orchestrator, Hello + heartbeat every 3 s, OTel logs/traces/metrics;
                               also "bridge" mode (see NAT without firewall rules)
  AppHost/                     Aspire AppHost (below)
  start-apphost.ps1            runs AppHost.exe with the "http" launch-profile env, hidden console, log to file
  deploy-node.ps1              dotnet publish Node -r linux-x64 --self-contained; cp to ~/aiakos-spikes/0003/node
  ctrlc/ctrlc.cs               AttachConsole + GenerateConsoleCtrlEvent: a real Ctrl+C to the AppHost
```

1. `dotnet publish Node -r linux-x64 --self-contained -c Release -o out/node` (85 MB, 230 files), then
   copy it into WSL: `cp -r /mnt/c/…/out/node ~/aiakos-spikes/0003/node && chmod +x …/Spike0003.Node`.
   Copy it rather than exec from `/mnt/c`, because 9P file access is slow.
2. `dotnet build AppHost`, then run `AppHost.exe` with the launch-profile env below. The test matrix:

| Run | Networking | Postgres | What was tested |
|---|---|---|---|
| 1 | NAT | – | first start, Ctrl+C to the AppHost |
| 2 | NAT | – | dashboard Stop/Start of `node-wsl`, `Stop-Process -Force` on the AppHost |
| 3 | NAT | – | gRPC + OTLP over the gateway IP (after firewall allow rules existed), `Stop-Process -Force` on the AppHost + DCP |
| 4–6 | bridge | yes | NAT without firewall rules, via reverse tunnel; kill AppHost + all `dcp.exe` |
| 7 | NAT | – | dashboard Stop/Start of the orchestrator (node reconnect), Ctrl+C |

### AppHost (essential part)

```csharp
var distro  = builder.Configuration["Wsl:Distro"] ?? "Ubuntu";
var nodeExe = builder.Configuration["Wsl:NodePath"] ?? "/home/bsakel/aiakos-spikes/0003/node/Spike0003.Node";
var mode    = builder.Configuration["Wsl:Networking"] ?? "nat";   // nat | mirrored
// How Linux processes in WSL reach services listening on Windows:
var windowsFromWsl = mode == "mirrored" ? "127.0.0.1" : WslNat.WindowsHostAddress();
const int OrchestratorPort = 5180;

var orchestrator = builder.AddProject<Projects.Orchestrator>("orchestrator")
    // Fixed, unproxied port: the WSL side must know it up front; DCP's proxy only binds localhost.
    .WithHttpEndpoint(port: OrchestratorPort, name: "grpc", isProxied: false)
    .WithEndpoint("grpc", e => e.TargetHost = mode == "nat" ? "0.0.0.0" : "localhost")
    .WithEnvironment("Kestrel__EndpointDefaults__Protocols", "Http2");   // h2c: gRPC without TLS

var pg = builder.AddPostgres("pg").AddDatabase("aiakos");                  // optional
orchestrator.WithReference(pg).WaitFor(pg);

builder.AddExecutable("node-wsl", "wsl.exe", workingDirectory: ".",
        "-d", distro, "--cd", "~", "--exec", nodeExe)                     // --exec: no shell in between
    .WithOtlpExporter()
    .WithEnvironment("AIAKOS_ORCHESTRATOR_URL", $"http://{windowsFromWsl}:{OrchestratorPort}")
    .WithEnvironment(ctx =>
    {
        // 1) Aspire injects the dashboard OTLP endpoint as an EndpointReference (localhost). Rewrite the host.
        if (ctx.EnvironmentVariables.TryGetValue("OTEL_EXPORTER_OTLP_ENDPOINT", out var ep) && ep is EndpointReference otlp)
            ctx.EnvironmentVariables["OTEL_EXPORTER_OTLP_ENDPOINT"] = ReferenceExpression.Create(
                $"{otlp.Property(EndpointProperty.Scheme)}://{windowsFromWsl}:{otlp.Property(EndpointProperty.Port)}");
        // 2) wsl.exe only forwards Windows env vars that are listed in WSLENV.
        var names = ctx.EnvironmentVariables.Keys
            .Where(k => k.StartsWith("OTEL_") || k.StartsWith("AIAKOS_") || k.StartsWith("DOTNET_"))
            .Select(k => k + "/u");
        var existing = Environment.GetEnvironmentVariable("WSLENV");
        ctx.EnvironmentVariables["WSLENV"] = string.Join(':', string.IsNullOrEmpty(existing) ? names : names.Prepend(existing));
    })
    .WaitFor(orchestrator);

static class WslNat   // NAT: Windows owns the WSL switch gateway = the vEthernet (WSL …) adapter address
{
    public static string WindowsHostAddress() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.Name.Contains("WSL", StringComparison.OrdinalIgnoreCase) && n.OperationalStatus == OperationalStatus.Up)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses)
        .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
        .Select(a => a.Address.ToString()).FirstOrDefault()
        ?? throw new InvalidOperationException("No vEthernet (WSL) adapter; is WSL in NAT mode?");
}
```

Launch profile (`Properties/launchSettings.json`, profile `http`):

```json
"applicationUrl": "http://localhost:15003",
"environmentVariables": {
  "ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL": "http://0.0.0.0:19003",
  "ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL": "http://localhost:20003",
  "ASPIRE_ALLOW_UNSECURED_TRANSPORT": "true"
}
```

In NAT mode the OTLP URL must be `0.0.0.0` (or the gateway IP). In mirrored mode `http://localhost:19003`
is enough. Fixed ports make the WSL-side config predictable.

### Node launch

DCP starts: `wsl.exe -d Ubuntu --cd ~ --exec /home/bsakel/aiakos-spikes/0003/node/Spike0003.Node`

What the node sees in Linux:

```
Node starting: pid 6795, ppid 6792 (Relay(6795)), stdin redirected True,
  orchestrator http://172.25.48.1:5180, otlp http://172.25.48.1:19003 (grpc), headers set True, service node-wsl
ps: 6795 6792 6795 6795 pts/11 Ssl+ /home/bsakel/aiakos-spikes/0003/node/Spike0003.Node   (parent 6792 = /init "Relay")
```

The process gets its own session and a **pseudo-terminal** (`pts/N`). It is the session leader,
and its parent is the WSL init relay for that `wsl.exe` invocation. On Windows, `wsl.exe` appears
twice (a parent/child pair) under `dcp.exe run-controllers`. The OTel env vars, including the
`x-otlp-api-key` header, arrive only because of `WSLENV`. A control test showed
`printenv SPIKE_FOO` fails without `WSLENV=SPIKE_FOO/u` and succeeds with it.

## Findings

### 1. Telemetry reaches the dashboard

- In runs 3 (NAT) and 6 (bridge), the dashboard lists `node-wsl` as a telemetry resource. **Structured
  logs** show "Sent heartbeat N" with trace links (46 entries after about 2 minutes). **Traces** show
  `node-wsl: node.heartbeat` spans. **Metrics** show the `Spike0003.Node/spike.node.heartbeats`
  counter plus the `System.Net.Http` and `System.Runtime` meters. The OTLP API key that Aspire sets
  in `OTEL_EXPORTER_OTLP_HEADERS` was accepted, so there was no need to turn off OTLP auth.
- Each of the three exporters (logs, traces, metrics) opens its own OTLP connection.
- **Trace propagation on a long-lived bidi stream:** the W3C context crosses the wire only once, on the
  `Connect` call. Per-message spans on the node (`node.heartbeat`) and on the orchestrator
  (`orchestrator.heartbeat`) are not linked, and the `Connect` span is not exported until the stream
  ends. **M1 contract implication:** put a `traceparent`/`tracestate` field in the stream envelope and
  start per-message activities from it.
- With `http` (h2c) everywhere, the dashboard shows a harmless "No trusted development certificate"
  banner.

### 2. gRPC stream to Windows

- NAT: the node connected to `http://172.25.48.1:5180`. The orchestrator logged
  `Node wsl-local connected from ipv4:172.25.48.184:34114`, followed by heartbeats every 3 s.
- The orchestrator's endpoint must be **unproxied with a fixed port**. `isProxied: false` plus
  `TargetHost = "0.0.0.0"` yields Kestrel `Now listening on: http://0.0.0.0:5180`. A DCP-proxied
  endpoint only binds `localhost`, and its port is only known at run time.
- Plain-HTTP gRPC needs `Kestrel__EndpointDefaults__Protocols=Http2` (h2c prior knowledge).
- Reconnect: stopping and restarting the orchestrator from the dashboard gave
  `Stream … failed: StatusCode="Unavailable"`. The node reconnected about 11 s later (backoff) and got
  `welcome` again.

### 3. Networking: NAT (current) versus mirrored

**NAT, as found**

- WSL → Windows goes to the gateway IP (`172.25.48.1`), which changes across reboots, so compute it at
  AppHost start (`WslNat.WindowsHostAddress()` above, or `ip route show default` inside WSL).
  `127.0.0.1` from WSL does **not** reach Windows (probe: `127.0.0.1:5180 FAIL`).
- **Windows Firewall blocks it by default.** On the first run, probes from WSL to `172.25.48.1:5180` and
  `:19003` timed out. Windows had created **inbound Block rules** (Private + Public) for
  `…\Orchestrator\bin\Debug\net10.0\orchestrator.exe` and `…\aspire.hosting.orchestration.win-x64\13.5.4\tools\dcp.exe`
  (the "allow access?" prompt that appears when a program first listens on a non-loopback address).
  Between run 2 and run 3 those rules were switched to **Allow**, and a `.NET Host`
  (`C:\program files\dotnet\dotnet.exe`) Allow rule appeared. **This spike did not make that change**;
  it was presumably done by answering the prompts. After that, NAT worked end to end.
  - These rules are **per executable path**. Every new worktree or bin path of the orchestrator
    (agents work in worktrees) prompts again. A cancelled prompt leaves a **Block rule, and Block
    overrides any Allow**, so it breaks silently: the connect hangs until the timeout.
  - The rules that matter are for the process that owns the socket. For the dashboard OTLP port that
    is `dcp.exe`, which proxies it. For the orchestrator it is `Orchestrator.exe`.
- With the OTLP URL set to `0.0.0.0`, DCP bound `:19003` on **every IPv4 address, including the LAN**
  (e.g. `192.168.x.y`). The API key protects it, but it is exposed. The dashboard UI (15003) and the
  resource service stay on localhost.
- Aspire displays and injects `http://localhost:19003` even when it is configured as `0.0.0.0`,
  which is why the rewrite is needed.

**NAT without firewall changes: reverse bridge (tested, not recommended)**

Windows → WSL `localhost` forwarding works in NAT mode (a WSL listener on `127.0.0.1:25999` answered
from Windows). A tiny reverse tunnel therefore gets around the firewall entirely:

- A Linux `bridge` process (a second `wsl.exe` resource) listens on `127.0.0.1:35180` for the node
  and on `127.0.0.1:25180` for Windows.
- An AppHost task pre-dials `127.0.0.1:25180` from Windows. When bytes arrive, it connects to
  `127.0.0.1:5180` and pipes the two. OTLP uses 49003 ↔ 39003 → 19003 in the same way.

Result: gRPC connected (`from ipv4:127.0.0.1:57361`), telemetry arrived (19 logs and 17 traces after
about 1 min), and no firewall rules were involved. **Pitfall discovered:** the Linux-side ports must
differ from the Windows ports. Localhost forwarding makes `wslrelay` bind *every* WSL listener port
on Windows `localhost` too. The first attempt used 5180 on both sides, and the orchestrator failed
with `Failed to bind to address http://127.0.0.1:5180: address already in use`. This is a
workaround only. Mirrored mode makes it unnecessary.

**Mirrored (ADR 0011): verified.** WSL reaches Windows services on `127.0.0.1`. The orchestrator and
the dashboard's OTLP endpoint stay bound to `localhost`, and the AppHost uses
`windowsFromWsl = "127.0.0.1"`. See [Mirrored networking verification](#mirrored-networking-verification).

**HTTPS / dev cert:** the spike used `http` (h2c) with `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true`. For
HTTPS from Linux, the ASP.NET dev cert (CN=localhost) would have to be exported
(`dotnet dev-certs https --export-path … --format PEM`) and trusted in the distro
(`/usr/local/share/ca-certificates` + `update-ca-certificates`, or `SSL_CERT_FILE`). In NAT mode it
still would not match the gateway IP. Recommendation: use plain HTTP on loopback for local dev, and
TLS for remote nodes (Tailscale).

### 4. Process lifetime: no orphans observed

The node logs the POSIX signals it receives to `~/aiakos-spikes/0003/node.pids`.

| How it was stopped | Windows side | Linux node |
|---|---|---|
| Dashboard **Stop** on `node-wsl` | DCP kills `wsl.exe` | gets **SIGHUP**, gone at once. **Start** relaunches it (new pid) |
| **Ctrl+C** to the AppHost | AppHost, DCP, dashboard, orchestrator and `wsl.exe` exit; Postgres container removed | SIGHUP, gone |
| `Stop-Process -Force` on the AppHost | DCP (`start-apiserver --monitor <apphost pid>`) notices and tears everything down within about 10 s | SIGHUP, gone |
| `Stop-Process -Force` on the AppHost **and every `dcp.exe`** (including the `monitor-process` helpers) | `wsl.exe` and the orchestrator still died (DCP-launched children appear to be tied to DCP, probably through a job object) | SIGHUP, gone. **The Postgres container and the `aspire-session-network-*` Docker network were left behind** (removed by hand) |

- The mechanism is the pseudo-terminal. When `wsl.exe` dies, the relay closes the pty master and the
  kernel sends SIGHUP to the session leader, which is the node.
- .NET's **default SIGHUP handling is abrupt**: no `ProcessExit`, no host shutdown, no telemetry
  flush. With a `PosixSignalRegistration` for SIGHUP that sets `Cancel = true` and calls
  `StopApplication()`, the node shut down gracefully (`got SIGHUP` … `exiting`). The node never saw
  SIGTERM in any scenario. From the Linux side, SIGHUP is the only stop signal.
- On Ctrl+C the dashboard stops at the same time as the node, so the node's final OTLP flush hits a
  dead endpoint. Graceful exit took about 10 s (the exporter timeout) instead of about 0.1 s. Keep the
  shutdown export timeout short.
- Mitigations, only if orphans ever show up (they did not here):
  1. **Treat SIGHUP as a graceful stop** in the node. Do this for M1.
  2. A single-instance lock: the node takes `flock` on `~/.aiakos/node.lock` and records its pid; a
     new instance SIGTERMs a stale holder or refuses to start and says so (rule 3, honest state).
  3. Optional dev-only watchdogs, both implemented and not needed: exit on stdin EOF; exit when the
     parent pid changes (reparented away from the relay).
  4. Do **not** make the node exit when the gRPC stream drops. It has to survive orchestrator
     restarts (verified: it reconnects).
  - Children that daemonize (a tmux server) survive the node **by design**. That is what lets
    sessions outlive node restarts (spike #1/#2), so they are not orphans.

## Pitfalls (checklist)

1. **`WSLENV`**: without it, none of Aspire's `OTEL_*` or your own variables reach the Linux process.
   Append to any existing `WSLENV`; do not overwrite it. `/u` = only Windows → WSL.
2. `OTEL_EXPORTER_OTLP_ENDPOINT` is an **`EndpointReference`**, not a string, in Aspire 13.x.
   Rewrite it with `ReferenceExpression.Create(...)` and `EndpointProperty.Scheme/Port`.
3. `ReferenceExpression`/`WithEnvironment` interpolation does not accept an `int` hole
   (`CS0315`). Format the string first, or use `EndpointProperty.Port`.
4. The orchestrator endpoint must be `isProxied: false` with a fixed port, and `0.0.0.0` in NAT mode.
5. `Kestrel__EndpointDefaults__Protocols=Http2` is needed for h2c gRPC.
6. **NAT + Windows Firewall**: prompts per exe path; a cancelled prompt becomes a Block rule that beats
   any Allow. `dcp.exe` owns the OTLP port.
7. A `0.0.0.0` OTLP URL makes DCP listen on the LAN too.
8. **WSL localhost forwarding binds WSL listener ports on Windows**, so a WSL service on port P can
   stop a Windows service on P from starting. Keep WSL-side and Windows-side ports distinct.
9. SIGHUP (not SIGTERM) is how the node learns it is being stopped. Handle it.
10. A hard kill of DCP leaks containers and the session network.
11. Build warning `ASPIRE010` (`AspireUseCliBundle=false`) with `Aspire.AppHost.Sdk/13.5.4` when not
    using the `aspire` CLI. It was suppressed with `NoWarn`. Decide in #9 whether we standardize on
    `aspire run` (CLI bundle) or plain `dotnet run`.
12. The Aspire dashboard has a `/api/telemetry/*` HTTP API (401 without an API key). It could be used
    later for automated telemetry assertions. The spike checked the UI instead.
13. Run the node from the WSL filesystem, not `/mnt/c`, and publish it self-contained. The distro
    has no .NET, and none is needed.

## Mirrored networking verification

**Result: PASS.** This was run after the other spikes had finished, because it needs a WSL restart.
Run `run8-mirrored` gave these results:

- `wsl.exe -d Ubuntu -- ip -br addr` shows the Windows NIC address on `eth0`, and the default route
  is the LAN router. There is no `172.x` address.
- In WSL, `ss -tn` shows the node's connections to `127.0.0.1:5180` (gRPC) and to `127.0.0.1:19003`
  (OTLP; one per exporter).
- The orchestrator console log shows `Heartbeat N from ipv4:127.0.0.1:<port>` for the node
  `wsl-local`.
- The dashboard shows 43 structured logs for `node-wsl`, 52 `node-wsl: node.heartbeat` traces, and
  `node-wsl` metrics (`Spike0003.Node`, `System.Net.Http`, `System.Runtime`), all after about 2
  minutes.
- Ctrl+C on the AppHost (the `ctrlc` helper) stopped everything: the AppHost exited, no
  `Spike0003.Node` process was left in WSL, and all `dcp.exe` processes exited within seconds.
- No firewall prompt appeared. The traffic is loopback, which the inbound firewall rules do not
  filter.

Not rechecked in mirrored mode: a **fresh** orchestrator output path (to rule out the old NAT-era
allow rules helping), and pitfall 8 (shared port space). Loopback makes the first one moot. The
second one is inherent to mirrored mode, so M1 must keep the orchestrator and node ports distinct.

The steps that were followed:

1. Add to `%USERPROFILE%\.wslconfig` (the file does not exist today):

   ```ini
   [wsl2]
   networkingMode=mirrored
   # optional, recommended with mirrored:
   # dnsTunneling=true
   # firewall=true        ; Hyper-V firewall (default true) applies to inbound *into* WSL
   ```

   Do not set `hostAddressLoopback`. It is only needed to reach Windows via the host's LAN IP, and
   `127.0.0.1` is the goal here.
2. `wsl --shutdown`, then start `Ubuntu` again. Check with `wsl.exe -d Ubuntu -- ip addr`: the
   Windows NIC addresses should now appear, and there should be no `172.x` eth0.
3. Rerun the spike with `Wsl:Networking=mirrored` and `ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL=http://localhost:19003`
   (`start-apphost.ps1 -Networking mirrored`). The orchestrator binds `localhost:5180`, and the node
   gets `AIAKOS_ORCHESTRATOR_URL=http://127.0.0.1:5180` and OTLP `http://127.0.0.1:19003`.
4. Verify: the orchestrator logs `Node wsl-local connected from ipv4:127.0.0.1:…`; `node-wsl`
   structured logs, traces and metrics show in the dashboard; there is **no** firewall prompt, and a
   **fresh** orchestrator path works too (build into a new output dir). Also confirm pitfall 8 still
   holds, since in mirrored mode Windows and WSL share the port space.

## Decision / follow-ups

- **ADR 0011 stands.** The `wsl.exe` launch path works, lifetime is clean, and telemetry flows. The
  NAT experience (firewall prompts per exe path, gateway IP discovery, LAN-exposed OTLP) is a strong
  argument for **mirrored networking as the documented dev prerequisite**, and the verification
  above confirms it works. NAT remains possible with firewall allow rules. The reverse bridge is a known escape hatch;
  do not build it unless mirrored mode fails.
- **For the M1 solution skeleton (#9):**
  - AppHost: the `AddExecutable("node-wsl", "wsl.exe", "-d", distro, "--cd", "~", "--exec", nodePath)`
    shape above, with **WSLENV propagation and OTLP-endpoint rewrite as a reusable extension**, e.g.
    `builder.AddWslExecutable(name, distro, path).WithWslOtlpExporter()`. Distro and node path come
    from configuration. Networking mode comes from config (`mirrored` default, `nat` supported).
  - Fixed, unproxied orchestrator gRPC port (h2c on loopback in dev), passed to the node as an env var.
    The node identity token also goes through env/`WSLENV` (rule 2).
  - Node deployment in dev: `dotnet publish -r linux-x64 --self-contained` from Windows, then copy into
    the WSL filesystem as an AppHost pre-start step (or a `dotnet publish` target). Since the bootstrap
    rule says the rig runs on the *released* build, the dev AppHost always points at the working-tree
    build and never at a running rig.
  - Node: handle SIGHUP as a graceful stop; reconnect with backoff; single-instance lock; short OTLP
    shutdown timeout.
  - gRPC contract (M1): carry `traceparent` per stream message so orchestrator ↔ node spans correlate.
  - Consider a dashboard-API-based smoke test (pitfall 12) once there is CI on Windows.
- No firewall rule, `.wslconfig` change or WSL restart was made by this spike. The per-program
  Allow rules for `orchestrator.exe` (spike scratch path), `dcp.exe` and `dotnet.exe` now exist on
  this machine because of the prompts. They can be removed in *Windows Defender Firewall → Inbound
  Rules* if unwanted.
