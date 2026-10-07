## S1: Core command protocol and safe dispatch admission
goal: Publish the shared caller, envelope, command, reply and injected-port contracts and validate public command admission.
depends: -
owns: R1
outputs: PROTOCOL
tests: T1
notes: 15-2 consumes Core.ISeatCommandDispatcher and SeatCommandEnvelope; no up decision belongs here. Existing serialized SeatActor and SeatRegion routing are external prerequisites by type; unsupported command services fail closed.

## S2: Atomic command transaction protocol
goal: Define the exact immutable insert/update/transaction and store receipt surface for actor-owned command writes.
depends: S1
owns: R2
outputs: STORE-shape
tests: -
notes: Ports and supplied snapshots only. R3 supplies the concrete transaction; S3 owns the permanent SQL tests including this shape/no-op case.

## S3: PostgreSQL atomic command persistence
goal: Persist exact schema rows, command dispatch statuses, desired configuration and optimistic state changes in one transaction.
depends: S2
owns: R3, R4
outputs: STORE-inserts, STORE-rollback, STORE-status
tests: T2
notes: One transactional store concern. Requires PostgresSeatActorWriter atomic finding/session/result stages and PostgresSeatActorReader from 13-3; never a nested separately committed writer call. Tests provide pure steps directly; no command actor implementation required.

## S4: Harness-neutral launch preparation and token generation
goal: Build a wire launch and matching safe inserts from a pure StartLaunch effect with an injected adapter/material/token generator.
depends: S1, S2
owns: R5
outputs: LAUNCH-build, LAUNCH-invalid
tests: T3
notes: Consumes IHarnessAdapter.BuildLaunch/Profile from Claude orchestrator adapter. Tests use supplied file/material fakes; real finalized projection and relay material is an explicit production prerequisite. This factory does not make the up decision or send/write anything.

## S5: Serialized command routing and committed outcome waits
goal: Extend the one seat actor/gateway to route command requests and complete pending outcome waits only after result commit and refresh.
depends: S1, S3
owns: R7, C1
outputs: RESULT-order
tests: T5
notes: Requires Serialized seat apply and commit actor plus SeatRegion/SeatActorGateway and SeatActorReloaded from 13-3, including received command/launch outcomes. No direct node-port outcome. New command handlers follow later; generic ApplyAsync rejection remains unchanged.

## S6: Up lifecycle decisions
goal: Apply every pure up decision, commit session/launch/command/state atomically and send one recorded launch with caller attribution.
depends: S3, S4, S5
owns: R6
outputs: UP-matrix, UP-start
tests: T4
notes: R6 is the sole owner of up decisions. R5 supplies preparation; R3/R4 supply precommit and dispatch status. Actual launch readiness/resume loss still comes through R7's existing result event path, never the send acknowledgement.

## S7: Down lifecycle commands
goal: Commit desired-down, one stop command and honest stop outcomes while preserving resumability.
depends: S3, S5
owns: R8
outputs: DOWN-stop
tests: T6
notes: One stop concern; pure DownRequested and committed StopResult handling remain authoritative. Payload grace and command timeout are the fixed R8 policy.

## S8: Capture commands and bounded pending replies
goal: Persist and send evidence captures, then complete the waiting caller from a committed result or committed timeout.
depends: S3, S5
owns: R9
outputs: CAPTURE-result
tests: T7
notes: R7's pending completion must register before send; actor never awaits its final result in the mailbox. Fake time directly exercises R9 deadline before S10 schedules all timer classes.

## S9: At-most-once delivery admission and outcomes
goal: Persist safe sends using trusted adapter-derived lead/body, record one result and detect disagreement with committed prompt evidence.
depends: S3, S5
owns: R10, R11, R12
outputs: SEND-reject, SEND-accepted, SEND-outcome, SEND-evidence
tests: T8
notes: One delivery concern, three rules/four outputs. Requires IHarnessAdapter.BuildDelivery and existing DeliveryStateMachine plus received command/launch outcomes. Public API returns admission accepted; terminal delivery metadata/subscribers follow only R7 committed events. No node reconnection implementation.

## S10: Seat actor deadline scheduling
goal: Arm and cancel profile-driven state timers and fixed command deadlines through actor messages with generation guards.
depends: S6, S7, S8, S9
owns: R13
outputs: TIMERS
tests: -
notes: One timer concern. Timer callbacks enqueue only; R3 transactions commit timeout states. S11's T9 supplies permanent fake-time tests covering both timer/reload restoration; no production profile constants invented.

## S11: Reload and cancellation coordination
goal: Restore persisted command/deadline state, honor reload-before-release and request one gap capture without reissuing launches or deliveries.
depends: S5, S8, S9, S10
owns: R14
outputs: RELOAD, CANCEL
tests: T9
notes: Requires SeatActorReloaded publication and region-ready handshake from 13-3. R9 owns the gap-capture execution path; R13 restores original deadlines. Wait cancellation never cancels committed work or permits a resend.

## S12: Committed command observability
goal: Emit exact structured transition/finding/command signals with bounded safe attributes and no sensitive content.
depends: S6, S7, S8, S9, S10, S11
owns: R15
outputs: OBSERVE
tests: T10
notes: One telemetry concern. Reuse the existing event commit boundary so duplicate/prospective/rolled-back inputs never count; MeterListener and ActivityListener assertions pin names and sensitive sentinel absence.

## S13: Command service integration into existing shell
goal: Register the shared dispatcher/store/factory into the existing gateway and preserve event-only operation when command dependencies are absent.
depends: S1, S3, S4, S5, S6, S7, S8, S9, S10, S11
owns: R16
outputs: HOST-command
tests: T11
notes: Requires Production shell registration and host integration from 13-3. Fake real seams prove registration without the missing production node port/connection/material providers; activation stays gated on those services and real projection/relay bundles. No second actor system or dummy success services.
