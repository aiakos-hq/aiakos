---
id: 0035
title: The CLI talks to the orchestrator through a loopback HTTP/JSON API with a token file
status: accepted
date: 2026-10-01
---

# 0035 — The CLI talks to the orchestrator through a loopback HTTP/JSON API with a token file

## Context
Spec 0006 leaves the transport between the CLI and the SeatActors to #15: an API layer must
resolve seat addresses, build `CallerContext` and send commands, and the CLI waits for outcomes by
polling launches and commands. Identity must come from the environment or a token, never from a
request body (rule 2). The M2 MCP server and the M5 dashboard will need the same application
operations. From [spec 0007](../specs/0007-cli-and-released-instance.md) (R22–R28, D2).

## Decision
- The orchestrator serves an **HTTP/JSON API under `/v1`** on `127.0.0.1` at port base + 1,
  HTTP/1.1, next to `/health`. It **refuses to bind a non-loopback address** in M1.
- Every request carries **`Authorization: Bearer <token>`**. The token is generated per instance
  and stored in a **file readable only by the user**. It resolves to `CallerContext` (tenant and
  operator); no body carries a tenant, a user or a sender.
- Seat addresses and rig names are resolved to UUIDs in the API layer (ADR 0012). Commands go to
  the `SeatRegion`; reads use `SeatQueries` and never ask an actor.
- Errors are `application/problem+json` with a stable `reason`, passed through from the SeatActor
  when there is one. Request and response types live in one shared contracts project.
- The CLI sends `traceparent`, so one CLI command is one trace.
- The dev AppHost serves the same API, so the development CLI can target the dev stack.

## Consequences
One application layer serves the CLI now and MCP and the dashboard later. The API can be tried
with `curl`, and polling needs no streaming. Loopback plus a same-user token file is not a
security boundary against other processes of the same user, including seats (spec 0007 RK4);
remote access, TLS and several users need a new decision (M6/M8).

## Alternatives considered
- **A second gRPC service (`aiakos.api.v1`)**: more tooling for clients and no streaming need in
  M1.
- **The CLI reading Postgres directly**: bypasses the SeatActor as sole writer (ADR 0032) and
  couples the CLI to the schema.
- **No token on loopback**: any local process could drive the seats, and `CallerContext` would
  have no source.
