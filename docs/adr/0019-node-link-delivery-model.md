---
id: 0019
title: Node link delivery model
status: accepted
date: 2026-09-30
---

# 0019 — Node link delivery model

## Context
The node link (ADR 0004) carries commands to nodes and seat events back. Connections drop
(orchestrator restarts, network), nodes restart, hook events arrive out of order (spike 0001),
and some harness streams have no replay (spike 0004). Events must be recorded exactly once
without the orchestrator ever inventing what it did not see (rule 3), and a pasted prompt must
never be typed twice. From [spec 0002](../specs/0002-orchestrator-node-grpc-contract.md)
(R2, R14–R18, R26–R33).

## Decision
- **One bidirectional stream per node, opened by the node** (`NodeLinkService.Connect`); the
  orchestrator never dials a node. The newest authenticated stream for a node replaces the old one.
- The node numbers **seat events per seat** (`seq`, starting at 1, +1, never reused) within a
  `node_instance_id` that is new at every node process start. Command results are seat events too.
- Delivery is **at least once**; the orchestrator **dedupes by `(tenant, node instance, seat,
  seq)`** and **acks cumulatively only after the Postgres commit**. The node buffers unacked
  events and, on reconnect, **replays from the `next_seq` given in `Welcome`**.
- Commands carry an idempotency key. Idempotent commands (start, capture, stop) may be resent;
  **input delivery (`DeliverInput`, `SendKeys`) is at most once**: resent only to the same node
  instance, otherwise its outcome is recorded as `unknown`.
- **Gaps are reported, never filled**: node restarts, buffer overflow and harness stream
  reconnects produce an explicit gap; the orchestrator records it and resyncs from fresh evidence.

## Consequences
Every event the node still holds reaches the database exactly once, in order per seat, across any
number of reconnects. Losses the node cannot prevent become visible `unknown`s instead of silent
holes. The node needs a bounded buffer and a launch registry; the SeatActor must order by
`source_seq`/`seq` and handle gaps. A durable on-disk spool is deferred (M7).

## Alternatives considered
- **Unary RPCs per event or command**: simpler per call, but the orchestrator would need to reach
  nodes behind NAT and ordering would be lost.
- **Orchestrator-assigned sequence numbers**: cannot number events produced while disconnected.
- **Ack on receipt**: an orchestrator crash before commit would lose acked events.
- **At-least-once input delivery**: a duplicate paste into a harness cannot be undone.
