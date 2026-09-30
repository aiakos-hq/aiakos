---
id: 0034
title: The released instance is one host process that runs the orchestrator and supervises the WSL node
status: accepted
date: 2026-10-01
---

# 0034 — The released instance is one host process that runs the orchestrator and supervises the WSL node

## Context
The dev stack is an Aspire AppHost, which is never shipped (spec 0001). The bootstrap rule
(ADR 0008) needs a released instance that the team runs from a pinned `dotnet tool`: Postgres,
the orchestrator and the node in WSL must start together, stay up after the terminal closes, keep
WSL from idling out while seats run (spec 0004 RK5), and allow the tool to be updated
deliberately. From [spec 0007](../specs/0007-cli-and-released-instance.md) (R11–R21, D1, D4, D5,
D7).

## Decision
- `aiakos instance start` launches **one hidden, detached Windows process, the instance host**.
  It runs the **orchestrator in-process**, through the same composition methods
  (`AddAiakosOrchestrator`, `MapAiakosOrchestrator`) that the orchestrator's own entry point uses
  under Aspire.
- The host manages **Postgres as a Docker container** per instance by default
  (`aiakos-<instance>-postgres`, a named volume, a loopback port); an external connection string
  is the option.
- The host installs the node into the instance's WSL home when its version differs and runs it as
  a **held `wsl.exe` child**, which keeps WSL alive while the host runs. It restarts the node with
  backoff after an unexpected exit, except after a configuration (2) or lock (3) exit.
- The host runs from a **versioned runtime copy** of the tool, takes an instance lock, and writes
  a `connection.json` that is the only way the CLI finds it.
- `instance stop` is **refused while agent seats are up**, unless `--keep-seats` is given.

## Consequences
One process to start, find, lock, log and stop, and the dev stack and the release differ only in
wiring. `dotnet tool update` works while the instance runs, and the running version is always
explicit. The tool package carries the orchestrator (with the ASP.NET Core shared framework) and a
self-contained Linux node, so it is large (spec 0007 RK1). The instance is Windows-only in M1; a
Linux deployment (`aspire publish`, M6) needs its own host. Reboots need a manual recovery until
M7.

## Alternatives considered
- **The orchestrator as a separate child process of the host**: a second supervision layer for
  no M1 gain.
- **The orchestrator inside WSL**: needs a Linux orchestrator build in the package and Docker
  inside WSL.
- **Postgres installed in WSL**: another system package and a second data location.
- **Running from the tool directory**: Windows locks the files, so updating the tool fails while
  the instance runs.
- **`up` starting the instance implicitly**: a large side effect hidden in a seat command.
