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
owns: R7, R8
outputs: E7, E8
tests: T4
notes: R4 supplies observations, R5/R6 own publication, R2 protects SIGUSR1. Learn identity through the responding private socket; no node registration or automatic seat action.

## S5: Verified process-tree stop and dead cleanup
goal: Explicit stop terminates tracked identities, reports partial progress and leftovers, and removes only a verified dead matching session.
depends: S1, S2, S3
owns: R9, R10, R11, R12
outputs: E9, E10, E11, E12
tests: T5
notes: One stop state machine. R1/R2 provide expansion/signals, R3 status, R5 the name lease and non-locking exit helper. Never recursively acquire ObserveAsync. Scripted handles allow merging before the starter exists; R11 defines a conservative outcome with error on early partial failure.

## S6: Start-stop lifecycle integration and real tmux proof
goal: The real starter shares lifecycle exclusion/registration with opt-in end-to-end evidence for exit, stop and socket recovery.
depends: S3, S4, S5
owns: C1
outputs: E13
tests: T6, T7
notes: MERGE PREREQUISITES: 11-2-5 (#215) and 11-2-6 must merge before baseline/ready; no missing starter/launch stubs. C1 adds an optional constructor parameter preserving existing serialization/callbacks. T7 covers AC7/8/10/16 on unique sockets. 11-2-7/8 are not prerequisites; preserve their contracts if present. No DI/facade.
