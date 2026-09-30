---
id: 0014
title: Flat, rig-unique seat addresses; pods are grouping only
status: accepted
date: 2026-09-30
---

# 0014 — Flat, rig-unique seat addresses; pods are grouping only

## Context
A seat's address is its identity in the environment (`AIAKOS_SEAT`), in tmux session names,
paths, branch names, chat mentions and history. Plan §5 sketched pods as a namespace with
pod-qualified addresses (`dev.impl`). Pods are not in v1, and if they were part of the address,
introducing or reorganising pods later would rename seats and break history (M7 promises "same
addresses"). From [spec 0003](../specs/0003-rig-file-format-v1.md) (R7, Q1).

## Decision
Seat IDs are **flat and unique within a rig**. A seat's address is **`<seat>@<rig>`** (e.g.
`impl@aiakos-dev`); with tenants it is qualified as `tenant/rig/seat` (plan §9, ADR 0012). Seats
exist only inside one `rig.yaml`. **Pods** arrive in M2 as an **optional grouping attribute**
(for display, routing defaults and edges), **not part of the address**; `pods` and seat `pod`
are reserved names in v1 (ADR 0013).

This changes plan §5: its examples now use flat seat IDs (`lead`, `impl`) and `seat@rig`
instead of `dev.impl`, and the plan's `member@rig` wording is now `seat@rig`.

## Consequences
Addresses never change when pods are added, renamed or removed. Seat IDs must be unique across
the whole rig, so large rigs need distinct names (`api-impl`, `web-impl`) rather than reusing
`impl` per pod. Two rigs may use the same seat ID; paths and branches include the rig name
(`aiakos/<rig>/<seat>`).

## Alternatives considered
- **Pod-qualified addresses (`pod.seat@rig`)**, as in the plan: allows reuse of short IDs per
  pod, but ties identity to organisation and makes pods mandatory to reason about.
- **Seat IDs unique per pod, with the pod optional in the address**: ambiguous addresses when
  pods are later added.
