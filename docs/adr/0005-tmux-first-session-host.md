---
id: 0005
title: tmux as the first session host; terminals are transport
status: accepted
date: 2026-09-29
---

# 0005 — tmux as the first session host; terminals are transport

## Context
Interactive harnesses need a real terminal that outlives the orchestrator, can be attached to by
humans (including read-only), and can be captured. herdr and tuios offer similar capabilities
(and native Windows support) but are young.

## Decision
The first `ISessionHost` implementation is **tmux** (in WSL/Linux). Terminal-driven harnesses
receive messages by bracketed paste plus a separate submit key. The database, not the terminal,
is the record of what happened. herdr/tuios are later `ISessionHost` implementations or viewers.

## Consequences
Native Windows seats are out of scope until a ConPTY-capable session host is added. Screen
capture is evidence, never the primary state signal (hooks and events are).
