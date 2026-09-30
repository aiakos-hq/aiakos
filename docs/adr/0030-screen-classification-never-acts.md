---
id: 0030
title: Screen classification explains outcomes but never triggers input
status: accepted
date: 2026-09-30
---

# 0030 — Screen classification explains outcomes but never triggers input

## Context
When a launch does not become ready, the harness is usually sitting on a screen: a trust dialog,
a resume picker, "not logged in", an API-key dialog, a settings error. A pane capture shows it,
and recognising it gives a human a precise reason instead of "timed out". But screen text is
fragile, changes between harness versions, and a misread screen answered with a key can pick a
destructive default (spikes 0001, 0002). ADR 0005 already says a capture is evidence, never the
primary state signal, and ADR 0023 forbids the session host from sending input on its own. From
[spec 0005](../specs/0005-claude-code-adapter.md) (R26, R28).

## Decision
- Pane text and screen classification may **explain** an outcome: they refine the **reason** of a
  `FAILED` or `UNKNOWN` result (for example `RESUME_SESSION_NOT_FOUND`, `SESSION_ID_IN_USE`, or
  `metadata.screen = trust-dialog | resume-picker | login-required | api-key-dialog |
  settings-error | unrecognized` on a `READY_TIMEOUT`), and the capture is attached as evidence.
- They **never trigger keys or input**, never change an outcome from `UNKNOWN` to `READY` or
  `FAILED` on their own, and never cause a relaunch or a fresh start.
- Outcomes come from hook evidence and process facts (readiness hook for this launch, pane death,
  timeouts). A resume never falls back to a fresh launch because of what the screen shows.

## Consequences
A wrong classification costs at most a misleading reason on an outcome that is already `FAILED` or
`UNKNOWN`; it cannot damage a conversation or answer a dialog. The classifier can be tested
against recorded captures and updated freely when the harness changes its screens. Humans resolve
blocked screens themselves in the attached pane; automated recovery, if ever added, needs its own
ADR.

## Alternatives considered
- **Auto-answer recognised dialogs**: saves a human step, but turns a text-matching bug into a
  wrong answer with side effects.
- **Derive readiness from the screen** (prompt visible): fragile across versions, and contradicts
  ADR 0005.
- **No classification**: every blocked launch would be a bare timeout, and the human would have
  to capture the pane to learn why.
