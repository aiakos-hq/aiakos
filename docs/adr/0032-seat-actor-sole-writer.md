---
id: 0032
title: The SeatActor is the sole writer of seat state; one transaction per input
status: accepted
date: 2026-09-30
---

# 0032 — The SeatActor is the sole writer of seat state; one transaction per input

## Context
ADR 0002 uses Akka.NET for live entities while the database stays the record; ADR 0003 puts that
record in Postgres. ADR 0019 requires the orchestrator to ack node events only after they are
committed, and delivery of input to be at most once. If events were written by one component and
state by another, an orchestrator crash between the two could leave evidence without a conclusion
(or the reverse), and a restart would need to replay the event log to rebuild state. From
[spec 0006](../specs/0006-seat-actor.md) (R3–R7, R34–R36, Q4).

## Decision
- The **SeatActor** is the **only writer** of a seat's state: `seat_state`, `seat_transition`,
  `seat_launch`, `seat_session`, command outcomes and seat findings. Other components only read;
  the `up` API writes only desired configuration (`rig`, `seat`).
- State derivation is a pure function (`SeatStateMachine.Apply`); the actor does the I/O around it
  and processes **one input at a time**, stashing further messages while a commit is in flight.
- **Each input is one transaction**: the new events (deduplicated by
  `(tenant_id, node_instance_id, seat_id, seq)`), the transitions, the new `seat_state` snapshot
  with an **optimistic `version` check**, finding changes and new command rows commit together
  or not at all.
- **Acks and commands go out only after the commit**: `EventsCommitted` (and so `EventAck` to the
  node), dispatched commands and replies to callers follow the successful commit.
- A failed commit crashes the actor; the supervisor restarts it, it reloads its snapshot from
  Postgres, and the NodeProxy re-forwards the uncommitted events. Restart never replays the event
  log: the snapshot is already consistent with the events. `seat_event` and `seat_transition` are
  append-only.

## Consequences
Evidence and conclusion can never disagree, and no input is half-applied. An orchestrator crash
loses nothing the node still holds, and a second writer (a bug) is detected by the version check
instead of silently overwriting state. The cost is one transaction per event batch per seat,
fine for M1; M8 can shard actors by tenant and seat without Akka.Persistence because state is
rebuilt from Postgres.

## Alternatives considered
- **NodeProxy writes events, SeatActor writes state**: two transactions that a crash can split.
- **Event sourcing (Akka.Persistence or log replay on start)**: slower restarts and a second
  source of truth next to the relational record; rejected by ADR 0002 until needed.
- **Send commands before committing**: a crash would leave a command in flight that the database
  does not know about, breaking at-most-once delivery.
