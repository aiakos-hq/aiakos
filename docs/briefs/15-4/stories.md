## S1: Instance layout and configuration
goal: Instance layout and configuration implements the owned behavior with independent permanent tests.
depends: -
owns: R1, R2, R3
outputs: E1
tests: T1
notes: Pure concern after external15-1 S1. Sibling filesystem discovery belongs S2.

## S2: Transactional initialization and ACLs
goal: Transactional initialization and ACLs implements the owned behavior with independent permanent tests.
depends: S1
owns: R4
outputs: E2
tests: T2
notes: Owns random/security ports and production adapters, sibling discovery and force rollback; temporary Windows proof only.

## S3: Versioned runtime snapshots
goal: Versioned runtime snapshots implements the owned behavior with independent permanent tests.
depends: -
owns: R5
outputs: E3
tests: T3
notes: Independent after15-1 S1; returns directory, orders by publication marker, protects running copy.

## S4: Shared orchestrator composition
goal: Shared orchestrator composition implements the owned behavior with independent permanent tests.
depends: -
owns: C1
outputs: E4
tests: T4
notes: Extract current main, no15-2 prerequisite; later15-2 additions enter shared composition.

## S5: Aspire-free WSL extraction
goal: Aspire-free WSL extraction implements the owned behavior with independent permanent tests.
depends: -
owns: C2, R6
outputs: E5
tests: T5
notes: Creates Wsl/Wsl.Tests and compatible Hosting.Wsl wrappers; modifies Hosting.Wsl.Tests and AppHost.NodeDeployment delegation only.

## S6: Released WSL node installation
goal: Released WSL node installation implements the owned behavior with independent permanent tests.
depends: S5
owns: R7
outputs: E10
tests: T10
notes: Consumes shared runner/script; owns NodeInstaller and released VERSION behavior; host later enforces install before node.

## S7: Owned node restart policy and supervisor
goal: Owned node restart policy and supervisor implements the owned behavior with independent permanent tests.
depends: -
owns: R8, R9
outputs: E6
tests: T6
notes: Independent after15-1 S1; fake-time/process tests; exposes LastError and terminal/cancellation behavior.

## S8: Admin DTOs and control contract
goal: Admin DTOs and control contract implements the owned behavior with independent permanent tests.
depends: -
owns: C5, R22
outputs: E21
tests: T21
notes: External15-2 S2 contracts project; defines admin-only DTO/problem exception and control interface, spec amendment; no route handlers.

## S9: Released host project references
goal: Released host project references implements the owned behavior with independent permanent tests.
depends: S4, S5, S8
owns: C4
outputs: E20
tests: T20
notes: References existing project identities; external15-1 S5 prerequisite for isolated package/help smoke. No command wiring.

## S10: Bounded short-process adapter
goal: Bounded short-process adapter implements the owned behavior with independent permanent tests.
depends: S9
owns: R23
outputs: E22
tests: T22
notes: Owns reusable request/result/runner for database, dashboard and WSL preparation, not long-lived node process.

## S11: Readable exclusive instance lock
goal: Readable exclusive instance lock implements the owned behavior with independent permanent tests.
depends: -
owns: R17
outputs: E11
tests: T11
notes: Independent after15-1 S1; actual temporary file share-mode tests and unknown holder message.

## S12: Instance database preparation
goal: Instance database preparation implements the owned behavior with independent permanent tests.
depends: S2, S10
owns: R10
outputs: E7
tests: T7
notes: Consumes S2 security port and S10 runner. Exact Docker18 mount/argv and bounded readiness; no host/node starts.

## S13: Redacted rolling host logs
goal: Redacted rolling host logs implements the owned behavior with independent permanent tests.
depends: S2
owns: R16
outputs: E9
tests: T9
notes: Owns IHostLogs/HostLogs and Serilog package pins; independent of database and node; temporary sinks.

## S14: Explicit host telemetry and dashboard
goal: Explicit host telemetry and dashboard implements the owned behavior with independent permanent tests.
depends: S1, S10
owns: R19
outputs: E19
tests: T19
notes: Consumes config and process runner; own IHostTelemetry/HostTelemetry, per-instance dashboard and disposal.

## S15: Held WSL node process factory
goal: Held WSL node process factory implements the owned behavior with independent permanent tests.
depends: S6, S10, S13
owns: R12
outputs: E13
tests: T13
notes: Owns IOwnedNodeLauncher, ReleasedNodeProcessFactory/provider and IInstanceWslPreparation adapter; shared prep uses S6 installer and S10 bounded helper, output drains to S13 logs.

## S16: Readiness connection publication and discovery
goal: Readiness connection publication and discovery implements the owned behavior with independent permanent tests.
depends: S11
owns: R13
outputs: E8
tests: T8
notes: Owns ConnectionInfo/store/liveness adapter, uses S11 lock held check; no HTTP client or detached launch.

## S17: Minimal instance admin HTTP client
goal: Minimal instance admin HTTP client implements the owned behavior with independent permanent tests.
depends: S8, S16
owns: R20
outputs: E17
tests: T17
notes: Owns InstanceAdminClient/InstanceAdminException; uses DTOs/connection, fake HttpMessageHandler; no15-3 client prerequisite.

## S18: Authenticated admin lifecycle routes
goal: Authenticated admin lifecycle routes implements the owned behavior with independent permanent tests.
depends: S4, S8
owns: R14
outputs: E15
tests: T15
notes: External15-2 S2/S3/S5 supplies auth/reads/version policy. Uses fake IInstanceHostControl; no live CLI host needed to test routes.

## S19: In-process released host lifecycle
goal: In-process released host lifecycle implements the owned behavior with independent permanent tests.
depends: S7, S9, S11, S12, S14, S15, S16, S18
owns: R11
outputs: E12
tests: T12
notes: Owns InstanceHost/request, orchestrator factory adapter and host control implementation. Composes earlier ports, readiness and owned shutdown; missing Node hook-port consumer is partial, never built here.

## S20: Detached instance starter
goal: Detached instance starter implements the owned behavior with independent permanent tests.
depends: S3, S17
owns: R18
outputs: E14
tests: T14
notes: Uses runtime and admin/discovery from S17 context. Owns detached launcher adapter. Fake launch before final command wiring; real Windows packaged demo waitsS22 and15-5 payload.

## S21: Instance status renderers
goal: Instance status renderers implements the owned behavior with independent permanent tests.
depends: S8
owns: R15
outputs: E16
tests: T16
notes: Pure status/CLI-version golden text and JSON, no live host/client.

## S22: Instance command application wiring
goal: Instance command application wiring implements the owned behavior with independent permanent tests.
depends: S2, S19, S20, S21
owns: C3, R21
outputs: E18
tests: T18
notes: External15-1 S5 required. Preserves constructor, injects Windows platform/services on Linux; owns production IInstanceHostRunner/InstanceCommands adapter; admin client inherited throughS20. Does not implement15-3 seat commands.
