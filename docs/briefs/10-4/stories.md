## S1: Sequence and replay buffer
goal: An in-memory node buffer assigns sequence numbers and retains events through cumulative ack and ordered replay.
depends: -
owns: R1, R2, R3
outputs: E1, E2, E3
tests: T1
notes: No actor prerequisite. Later R4-R6 extends this buffer; E1-E3 use Other events with small attribute/raw values so later rate/admission rules preserve these results. Recovered registration is an explicit producer API, not session discovery.

## S2: Evidence normalization and bounded telemetry
goal: The node buffer normalizes proto evidence and stays bounded while retaining honest overflow gaps and controlling connected telemetry rate.
depends: S1
owns: R4, R5, R6
outputs: E4, E5, E6
tests: T2
notes: No actor prerequisite. Extends R1-R3; per-seat eviction touches only that seat, node count/bytes eviction is global after telemetry coalescing. Pending overflow bookkeeping is scalar, never a durable spool.

## S3: Actor commit interface and cursor adapter
goal: The transport publishes a postcommit facade contract and pure cursor helper, with an application adapter tested against fake delayed committers.
depends: -
owns: R9, R10, R11
outputs: E9, E10, E11
tests: T4
notes: No actor prerequisite. No production committer/storage implementation. The real facade and independent13-3 actor ports are consumed by S6/S7.

## S4: Buffered node source and stream integration
goal: Production node composition sends buffer inventory and event replay through the reconnecting stream with immutable original trace and one writer.
depends: S2
owns: C1, R7, R8, R13
outputs: E7, E8, E13
tests: T3
notes: No actor prerequisite. S2 supplies the complete buffer; C1 changes only buffered SeatEvent sender tracing. Existing sources without INodeEventSource remain supported. R7 amendment requires refreshed Hello after post-Hello new registration, including held Welcome; E7/E8/T3 cover node-wide reconnect costs and retained original replay.

## S5: Acknowledgement transport and bounded callbacks
goal: NodeProxy and the stream emit acknowledgements only after an optional event application completes, and end stalled event callbacks on liveness expiry.
depends: S3
owns: C2, R12
outputs: E12
tests: T5
notes: No actor prerequisite and no S4 dependency: a scripted node/fake committer exercises transport. R11 provides the optional event callback/nullable ack. C2 changes only event-application pending callbacks, preserving ordinary10-3 behavior. Other seats wait behind a pending callback until completion or bounded termination.

## S6: Authenticated actor facade and node findings
goal: Production composition reads authenticated assignment/checkpoints, routes event and control inputs through actor commit ports, and coalesces node-scoped findings.
depends: S5
owns: R15
outputs: E15
tests: T6
notes: EXTERNAL PREREQUISITE: slice13-3 merged with ISeatEventCommitter and ISeatInputCommitter as published in brief. Lead must not ready before that. S5 supplies the ack path; fake immutable profile is injected into the real actor for tests. Only node-scoped findings in the existing table are written by link, per spec clarification. No transport inbox/table/seat-state/event writer. Retry is added by S7.

## S7: Actor re-forwarding and durable replay proof
goal: Failed actor commits await matching typed reload recovery and re-forward immutable pending evidence, with real-provider replay/restart acceptance.
depends: S6, S4
owns: R14, R16
outputs: E14, E16
tests: T7
notes: EXTERNAL PREREQUISITE: slice13-3 merged with SeatCommitFailedException/ISeatActorLifecycle/SeatActorReloaded exact public types. S6 supplies production facade/actor route; S4 supplies node buffer/stream for end-to-end replay. R12 liveness/supersession/shutdown/cancellation budget spans retry without reset; late recovery never ack/retries. No event log replay or fabricated actor/harness.
