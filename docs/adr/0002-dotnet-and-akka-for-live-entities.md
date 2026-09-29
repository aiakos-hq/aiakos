---
id: 0002
title: .NET 10, with Akka.NET for live entities only
status: accepted
date: 2026-09-29
---

# 0002 — .NET 10, with Akka.NET for live entities only

## Context
The maintainer works in C#. The orchestrator manages many concurrent, long-lived things (seats,
sessions, nodes, detectors) that need ordered message handling, supervision and timers. Records
such as the work queue, chat and audit need querying and joins.

## Decision
Build on **.NET 10 / C#**. Use **Akka.NET** (with Akka.Hosting) for *live* entities: one
`SeatActor` per seat, a `NodeProxyActor` per node, health/watchdog actors. Keep queryable records
in the relational database; actors cache live state only. No Akka.Persistence until a concrete
need appears.

## Consequences
Per-seat ordering and supervision come for free. Scaling to Akka.Cluster (sharded by tenant) is a
later step, not a rewrite, because the database remains the record.
