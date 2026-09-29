---
id: 0001
title: Record architecture decisions
status: accepted
date: 2026-09-29
---

# 0001 — Record architecture decisions

## Context
Aiakos will be developed largely by AI agent seats. Agents (and new contributors) need a durable,
reviewable record of decisions already made, so they neither reopen nor contradict them.

## Decision
Record significant decisions as ADRs in `docs/adr/NNNN-title.md` using [`TEMPLATE.md`](TEMPLATE.md).
ADRs are immutable once accepted; a change of mind is a new ADR that supersedes the old one.

## Consequences
Decisions are reviewed in PRs and readable by agents. `CLAUDE.md` points agents here.
