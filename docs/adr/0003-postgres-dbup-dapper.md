---
id: 0003
title: Postgres with DbUp and Dapper; tenant_id from day one
status: accepted
date: 2026-09-29
---

# 0003 — Postgres with DbUp and Dapper; tenant_id from day one

## Context
Multitenancy is a future goal. The queue needs row locking and atomic handoff. The maintainer
prefers explicit SQL over an ORM. Aspire makes running Postgres locally trivial.

## Decision
Use **Postgres** from the start (no SQLite phase). Schema migrations are embedded `.sql` scripts
applied by **DbUp** at startup; data access uses **Dapper** through thin repositories. Every
table has a `tenant_id` column from its first migration, with a single default tenant for now.

## Consequences
One SQL dialect and no later data migration. Row-level security, `SKIP LOCKED` and
`LISTEN/NOTIFY` are available. Docker is required on the development machine (it is needed for
sandboxes anyway).
