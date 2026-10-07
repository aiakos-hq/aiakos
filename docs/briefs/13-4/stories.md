## S1: Core command protocol
goal: Implement core command protocol with the exact owned rules and outputs.
depends: -
owns: R1
outputs: PROTOCOL
tests: T1
notes: Protocol only; runtime validation/forwarding belongs to S6.

## S2: Atomic command transaction protocol
goal: Implement atomic command transaction protocol with the exact owned rules and outputs.
depends: S1
owns: R2
outputs: STORE-shape
tests: -
notes: Shape only; SQL/no-op tests belong to S3.

## S3: PostgreSQL atomic command persistence
goal: Implement postgresql atomic command persistence with the exact owned rules and outputs.
depends: S2
owns: R3, R4
outputs: STORE-inserts, STORE-rollback, STORE-status
tests: T2
notes: Requires 13-3 PostgresSeatActorWriter finding/session/result stages. One transaction concern; supplied steps need no actor.

## S4: Launch preparation and token generation
goal: Implement launch preparation and token generation with the exact owned rules and outputs.
depends: S1, S2
owns: R5
outputs: LAUNCH-build, LAUNCH-invalid
tests: T3
notes: Requires IHarnessAdapter.BuildLaunch/Profile; injected material/files suffice for tests. No up decision or send.

## S5: Command reader metadata and prompt query
goal: Implement command reader metadata and prompt query with the exact owned rules and outputs.
depends: S1
owns: R17, C3
outputs: READER-command
tests: T13
notes: Extend PostgresSeatActorReader with exact columns/query, preserving old constructors. Supplies address/epochs/timestamps/turn id before consumers.

## S6: Serialized command admission and routing
goal: Implement serialized routing and committed outcome waits with the exact owned rules and outputs.
depends: S1, S3, S5
owns: R7, C1
outputs: ROUTE-admission
tests: T5
notes: Requires 13-3 SeatActor/SeatRegion/SeatActorGateway and committed outcome writer. Owns exact service bundle/overloads and validation; no pending outcome registry/tests until S9. Valid command without bundle safely rejects.

## S7: Up lifecycle decisions
goal: Implement up lifecycle decisions with the exact owned rules and outputs.
depends: S3, S4, S6
owns: R6
outputs: UP-matrix, UP-start
tests: T4
notes: Sole owner of up decisions; preparation failures have exact replies and rejected findings commit without commands.

## S8: Down lifecycle commands
goal: Implement down lifecycle commands with the exact owned rules and outputs.
depends: S3, S6
owns: R8
outputs: DOWN-stop
tests: T6
notes: Executes only pure DispatchStop; disconnected branch exact. Owns directly injected StopDeadlineFired handling; S13 arms later.

## S9: Capture commands and pending replies
goal: Implement capture commands and pending replies with the exact owned rules and outputs.
depends: S3, S6
owns: R9
outputs: CAPTURE-result
tests: T7
notes: Owns CaptureDeadlineFired handling through injected message before scheduler; no-launch precedence and link-loss row finalization exact. Owns pending outcome registry and committed-result ordering tests.

## S10: At-most-once delivery admission and outcomes
goal: Implement at-most-once delivery admission and outcomes with the exact owned rules and outputs.
depends: S3, S5, S6
owns: R10, R11
outputs: SEND-reject, SEND-accepted, SEND-outcome
tests: T8
notes: One delivery command concern. Requires BuildDelivery and result writer. Owns injected DeliveryDeadlineFired; link loss retains sent, different-instance replacement unknown.

## S11: Prompt evidence comparison
goal: Implement prompt evidence comparison with the exact owned rules and outputs.
depends: S5, S10
owns: R12
outputs: SEND-evidence
tests: T12
notes: Separate event-reading concern using HasPromptAsync and current batch. Finding joins same input commit; no activity fabrication.

## S12: Automatic capture effects
goal: Implement automatic capture effects with the exact owned rules and outputs.
depends: S6, S9
owns: R18, C2
outputs: CAPTURE-effect
tests: T14
notes: Consume committed RequestCapture via R9; requested_by seat-actor, disconnect/dedupe no send, no second external executor.

## S13: Actor timer scheduling
goal: Implement actor timer scheduling with the exact owned rules and outputs.
depends: S7, S8, S9, S10
owns: R13
outputs: TIMERS
tests: -
notes: Arming only; handlers already exist in R8/R9/R11. Sole start watchdog; delivery timeout+confirm+30s margin. T9 in S14 permanently guards scheduler/reload.

## S14: Reload and cancellation coordination
goal: Implement reload and cancellation coordination with the exact owned rules and outputs.
depends: S6, S9, S10, S12, S13
owns: R14
outputs: RELOAD, CANCEL
tests: T9
notes: Requires SeatActorReloaded/ready handshake; R18 executes capture, R17 restores actual timestamps/epoch, no start/delivery resend.

## S15: Committed command observability
goal: Implement committed command observability with the exact owned rules and outputs.
depends: S7, S8, S9, S10, S11, S12, S13, S14
owns: R15
outputs: OBSERVE
tests: T10
notes: One telemetry concern; R17 Address available on event commits. No extra commands counter/spec amendment.

## S16: Command service integration
goal: Implement command service integration with the exact owned rules and outputs.
depends: S1, S3, S4, S5, S6, S7, S8, S9, S10, S12, S13, S14
owns: R16
outputs: HOST-command
tests: T11
notes: Requires 13-3 shell registration; constructors/bundle in R7. Production node/material/connection/relay/projection remain prerequisites, no dummy services.
