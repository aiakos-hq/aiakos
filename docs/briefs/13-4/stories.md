## S1: Command protocol and caller context
goal: Publish transport-free command/reply records and trusted CallerContext for downstream API use.
depends: -
owns: R1
outputs: UP-reject
tests: T1
notes: 13-3 ports remain unchanged; 15-2 consumes these records.
## S2: Up lifecycle dispatcher
goal: Persist and dispatch up decisions, including explicit fresh and resume-loss handling.
depends: S1
owns: R2
outputs: UP-resume
tests: T2
notes: Depends on the merged transactional SeatActor input writer (`ISeatInputCommitter.ApplyAsync`),
the actor reload observer (`ISeatActorLifecycle`), and the region gateway; 13-3 retry/reload
integration may still be in flight and must be present before implementation.
## S3: Down and capture dispatcher
goal: Dispatch stop/capture commands with bounded replies and state preservation.
depends: S1
owns: R3, R4
outputs: DOWN-stop, CAPTURE
tests: T3
notes: Depends on the merged SeatActor lifecycle port and region gateway; capture remains evidence only.
## S4: Send and delivery dispatcher
goal: Enforce safe send admission and persist one at-most-once delivery outcome.
depends: S1, S2
owns: R5, R6, R7
outputs: SEND-reject, SEND-outcome
tests: T4
notes: NodeProxy reconnect resend remains 10-4; depends on committed command/result writer stages.
## S5: Restart, link and cancellation coordination
goal: Preserve overlays, gap capture, reload ordering and bounded cancellation behavior.
depends: S2, S3, S4
owns: R8
outputs: RELOAD
tests: T5
notes: Depends on the merged `SeatActorReloaded` observer and region gateway; host registration
and 13-5 restart/resync integration remain out of scope.
## S6: Observability and safety tests
goal: Emit bounded structured observability for lifecycle operations without sensitive values.
depends: S1, S2, S3, S4, S5
owns: R9, R10
outputs: OBSERVE
tests: T6
notes: Depends on all dispatcher behavior; no harness or transport logging.
