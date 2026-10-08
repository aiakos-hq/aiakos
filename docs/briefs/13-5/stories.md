## S1: Durable process-start overlay
goal: Startup-selected live seats commit unknown/orchestrator-restarted before the region admits external work, without treating supervised reload as process restart.
depends: -
owns: C1, R1, R2
outputs: BOOT
tests: T1
notes: Uses existing 13-1 Apply and 13-3 reader/writer/region handshake. Only initial startup changes; notification-before-release and child restart remain unchanged. Existing live startup test expectations change exactly per C1, never pure table outputs. Internal startup coordination must persist no-op/commit evidence before child readiness.

## S2: Real database same-instance restart replay
goal: Two actor systems over one private Postgres database prove same-instance durable catch-up.
depends: S1
owns: R3, R4
outputs: FIXTURE, SAME
tests: T2
notes: MERGE PREREQUISITE: 10-4 S6 production SeatNodeEventCommitter. No 13-4 prerequisite: the fixture supports optional command services but SAME uses only reader/writer/gateway/facade. No fake production provider; seed initial rows before actor work.

## S3: New-instance recovery evidence
goal: A new node epoch establishes inventory-derived session and durable gap/capture evidence without adopting a mismatched launch.
depends: S2
owns: R5
outputs: NEW
tests: T6
notes: MERGE PREREQUISITES: 13-4 R7/R11/R14/R18 command integration contracts. Reuse the S2 fixture, adding real command services for this scenario; existing RequestCapture consumer executes capture, never a second handler.

## S4: Persisted command and timer recovery evidence
goal: Restart and epoch replacement preserve at-most-once delivery, final outcomes, timers and evidence capture ordering.
depends: S1, S2, S3
owns: R6, R7
outputs: COMMAND, ORDER
tests: T3
notes: MERGE PREREQUISITES: 13-4 sent-command store/results, timer/reload/capture stories and service bundle are implemented. R6/R7 only add integration tests of their exact published behavior. No production command/timer fixes if those dependencies fail. Pending rows may be seeded solely as fixture setup for the documented crash boundary.

## S5: Real transport crash and lost-receipt proof
goal: Production NodeProxy and actor recovery re-forward a retained event only after reload and acknowledge one durable commit.
depends: S1, S2
owns: R8, R9
outputs: CRASH, AMBIGUOUS
tests: T4
notes: MERGE PREREQUISITES: 10-4 S6/S7 facade/re-forward/ack wiring. Extend existing authenticated loopback fixtures; do not simulate the retry by manually calling gateway twice. Barrier/failure wrappers delegate successful work to the real Postgres store. Ordinary exceptions are not the typed recovery signal.

## S6: Tenant and diagnostic integration evidence
goal: The established restart scenarios prove isolation and safe emitted diagnostics with positive listener evidence.
depends: S2, S3, S4, S5
owns: R10
outputs: ISOLATION
tests: T5
notes: Adds assertions to earlier scenario fixtures; no new state writer or runtime registration. Requires 13-4 committed observability story for command spans. Every tenant/seat/epoch count is explicitly filtered, not a global row count; live demo/performance risks remain unverified.
