---
id: 0001
title: Solution skeleton
status: accepted         # draft | accepted | implemented | superseded
issue: https://github.com/aiakos-hq/aiakos/issues/9
milestone: M1
owner: "@bsakel"
---

# 0001 — Solution skeleton

## Context

Issue [#9](https://github.com/aiakos-hq/aiakos/issues/9) creates the first code in the repository:
the .NET 10 solution, the Aspire AppHost that starts Postgres, the orchestrator and a node agent
inside WSL, the DbUp migration setup, OpenTelemetry wiring and a CI build. Every other M1 spec
builds on it, so it fixes the layout and the conventions that later specs must not have to revisit.

Decisions this spec implements (not reopened here):

- [ADR 0002](../adr/0002-dotnet-and-akka-for-live-entities.md): .NET 10 / C#, Akka.NET via
  Akka.Hosting for live entities only.
- [ADR 0003](../adr/0003-postgres-dbup-dapper.md): Postgres, embedded `.sql` migrations applied by
  DbUp at startup, Dapper through thin repositories, `tenant_id` on every table from the first
  migration.
- [ADR 0004](../adr/0004-orchestrator-node-split.md): orchestrator and node agent are separate
  processes; the node dials out over gRPC.
- [ADR 0008](../adr/0008-bootstrap-rule.md): the team runs on the last release, so instances must
  be isolatable by home directory, port and database.
- [ADR 0011](../adr/0011-aspire-for-local-development.md): Aspire AppHost for local development,
  WSL node launched via `wsl.exe`, mirrored networking, Aspire dashboard as the telemetry UI.
- Plan [§3](../plan.md#3-architecture) (components), [§4](../plan.md#4-technology-decisions)
  (technology table), [§6](../plan.md#6-node-profiles-and-auth) (the `local` node profile needs
  mirrored networking), [§9](../plan.md#9-multitenancy-seams-build-now-activate-later) (tenant
  seams), [§10](../plan.md#10-roadmap-each-milestone-mvp-complete) (M1 scope) and
  [§11](../plan.md#11-how-we-build-it--aiakos-builds-aiakos) (bootstrap rule).

The main evidence is [spike 0003](../spikes/0003-aspire-wsl-node.md) (Aspire launching a WSL node).
Its concrete findings are carried over below as requirements, and each one cites the finding.
Spikes [0001](../spikes/0001-claude-wsl-tmux-hooks.md), [0002](../spikes/0002-claude-session-resume.md)
and [0005](../spikes/0005-docker-seat.md) affect only the reserved seams (node-side hook ingest,
a possible hook relay binary, seat images).

Specs written in parallel own neighbouring parts. This spec only reserves their places:

| Topic | Owner |
|---|---|
| gRPC contract (proto, services, messages, node authentication semantics, per-message trace context) | spec 0002, issue [#10](https://github.com/aiakos-hq/aiakos/issues/10) |
| tmux `ISessionHost` | issue [#11](https://github.com/aiakos-hq/aiakos/issues/11) |
| Claude Code adapter (`IHarnessAdapter`), node-side hook ingest | issue [#12](https://github.com/aiakos-hq/aiakos/issues/12) |
| `SeatActor` and its tables | issue [#13](https://github.com/aiakos-hq/aiakos/issues/13) |
| Rig file format | spec 0003, issue [#14](https://github.com/aiakos-hq/aiakos/issues/14) |
| CLI, `dotnet tool` packaging, the released instance's runtime | issue [#15](https://github.com/aiakos-hq/aiakos/issues/15) |

## Goals

- A clean clone builds with one command and zero warnings, and its tests pass with one command.
- `dotnet run --project src/Aiakos.AppHost` on the Windows dev machine starts Postgres, the
  orchestrator and a node agent in WSL. The node reaches the orchestrator over gRPC, and all three
  show logs, traces and metrics in the Aspire dashboard.
- The database is migrated at orchestrator startup. The first migration creates the tenant table
  and the single default tenant, and a test enforces rule 6 for every later table.
- The node behaves as spike 0003 recommends: graceful stop on SIGHUP, reconnect with backoff,
  single-instance lock, bounded final telemetry flush.
- A GitHub Actions workflow builds and tests every pull request.
- The dev stack can run next to the released tool without sharing a home directory, port or
  database (bootstrap rule).
- Empty, named seams exist for the gRPC contract and the rule-5 interfaces, and the existing
  `Aiakos.Cli` placeholder tool joins the shared build, so the next specs add code without moving
  projects.

## Non-goals / out of scope

- The gRPC contract, node registration and token validation (spec 0002). The skeleton proves
  connectivity with the standard gRPC health service instead (R34).
- Any `ISessionHost`, `ISandbox`, `IHarnessAdapter`, `ISeatChannel` or `IChatConnector` behaviour.
  They exist only as empty placeholder interfaces (R43).
- Actors other than the empty `ActorSystem`; tables other than `tenant`.
- The real CLI, publishing to NuGet, the released instance's process model, release pipeline (#15).
  The skeleton only adopts the existing `Aiakos.Cli` placeholder into the shared build (R48).
- Native-AOT publishing. The node is marked AOT-compatible but published self-contained, non-AOT
  (D10).
- NAT networking. It is not supported (CLAUDE.md); the AppHost detects it and refuses (R25).
- TLS, Tailscale, remote or sandboxed nodes (M6), `aspire publish` / Docker Compose (M6).
- Row-level security and multitenancy activation (M8).
- Blazor UI, REST API, MCP server, chat connectors.
- Several dev stacks at once on one machine (D11). M1 assumes one dev AppHost per machine and
  makes a second one fail loudly.
- A Windows CI runner and automated WSL end-to-end tests in CI (D7).
- Serilog (D8).

## Requirements

### Repository and build

- **R1** The solution file is `Aiakos.slnx` at the repository root and contains every project
  under `src/` and `tests/`.
- **R2** Projects follow the layout in [Design → Layout](#layout). Production projects live in
  `src/`, test projects in `tests/`. Every project and root namespace starts with `Aiakos.`.
- **R3** `global.json` pins the .NET SDK to feature band `10.0.100` or later
  (`"rollForward": "latestFeature"`), pins `Aspire.AppHost.Sdk` under `msbuild-sdks`, and selects
  Microsoft.Testing.Platform as the `dotnet test` runner.
- **R4** `Directory.Build.props` at the root sets for every project: `TargetFramework` `net10.0`,
  `Nullable` `enable`, `ImplicitUsings` `enable`, `TreatWarningsAsErrors` `true`, `AnalysisLevel`
  `latest-recommended`, `EnforceCodeStyleInBuild` `true`, `GenerateDocumentationFile` `true` with
  `CS1591` in `NoWarn` (needed for IDE0005 in builds), `Deterministic` `true`,
  `ContinuousIntegrationBuild` `true` when `GITHUB_ACTIONS` is `true`, `IsPackable` `false`
  (`Aiakos.Cli` opts in, R48), `NuGetAudit` with `NuGetAuditMode` `all`, and the Apache-2.0 license
  and repository metadata.
- **R5** Central package management: `Directory.Packages.props` with
  `ManagePackageVersionsCentrally` and `CentralPackageTransitivePinningEnabled` set to `true`.
  No `PackageReference` in any project file carries a `Version` attribute.
- **R6** `nuget.config` clears inherited sources, adds only nuget.org, and maps every package to it
  (`packageSourceMapping`, pattern `*`).
- **R7** Warning suppressions are local and justified: no repository-wide `NoWarn` other than
  `CS1591`. Any other suppression is in the one project or file that needs it, with a comment that
  says why (for example ASPIRE010 in the AppHost, R9).
- **R8** All new text files are LF, as `.gitattributes` already enforces. `*.ps1` keeps CRLF.
  `.gitignore` gains `artifacts/`.
- **R48** The existing placeholder tool `src/Aiakos.Cli` (merged in #31 to claim the NuGet ID, #6)
  is adopted, not recreated: it joins `Aiakos.slnx`; the settings that `Directory.Build.props`
  now provides (`TargetFramework`, `Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors`,
  `Authors`, license and repository metadata) are removed from its project file; any package it
  gains later is versioned centrally. It keeps its tool identity unchanged: `PackageId` `Aiakos`,
  `ToolCommandName` `aiakos`, `AssemblyName` `aiakos`, `Version` `0.0.1-preview.1`,
  `RollForward` `Major`, `IsPackable` `true`, `PackAsTool` `true` and its packed `README.md`. Its
  behaviour does not change; #15 replaces the code under the same ID.

### AppHost and configuration

- **R9** `Aiakos.AppHost` uses `Aspire.AppHost.Sdk` 13.5.x (the version verified in spike 0003)
  and defines these resources: `postgres` (container) with database `aiakos`, `orchestrator`
  (project), `node-publish` and `node-install` (one-shot executables, R19–R21) and `node-wsl`
  (the node in WSL). The build warning ASPIRE010 is suppressed only in `Aiakos.AppHost.csproj`,
  with a comment pointing to spike 0003 pitfall 11 (D1).
- **R10** The AppHost uses a single `http` launch profile with fixed dashboard ports (R15) and
  `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true`. All local traffic is plain HTTP on loopback; the
  "No trusted development certificate" banner is expected (spike 0003 §1, §3 "HTTPS / dev cert").
- **R11** All instance-specific values come from the `Aiakos` configuration section of the AppHost
  (`appsettings.json`, overridable by user secrets, environment and command line). There are no
  other hard-coded ports, paths, distro names or database names. Keys and defaults are in
  [Design → Configuration](#configuration).
- **R12** `postgres` is a persistent container (`ContainerLifetime.Persistent`) with a named data
  volume that includes the instance name (`aiakos-<instance>-pgdata`), so data survives AppHost
  restarts and the container is not left over by a hard kill of DCP (spike 0003 pitfall 10). The
  image tag is pinned to a Postgres major version (18). The generated password parameter is
  persisted in the AppHost's user secrets, so it matches the volume on the next start.
- **R13** The orchestrator gRPC endpoint is **unproxied, on a fixed port, bound to localhost**, and
  serves **HTTP/2 only without TLS (h2c)**. The port comes from configuration and is passed to the
  node as `AIAKOS_ORCHESTRATOR_URL=http://127.0.0.1:<port>` (spike 0003 §2, pitfalls 4 and 5).
- **R14** The orchestrator has a second endpoint, `http`, which Aspire allocates and proxies, for
  `/health` and `/alive` over HTTP/1.1. Protocols are set per endpoint, not through
  `Kestrel__EndpointDefaults__Protocols`, because an HTTP/2-only endpoint would fail Aspire's
  HTTP/1.1 health probe. gRPC services are only reachable on the `grpc` endpoint.
- **R15** The dashboard UI, the dashboard OTLP (gRPC) endpoint and the resource service use fixed
  ports from the launch profile (15180, 19180, 20180 for the dev instance). No WSL-side listener
  may use a port that a Windows-side listener uses, because mirrored networking shares one port
  space (spike 0003 pitfall 8).
- **R16** The AppHost creates a secret parameter `node-token` (generated, persisted in user
  secrets) and passes it to the orchestrator and to `node-wsl` (`AIAKOS_NODE_TOKEN`). Its
  validation belongs to spec 0002; the skeleton only transports it (rule 2: identity from the
  environment).
- **R17** `orchestrator` waits for `postgres`; `node-wsl` waits for the orchestrator to be healthy
  (`/health`) and for `node-install` to complete.
- **R18** The AppHost refuses to start (with an error naming the offending key) when the
  configured instance collides with the released tool's defaults: the instance name `release`, the
  released WSL home `.aiakos`, or the released port base (R46). This is the dev-side half of the
  bootstrap rule.

### Node deployment into WSL

- **R19** `node-publish` runs `dotnet publish src/Aiakos.Node -c Debug -r linux-x64
  --self-contained -o artifacts/node/linux-x64` on Windows. WSL needs no .NET (spike 0003 pitfall 13).
- **R20** `node-install` runs through `wsl.exe` in the configured distro. It converts the Windows
  publish path with `wslpath -u` (never by string manipulation), copies it to
  `$AIAKOS_HOME/node.staging`, marks the executable with `chmod +x`, and then swaps it into
  `$AIAKOS_HOME/node` (remove old, rename staging). The node always runs from the WSL filesystem,
  never from `/mnt/c` (spike 0003 step 1, pitfall 13).
- **R21** `node-publish` and `node-install` appear in the dashboard with their output. A failure
  in either stops `node-wsl` from starting and is visible as a failed resource (rule 3).

### AppHost extension for WSL executables

- **R22** `Aiakos.Hosting.Wsl` provides the reusable AppHost API from spike 0003's follow-ups:
  `AddWslExecutable`, `WithWslEnvironment` and `WithWslOtlpExporter`
  ([Design → WSL extension](#wsl-extension)). The AppHost uses only this API for WSL resources.
- **R23** `AddWslExecutable(name, distro, linuxPath, args…)` launches
  `wsl.exe -d <distro> --cd ~ --exec <linuxPath> <args…>`. `--exec` means no shell sits in between
  (spike 0003 AppHost snippet), so `linuxPath` is either absolute or relative to the WSL user's
  home; `~` is **not** expanded.
- **R24** `WithWslEnvironment(prefixes…)` sets `WSLENV` so that every environment variable of the
  resource whose name starts with one of the prefixes (default `OTEL_`, `AIAKOS_`, `DOTNET_`)
  crosses into WSL with the `/u` flag. It appends to an existing `WSLENV` and never overwrites it
  (spike 0003 pitfall 1).
- **R25** `WithWslOtlpExporter()` calls `WithOtlpExporter()` and then rewrites the host of the
  injected `OTEL_EXPORTER_OTLP_ENDPOINT`, which is an `EndpointReference`, to `127.0.0.1` with
  `ReferenceExpression.Create(...)` from `EndpointProperty.Scheme` and `EndpointProperty.Port`
  (spike 0003 pitfalls 2 and 3). It also forwards `OTEL_*` through `WSLENV`.
- **R26** Before `node-wsl` starts, the extension runs a preflight in the distro and fails the
  resource with an actionable message if: the distro does not exist; or the networking mode is not
  `mirrored` (message names `%USERPROFILE%\.wslconfig`, `networkingMode=mirrored` and
  `wsl --shutdown`). NAT is not supported and there is no fallback (CLAUDE.md, rule 3).

### Orchestrator

- **R27** `Aiakos.Orchestrator` is an ASP.NET Core app (`Microsoft.NET.Sdk.Web`) that hosts an
  Akka.NET `ActorSystem` named `aiakos` through Akka.Hosting, with Akka logging routed to
  `ILogger` and no remoting, clustering or persistence. The actor system stops with the host.
- **R28** At startup, before any endpoint serves traffic, the orchestrator applies pending
  migrations (R30). If a migration fails, it logs the script name and the error and exits with a
  non-zero code; it never serves on a partially migrated database.
- **R29** `/health` reports healthy only when the database is reachable and migrations are
  applied; `/alive` reports process liveness. The gRPC health service (`grpc.health.v1.Health`)
  on the `grpc` endpoint mirrors `/health`.

### Database

- **R30** `Aiakos.Data` holds migrations as embedded resources `Migrations/NNNN_snake_case.sql`
  (four digits, ordered by name) and a migrator built on DbUp for PostgreSQL that runs each script
  in its own transaction and holds a Postgres advisory lock for the whole upgrade.
- **R31** Application tables live in schema `aiakos`. The DbUp journal lives in
  `aiakos_meta.schema_versions` and is the only table exempt from rule 6 (D4).
- **R32** Migration `0001_tenant.sql` creates `aiakos.tenant` and inserts the default tenant
  ([Design → Database](#database)).
- **R33** A test fails if any table in schema `aiakos` lacks a `tenant_id uuid NOT NULL` column
  (rule 6). Migrations are forward-only; a merged migration is never edited.

### Node agent

- **R34** `Aiakos.Node` is a Generic Host worker whose executable is named `aiakos-node`. In the
  skeleton, its one job is to keep a connection to the orchestrator: on each connect it calls
  `grpc.health.v1.Health/Check` (unary) and then holds a `Health/Watch` stream. Spec 0002 replaces
  both calls with the real `Connect` stream; the connection loop (R35) stays.
- **R35** The node reconnects with exponential backoff and jitter (1 s, doubling, capped at 30 s,
  ±20 % jitter, reset after a successful connect). It never exits because the orchestrator is
  unavailable, and it logs each state change (`connecting`, `connected`, `unavailable`, with the
  next retry delay) (spike 0003 §2 "Reconnect", mitigation 4).
- **R36** The node treats **SIGHUP** as a graceful stop: a `PosixSignalRegistration` sets
  `Cancel = true` and calls `IHostApplicationLifetime.StopApplication()`. SIGTERM and SIGINT keep
  the Generic Host's default graceful handling (spike 0003 §4, pitfall 9).
- **R37** The final telemetry flush on shutdown is bounded (default 2 s), so a stop during which
  the dashboard is already gone still exits in about 3 s rather than the 10 s exporter default
  (spike 0003 §4).
- **R38** Single-instance lock: at startup the node takes an exclusive lock on
  `$AIAKOS_HOME/node.lock` and writes its pid into it. If the lock is held, it exits with code 3
  and a message that names the lock file and the holder's pid. It never kills the holder: the lock
  is released by the kernel when its holder dies, so a held lock means a live process (spike 0003
  mitigation 2, rule 3).
- **R39** Required settings (`AIAKOS_ORCHESTRATOR_URL`, `AIAKOS_HOME`, `AIAKOS_NODE_ID`) are
  validated at startup. If one is missing or invalid, the node exits with code 2 and names it.
  There is no default orchestrator URL (rule 3: no silent fallbacks). `AIAKOS_HOME` may be
  absolute or relative to `$HOME`; a leading `~/` is expanded by the node.
- **R40** The node is marked `IsAotCompatible` and must not reference `Aiakos.Orchestrator`,
  `Aiakos.Data`, Akka or Npgsql (ADR 0004: no business logic on the node). It may reference
  ASP.NET Core, for the hook ingest only: Kestrel through `WebApplication.CreateSlimBuilder`
  (AOT-supported), bound to `127.0.0.1` at port base + 10 (spec 0005).

### Telemetry

- **R41** `Aiakos.ServiceDefaults` provides `AddAiakosServiceDefaults()` for
  `IHostApplicationBuilder`, used by the orchestrator and the node. It registers OpenTelemetry
  logs, traces and metrics, and it exports through OTLP only when `OTEL_EXPORTER_OTLP_ENDPOINT` is
  set. It has no ASP.NET Core dependency; hosts that serve HTTP add ASP.NET Core instrumentation
  themselves.
- **R42** Every component emits telemetry under its own `ActivitySource` and `Meter`
  (`Aiakos.Orchestrator`, `Aiakos.Node`), and the resource carries `service.name` (from
  `OTEL_SERVICE_NAME`), `aiakos.instance` and, on the node, `aiakos.node.id`.

### Seams

- **R43** The rule-5 interfaces exist as empty placeholders in `Aiakos.Core`: `ISessionHost`,
  `ISandbox`, `IHarnessAdapter`, `ISeatChannel`, `IChatConnector`. Each XML doc comment names the
  issue that designs it. Later specs may move them; this spec does not define their members.
- **R44** `Aiakos.Contracts` exists with the Protobuf/gRPC build wiring shown in [Design → Layout](#layout) and no `.proto`
  files. Spec 0002 adds them. Both the orchestrator and the node reference this project.

### CI

- **R45** `.github/workflows/ci.yml` runs on pull requests to `main`, pushes to `main` and manual
  dispatch, on `ubuntu-latest`, with `permissions: contents: read`. It restores, builds in
  Release (warnings are errors), runs all tests (Postgres through Docker on the runner), publishes
  the node for `linux-x64` self-contained, packs `Aiakos.Cli` (R48), and uploads test results. Runs on the same PR cancel
  each other.

### Bootstrap rule

- **R46** An *instance* is the tuple (instance name, WSL home, port base, database). The dev
  AppHost and the released tool use different defaults for every element
  ([Design → Side by side with the released tool](#side-by-side-with-the-released-tool)), and the
  defaults live in one place (`Aiakos.Core.InstanceDefaults`) so that the AppHost guard (R18) and
  the CLI (#15) read the same values.

### Documentation

- **R47** The implementation PR replaces the "Commands" section of `CLAUDE.md` with the block in
  [Design → Commands](#commands-for-claudemd) and updates the "Current stage" text for the code
  that now exists.

## Design

### Layout

```
/
├─ Aiakos.slnx
├─ global.json                  SDK 10.0.1xx+, Aspire.AppHost.Sdk pin, test runner = MTP
├─ nuget.config                 nuget.org only, package source mapping
├─ Directory.Build.props        shared build settings (R4)
├─ Directory.Packages.props     central package versions (R5)
├─ .github/workflows/ci.yml     (R45)
├─ src/
│  ├─ Aiakos.AppHost/           Aspire AppHost; dev only, never shipped
│  ├─ Aiakos.Hosting.Wsl/       AppHost extension for WSL executables (R22–R26)
│  ├─ Aiakos.ServiceDefaults/   OpenTelemetry + health-check registration (R41)
│  ├─ Aiakos.Core/              dependency-free shared types: InstanceDefaults, TenantIds,
│  │                            rule-5 placeholder interfaces (R43, R46)
│  ├─ Aiakos.Contracts/         gRPC/protobuf; content owned by spec 0002 (R44)
│  ├─ Aiakos.Data/              DbUp migrator, embedded migrations, Dapper conventions (R30–R33)
│  ├─ Aiakos.Orchestrator/      ASP.NET Core + Akka.Hosting + gRPC server (R27–R29)
│  ├─ Aiakos.Node/              node agent "Ergates"; executable aiakos-node; Kestrel hook
│  │                            ingest from spec 0005 (R34–R40)
│  └─ Aiakos.Cli/               EXISTING placeholder `dotnet tool` (PackageId Aiakos, command
│                               aiakos); adopted as is (R48), replaced by #15
└─ tests/
   ├─ Directory.Build.props     imports the root file; test defaults (xUnit v3, MTP)
   ├─ Aiakos.Hosting.Wsl.Tests/
   ├─ Aiakos.Data.Tests/        Testcontainers Postgres
   ├─ Aiakos.Orchestrator.Tests/
   ├─ Aiakos.Node.Tests/
   └─ Aiakos.AppHost.Tests/     Aspire.Hosting.Testing; model tests + opt-in WSL end-to-end
```

Project references:

```
Aiakos.AppHost ──► Aiakos.Hosting.Wsl, Aiakos.Core
                   (+ orchestrator as an Aspire project resource; the node is NOT an Aspire
                    project reference: it is published and launched through wsl.exe)
Aiakos.Orchestrator ──► Aiakos.ServiceDefaults, Aiakos.Contracts, Aiakos.Data, Aiakos.Core
Aiakos.Node ──► Aiakos.ServiceDefaults, Aiakos.Contracts, Aiakos.Core
Aiakos.Data ──► Aiakos.Core
Aiakos.Cli  ──► (none in the skeleton; #15 adds its references)
```

Reserved names, not created by this spec: `src/Aiakos.HookRelay` (only
if a native relay binary is needed; spikes 0001 and 0005 used a script), `images/` (M6 seat
images).

Package choices (versions in `Directory.Packages.props`; versions verified in spike 0003 are the
starting point: Aspire 13.5.4, Grpc.* 2.84.0, Google.Protobuf 3.36.2, OpenTelemetry 1.19.x):

| Purpose | Packages |
|---|---|
| AppHost | `Aspire.AppHost.Sdk` (SDK), `Aspire.Hosting.PostgreSQL` |
| Orchestrator | `Akka.Hosting`, `Grpc.AspNetCore`, `Grpc.AspNetCore.HealthChecks`, `Aspire.Npgsql` |
| Data | `dbup-postgresql`, `Dapper`, `Npgsql` |
| Contracts | `Google.Protobuf`, `Grpc.Core.Api`, `Grpc.Tools` (private assets) |
| Node | `Grpc.Net.Client`, `Grpc.HealthCheck` (client types); framework reference `Microsoft.AspNetCore.App` for the hook ingest (R40) |
| ServiceDefaults | `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`, instrumentation for HTTP client, runtime, ASP.NET Core (orchestrator only), gRPC client |
| Tests | `xunit.v3`, `Microsoft.Testing.Platform` (via xUnit v3), `Testcontainers.PostgreSql`, `Aspire.Hosting.Testing`, `Microsoft.AspNetCore.Mvc.Testing` |

`Aiakos.Contracts` wiring (no `.proto` files yet):

```xml
<ItemGroup>
  <Protobuf Include="Protos/**/*.proto" ProtoRoot="Protos" GrpcServices="Both" />
</ItemGroup>
```

Server stubs only need `Grpc.Core.Api`, so referencing the same project adds no ASP.NET Core
dependency to the node (its only ASP.NET Core use is the hook ingest, R40). File names, packages and `csharp_namespace` are spec 0002's.

### Configuration

AppHost configuration (`src/Aiakos.AppHost/appsettings.json`), with the dev defaults:

```json
{
  "Aiakos": {
    "Instance": "dev",
    "PortBase": 5180,
    "Wsl": {
      "Distro": "Ubuntu",
      "Home": ".aiakos-dev",
      "NodeId": "wsl-local"
    }
  }
}
```

| Key | Default (dev) | Used for |
|---|---|---|
| `Aiakos:Instance` | `dev` | telemetry attribute `aiakos.instance`, Postgres volume name |
| `Aiakos:PortBase` | `5180` | port allocation (below) |
| `Aiakos:Wsl:Distro` | `Ubuntu` | `wsl.exe -d` |
| `Aiakos:Wsl:Home` | `.aiakos-dev` | `AIAKOS_HOME`, relative to the WSL user's home |
| `Aiakos:Wsl:NodeId` | `wsl-local` | `AIAKOS_NODE_ID` (matches plan §5's `rig.env.yaml` example) |

Ports, from the port base (dev values):

| Offset | Dev port | Listener | Side |
|---|---|---|---|
| +0 | 5180 | orchestrator `grpc` (h2c, localhost, unproxied) | Windows |
| +10 | 5190 | node hook ingest (Kestrel, `127.0.0.1`; spec 0005, #12) | WSL |
| — | 15180 | dashboard UI (launch profile) | Windows |
| — | 19180 | dashboard OTLP gRPC (launch profile) | Windows |
| — | 20180 | dashboard resource service (launch profile) | Windows |
| — | dynamic | orchestrator `http` (`/health`, `/alive`), Postgres | Windows |

Launch profile (`Properties/launchSettings.json`):

```json
{
  "profiles": {
    "http": {
      "commandName": "Project",
      "applicationUrl": "http://localhost:15180",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development",
        "DOTNET_ENVIRONMENT": "Development",
        "ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL": "http://localhost:19180",
        "ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL": "http://localhost:20180",
        "ASPIRE_ALLOW_UNSECURED_TRANSPORT": "true"
      }
    }
  }
}
```

With mirrored networking the OTLP endpoint stays on `localhost` (spike 0003: "In mirrored mode
`http://localhost:19003` is enough"); no `0.0.0.0` binding, so nothing is exposed on the LAN
(pitfall 7).

Node environment (all set by the AppHost, forwarded through `WSLENV`):

| Variable | Value (dev) | Required |
|---|---|---|
| `AIAKOS_ORCHESTRATOR_URL` | `http://127.0.0.1:5180` | yes |
| `AIAKOS_HOME` | `.aiakos-dev` | yes |
| `AIAKOS_NODE_ID` | `wsl-local` | yes |
| `AIAKOS_NODE_TOKEN` | generated secret parameter | not validated in the skeleton (0002) |
| `AIAKOS_INSTANCE` | `dev` | no (telemetry attribute) |
| `DOTNET_ENVIRONMENT` | `Development` | no |
| `OTEL_*` | set by `WithOtlpExporter`, endpoint rewritten | no |

### AppHost

Sketch of `Program.cs` (illustrative; the names are normative, the exact code is not):

```csharp
var builder = DistributedApplication.CreateBuilder(args);
var cfg = builder.Configuration.GetSection("Aiakos").Get<AiakosDevOptions>()!;
InstanceGuard.EnsureNotReleased(cfg);                               // R18

var pg = builder.AddPostgres("postgres")
    .WithImageTag("18")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume($"aiakos-{cfg.Instance}-pgdata");               // R12
var db = pg.AddDatabase("aiakos");

var nodeToken = builder.AddParameter("node-token", secret: true /* generated default */); // R16

var orchestrator = builder.AddProject<Projects.Aiakos_Orchestrator>("orchestrator")
    .WithHttpEndpoint(name: "grpc", port: cfg.PortBase, isProxied: false)   // R13, h2c set per endpoint
    .WithHttpHealthCheck("/health", endpointName: "http")                   // R14
    .WithEnvironment("Aiakos__Orchestrator__GrpcPort", cfg.PortBase.ToString(CultureInfo.InvariantCulture))
    .WithEnvironment("Aiakos__Instance", cfg.Instance)
    .WithEnvironment("Aiakos__Nodes__0__Id", cfg.Wsl.NodeId)
    .WithEnvironment("Aiakos__Nodes__0__Token", nodeToken)
    .WithReference(db).WaitFor(db);

var publish = builder.AddExecutable("node-publish", "dotnet", repoRoot,
    "publish", "src/Aiakos.Node", "-c", "Debug", "-r", "linux-x64", "--self-contained",
    "-o", "artifacts/node/linux-x64");                                     // R19

var install = builder.AddWslExecutable("node-install", cfg.Wsl.Distro, "/bin/sh",
        "-c", WslInstallScript, "sh", publishDirWindowsPath, cfg.Wsl.Home) // R20
    .WaitForCompletion(publish);

builder.AddWslExecutable("node-wsl", cfg.Wsl.Distro, $"{cfg.Wsl.Home}/node/aiakos-node")
    .WithWslOtlpExporter()                                                   // R25
    .WithEnvironment("AIAKOS_ORCHESTRATOR_URL", $"http://127.0.0.1:{cfg.PortBase}")
    .WithEnvironment("AIAKOS_HOME", cfg.Wsl.Home)
    .WithEnvironment("AIAKOS_NODE_ID", cfg.Wsl.NodeId)
    .WithEnvironment("AIAKOS_NODE_TOKEN", nodeToken)
    .WithEnvironment("AIAKOS_INSTANCE", cfg.Instance)
    .WithEnvironment("DOTNET_ENVIRONMENT", "Development")
    .WithWslEnvironment()                                                    // R24
    .WaitForCompletion(install)
    .WaitFor(orchestrator);                                                  // R17

builder.Build().Run();
```

`WslInstallScript` (R20), run with `sh -c` and positional arguments so nothing is interpolated
into the script text:

```sh
set -eu
src="$(wslpath -u "$1")"; home="$HOME/$2"
mkdir -p "$home"; rm -rf "$home/node.staging"
cp -r "$src" "$home/node.staging"; chmod +x "$home/node.staging/aiakos-node"
rm -rf "$home/node"; mv "$home/node.staging" "$home/node"
```

(`$2` is always relative to `$HOME` in the dev AppHost; an absolute `Aiakos:Wsl:Home` is out of
scope for M1.)

A running node from an earlier AppHost cannot exist here in normal operation (spike 0003 §4: every
stop path took the node down). If one does, the new node refuses to start on the lock (R38) and
the failure is visible.

The orchestrator's `Protocols` are set per endpoint in the orchestrator's own Kestrel setup: the
endpoint whose port equals `Aiakos:Orchestrator:GrpcPort` (passed by the AppHost) is `Http2`; all
others keep the default `Http1AndHttp2`. gRPC services are mapped with `RequireHost("*:<grpc
port>")`.

### WSL extension

```csharp
namespace Aiakos.Hosting.Wsl;

public sealed class WslExecutableResource(string name, string distro, string linuxPath, string workingDirectory)
    : ExecutableResource(name, "wsl.exe", workingDirectory)
{
    public string Distro { get; } = distro;
    public string LinuxPath { get; } = linuxPath;
}

public static class WslResourceBuilderExtensions
{
    // wsl.exe -d <distro> --cd ~ --exec <linuxPath> <args…>              (R23)
    public static IResourceBuilder<WslExecutableResource> AddWslExecutable(
        this IDistributedApplicationBuilder builder, string name, string distro,
        string linuxPath, params string[] args);

    // WSLENV += NAME/u for every env var with one of the prefixes; appends (R24)
    public static IResourceBuilder<WslExecutableResource> WithWslEnvironment(
        this IResourceBuilder<WslExecutableResource> builder, params string[] prefixes); // default OTEL_, AIAKOS_, DOTNET_

    // WithOtlpExporter + rewrite OTEL_EXPORTER_OTLP_ENDPOINT host to 127.0.0.1 (R25)
    public static IResourceBuilder<WslExecutableResource> WithWslOtlpExporter(
        this IResourceBuilder<WslExecutableResource> builder);
}
```

- `WithWslEnvironment` runs as the **last** environment callback so it sees every variable added
  before it, including those added by `WithOtlpExporter`. The implementation registers its callback
  so that ordering does not depend on the call order in `Program.cs` (for example, by computing
  `WSLENV` in a final callback added once per resource).
- The rewrite of `OTEL_EXPORTER_OTLP_ENDPOINT` is the spike's code, with the host fixed to
  `127.0.0.1` (mirrored networking only). It uses `127.0.0.1`, not `localhost`, so the Linux
  resolver cannot pick `::1`.
- The preflight (R26) is a `BeforeResourceStartedEvent` subscription (or the Aspire 13 equivalent)
  on each `WslExecutableResource`. It runs `wsl.exe -d <distro> --exec wslinfo --networking-mode`
  and requires the output `mirrored`. Missing distro → `wsl.exe` exits non-zero; the preflight
  reports "WSL distro '<distro>' not found (wsl -l -v)". The check runs once per AppHost start
  and caches its result per distro.
- The library has no knowledge of Aiakos resources (orchestrator, node); it is reusable for any
  Linux process in WSL.

### Orchestrator

- `Program.cs`: `AddAiakosServiceDefaults()`, `AddNpgsqlDataSource("aiakos")` (Aspire client
  integration: `NpgsqlDataSource`, health check and tracing), `AddAkka("aiakos", …)` with the
  logger factory bridge, `AddGrpc()`, `AddGrpcHealthChecks()` mapped to the same health checks as
  `/health`.
- Migration step: a hosted service registered **first**, whose `StartAsync` runs the migrator
  and throws on failure. Because hosted services start in order and before Kestrel accepts
  requests, the app never serves on an unmigrated database (R28). A `MigrationsHealthCheck`
  reports unhealthy until the migrator has finished.
- `/health` includes `npgsql` and `migrations`; `/alive` only a self check.
- No actors are registered. `ActorSystem` startup and shutdown are covered by a test (R27).

### Node

```
Program
 ├─ NodeOptions            AIAKOS_ORCHESTRATOR_URL, AIAKOS_HOME, AIAKOS_NODE_ID, AIAKOS_NODE_TOKEN;
 │                         source-generated validation ([OptionsValidator]), ValidateOnStart → exit 2
 ├─ InstanceLock           $AIAKOS_HOME/node.lock, FileShare.None (flock on Linux) → exit 3
 ├─ SighupHandler          PosixSignalRegistration(SIGHUP) → Cancel=true, StopApplication()
 └─ OrchestratorConnection BackgroundService: connect loop with Backoff; Check, then Watch;
                           state: connecting | connected | unavailable
```

- Exit codes: `0` graceful stop, `2` invalid configuration, `3` instance lock held, `1` anything
  else unexpected.
- Lock file content: the pid and the start time, written after the lock is taken, so a refusal
  can name the holder.
- The gRPC channel uses `SocketsHttpHandler` with HTTP/2 keep-alive pings (defaults: 30 s delay,
  10 s timeout) so a silently dead connection is detected on the long stream. The keep-alive values
  may be overridden by spec 0002.
- Telemetry: metric `aiakos.node.orchestrator.connected` (observable gauge 0/1) and counter
  `aiakos.node.orchestrator.reconnects`. Each connect attempt is an activity
  `node.connect`; the unary `Check` call makes a client span whose server span appears in the
  orchestrator, so one trace crosses the WSL boundary. (Per-message trace context on the long
  stream is spec 0002's, following spike 0003 §1.)
- Shutdown: `HostOptions.ShutdownTimeout` 5 s; the telemetry providers are flushed with a 2 s
  bound (R37), and the OTLP exporter timeout for the node defaults to 2 s (loopback).
- The node never binds a port in the skeleton. The hook-ingest listener (Kestrel slim builder on
  `127.0.0.1`, port base + 10) arrives with #12 (spec 0005).

### Database

`src/Aiakos.Data/Migrations/0001_tenant.sql`:

```sql
CREATE SCHEMA IF NOT EXISTS aiakos;

CREATE TABLE aiakos.tenant (
    tenant_id  uuid        PRIMARY KEY,
    slug       text        NOT NULL UNIQUE CHECK (slug ~ '^[a-z0-9][a-z0-9-]{0,62}$'),
    name       text        NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);

INSERT INTO aiakos.tenant (tenant_id, slug, name)
VALUES ('00000000-0000-0000-0000-000000000001', 'default', 'Default tenant');
```

- Tenant keys follow [ADR 0012](../adr/0012-tenant-keys.md) (D3): `uuid` keys, a unique slug for
  addresses. `Aiakos.Core.TenantIds.Default` is the default tenant's UUID. Until multitenancy is activated (M8), every write
  uses it, taken from a `CallerContext` (plan §9) rather than hard-coded in repositories.
- Conventions for every later migration (checked in review; the rule-6 part is checked by a test):
  - `tenant_id uuid NOT NULL REFERENCES aiakos.tenant (tenant_id)` on every table.
  - Primary keys are `uuid`, generated in the application with `Guid.CreateVersion7()`; child
    tables use composite foreign keys that include `tenant_id`, so a row can never point into
    another tenant.
  - `snake_case` names, `timestamptz` for times, `text` over `varchar(n)` unless a limit is a rule.
  - One logical change per script; forward-only; never edit a merged script.
- Migrator (`Aiakos.Data.DatabaseMigrator`): `DeployChanges.To.PostgresqlDatabase(cs)`
  `.WithScriptsEmbeddedInAssembly(typeof(DatabaseMigrator).Assembly)`
  `.JournalToPostgresqlTable("aiakos_meta", "schema_versions")` `.WithTransactionPerScript()`,
  logging to `ILogger`. The upgrade runs inside `pg_advisory_lock(<constant>)` on its own
  connection. The database itself is created by Aspire (`AddDatabase`); the migrator does not
  create databases.

Dapper conventions (`Aiakos.Data`):

- Repositories are small classes per aggregate (`TenantRepository` is the first), injected, taking
  `NpgsqlDataSource`; they open a connection per call with `await using var conn = await
  dataSource.OpenConnectionAsync(ct)`. Work that spans calls takes an explicit
  `NpgsqlTransaction`.
- SQL is inline `const string` next to the method that uses it, with explicit column lists (no
  `SELECT *`). All values are parameters; no string concatenation of values.
- `DefaultTypeMap.MatchNamesWithUnderscores = true` is set once in a module initializer.
- Every query passes `CancellationToken` via `CommandDefinition`.
- Every repository method takes `tenantId` as its first parameter and every statement filters by
  it (the repository test for a table checks that another tenant's rows are invisible).
- Read models are `record` types in `Aiakos.Data`; repositories do not return `dynamic`.

### Telemetry

`AddAiakosServiceDefaults()` (`Aiakos.ServiceDefaults`):

- Logging: `ILogger` → OpenTelemetry logs with `IncludeFormattedMessage` and `IncludeScopes`.
- Tracing: sources `Aiakos.*`, gRPC client, `HttpClient`; ASP.NET Core instrumentation is added by
  the orchestrator itself (to keep this library free of ASP.NET Core); Npgsql tracing comes from
  `Aspire.Npgsql`.
- Metrics: meters `Aiakos.*`, runtime, `HttpClient`; ASP.NET Core meters in the orchestrator.
- Resource: `service.name` from `OTEL_SERVICE_NAME`, `aiakos.instance` from `Aiakos:Instance` /
  `AIAKOS_INSTANCE`.
- Export: `UseOtlpExporter()` only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set; otherwise no
  exporter (the released tool configures its own in #15).
- Health: registers a `self` liveness check (tag `live`).
- Shutdown bound (R37): the providers are disposed from an `IHostedLifecycleService.StoppedAsync`
  with `ForceFlush(timeout)`, default 2 s, setting `Aiakos:Telemetry:ShutdownFlushTimeout`.

### Tests

- `tests/Directory.Build.props` imports the root file and adds `xunit.v3` and
  `IsTestProject=true`. Tests use xUnit's `Assert` only.
- Integration tests needing Postgres use Testcontainers (`postgres:18`) with one container per test
  assembly and a fresh database per test class.
- The WSL end-to-end test in `Aiakos.AppHost.Tests` is **opt-in**: it runs only when
  `AIAKOS_E2E_WSL=1`, otherwise it reports "skipped" (xUnit v3 dynamic skip), so `dotnet test`
  works unchanged on Linux CI.

### CI

```yaml
name: ci
on:
  pull_request: { branches: [main] }
  push: { branches: [main] }
  workflow_dispatch:
permissions: { contents: read }
concurrency: { group: ci-${{ github.ref }}, cancel-in-progress: true }
jobs:
  build-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { global-json-file: global.json }
      - run: dotnet restore
      - run: dotnet build -c Release --no-restore
      - run: dotnet test -c Release --no-build --results-directory artifacts/test-results -- --report-xunit-trx   # exact TRX switch per the xUnit v3/MTP version in use
      - run: dotnet publish src/Aiakos.Node -c Release -r linux-x64 --self-contained -o artifacts/node/linux-x64
      - run: dotnet pack src/Aiakos.Cli -c Release --no-build -o artifacts/packages
      - uses: actions/upload-artifact@v4
        if: always()
        with: { name: test-results, path: artifacts/test-results }
```

The AppHost project builds on Linux (only launching it needs Windows). After this workflow is
merged, the maintainer makes `build-test` a required status check on `main` (plan §0, repository
settings; not part of the PR).

### Side by side with the released tool

| | Dev AppHost (this spec) | Released tool (#15, proposed defaults) |
|---|---|---|
| Instance name | `dev` | `release` (reserved; R18) |
| WSL home | `~/.aiakos-dev` | `~/.aiakos` |
| Port base | `5180` (gRPC 5180, hooks 5190) | `7180` (gRPC 7180, hooks 7190) |
| Database | Aspire container, volume `aiakos-dev-pgdata` | its own Postgres/database (#15) |
| Node lock | `~/.aiakos-dev/node.lock` | `~/.aiakos/node.lock` |
| Dashboard | 15180 / 19180 / 20180 | not Aspire's dev dashboard (#15) |

The released defaults are constants in `Aiakos.Core.InstanceDefaults`, which the AppHost guard
(R18) checks against and which #15 consumes, so a later change to them is one edit. Everything
per-instance that later specs add must key off the instance too; in particular #11 should derive
the tmux socket name from it (`tmux -L aiakos-<instance>`), and #12 the hook port from the port
base. The dev AppHost only ever launches the working-tree build; it has no setting that points it
at a running rig (ADR 0008).

### Commands (for CLAUDE.md)

```markdown
## Commands

Prerequisites: .NET SDK 10.0.1xx+ (see `global.json`); Docker Desktop running; WSL2 distro
`Ubuntu` with mirrored networking (see above). WSL needs no .NET.

- Build (warnings are errors): `dotnet build`
- Test (Docker required for the database tests): `dotnet test`
- Run the dev stack (Postgres, orchestrator, node in WSL): `dotnet run --project src/Aiakos.AppHost`
  — dashboard at http://localhost:15180
- WSL end-to-end test (Windows only, stack not running): `$env:AIAKOS_E2E_WSL=1; dotnet test tests/Aiakos.AppHost.Tests`
- Pack the placeholder tool: `dotnet pack src/Aiakos.Cli -c Release -o artifacts/packages`
- Add a migration: `src/Aiakos.Data/Migrations/NNNN_description.sql` (next number; never edit a
  merged one; every table in schema `aiakos` gets `tenant_id`).
- Reset the dev database (stack stopped): `docker rm -f <postgres container>` then
  `docker volume rm aiakos-dev-pgdata`.
- The dev stack uses instance `dev` (`~/.aiakos-dev`, ports 5180+). It never touches the released
  tool's instance (`~/.aiakos`, ports 7180+).
```

## Acceptance criteria

- [ ] **AC1** On a clean clone (Windows and Linux), `dotnet build -c Release` succeeds with
  `0 Warning(s)`.
- [ ] **AC2** On Linux with Docker, `dotnet test -c Release` passes; the WSL end-to-end test is
  reported as skipped.
- [ ] **AC3** The `ci` workflow runs and passes on the implementation PR (build, test, node
  publish), and test results are attached as an artifact.
- [ ] **AC4** `git grep -nE 'PackageReference[^>]*Version=' -- '*.csproj'` finds nothing, and
  `git grep -n NoWarn` shows only `CS1591` (root) and `ASPIRE010` (AppHost, with a comment).
- [ ] **AC5** `git ls-files --eol | grep 'i/crlf'` lists only `*.ps1`, `*.cmd` or `*.bat` files.
- [ ] **AC6** With the prerequisites met, `dotnet run --project src/Aiakos.AppHost` shows in the
  dashboard (http://localhost:15180): `postgres` Running, `orchestrator` Running and Healthy,
  `node-publish` and `node-install` Finished (exit 0), `node-wsl` Running.
- [ ] **AC7** The dashboard shows structured logs, traces and metrics for `orchestrator` and
  `node-wsl`, including one trace that contains a `node-wsl` client span and an `orchestrator`
  server span for `grpc.health.v1.Health/Check`. The `node-wsl` log shows `connected` to
  `http://127.0.0.1:5180`.
- [ ] **AC8** In WSL, `ls ~/.aiakos-dev/node/aiakos-node` exists, and `ss -tn` shows the node's
  connection to `127.0.0.1:5180`. No process runs from `/mnt/c`.
- [ ] **AC9** `psql` against the `aiakos` database: `SELECT slug FROM aiakos.tenant` returns only
  `default`; `SELECT scriptname FROM aiakos_meta.schema_versions` lists the `0001_tenant.sql`
  script. Restarting the orchestrator applies no scripts (its log says so).
- [ ] **AC10** Dashboard **Stop** on `orchestrator`: `node-wsl` keeps running and logs
  `unavailable` with growing retry delays capped at 30 s. **Start**: `node-wsl` logs `connected`
  within 35 s.
- [ ] **AC11** Ctrl+C on the AppHost: the `node-wsl` log ends with a graceful stop line naming
  SIGHUP; within 5 s `pgrep -f aiakos-node` in WSL prints nothing.
- [ ] **AC12** While the stack runs, starting a second node by hand in WSL with the same
  `AIAKOS_HOME` exits with code 3 and a message that names `node.lock` and the first node's pid.
- [ ] **AC13** Starting the node without `AIAKOS_ORCHESTRATOR_URL` exits with code 2 and names the
  variable.
- [ ] **AC14** Setting `Aiakos:Wsl:Home` to `.aiakos` (or `Aiakos:PortBase` to 7180, or
  `Aiakos:Instance` to `release`) makes the AppHost refuse to start with a message naming the key.
- [ ] **AC15** The rule-6 guard test fails when pointed at a database with a table in schema
  `aiakos` that has no `tenant_id` (its own negative test shows this) and passes on the real
  migrations.
- [ ] **AC16** The architecture test shows `Aiakos.Node` references none of `Aiakos.Orchestrator`,
  `Aiakos.Data`, `Akka*`, `Npgsql*` (ASP.NET Core is allowed, R40).
- [ ] **AC17** `CLAUDE.md` "Commands" matches [Design → Commands](#commands-for-claudemd), and the
  spec table in `docs/specs/README.md` shows 0001 as `implemented` when the PR merges.
- [ ] **AC18** `Aiakos.Cli` is in `Aiakos.slnx`, its project file no longer sets the properties
  that `Directory.Build.props` provides, and `dotnet pack src/Aiakos.Cli -c Release` produces
  `Aiakos.0.0.1-preview.1.nupkg` containing `README.md` and the `aiakos` tool command; running the
  tool prints the same greeting and version as before.

## Test plan

**Unit tests (run everywhere)**

- `Aiakos.Hosting.Wsl.Tests`
  - `AddWslExecutable` produces command `wsl.exe` and arguments
    `-d <distro> --cd ~ --exec <path> <args…>`.
  - `WithWslEnvironment` forwards exactly the prefixed variables with `/u`, appends to an existing
    `WSLENV`, and includes variables added *after* it in `Program.cs` (ordering independence).
  - `WithWslOtlpExporter` turns an allocated dashboard endpoint `http://localhost:19180` into
    `http://127.0.0.1:19180` and keeps the scheme and port.
  - Preflight parsing: `mirrored` passes; `nat`, empty output and a non-zero exit fail with the
    documented messages (fake process runner).
- `Aiakos.AppHost.Tests` (model only; nothing started): the app model contains the six resources
  with the expected names and dependencies; `node-wsl` gets `AIAKOS_ORCHESTRATOR_URL` from
  `Aiakos:PortBase`; the instance guard rejects the three released values (AC14).
- `Aiakos.Node.Tests`
  - Backoff: the sequence 1, 2, 4, 8, 16, 30, 30 s within ±20 %, reset after success.
  - Options validation: each missing required variable → exit code 2 and its name; `~/x` and `x`
    resolve under `$HOME`.
  - Instance lock: a second `InstanceLock` on the same file fails and reports the first pid; the
    lock is free after the first is disposed.
  - SIGHUP handler: invoking the registered handler sets `Cancel` and calls `StopApplication`
    (the real signal is covered by AC11).
  - Connection loop against an in-process gRPC health server on a random port: connects, reports
    `unavailable` when the server stops, reconnects when it starts again.
  - Architecture test (AC16).

**Integration tests (Docker; run in CI)**

- `Aiakos.Data.Tests` (Testcontainers Postgres)
  - Migrating an empty database creates `aiakos.tenant` with exactly the default tenant and the
    journal in `aiakos_meta`; migrating twice is a no-op.
  - Two migrators started concurrently both succeed and apply each script once (advisory lock).
  - Rule-6 guard test on the real schema, plus its negative test on a scratch table (AC15).
  - `TenantRepository.GetAsync(TenantIds.Default)` returns the default tenant (Dapper conventions,
    underscore mapping).
- `Aiakos.Orchestrator.Tests` (`WebApplicationFactory` + Testcontainers)
  - `/health` is healthy after startup; with a broken migration script injected, startup fails and
    `/health` is never served.
  - gRPC health `Check` on the test server returns `SERVING`.
  - The `ActorSystem` named `aiakos` is running after start and terminated after stop.

**Opt-in end-to-end (Windows + WSL, `AIAKOS_E2E_WSL=1`)**

- `Aiakos.AppHost.Tests` starts the real AppHost with `Aspire.Hosting.Testing`, waits for
  `node-wsl` to run, waits for the orchestrator's `/health`, and checks the node's log stream for
  `connected`. It then stops the app and checks through `wsl.exe -- pgrep -f aiakos-node` that no
  node is left.

**Manual demo (reviewer, Windows)**

Walk AC6 to AC14 in order: start the AppHost, look at the dashboard, look in WSL, stop and start
the orchestrator, start a second node, Ctrl+C, then try the released-instance values.

## Decisions (resolved in review)

The draft carried these as open questions with recommendations. The maintainer accepted every
recommendation in the review of PR #26; they are folded into the requirements and design above.

- **D1 — `dotnet run` or `aspire run`?** Decision: standardize on
  `dotnet run --project src/Aiakos.AppHost` and suppress ASPIRE010 in `Aiakos.AppHost.csproj`
  only, with a comment (R7, R9). Rationale: no extra tool for CI or agent worktrees; spike 0003
  pitfall 11 showed ASPIRE010 is the only cost. Humans may still use `aspire run`.
- **D2 — How is the node deployed into WSL?** Decision: one-shot Aspire resources
  `node-publish` → `node-install` → `node-wsl` (R19–R21), not an MSBuild target or a script.
  Rationale: visible in the dashboard, fails honestly, reruns on every start, no MSBuild
  customization; about 10–20 s of incremental publish per start is acceptable for M1.
- **D3 — `tenant_id` type.** Decision: `uuid` keys plus a unique `slug` for addresses, default
  tenant at the fixed UUID `00000000-0000-0000-0000-000000000001` (R32,
  [Design → Database](#database)). Rationale: stable opaque keys for every table, readable
  `tenant/rig/seat` addresses (plan §9). Recorded in
  [ADR 0012](../adr/0012-tenant-keys.md) because it shapes every future table.
- **D4 — Is the DbUp journal exempt from rule 6?** Decision: yes. The journal lives in schema
  `aiakos_meta`, the guard test is scoped to schema `aiakos` (R31, R33), and rule 6 in
  `CLAUDE.md` says so. Rationale: migration bookkeeping is infrastructure, not tenant data; ADR
  0003's intent is tenant data, so no ADR is needed.
- **D5 — Test framework.** Decision: xUnit v3 on Microsoft.Testing.Platform
  ([Design → Tests](#tests), R3). Rationale: the .NET 10 direction, and dynamic skip for the opt-in
  end-to-end test. If Akka.TestKit does not support xUnit v3 when #13 needs it, that one test
  project uses xUnit v2 (see Risks).
- **D6 — Warnings as errors locally or only in CI?** Decision: everywhere (R4). Rationale: an
  agent's local build fails exactly like CI. `-p:TreatWarningsAsErrors=false` may be used ad hoc
  while iterating and is never committed.
- **D7 — Windows CI job?** Decision: not in M1 (R45). Rationale: a hosted Windows runner cannot run
  Linux containers or WSL, and CA1416 covers most platform risks. Add it with an automated WSL
  end-to-end run once a self-hosted Windows runner exists.
- **D8 — Serilog?** Decision: not in the skeleton (R41). Rationale: `ILogger` + OpenTelemetry covers
  the dashboard; file logging for the released tool is decided in #15.
- **D9 — Released-tool defaults.** Decision: `~/.aiakos`, port base 7180, instance `release`,
  as constants in `Aiakos.Core.InstanceDefaults` (R18, R46). Rationale: the guard needs concrete
  values now; #15 owns the final values and changes them in one place.
- **D10 — Native AOT for the node.** Decision: `IsAotCompatible` on the node and its libraries now;
  self-contained, non-AOT publish in dev and CI (R19, R40). Rationale: Native AOT cannot
  cross-compile from Windows to linux-x64 and WSL has no .NET; native-AOT release binaries
  (plan §0.5) come from a Linux runner in the release pipeline.
- **D11 — Several dev stacks on one machine.** Decision: M1 supports one dev stack per machine;
  the overrides (`Aiakos:Instance`, `Aiakos:PortBase`, `Aiakos:Wsl:Home`, launch-profile ports)
  allow a second one by hand, and the lock and port binding fail loudly otherwise. Rationale:
  keeps M1 small; automatic allocation becomes an issue only if stage B needs it.
- **D12 — Postgres container lifetime.** Decision: persistent, with a named volume per instance
  (R12); the reset command is in [Design → Commands](#commands-for-claudemd). Rationale: faster
  restarts, no leak on a hard kill of DCP, data kept across `down`/`up` tests.
- **D13 — The existing `Aiakos.Cli` placeholder.** Decision: the skeleton adopts it (R48) rather
  than treating it as a reserved name. Rationale: it already owns the NuGet ID and `aiakos`
  command (#6); moving its shared settings into `Directory.Build.props` and central package
  management now means #15 replaces only its code.

## Risks

- **`wslinfo --networking-mode`** was not exercised in spike 0003. If it is unavailable in the
  installed WSL version, the preflight falls back to reading `networkingMode` from
  `%USERPROFILE%\.wslconfig` and reports `unknown` (and refuses) when neither works.
- **Per-endpoint Kestrel protocols with Aspire-managed endpoints** (R14) were not tried in the
  spike, which used the global `EndpointDefaults`. If Aspire's injected configuration fights the
  per-endpoint setting, fall back to configuring both endpoints explicitly in Kestrel from the
  ports Aspire passes, and record it under "Changes after acceptance".
- **Aspire version churn.** `EndpointReference` handling and event names changed across Aspire
  releases (spike 0003 pitfall 2). The AppHost tests pin the behaviour; upgrade Aspire only in a
  dedicated PR.
- **Akka.TestKit and xUnit v3** (D5): check compatibility before #13 starts.
- **Publish time on each AppHost start** (D2) could annoy; measure it in the implementation PR.

## Changes after acceptance

- **2026-09-30 — the node carries ASP.NET Core for the hook ingest** (spec 0005 Q10, accepted in
  review). The node hosts the hook ingest on Kestrel (`WebApplication.CreateSlimBuilder`, which
  supports native AOT, so `IsAotCompatible` and D10 stand), bound to `127.0.0.1` at the port
  base plus 10. R40 now allows ASP.NET Core in the node while still forbidding `Aiakos.Orchestrator`,
  `Aiakos.Data`, Akka and Npgsql (AC16 unchanged apart from that note); R41's rationale, the
  layout, the package table, the port table and [Design → Node](#node) follow. No ADR.
