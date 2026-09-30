---
id: 0016
title: Secrets are node-local file references, delivered as files
status: accepted
date: 2026-09-30
---

# 0016 — Secrets are node-local file references, delivered as files

## Context
Shared rig files name the secrets a seat needs; the local binding says where they come from
(ADR 0007). Secret values that pass through the CLI, the orchestrator or the database end up in
logs, traces, backups and resolved-rig records. Environment variables and argv are visible in
`/proc`, `docker inspect` and `docker events` (spike 0005). From
[spec 0003](../specs/0003-rig-file-format-v1.md) (R21, R22) and spike 0005.

## Decision
- Shared files contain secret **names** only (`requires.secrets`).
- In `rig.env.yaml`, a secret source is a **`file:` reference to a path on the node**. Inline
  values are errors; other sources (`store:`, `env:`) are reserved for later milestones and must
  keep the properties below.
- The loader validates the reference shape and **never reads the file**. The **node** reads it at
  launch and **delivers it to the seat as a file** (for local seats, a mode-0600 file under the
  seat directory or the source file itself; in sandboxes, a tmpfs file). The adapter points the harness at the file (e.g. `apiKeyHelper`).
- Secret **values never reach the CLI, the orchestrator, the database**, logs, traces, argv or
  environment variables. `binding_hash` covers the reference, not the value.
- All files are scanned for credential-like strings; a match is an error that never echoes the
  value.

This covers user secrets. The per-seat identity token is different: the orchestrator mints it
(rule 2) and sends it to the node over the node link.

## Consequences
The orchestrator and database cannot leak secrets they never hold, and rotating a key file needs
no spec change. Secrets must be provisioned on each node by the operator. Seats see secrets only
as files, so harnesses that expect environment variables need a file-reading helper.

## Alternatives considered
- **Inline values in `rig.env.yaml`**: easy, but the file is one `git add` away from a leak.
- **Orchestrator-held secret store pushing values to nodes**: centralised, but puts values in the
  orchestrator and on the wire; revisit with M6 secret stores, resolved on the node.
- **Environment-variable delivery**: visible to other processes and container tooling.
