## S1: Embedded byte-preserving relay
goal: A supplied relay SeatFile executes safe bounded hook posts with persistent source sequencing.
depends: -
owns: R1, R2, R3
outputs: E1, E2, E3
tests: T1
notes: Owns relay resource and BuildFile only. Shell tests use a recording listener rather than HookIngest;12-1 consumes supplied bytes later.

## S2: Authenticated launch registry
goal: Current and recently ended tokens resolve to trusted immutable launch metadata.
depends: -
owns: R4
outputs: E4
tests: T2
notes: Owns HookLaunch and HookLaunchRegistry only. Callers register synthetic tokens; no token generation/file writing.

## S3: Loopback raw HTTP ingest
goal: The listener authenticates and enqueues full raw payloads without waiting on consumers.
depends: S2
owns: R5, R6
outputs: E5, E6
tests: T3
notes: Defines raw consumer records and HookIngest using R4. No default consumer/normalizer. Retains every status post until R8 extends admission.

## S4: Sequence loss intervals
goal: Missing source sequence runs become gaps after a two-second reorder window.
depends: S3
owns: R7
outputs: E7
tests: T4
notes: Adds tracker and ingest timer/callback. R6 accepted posts feed it before later R8 coalescing; source0/rejected posts do not affect deadlines.

## S5: Leading and trailing status coalescing
goal: Per-seat raw status output emits at most once per second with the newest trailing value.
depends: S4
owns: R8
outputs: E8
tests: T5
notes: Extends R6 worker admission after R7 accounting. Pending raw record retains R4 old-launch identity. TELEMETRY normalization stays later12-3.

## S6: Ingest telemetry and confidentiality proof
goal: Safe metrics and spans expose receipt/loss evidence with secret-exposure regression coverage.
depends: S5, S1
owns: R9
outputs: E9
tests: T6
notes: Instruments R5-R8 and scans relay R2 argv. Latency follows the decided spec R38 meaning: request received to enqueued; no production node wiring.
