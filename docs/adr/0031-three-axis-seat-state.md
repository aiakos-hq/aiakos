---
id: 0031
title: Three-axis seat state with fixed values, reasoned unknowns and a reporting overlay
status: accepted
date: 2026-09-30
---

# 0031 — Three-axis seat state with fixed values, reasoned unknowns and a reporting overlay

## Context
Plan §1 and §3 promise "honest state" on three orthogonal axes (session / activity /
resumability) with `unknown` as a valid answer, but name only some values. Without closed value
sets, every consumer (CLI, dashboard, queue, watchdogs) would interpret state differently, and
`unknown` would become a catch-all. Link loss and orchestrator restarts make state temporarily
unobservable without making it wrong, and replayed events must still apply to the facts
underneath. ADR 0003 stores the state in Postgres. From
[spec 0006](../specs/0006-seat-actor.md) (R8–R20).

## Decision
- A seat's state has exactly three axes with **only these values**:
  - **session**: `absent`, `starting`, `present`, `exited`, `unknown`;
  - **activity**: `none`, `idle`, `working`, `needs-input`, `unknown`;
  - **resumability**: `none`, `fresh-only`, `resumable`, `lost`, `unknown`.
- Every **`unknown` carries a reason from a closed list** per axis (for example
  `node-link-lost`, `orchestrator-restarted`, `launch-unconfirmed`, `observation-gap`,
  `quiet-timeout`, `sources-disagree`, `session-id-mismatch`), and every axis records `since`.
  `unknown` is entered only by defined triggers and left only by defined evidence; disagreement
  between sources gives `unknown` plus a health finding, never a chosen winner.
- The axes are constrained: activity is `none` exactly when session is `absent` or `exited`, and
  `unknown` while session is `starting` or `unknown`. Resumability is a property of the
  conversation and is independent of the other two.
- For session and activity, the stored **last-known** (derived) value is separate from the
  **reported** value. A **reporting overlay** (`node-link-lost` or `orchestrator-restarted`)
  makes the reported value `unknown` while events keep applying to the last-known value; the
  overlay clears when replay has caught up with the same node instance. A new node instance or a
  gap means lost evidence and changes the derived state itself (`observation-gap`). Resumability
  has no overlay.

## Consequences
`ps`, the dashboard and later the queue share one vocabulary, enforced by `CHECK` constraints
(including "an `unknown` has a reason"). Commands decide on reported values, so nothing is sent
to a seat that is not currently observed. A short link loss heals without false transitions.
Adding a value or reason is a schema change; harnesses add profiles, not values.

## Alternatives considered
- **Only the values named in the plan and issue**: no `starting` (input would be accepted during
  a trust dialog), no `lost` (no-fallback would be a convention, not a state).
- **`null` for "no activity"**: invites "treat as idle" bugs; `none` is a fact, `unknown` is not.
- **Writing `unknown` into the derived state on link loss**: replayed events would have to
  rebuild it, and a same-instance reconnect would look like lost evidence.
