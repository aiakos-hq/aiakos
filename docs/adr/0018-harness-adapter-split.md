---
id: 0018
title: Harness adapter split into orchestrator adapter and node driver
status: accepted
date: 2026-09-30
---

# 0018 — Harness adapter split into orchestrator adapter and node driver

## Context
Plan §3 and CLAUDE.md rule 5 name one `IHarnessAdapter` per harness (project, build launch,
parse events). But the orchestrator and node are separate processes (ADR 0004): the node sees
raw hook and SSE payloads first and holds the timers for readiness and confirmation, while the
orchestrator owns meaning. Each concern has to live on one side of the wire. From Q1 in
[spec 0002](../specs/0002-orchestrator-node-grpc-contract.md).

## Decision
Each harness is implemented in two halves that share only the node contract:
- **`IHarnessAdapter`** (orchestrator): builds launches (argv, settings, projected files, session
  ID handling for fresh/resume/fork) and **interprets** normalized events into seat state.
- **`IHarnessDriver`** (node): the mechanics that need to be close to the harness: **readiness**,
  **delivery**, **delivery confirmation**, **resume verification**, and **event normalization**
  (`kind`, native session ID, well-known attributes, usage).

The node **always forwards the raw payload** with each normalized event (truncated only above the
size limit, never omitted), so the orchestrator can re-derive anything the normalization missed.
Both interfaces are harness-neutral; the M2 OpenCode driver implements the same ones.

This refines ADR 0004's "no business logic on the node": the driver reports observations and
outcomes, it never decides seat state. CLAUDE.md rule 5 now lists both interfaces.

## Consequences
Readiness and confirmation are decided where the signals arrive, without a round trip. Seat state
logic stays in one place (the SeatActor). A normalization bug is recoverable from stored raw
payloads. Every harness needs code in both the orchestrator and node projects, and node and
orchestrator must agree on the normalized vocabulary, which is part of the versioned contract
(ADR 0020).

## Alternatives considered
- **Everything in the orchestrator** (node forwards raw bytes only): readiness and confirmation
  timers would run across the network, and the node could not verify a resume by itself.
- **Everything on the node**: puts state interpretation, and therefore business logic, on every
  machine, against ADR 0004.
