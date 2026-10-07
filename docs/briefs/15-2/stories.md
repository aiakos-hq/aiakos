## S1: Canonical rig hash port
goal: A single public Aiakos.Spec entry point parses canonical registration JSON, validates it, and recomputes spec_hash and binding_hash using RigCanonicalizer/CanonicalJson.
depends: -
owns: C3, R10
outputs: E8
tests: T8
notes: Head story; allowed paths are src/Aiakos.Spec and its tests only. Return a minimal typed result from a string or stream. Pin equality with the 14-4 loader goldens. S3 depends on this story. The orchestrator must not contain a second canonicalizer or hash implementation.


## S2: API contracts and authentication
goal: The local API has immutable v1 DTOs, source-generated snake_case JSON, loopback policy and bearer CallerContext authentication.
depends: S1
owns: C1, R1, R2
outputs: E1
tests: T1
notes: External prerequisite: 13-4 creates and merges Aiakos.Core.CallerContext before this story's authentication factory can compile. No actor or database write dependency. CallerContext is the exact shared type consumed by 13-4 and the later bridge.

## S3: Address resolution and read endpoints
goal: Authenticated callers can resolve full or unique short seat addresses and read existing seat, launch, command and node projections.
depends: S2
owns: R3, R4
outputs: E2, E3
tests: T2, T3
notes: Uses existing SeatQueries/read models; no SeatActor ask and no command dispatcher.

## S4: Rig revision registration
goal: Rig registration validates canonical hashes and persists append-only revisions with safe retirement and transaction boundaries.
depends: S2
owns: C2, R5
outputs: E4
tests: T4
notes: Independent of S2 reads; migration must be next available only and tenant-scoped.

## S5: Problem and version policy
goal: Every API failure and changing-command version mismatch has stable status, reason, detail and retryability.
depends: S2
owns: R6, R7
outputs: E5
tests: T5
notes: Read endpoints from S2 are context; changing-command handlers remain gated and are not invented here.

## S6: 13-4 command bridge
goal: After 13-4 merges, API up/down/send/capture requests dispatch through its exact transport-free port and map replies without identity leakage.
depends: S3, S5
owns: R8
outputs: E6
tests: T6
notes: Hard external prerequisite: 13-4 owns CallerContext, command messages and dispatcher. Tests use fake ports only; no production fake or ApplyAsync substitute.

## S7: tracing and cancellation
goal: API requests continue traceparent safely and stop before lookup/write on cancellation without sensitive attributes.
depends: S2, S5
owns: R9
outputs: E7
tests: T7
notes: No OTLP endpoint is invented; this slice does not export OTLP.
