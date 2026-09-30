---
id: 0025
title: Secrets never pass through tmux; the seat token is a file
status: accepted
date: 2026-09-30
---

# 0025 — Secrets never pass through tmux; the seat token is a file

## Context
ADR 0016 keeps secret values out of argv and environment variables and delivers them as files,
but leaves the per-seat identity token to the node link. Spec 0002 R45 then has the node place
the token in the seat's environment in M1. With tmux, the only ways into a pane's environment
are `new-session -e NAME=value`, which makes the value part of the tmux client's argv (readable in
`/proc/<pid>/cmdline` by same-host users), and `set-environment`, which stores it in the server
for every later pane. Paste buffers are readable by any client of the socket. M6 sandboxes
deliver secrets as tmpfs files anyway (spike 0005). From
[spec 0004](../specs/0004-tmux-session-host.md) (R11, R38, Q1), adopted by
[spec 0005](../specs/0005-claude-code-adapter.md) (R15, R16, Q7).

## Decision
- **Secret and token values never pass through tmux**: not in argv, not in `-e` or
  `set-environment`, not in paste buffers, labels, the registry, logs or traces. The session
  host's launch spec has no field for them, and environment names containing `TOKEN`, `SECRET`,
  `PASSWORD` or `API_KEY` are rejected, except names ending in `_FILE` whose value is an absolute
  path under the seat home (a path is not a secret).
- The **per-seat token** is written by the node to a **mode-0600 file** in the seat home and
  replaced at every launch. The seat learns only its path, from the non-secret variable
  **`AIAKOS_SEAT_TOKEN_FILE`**; the hook relay reads it from the file (ADR 0028).
- This is the same mechanism in M1 (local seats) and M6 (sandboxed seats, tmpfs).

This **amends [spec 0002](../specs/0002-orchestrator-node-grpc-contract.md) R45**: the node
delivers the token as a file named by `AIAKOS_SEAT_TOKEN_FILE`, not as an environment value.

## Consequences
No process listing, tmux command or tmux server state reveals a token. Anything that needs the
token must read a file, so helpers that expect environment variables need a small wrapper (as
ADR 0016 already requires for user secrets). The token file is kept out of the regenerated
projection and survives only until the next launch.

## Alternatives considered
- **Token in the pane environment via `-e`** (spec 0002 R45 as written): visible in
  `/proc/*/cmdline` while the client runs.
- **`set-environment` or tmux's `update-environment`**: stores the value in the server, where
  every client and later pane can read it.
- **Token in the hook URL**: ends up in argv of every relay invocation and in access logs.
