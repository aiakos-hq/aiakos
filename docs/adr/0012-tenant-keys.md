---
id: 0012
title: Tenant keys are UUIDs with a unique slug; fixed default tenant
status: accepted
date: 2026-09-30
---

# 0012 — Tenant keys are UUIDs with a unique slug; fixed default tenant

## Context
ADR 0003 puts `tenant_id` on every table from the first migration, but not its type. The type
shapes every table, foreign key and index that follows. Tenants also appear in human-facing
addresses (`tenant/rig/seat`, plan §9), which need a short, readable name. Raised as Q3 in
[spec 0001](../specs/0001-solution-skeleton.md).

## Decision
`aiakos.tenant.tenant_id` is a **`uuid`** primary key. Each tenant also has a **`slug`**
(`^[a-z0-9][a-z0-9-]{0,62}$`, unique) used in addresses and URLs, and a display `name`. Every
other table has `tenant_id uuid NOT NULL REFERENCES aiakos.tenant (tenant_id)`; child tables use
composite foreign keys that include `tenant_id`. The single default tenant has the fixed UUID
`00000000-0000-0000-0000-000000000001` and slug `default`, inserted by the first migration and
exposed as `Aiakos.Core.TenantIds.Default`. Code takes the tenant from `CallerContext`, never
from a literal in a repository.

## Consequences
Renaming a tenant changes only its slug, never a key. The default tenant is the same in every
database, so fixtures, tests and the M8 migration to real tenants can rely on it. Addresses must
resolve slug → UUID at the edge (CLI, API, chat); only UUIDs travel inside the system.

## Alternatives considered
- **Text slug as the key**: readable, but renames cascade through every table and slugs leak
  into foreign keys and logs.
- **Integer keys**: compact, but sequential IDs are guessable across tenants and awkward to merge
  between databases.
- **A random default tenant UUID**: forces every environment to look it up; no benefit.
