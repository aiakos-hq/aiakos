---
id: 0024
title: Stop terminates the harness process tree by signals and verifies it
status: accepted
date: 2026-09-30
---

# 0024 — Stop terminates the harness process tree by signals and verifies it

## Context
Closing a tmux session sends SIGHUP and loses the exit status. Children of a harness may
`setsid` away and be re-parented once the harness dies; for `docker exec` panes, killing the pane
leaves the harness running in the container, holding the session ID (spike 0005). A `pgrep -f`
pattern once matched the tmux server's own argv and killed the whole server (spike 0004), and
`kill-session` of the last session stops the server too. A leftover harness blocks the next
start with "session ID already in use". From [spec 0004](../specs/0004-tmux-session-host.md)
(R25–R27).

## Decision
Stopping a seat is signal-based termination of its process tree, with verification:
1. If the pane is dead or gone, the outcome is `NotRunning` with the recorded exit status.
2. **Snapshot the process tree** before any signal: the pane PID, all descendants (from `/proc`
   parent links) and every process in the pane's session, each identified by PID **and start
   time**, so a reused PID is never signalled. Only processes of the node's UID are considered;
   processes are never selected by a pattern over all processes.
3. Send **SIGTERM to the harness** (the pane process), or a driver-supplied graceful stop (M6:
   inside the container).
4. **Wait the grace period** (default 10 s, from `StopSeat.grace`).
5. If the harness is still alive, **SIGKILL** every tracked process (`Killed`); otherwise
   SIGTERM the remaining tracked processes, wait a short child grace, then SIGKILL (`Stopped`).
   Descendants are rescanned before each round.
6. **Verify** that no tracked process is left; **survivors are reported** by PID, never ignored.
7. Record the exit status, then remove the (now dead) session and the registry entry.

The node **never runs `kill-session` on a live harness** and **never runs `kill-server`**.

## Consequences
A stop leaves no orphan holding the conversation, and the exit status is kept as evidence. The
node needs a `/proc` reader and an AOT-safe `kill(2)` binding; the remaining PID-reuse race
closes later with `pidfd` (M7). Harnesses that need another graceful step supply it through the
driver (ADR 0018) without changing the session host.

## Alternatives considered
- **`kill-session`**: SIGHUP only, loses the exit status, misses processes that left the pane's
  session, and may stop the server with the last session.
- **Process groups**: children can create their own groups or sessions.
- **Deliver `/exit` to the harness**: input with side effects, harness-specific, and does not
  help a hung harness.
- **cgroups per seat**: exact, but needs privileges and setup the local profile does not have.
