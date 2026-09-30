---
id: 0023
title: The session host never sends input on its own; delivery is one composite operation
status: accepted
date: 2026-09-30
---

# 0023 — The session host never sends input on its own; delivery is one composite operation

## Context
A keystroke sent to a harness cannot be taken back. A harness can sit on a screen the node does
not recognise (trust dialog, resume picker), where an Enter selects the default, and the trust
dialog's default is "No, exit" (spikes 0001, 0002). Input sent before the TUI is ready loses the
submit key; pastes of five or more lines arrive wrapped as data unless a typed line leads them;
an `ESC [201~` inside a paste ends bracketed paste early and turns the rest into keystrokes.
ADR 0019 makes input delivery at most once. From
[spec 0004](../specs/0004-tmux-session-host.md) (R3, R14–R21).

## Decision
- The session host **never sends input on its own initiative**: no key, text or Enter on start,
  on a timeout, on an unknown screen or during adoption. Every byte written to a pane comes from
  an explicit delivery, an explicit send-keys, or the confirmer's single resubmit.
- **Delivery is one composite operation**, in this order: load the body into a named tmux buffer
  **from stdin** (`load-buffer -`); type the one-line **lead** with **`send-keys -l`**; paste the
  body as **one** bracketed paste (**`paste-buffer -p -r -d`**); send the submit key as a
  **separate `C-m`**; then call the harness driver's **confirmer**, which may resubmit **at most
  once**. The report states the last completed stage, so partial progress is never hidden; after
  the pane was touched, a failure is not retryable and nothing is sent to clean up.
- **Control characters are rejected**, not repaired: the lead is one line with no C0/C1
  controls or DEL; the body allows only LF and TAB (CRLF and CR are normalized to LF and
  reported). ESC is always rejected. Send-keys accepts only a closed set of named keys.
- There is **one input operation per seat** at a time: a second delivery or send-keys fails
  immediately with `Busy` (no queueing in the host). The gate is held until the confirmer
  returns. Capture, status and stop never take it.

## Consequences
The worst case on an unexpected screen is a `READY_TIMEOUT` with a capture as evidence, never a
wrong answer to a dialog. Delivered text never passes through a shell or the tmux command parser.
Content whose exact bytes matter (TAB becomes spaces in the Claude TUI) must be sent by file
path. Harness-specific behaviour enters only through the confirmer, orphan probe and graceful
stop callbacks supplied by the driver (ADR 0018).

## Alternatives considered
- **Auto-dismiss known dialogs with keys**: a misclassified screen would be answered wrongly,
  and the default answers are destructive.
- **Pass the body as a `send-keys` argument**: exposes it to tmux's parser and argument limits,
  and types it instead of pasting.
- **Queue input in the session host**: a second paste could land while the first is
  unconfirmed; queueing belongs to the command executor.
- **Silently strip control characters**: the harness would not receive what was sent.
