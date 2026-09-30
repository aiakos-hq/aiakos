---
id: 0022
title: A private tmux server per instance; sessions are adopted after a node restart
status: accepted
date: 2026-09-30
---

# 0022 — A private tmux server per instance; sessions are adopted after a node restart

## Context
ADR 0005 makes tmux the first session host. A tmux server is shared state: the user's
`~/.tmux.conf` can change options Aiakos relies on (`remain-on-exit`, `base-index`), the first
client's environment becomes every pane's base environment (and the node's environment holds
`AIAKOS_NODE_TOKEN` and `OTEL_*`), and the dev stack and the released tool must never see each
other's seats (ADR 0008). The node is a long-lived process stopped with SIGHUP, while the tmux
server daemonizes and outlives it (spike 0003), so a restarted node finds sessions it did not
start in this process. From [spec 0004](../specs/0004-tmux-session-host.md) (R5–R10, R31–R35).

## Decision
- Each Aiakos instance has its **own tmux server** on the socket **`-L aiakos-<instance>`**
  (`aiakos-dev`, `aiakos-release`). Every invocation passes **`-f $AIAKOS_HOME/tmux/tmux.conf`**,
  a configuration the node generates at every start; the user's `~/.tmux.conf` is never loaded.
  When a server is already running, the node re-applies the same options so it converges.
- tmux clients are started with an **allowlisted environment** (`HOME`, `USER`, `LOGNAME`,
  `SHELL`, `PATH`, `TMUX_TMPDIR`, `XDG_RUNTIME_DIR`, a UTF-8 `LANG`), never the node's full
  environment; global variables outside the allowlist on a running server are removed and logged.
- Each managed session carries **labels** (tmux user options: instance, seat ID, address,
  launch ID, harness; no secrets), and the node keeps a **per-seat registry** file under
  `$AIAKOS_HOME/sessions/`, written atomically before `new-session`.
- On start, the node **reconciles** once, before `Hello`: sessions with matching labels and
  registry entry are **adopted** (alive ones reported with lifecycle **`UNKNOWN`**, since
  readiness cannot be re-established from a pane; dead ones report their recorded exit);
  registry entries without a session are reported vanished; labelled sessions without an entry
  are adopted read-only for a health finding; unlabelled (foreign) sessions are never touched.
  **Nothing is stopped, killed or sent keys** during reconciliation, and node shutdown never
  stops seats.

## Consequences
Seats survive node restarts and upgrades, a user's tmux habits cannot break them, and the node's
secrets cannot leak into panes through the server. Humans attach with the instance socket
(`tmux -L aiakos-<instance> attach -r`). Labels and registry are a compatibility surface
(`@aiakos-schema`). Every process of the node's user can still reach the socket; accepted for the
local profile, closed by sandboxing in M6.

## Alternatives considered
- **The user's default tmux server**: the user's config and sessions mix with seats, and dev
  and released instances collide.
- **`-S` with a socket under `$AIAKOS_HOME`**: survives `/tmp` cleaners, but is not what humans
  type.
- **Stop all seats when the node stops**: every node restart or upgrade would kill running work.
- **Adopt by session name only**: a human-created session with that name would become a seat.
