---
id: 0009
title: Tickets in GitHub Issues; specs and ADRs in the repository
status: accepted
date: 2026-09-29
---

# 0009 — Tickets in GitHub Issues; specs and ADRs in the repository

## Context
Work must be visible (including from a phone), linked to PRs and usable by agents, while designs
must be versioned and reviewed together with the code.

## Decision
- **Tickets** (what/why, status, discussion): GitHub Issues and milestones, plus a Project board.
- **Specs** (how): `docs/specs/`, linked from the issue, reviewed in PRs.
- **Decisions**: `docs/adr/`. **Spike findings**: `docs/spikes/`.
- From M3, Aiakos queue items link to issues as the live execution record; a GitHub connector may
  sync them later.

## Consequences
Agents need `gh` access. Documentation under `docs/` is published to aiakos.dev by the website
repository.
