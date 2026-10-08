## S1: Pure hook mapping and evidence limits
goal: Supply immutable normalization snapshots and ordinary hook mappings with extraction before raw truncation.
depends: -
owns: R1, R2, R3, R4
outputs: E1, E2, E3, E4
tests: T1, T2
notes: G1/G2 apply throughout. R4 leaves permission events as Other until S3; R1 leaves clear/statusLine as Other until S4/S2. No driver or ingest dependency.

## S2: statusLine usage and rate limits
goal: Normalize each statusLine into Telemetry with precise optional presence and rate-limit attributes.
depends: S1
owns: R5, R6
outputs: E5, E6, E7
tests: T3
notes: Uses R1 full-body parsing/envelope/limits and G2 field extraction. Transport throttling stays in 12-2.

## S3: Permission pairing and echo suppression
goal: Pair permission requests to open tools using canonical input and suppress late notification echoes.
depends: S1
owns: R7, R8, R9
outputs: E8, E9, E10
tests: T4
notes: Extends immutable state from S1; replaces R4 permission fallback without changing other Notifications. R3 tool/prompt mappings drive open-tool and flag updates. Identical parallel calls intentionally pick the latest per R7.

## S4: Clear rotation and orphan identity
goal: Rotate current session ID on clear starts and preserve old orphan events without reverting it.
depends: S1, S3
owns: R10, R11
outputs: E11
tests: T5
notes: Replaces R1 clear fallback and R2 clear-end behavior; clears pairing/echo state introduced by R7/R9. Does not modify registry, driver or readiness outcomes.

## S5: Evidence-only screen classification
goal: Classify blocked-screen labels and dead-pane reason strings through a pure deterministic classifier.
depends: -
owns: R12, R13
outputs: E12, E13
tests: T6
notes: Independent of normalization and session host; G1/G2 ensure strings are evidence with no side effects. Driver 12-4 owns applying labels after timeout or pane death.
