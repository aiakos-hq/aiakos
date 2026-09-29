---
id: 0011
title: Aspire for local development and telemetry
status: accepted
date: 2026-09-29
---

# 0011 — Aspire for local development and telemetry

## Context
The developer machine is Windows; agents run in WSL. Several processes (orchestrator, Postgres,
node agent) must start together, and OpenTelemetry should be easy to inspect.

## Decision
Use a **.NET Aspire AppHost** for local development: Postgres, the orchestrator, and the WSL node
agent (launched via `wsl.exe`, with WSL mirrored networking so OTLP and gRPC reach Windows). The
Aspire dashboard is the local telemetry UI. Deployment to a Linux box uses `aspire publish`
(Docker Compose).

## Consequences
All components emit OpenTelemetry from day one. The WSL launch path must be validated in the M0
spike.
