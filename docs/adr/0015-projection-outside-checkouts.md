---
id: 0015
title: Guidance and skills are projected outside repository checkouts
status: accepted
date: 2026-09-30
---

# 0015 — Guidance and skills are projected outside repository checkouts

## Context
Rig files are resolved and *projected* into each harness's native files (ADR 0007): for Claude
Code, `CLAUDE.md` and `.claude/skills/`. The repositories seats work on usually have their own
`CLAUDE.md` and `.claude/` (this one does). Writing generated files into a checkout would
overwrite or merge with the repo's files, dirty the worktree, risk being committed, and make
`shared` checkouts conflict between seats. From [spec 0003](../specs/0003-rig-file-format-v1.md)
(R28–R30, Q3).

## Decision
Projection writes only to a **per-seat, Aiakos-owned projection root** on the node,
`<seat_root>/<rig>/<seat>/projection`, regenerated on every launch. It **never writes, modifies
or deletes files inside a repository checkout**. The harness adapter makes the harness load the
projection in addition to the checkout's own files (for Claude Code: `--add-dir <projection
root>` with `CLAUDE_CODE_ADDITIONAL_DIRECTORIES_CLAUDE_MD=1`, to be verified by the adapter
spec). The generated `CLAUDE.md` is deterministic and carries a "do not edit" header with the
seat address and `spec_hash`.

## Consequences
The repo's own guidance keeps applying unchanged, worktrees stay clean, and `shared` and
`seat-worktree` checkouts behave the same. Each harness adapter needs a mechanism to load
guidance and skills from an extra directory; if one does not exist for a harness or checkout
policy, projection fails with a clear error rather than falling back to the checkout.

## Alternatives considered
- **Write into the checkout and git-ignore it**: conflicts with the repo's own `CLAUDE.md`, needs
  changes to every target repo, and breaks `shared` checkouts.
- **Merge into the repo's `CLAUDE.md`**: silently edits user files; merges are fragile.
- **User-level harness config (`~/.claude`)**: shared by every seat on the node, so seats would
  see each other's guidance.
