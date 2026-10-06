# Story review: slice 10-4

Reviewed at commit c8a46cf. Stories: 5. Check: ok.

## Findings

- [x] S5 (judgment-gap): R16 waits for a matching `SeatActorReloaded` with no end, and R12 reads no further envelope while a callback is pending, so when one seat's actor never reloads, every other seat's events on that node stay uncommitted, and `NodeLinkService` checks liveness, supersession and shutdown only inside `readNext`, which is not running. Fix: state in R16 and E16 what ends the wait (which of liveness expiry, supersession and shutdown the pending callback observes) and the exact result, and say in R12 that other seats' events wait behind it.
- [x] S5 (judgment-gap): R15 inserts a new `seat_finding` row for every unknown inventory entry of every Hello and for every rejected event, and a rejected event gets a null ack, so the node keeps it and sends it again on each reconnect: one misassigned seat adds rows without bound, although the table has `occurrences` and `last_seen_at`. Fix: state in R15 and E15 that an open node-scoped row for the same tenant, node name, kind and seat is updated (`occurrences`, `last_seen_at`) instead of inserted, with the seat id in its evidence, or record a bound the maintainer accepted.
- [x] S5 (judgment-gap): R16 recognises the retryable failure by the exception message `SEAT_COMMIT_FAILED`, which G2 forbids ("no message-text parsing") and which is not in the list of ports that 13-3 publishes. Fix: add a typed signal (an exception type or a result) to the 13-3 prerequisite block and use it in R16 and E16.
- [x] S2 (judgment-gap): R5 drops the globally oldest retained envelope "until bounds hold" for all three limits, so when only seat A exceeds `MaxEventsPerSeat` and seat B holds older events, B's events are dropped first and A stays over its limit until B is empty. Fix: state in R5 and E5 the victim per limit (the oldest envelope of the seat that exceeds its own limit; the globally oldest for the two node limits) and whether telemetry coalescing applies to that seat or to all.
- [x] S1 (context-gap): E1 to E3 do not name the kind of the fixture's events, and S2 then holds a second Telemetry event of a seat for one second (R6) and rejects attribute values above 1024 bytes (R4), so an S1 test written with Telemetry events turns red when S2 merges. Fix: state in E1 to E3 that the fixture uses a non-Telemetry kind and attribute values within R4's limit.
- [x] S5 (judgment-gap): S5 owns the acknowledgement path through `NodeProxyActor`, the registry and `NodeLinkService` (R12), the facade with its SQL reads, Hello validation and finding writer (R15), re-forwarding (R16) and the 1000-event restart test on Postgres (R14), which is more than one run for a Sonnet-level implementer, and the R12 path needs only S3's `INodeEventApplication`, not 13-3. Fix: split it into the acknowledgement path with a fake committer (no external prerequisite), the facade, and re-forwarding with the end-to-end test, with T5 divided to match.

## Not checked

- The spec 0006 sentence in this branch was compared with ADR 0032 and with lead's qitem-20261006074916-a1895c92: the ADR names "seat findings" as the SeatActor's, node-scoped findings are not among them, and I see no contradiction; no new ADR is asked for.
- The 13-3 ports in the brief do not exist yet; S5's text has to be read against the 13-3 brief when that is written.
- `seat_finding` in migration 0002 already allows `seat_id` null with `node_name` set and has no unique index for such rows; read, not run.
- R5's pending-gap accounting, R6's rate clock and R8's cancellation order were read for a determinate answer, not simulated.
- Whether `tools/story.sh analysis-pr` accepts the spec file that this branch carries beside the brief.

## Author resolution, round 1

- Finding 1: R12/C2/E12 now bound pending optional event callbacks by the existing liveness
  clock and specify Unknown/no ack, fixed Unavailable, supersession/shutdown/cancellation
  precedence, other-seat waiting and bounded state cleanup. R16/E16 share that budget without
  reset. Source detail: current NodeLinkService already has waitForCallback checks; its expiry
  only marks Unknown and keeps waiting. Ordinary applications retain the prior behavior.
- Finding 2: R15/E15/T6 update one open node-scoped finding per tenant/node/kind/evidence seat,
  serializing concurrent first insert with a transaction advisory lock. No new schema/index.
- Finding 3: prerequisites/R16/E16/T7 use SeatCommitFailedException, not exception text.
  Typed declaration and behavior sent to author2 as durable13-3 coordination item.
- Finding 4: R5/E5 specify per-seat coalescing/eviction first; node-wide limits then coalesce
  all seats and evict globally. A-only overflow cannot evict B.
- Finding 5: E1-E3 base fixture names Other, small raw/attributes; no Telemetry assumptions.
- Finding 6: replaced S5 with independent ack path S5, real facade S6, retry/end-to-end S7;
  T5-T7 split. S5 depends onlyS3; S6 external13-3; S7 addsS4 and typed reload port.

Author tick records the edits; architect reviews the diff independently.
