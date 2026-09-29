---
id: 0004
title: Orchestrator and node agent split; nodes dial out over gRPC
status: accepted
date: 2026-09-29
---

# 0004 — Orchestrator and node agent split; nodes dial out over gRPC

## Context
Agents run in the developer's WSL, on dedicated Linux boxes, and later in sandboxes or on other
tenants' machines. OpenRig's single daemon inspects host processes and files directly, which
breaks for sandboxed and remote agents.

## Decision
Split the system into an **orchestrator** (meaning: rigs, seats, identity, queue, routing,
humans, history) and a **node agent** per machine (execution: sessions, sandboxes, files, probes,
local event ingest; no business logic). Nodes **dial out** to the orchestrator over a **gRPC
bidirectional stream**, authenticated with a node token; the network boundary is the local
network or Tailscale.

## Consequences
Local, remote and sandboxed seats share one model, and nodes work behind NAT. The gRPC contract
is the most important interface in M1 and must be versioned.
