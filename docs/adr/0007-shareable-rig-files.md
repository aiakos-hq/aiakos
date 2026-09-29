---
id: 0007
title: Shareable rig files separate from local bindings
status: accepted
date: 2026-09-29
---

# 0007 — Shareable rig files separate from local bindings

## Context
OpenRig's rig/agent spec files (team → reusable roles → shared libraries → culture → bundles)
make teams shareable. Aiakos adds machines, sandboxes, secrets, chat channels and tenants, none
of which may leak into shared files.

## Decision
Rig definitions are files (`rig.yaml`, `agent.yaml` folders, a culture file, bundles), resolved
at launch and projected into each harness's native files. Everything machine-, secret- or
tenant-specific lives in a separate **local binding** (`rig.env.yaml` or `up` arguments). Shared
files state *requirements* (e.g. `sandbox: required`, secret names); bindings state *provision*.
`aiakos up --plan` checks one against the other. The format is inspired by OpenRig; interop is a
converter (`aiakos import openrig`), not format compatibility.

## Consequences
Bundles are portable and tenant-neutral. The database stores rig instances with a hash of the
resolved spec.
