## S1: Sequence and replay buffer
goal: An in-memory node buffer assigns sequence numbers and retains events through cumulative ack and ordered replay.
depends: -
owns: R1, R2, R3
outputs: E1, E2, E3
tests: T1
notes: No actor prerequisite. Later budget normalization R4-R6 extends this buffer; this story uses default limits and no overflow workload. Recovered registration is an explicit producer API, not session discovery.

## S2: Evidence normalization and bounded telemetry
goal: The node buffer normalizes proto evidence and stays bounded while retaining honest overflow gaps and controlling connected telemetry rate.
depends: S1
owns: R4, R5, R6
outputs: E4, E5, E6
tests: T2
notes: No actor prerequisite. Extends R1-R3 with admission/eviction/rate behavior; pending overflow bookkeeping is scalar and never a durable spool.

## S3: Actor commit interface and cursor adapter
goal: The transport publishes a postcommit facade contract and pure cursor helper, with an application adapter tested against fake delayed committers.
depends: -
owns: R9, R10, R11
outputs: E9, E10, E11
tests: T4
notes: No actor prerequisite. No production committer/storage implementation. The real facade and 13-3 actor port are consumed only by S5.

## S4: Buffered node source and stream integration
goal: Production node composition sends buffer inventory and event replay through the reconnecting stream with immutable original trace and one writer.
depends: S2
owns: C1, R7, R8, R13
outputs: E7, E8, E13
tests: T3
notes: No actor prerequisite. S2 supplies the complete buffer; C1 changes only buffered SeatEvent sender tracing. Existing sources without INodeEventSource remain supported.

## S5: Committed orchestrator ingress and acknowledgements
goal: The production orchestrator routes authorized events to the real SeatActor commit port and acknowledges only committed evidence across retries and reconnects.
depends: S3, S4
owns: R12, R14, R15, R16
outputs: E12, E14, E15, E16
tests: T5
notes: EXTERNAL PREREQUISITE: slice13-3 merged with ISeatEventCommitter, committed ApplyAsync and explicit failure/reloaded seam described in the brief; lead must not ready this story before that. S3 defines the facade/adapter and S4 provides the actual node stream for end-to-end evidence. R15 permits only node-scoped findings in the existing table, per the spec clarification in this analysisPR. R16 re-forwards on the exact actor failure/reload seam. No new transport inbox/table/seat-state writer. Real-provider tests exercise the existing actor transaction, not a fake replacement.
