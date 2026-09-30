---
id: 0013
title: Rig file header, strict fields and additive-only v1
status: accepted
date: 2026-09-30
---

# 0013 — Rig file header, strict fields and additive-only v1

## Context
Rig files (ADR 0007) are shared between people and loaded by different Aiakos releases; the
`aiakos-dev` rig is loaded by the *pinned* release (ADR 0008) while its files are edited next to
newer code. Typos in hand-written YAML must fail loudly, users need somewhere to annotate files,
and later milestones need room to add fields without breaking existing files. From
[spec 0003](../specs/0003-rig-file-format-v1.md) (R2–R5, R31, Q5).

## Decision
- Every rig file starts with **`apiVersion: aiakos.dev/v1`** and a **`kind`** (`Rig`, `Agent`,
  `RigEnv`). A missing or unknown version, or the wrong kind for the file, is an error naming the
  versions the tool supports.
- **Unknown fields are errors** (with a did-you-mean hint). Fields starting with **`x-`** are
  ignored at any level and are not part of the spec hash.
- **Reserved names** (e.g. `pods`, `edges`, `imports`, `opencode`) are rejected with a dedicated
  error that names the milestone introducing them.
- **v1 is additive-only**: later releases may add optional fields and values to `aiakos.dev/v1`,
  never change the meaning of existing ones. A breaking change needs `aiakos.dev/v2`, and the
  tool keeps reading v1.

## Consequences
Typos and unsupported features are caught at `validate`/`up`, not at runtime. A file using a new
field fails on an older tool with a precise message; therefore changes to `rigs/aiakos-dev/` may
use a new field only after the release that introduced it is pinned. Every new field must be
optional with v1 behaviour as its default.

## Alternatives considered
- **Lenient parsing (ignore unknown fields)**: tolerates newer files, but silently drops typos
  such as `chekout`, violating honest state (rule 3).
- **A bare `version: "1"` field**: no `kind`, so a misplaced file cannot be detected, and no
  namespace for a future registry of kinds.
- **Semver-style minor versions in the header**: more precise, but every file edit would need a
  version bump; additive evolution under one `v1` is simpler.
