---
id: 0020
title: "Node contract versioning: package major, negotiated minor, capabilities"
status: accepted
date: 2026-09-30
---

# 0020 — Node contract versioning: package major, negotiated minor, capabilities

## Context
ADR 0004 requires the node contract to be versioned. Nodes and the orchestrator are released
separately in practice: the bootstrap rule (ADR 0008) runs a pinned release against nodes that
may be older or newer, and later nodes differ in what they can do (harnesses, sandboxes, send
keys, fork). From [spec 0002](../specs/0002-orchestrator-node-grpc-contract.md) (R3–R5,
compatibility rules).

## Decision
- The **major version is the proto package** (`aiakos.node.v1`). A breaking change creates
  `aiakos.node.v2`, served side by side with `v1` for at least one release. A different major is
  rejected at connect.
- Within a major, changes are **additive** only (no renumbering, retyping or reuse of fields;
  removed ones become `reserved`). Each revision increments a **minor** version carried in
  `Hello` and `Welcome`; the **negotiated minor is the lower of the two**, and neither side uses a
  feature above it.
- Optional features are gated by **capability strings** advertised by the node in `Hello` (e.g.
  `harness.claude-code`, `command.send-keys`, `launch.fork`). The orchestrator sends only what
  the node advertised; anything else is rejected as `UNSUPPORTED`.
- Every enum has `*_UNSPECIFIED = 0`; unknown enum values read as unknown, never as a default.

## Consequences
An orchestrator accepts nodes of its major at any lower minor, and newer nodes degrade to the
negotiated minor, so the released tool keeps working with nodes from the same or older releases.
Node features can differ per machine without version bumps. Breaking changes are checked in CI
(ADR 0021).

## Alternatives considered
- **Version only in the package**: every addition would be a new major.
- **Minor version only, no capabilities**: cannot express that one node has Docker and another
  does not.
- **Feature detection by trial** (send and see if it fails): turns missing features into runtime
  errors on real work.
