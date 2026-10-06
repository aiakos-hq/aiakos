## S1: API contracts and authentication
goal: The local API has immutable v1 DTOs, source-generated snake_case JSON, loopback policy and bearer CallerContext authentication.
depends: -
owns: C1, R1, R2
outputs: E1
tests: T1
notes: No actor or database write dependency. CallerContext is the exact shared type consumed by 13-4 and the later bridge.

## S2: Address resolution and read endpoints
goal: Authenticated callers can resolve full or unique short seat addresses and read existing seat, launch, command and node projections.
depends: S1
owns: R3, R4
outputs: E2, E3
tests: T2, T3
notes: Uses existing SeatQueries/read models; no SeatActor ask and no command dispatcher.

## S3: Rig revision registration
goal: Rig registration validates canonical hashes and persists append-only revisions with safe retirement and transaction boundaries.
depends: S1
owns: C2, R5
outputs: E4
tests: T4
notes: Independent of S2 reads; migration must be next available only and tenant-scoped.

## S4: Problem and version policy
goal: Every API failure and changing-command version mismatch has stable status, reason, detail and retryability.
depends: S1
owns: R6, R7
outputs: E5
tests: T5
notes: Read endpoints from S2 are context; changing-command handlers remain gated and are not invented here.

## S5: 13-4 command bridge
goal: After 13-4 merges, API up/down/send/capture requests dispatch through its exact transport-free port and map replies without identity leakage.
depends: S2, S4
owns: R8
outputs: E6
tests: T6
notes: Hard external prerequisite: 13-4 owns CallerContext, command messages and dispatcher. Tests use fake ports only; no production fake or ApplyAsync substitute.

## S6: tracing and cancellation
goal: API requests continue traceparent safely and stop before lookup/write on cancellation without sensitive attributes.
depends: S1, S4
owns: R9
outputs: E7
tests: T7
notes: No OTLP endpoint is invented; connection metadata controls bounded export.
