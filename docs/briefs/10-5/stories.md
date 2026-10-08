## S1: Node command protocol and seat registration
goal: Publish the harness-neutral driver protocol and executor registration surface without real harness mechanics.
depends: -
owns: R1
outputs: E1
tests: T1
notes: NodeEventBuffer is already present. This story publishes the executor constructor/RegisterSeat and class shell; Receive/Acknowledge/InflightCount behavior is supplied by S2, not a successful stub. A scripted driver is test-only. Driver implementation belongs to unbriefed12-4. R1 publishes inventory transitions; S4 applies start/stop transitions while S1 tests RegisterSeat only.

## S2: Command admission and retained identity
goal: Acknowledge commands immediately, prevent duplicate execution and retain completed IDs through cumulative result acknowledgement.
depends: S1
owns: R2
outputs: E2
tests: T2
notes: R2 owns command lifetime/dedupe and safe cached evidence. Start registration arrives with S4; S2 StartSeat fixtures use RegisterSeat first. Use CapturePane on an unknown seat to prove minimal inventory. Use an internal execution callback in tests until S3/S4 adds the scheduler/driver path. Never publish a default successful driver. Result sequence/trace comes from the existing buffer; Welcome replay notification is wired by S6.

## S3: Per-seat scheduling and cancellation
goal: Execute FIFO work per seat with bypass capture/stop, relative deadlines and bounded shutdown.
depends: S2
owns: R3
outputs: E3
tests: T3
notes: Builds on R2 single-terminal lifetime and admission. Start registration arrives with S4; register S3 StartSeat fixture seats with RegisterSeat. FIFO fixtures use SendKeys behind SendKeys and start behind SendKeys on a registered stopped seat, not two different-launch starts or two unfinished deliveries. Use CapturePane for unknown-seat minimal inventory. Scripted internal execution callback exercises scheduling before S4 wires drivers. Stop and timeout race must share the same terminal gate; independent queue/driver mechanics are not reimplemented.

## S4: Launch and stop execution
goal: Map launch/stop with exact occupancy and replay rules; publish shared validation and safe driver failure mapping.
depends: S3
owns: R4
outputs: E4
tests: T4
notes: Requires R1 protocol, R2 lifetime and R3 scheduler. R4 same-pending-launch waiters copy the original STOPPED result and are exempt from R3 queued-start rejection. R4 evaluates seat preconditions at receipt before FIFO admission. Driver-owned readiness/confirmation deadlines are distinct from R3 outer deadline. Real driver is not a prerequisite; do not build12-4 or11-3/11-4. Failed/unknown launch remains occupied until explicit stop.

## S5: Input and capture execution
goal: Dispatch delivery, keys and capture with exact readiness, busy and capture-size rules.
depends: S4
owns: R9
outputs: E9
tests: T9
notes: R4 supplies validation and safe driver mapping; R2 supplies envelopes and R3 scheduling. R4 receipt-time checks reject a delivery while start is pending rather than queueing it until Ready. This story owns no launch/stop state transition.

## S6: Node command source and stream writer
goal: Compose immediate command acknowledgements and event replay with the existing reconnecting single-writer node stream.
depends: S4, S5
owns: C1, R5
outputs: E5
tests: T5
notes: EXTERNAL PREREQUISITE:10-4 source/stream story merged with EventNodeLinkSource and INodeEventSource. R2 ack/replay retention notification and R4 results use the same buffer. New seat inventory reconnect follows10-4; executor survives it. No real driver is registered by default.

## S7: Authenticated command sender and capacity
goal: Send commands through the current authenticated stream, await acceptance and release inflight slots after committed results.
depends: -
owns: C2, C3, R6
outputs: E6
tests: T6
notes: EXTERNAL PREREQUISITE:10-4 optional postcommit event application/ack transport merged. Fake node and delayed event application suffice; no node S6/actor prerequisite. All responses use existing writer gate. R6 keeps pending acceptance across removal/supersession until a later matching ack or the original deadline; R7 adds actual resend. C3 adds ambiguity-safe tenant/name lookup while preserving NodeId ownership; R6 adds outbound writer. Preserve old constructors; change tests only for C2/C3.

## S8: Safe command resend after reconnect
goal: Resend unfinished commands under original deadlines while refusing input resend across changed node epochs.
depends: S7
owns: R7
outputs: E7
tests: T7
notes: R6 owns capacity/acceptance tracking; R7 retains it across stream replacement. Resends preserve original trace and payload; actor13-4 owns durable unknown/restart policy. This story implements no cold-start recovery or command store.

## S9: SeatActor port and loopback proof
goal: Supply the production seat command/connection adapter and prove the loopback path with delayed committed results.
depends: S6, S8
owns: R8
outputs: E8
tests: T8
notes: EXTERNAL PREREQUISITES:13-4 protocol story and10-4 postcommit event application only. S6 and S8 supply transport; scripted driver remains test-only. No production actor/store prerequisite for this story.

## S10: Real actor command integration proof
goal: Prove persistence-before-send, durable replay idempotency and readiness/result ordering using production actors and stores.
depends: S9
owns: R10
outputs: E10
tests: T10
notes: EXTERNAL PREREQUISITES:13-3 and13-4 production command/event stores and10-4 production event application merged. Isolated Postgres and immutable injected synthetic profile; no production substitute. This gate waits for those stores without delaying S9 adapter readiness. Does not close real harness risks.
