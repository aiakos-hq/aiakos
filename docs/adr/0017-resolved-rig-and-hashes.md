---
id: 0017
title: Self-contained resolved rig with separate spec and binding hashes
status: accepted
date: 2026-09-30
---

# 0017 — Self-contained resolved rig with separate spec and binding hashes

## Context
ADR 0007 says the database stores rig instances "with a hash of the resolved spec". Rig files
reference other files (agents, guidance, culture, skills), are edited on Windows and in WSL
(CRLF vs LF, key order), and are paired with a machine-specific binding. The orchestrator and
node must launch exactly what was validated, and `up` must tell a team change from a local
re-binding. From [spec 0003](../specs/0003-rig-file-format-v1.md) (R25, R26).

## Decision
- The loader produces a **self-contained resolved rig**: the content of every referenced file is
  embedded (guidance and culture normalised to LF without BOM; skill files byte-exact) with its
  SHA-256. Nothing downstream reads rig files from disk again.
- The resolved rig has a **canonical form** (JSON, sorted keys, NFC strings, meaningless lists
  sorted, file content replaced by path + hash + size; `x-` fields and formatting excluded).
- **`spec_hash`** = SHA-256 of the canonical form of the **shared part** (rig, agents, embedded
  file hashes). **`binding_hash`** = the same over the **resolved binding** (`rig.env.yaml`:
  placement, paths, secret references).
- The orchestrator stores per rig instance: both hashes, the tool version, the canonical resolved
  rig and file contents by hash (with `tenant_id`).

## Consequences
The same shared files give the same `spec_hash` on every machine, so bundles, reviews and
running instances can be compared by hash. A changed `spec_hash` is a team change; a changed
`binding_hash` alone is a placement change. Running seats are never hot-reloaded; applying a
change is the CLI's decision. The canonical form is a compatibility surface: changing it changes
every hash and needs care (and an ADR if it breaks stored hashes).

## Alternatives considered
- **One hash over everything**: moving a seat to another node would look like a team change, and
  hashes would differ between machines.
- **Hashing raw file bytes**: CRLF, key order and comments would change the hash.
- **Storing references and re-reading files at launch**: what runs could differ from what was
  validated.
