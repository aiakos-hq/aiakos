---
id: 0029
title: The node may write minimal harness user configuration (exact-workdir trust)
status: accepted
date: 2026-09-30
---

# 0029 — The node may write minimal harness user configuration (exact-workdir trust)

## Context
Claude Code shows a trust dialog for an untrusted workspace before `SessionStart`, and its
default answer is "No, exit" (spike 0002 F8). ADR 0023 forbids answering it with keys. Spike 0002
concluded that trusting an ancestor directory covers everything below it, and spec 0003 R30
therefore asks the adapter to *check* that the seat root is trusted. Spec 0005's experiment
(T1–T7) showed that **trust is inherited from an ancestor only up to a git repository root**: a
trusted seat root does not cover a checkout or `git worktree` under it; the exact path (or the
worktree's main clone) must be trusted. A manual "trust this path" step before each new worktree
would fail the first `up` of every seat. From
[spec 0005](../specs/0005-claude-code-adapter.md) (R12, R13, Q2).

## Decision
The node may write the user's `~/.claude.json`, **narrowly**:
- It sets only `projects["<workdir>"].hasTrustDialogAccepted = true` for the **exact working
  directory** of the seat (the worktree for `seat-worktree`, the bound clone for `shared`). It
  never trusts the seat root, an ancestor or a main clone as a substitute, and never sets
  onboarding, theme or other keys in the user's own config.
- The write is a read-modify-write that preserves every other key, goes through a temp file,
  `fsync` and rename (**atomic**), and is **verified by re-reading** (up to 3 attempts). An entry
  that is already `true` is not rewritten. Writes are logged by path, never by content.
- Failure is honest: `TRUST_NOT_ESTABLISHED` before launch, or a later `UNKNOWN READY_TIMEOUT`
  with the screen classified as `trust-dialog` (ADR 0030). No key is ever sent.

This **corrects spike 0002's trust finding and spec 0003 R30**: "check that the seat root is
trusted" becomes "ensure the exact workdir is trusted".

## Consequences
Seats start without a manual trust step, and trust does not spread to other worktrees of the same
clone (other rigs', or the user's own). Aiakos now writes a file it does not own, which Claude
rewrites concurrently; a lost update is detected by the re-read or by the trust-dialog timeout,
never silent. Sandboxed homes (M6) are Aiakos-owned and are seeded fully; this ADR is about the
user's own home on local nodes.

## Alternatives considered
- **Trust the seat root once**: does not work for git checkouts (T1, T4).
- **Trust the main clone**: works (T5), but extends trust to every worktree of that clone.
- **Require the user to trust each path**: fails the first `up` of each seat.
- **Answer the trust dialog with a key**: forbidden by ADR 0023; a misread screen would be
  answered wrongly.
