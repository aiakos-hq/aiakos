## S1: Canonical rig hash port
goal: A single public Aiakos.Spec entry point parses canonical registration JSON, validates it, and recomputes spec_hash and binding_hash using RigCanonicalizer/CanonicalJson.
depends: -
owns: C3, R10
outputs: E8
tests: T8
notes: Head story; allowed paths are src/Aiakos.Spec and its tests only. Return a minimal typed result from a string or stream. Pin equality with the 14-4 loader goldens. S7 depends on this story. The orchestrator must not contain a second canonicalizer or hash implementation.


## S2: API contracts and authentication
goal: The local API has immutable v1 DTOs, source-generated snake_case JSON, loopback policy and bearer CallerContext authentication.
depends: S1
owns: C1, R1, R2
outputs: E1
tests: T1
notes: External prerequisite: 13-4 S1 creates and merges Aiakos.Core.CallerContext before this story's authentication factory can compile. No actor or database write dependency. CallerContext is the exact shared type consumed by 13-4 and the later bridge.

## S3: Address resolution and read endpoints
goal: Authenticated callers can resolve full or unique short seat addresses and read existing seat, launch, command and node projections.
depends: S2
owns: R3, R4
outputs: E2, E3
tests: T2, T3
notes: Uses existing SeatQueries/read models; no SeatActor ask and no command dispatcher.

## S4: Problem and version policy
goal: Every API failure and changing-command version mismatch has stable status, reason, detail and retryability.
depends: S2
owns: R6, R7
outputs: E5
tests: T5
notes: E5 tests the mapping and version policy alone. S3, S5, S6 and S7 assert their own producing reasons; S7 gates rig.put and S6 gates up/down/send. Capture is ungated.

## S5: Rig revision migration and repository
goal: The migration and repository persist append-only revisions, raw file bytes, seat metadata and guarded retirement atomically.
depends: S2, S4
owns: C2, R5
outputs: E4
tests: T4
notes: No HTTP handler or hash/file verification here; S7 owns those. Migration must be next available only and tenant-scoped.

## S6: 13-4 command bridge
goal: After 13-4 merges, API up/down/send/capture requests dispatch through its exact transport-free port and map replies without identity leakage.
depends: S3, S4
owns: R8
outputs: E6
tests: T6
notes: Hard external prerequisite: merged 13-4 S16 registers production services; 13-4 S1 owns CallerContext, command messages and dispatcher. Tests use fake ports only; no production fake or ApplyAsync substitute.

## S7: Registration HTTP validation and route
goal: Apply the version gate, canonical hash and byte-map verification before repository registration and map exact receipts/problems.
depends: S1, S5, S4
owns: R11
outputs: E9
tests: T9
notes: Owns PUT /v1/rigs/{rig}; uses the S5 repository with fake probes in route tests. No migration, numbering or seat persistence here. S8 instruments this route after it exists.

## S8: tracing and cancellation
goal: API requests continue traceparent safely and stop before lookup/write on cancellation without sensitive attributes.
depends: S3, S5, S6, S7
owns: R9
outputs: E7
tests: T7
notes: No OTLP endpoint is invented; this slice does not export OTLP.
