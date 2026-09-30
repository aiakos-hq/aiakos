---
id: 0033
title: No relaunch or fresh start without a recorded decision
status: accepted
date: 2026-09-30
---

# 0033 — No relaunch or fresh start without a recorded decision

## Context
Rule 3 forbids silent fallbacks, and plan §1 says "no silent fresh-fallback on resume". Spike 0002
showed the tempting shortcuts: a failed resume exits 1, and a relaunch could quietly start a new
conversation instead. A fresh start discards the seat's accumulated context, and a relaunch after
a crash can loop (auth errors, a broken workspace) or collide with an orphan that still holds the
session ID. Automatic restarts need budgets and backoff that M1 does not have. From
[spec 0006](../specs/0006-seat-actor.md) (R21–R25, Q7).

## Decision
- The orchestrator **never changes a seat's process or conversation without a recorded
  decision**. Every launch stores its **decision** (`new-session`, `no-conversation-yet`,
  `resume`, `resume-unverified`, `fresh-explicit`), the caller identity and an optional note.
- **`up` never falls back to a fresh session.** A resume that fails with
  `RESUME_SESSION_NOT_FOUND` makes resumability `lost` and leaves the seat `exited`; later `up`
  calls are rejected with `RESUME_LOST` until the caller asks for a fresh start.
- A fresh start that discards a conversation (resumability `resumable`, `lost` or `unknown`)
  happens only through an explicit option and is recorded as **`fresh-explicit`** with **who made
  it** (from `CallerContext`, rule 2). The previous session is kept and marked abandoned.
- **No automatic relaunch.** When a seat exits unexpectedly while `desired = up`, M1 only opens
  an **`unexpected-exit` finding**; a human runs `up`. Automatic restarts, refocus and handover are
  **M7 watchdogs**, which will record their decisions the same way.

## Consequences
Every conversation change has an author and a reason in the database, so history explains why a
seat forgot something. A crashed seat stays down until a human acts, which costs availability in
M1 but never loops or silently loses context. The M7 watchdogs must use the same decision record
and need a restart budget. Changes made by the harness itself are recorded too: a human `/clear`
in the pane becomes a new session with decision `harness-cleared` (spec 0005).

## Alternatives considered
- **Fresh fallback after a failed resume**: the exact silent loss rule 3 forbids.
- **Automatic relaunch in M1**: needs budgets, backoff and orphan handling that belong to M7.
- **Record decisions only in logs**: logs are not the record (rule 4) and are not queryable.
