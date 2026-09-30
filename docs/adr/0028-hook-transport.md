---
id: 0028
title: Hooks reach the node through a POSIX sh relay with a per-seat sequence counter
status: accepted
date: 2026-09-30
---

# 0028 — Hooks reach the node through a POSIX sh relay with a per-seat sequence counter

## Context
Claude Code runs each hook and the statusLine as a separate process, so hooks arrive out of
emission order (spike 0001) and the node needs a sequence number from the source. A hook must
never slow down or fail the harness: stdout of `SessionStart` and `UserPromptSubmit` hooks is
added to the model's context, and other output can be read as a decision. ADR 0019 needs
`source_seq` for ordering and treats losses as gaps; ADR 0025 puts the seat token in a file.
Native AOT cannot be cross-compiled from Windows, and a non-AOT .NET start costs 50–100 ms per
hook. From [spec 0005](../specs/0005-claude-code-adapter.md) (R16–R19, Q1).

## Decision
- Every hook and the statusLine run **`aiakos-hook-relay`**, a **POSIX `sh` script** (with
  `curl` and `flock`) that Aiakos projects into the seat home. It POSTs the payload unchanged to
  the node's **loopback hook ingest** (`127.0.0.1`, instance port base + 10), with short connect
  and total timeouts.
- The relay **always exits 0** and **prints nothing** in hook mode (in status mode exactly one
  line, `aiakos <seat>`). It never returns a decision and never blocks a tool call.
- It reads the **token from the file** named by `AIAKOS_SEAT_TOKEN_FILE` and passes it as a
  header file to curl, so the token is never in argv. The ingest attributes posts by token only,
  never by the payload's session ID (rule 2).
- It stamps **`source_seq`** from a **per-seat counter file** incremented under `flock`. A missing
  counter is **seeded from the clock** (Unix time in microseconds), and the node never resets it,
  so `source_seq` **rises strictly per seat across launches**. The ingest reports a number still
  missing 2 s after a higher one arrived as an observation gap: **gaps reveal loss**.
- The node checks for `curl` and `flock` before advertising the harness. A native
  **`Aiakos.HookRelay`** with the same contract is **deferred to M6** seat images.

## Consequences
A relay costs a few milliseconds per hook and cannot hang or confuse Claude. The SeatActor can
order a seat's hook events across relaunches and detect lost posts without trusting clocks. Nodes
need `sh`, `curl` and `flock` (standard on the supported distros). The relay's behaviour is a
contract that the native relay in M6 must match, and it has its own tests (exit code, empty
stdout, distinct increasing numbers under concurrency, no token in `/proc/*/cmdline`).

## Alternatives considered
- **`Aiakos.HookRelay` native binary in M1**: no `curl`/`flock` dependency, but needs a Linux
  build runner for AOT; kept for M6.
- **Ingest-assigned sequence numbers**: reflect arrival order, which is exactly what is wrong.
- **Per-launch counters starting at 1**: late hooks of an old launch would look newer than the
  new launch's first events.
- **Payload timestamps for ordering**: not always present, and clocks are not a sequence.
