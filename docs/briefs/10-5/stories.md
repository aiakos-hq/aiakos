## S1: Node command protocol and seat registration
goal: Publish the harness-neutral driver protocol and executor registration surface without real harness mechanics.
depends: -
owns: R1
outputs: E1
tests: T1
notes: NodeEventBuffer is already present. This story publishes the executor constructor/RegisterSeat and class shell; Receive/Acknowledge/InflightCount behavior is supplied by S2, not a successful stub. A scripted driver is test-only. Driver implementation belongs to unbriefed12-4.

## S2: Command admission and retained identity
goal: Acknowledge commands immediately, prevent duplicate execution and retain completed IDs through cumulative result acknowledgement.
depends: S1
owns: R2
outputs: E2
tests: T2
notes: R2 owns command lifetime/dedupe and safe cached evidence. Use an internal execution callback in tests until S3/S4 adds the scheduler/driver path. Never publish a default successful driver. Result sequence/trace comes from the existing buffer; Welcome replay notification is wired by S5.

## S3: Per-seat scheduling and cancellation
goal: Execute FIFO work per seat with bypass capture/stop, relative deadlines and bounded shutdown.
depends: S2
owns: R3
outputs: E3
tests: T3
notes: Builds on R2 single-terminal lifetime and admission. Scripted internal execution callback exercises scheduling before S4 wires drivers. Stop and timeout race must share the same terminal gate; independent queue/driver mechanics are not reimplemented.

## S4: Command execution and result mapping
goal: Map five command kinds to registered drivers and publish one sequenced result with exact preconditions and errors.
depends: S3
owns: R4
outputs: E4
tests: T4
notes: Requires R1 protocol, R2 lifetime and R3 scheduler. Driver-owned readiness/confirmation deadlines are distinct from R3 outer deadline. Real driver is not a prerequisite; do not build12-4 or11-3/11-4. Failed/unknown launch remains occupied until explicit stop.

## S5: Node command source and stream writer
goal: Compose immediate command acknowledgements and event replay with the existing reconnecting single-writer node stream.
depends: S4
owns: C1, R5
outputs: E5
tests: T5
notes: EXTERNAL PREREQUISITE:10-4 source/stream story merged with EventNodeLinkSource and INodeEventSource. R2 ack/replay retention notification and R4 results use the same buffer. New seat inventory reconnect follows10-4; executor survives it. No real driver is registered by default.

## S6: Authenticated command sender and capacity
goal: Send commands through the current authenticated stream, await acceptance and release inflight slots after committed results.
depends: -
owns: C2, R6
outputs: E6
tests: T6
notes: EXTERNAL PREREQUISITE:10-4 optional postcommit event application/ack transport merged. Fake node and delayed event application suffice; no node S5/actor prerequisite. All responses use existing writer gate. Only R6 changes registry lookup to tenant/name and adds outbound writer, preserving old constructors and tests.

## S7: Safe command resend after reconnect
goal: Resend unfinished commands under original deadlines while refusing input resend across changed node epochs.
depends: S6
owns: R7
outputs: E7
tests: T7
notes: R6 owns capacity/acceptance tracking; R7 retains it across stream replacement. Resends preserve original trace and payload; actor13-4 owns durable unknown/restart policy. This story implements no cold-start recovery or command store.

## S8: SeatActor port and integration proof
goal: Supply the production seat command/connection adapter and prove the complete link path with delayed and real committed results.
depends: S5, S7
owns: R8
outputs: E8
tests: T8
notes: EXTERNAL PREREQUISITES:13-4 protocol plus13-3/13-4 production command/event stores and10-4 production event application merged. No issue may be readied before these exist. S5 and S7 supply transport; scripted driver remains test-only. Real actor proof uses immutable injected profile and isolated Postgres, no production substitute. Readiness ordering fixture is synthetic and does not close real harness risks.
