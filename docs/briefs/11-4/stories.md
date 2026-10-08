## S1: Tracked process trees and identity-safe signals
goal: Same-user process identities expand across ancestry and session membership, and Linux signals verify PID/start time immediately before the syscall.
depends: -
owns: R1, R2
outputs: E1, E2
tests: T1
notes: Uses merged IProcessSnapshot; no starter, groups or argv search. R1 retains detached identities; R2 documents the remaining pre-pidfd race.

## S2: Real tmux pane and server observations
goal: Exact read-only queries report alive, exited, missing or unknown without inferring state from errors or text.
depends: -
owns: R3, R4
outputs: E3, E4
tests: T2
notes: Uses merged TmuxClient/ProcessSnapshot. GetServerAsync is the private-socket identity seam for R8. No adoption or start dependency.

## S3: Durable exit publication and shared lifecycle leases
goal: Registered launches have per-name exclusion and a single event stream with persisted exit deduplication.
depends: S2
owns: R5, R6
outputs: E5, E6
tests: T3
notes: Uses existing RegistryExit. ObserveAsync takes a lease; S5 uses its internal non-locking helper. Publish accepts the later starter callback. Enqueue/persist crash window is explicit; unreturned starting entries get no handle/event.

## S4: Liveness watcher and private socket recovery
goal: Polling emits terminal events once and restores a missing socket through the recorded server identity.
depends: S1, S2, S3
owns: R7, R8, C2
outputs: E7, E8
tests: T4
notes: R4 supplies observations, R5/R6 own publication, R2 protects SIGUSR1. Learn identity through the responding private socket; no node registration or automatic seat action.

## S5: Not-running stop validation and evidence cleanup
goal: Shared stop validation and an internal leased cleanup helper remove only verified dead or missing launches.
depends: S2, S3
owns: R9, R12
outputs: E9, E12
tests: T5
notes: Deliver ValidateRequest, ObserveUnderLeaseAsync and CleanupUnderLeaseAsync with the exact signatures and caller-held lease contract in Public surface; public StopAsync is completed in S6. No temporary public Alive behavior or missing live-path stub. Test helpers directly using the existing test friend assembly. R12 is a reusable postcondition; S6 invokes it only after successful live verification. Never recursively acquire ObserveAsync.

## S6: Live process-tree termination and partial reports
goal: Public StopAsync uses the merged validation/cleanup helpers and terminates tracked live identities with verified leftovers.
depends: S1, S2, S3, S5
owns: R10, R11
outputs: E10, E11
tests: T8
notes: Reuse S5 for validation, initial NotRunning and final cleanup; this story owns graceful waits, identity tracking, signal rounds and the partial-report boundary. Grace polling includes snapshots before status. No cleanup on a failed live verification. R10 owns lifecycle lease acquisition/disposal and T8 proves release on every return/throw.

## S7: Start-stop lifecycle integration
goal: The actual starter shares lifecycle exclusion and registers committed handles while preserving old callers.
depends: S3, S6
owns: C1
outputs: E13
tests: T6
notes: MERGE PREREQUISITES: 11-2-5 (#215) and 11-2-6 must merge before baseline/ready. No launch/starter stubs. Optional trailing constructor parameter; retain callback and existing serialization. No DI/facade.

## S8: Isolated real tmux lifecycle verification
goal: Permanent opt-in tests prove exit, stop, socket recovery and vanished-server behavior on unique test sockets.
depends: S4, S6, S7
owns: R13
outputs: -
tests: T7
notes: Tests only; R13 makes the verification requirement explicit. Actual merged starter required. Run AIAKOS_TEST_TMUX=1 and report version/latency; skipped tests are not proof. Production failures go to lead without extending this story. Includes C2 cold-watcher degraded case.
