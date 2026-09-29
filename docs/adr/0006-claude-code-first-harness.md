---
id: 0006
title: Claude Code as the first harness; OpenCode second
status: accepted
date: 2026-09-29
---

# 0006 — Claude Code as the first harness; OpenCode second

## Context
Aiakos builds itself from milestone M1 on, and the first builders are Claude Code seats.
OpenCode is the priority harness for users and is API-driven (`opencode serve` plus events).

## Decision
M1 ships a **terminal-driven Claude Code adapter** (tmux, hooks for state and session ID,
statusLine for context usage, resume). M2 adds the **API-driven OpenCode adapter** behind the
same `ISeatChannel` abstraction, then Codex.

## Consequences
The self-hosting threshold is reachable with one adapter. The seat model must support both
harness styles (terminal-driven and API-driven) from M2.
