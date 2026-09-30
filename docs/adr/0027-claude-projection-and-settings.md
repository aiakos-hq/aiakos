---
id: 0027
title: Claude Code loads the projection via --add-dir and all Aiakos settings via --settings
status: accepted
date: 2026-09-30
---

# 0027 — Claude Code loads the projection via `--add-dir` and all Aiakos settings via `--settings`

## Context
ADR 0015 projects guidance and skills into a per-seat root outside the checkout and names
`--add-dir` with `CLAUDE_CODE_ADDITIONAL_DIRECTORIES_CLAUDE_MD=1` as the Claude Code mechanism,
"to be verified by the adapter spec". Aiakos also needs its own hooks, statusLine and permission
rules in every seat, while the checked-out repository has its own `.claude/settings.json` and
possibly a `settings.local.json`. Spec 0005's experiment (Claude Code 2.1.284, WSL, 2026-09-29)
tested both. From [spec 0005](../specs/0005-claude-code-adapter.md) (R5, R7–R9, E1–E17).

## Decision
- Claude Code seats load guidance with **`--add-dir <projection root>`** plus
  **`CLAUDE_CODE_ADDITIONAL_DIRECTORIES_CLAUDE_MD=1`** in the seat environment. Skills under
  `<projection>/.claude/skills` load with **`--add-dir` alone**. The repository's own `CLAUDE.md`
  keeps loading next to the projected one.
- **All Aiakos-owned settings** (hook registrations, statusLine, `permissions.defaultMode`,
  allow/ask/deny rules including `Bash(tmux:*)`, and `apiKeyHelper` for `auth: api-key`) come
  from **one per-seat file passed with `--settings`**, generated deterministically in the seat
  home. It always contains **`"disableAllHooks": false`**.
- Launches never use `--setting-sources`, `--bare`, `--continue` or permission-bypass flags, and
  nothing Aiakos-specific is written to the projection's or the checkout's `.claude/settings*`.

This **confirms ADR 0015's mechanism** with evidence; ADR 0015 is not changed.

## Consequences
Experiment findings this rests on: the projected `CLAUDE.md` loads only with both flag and
variable (E1–E3); skills need only `--add-dir` (E4–E6); settings are **never** read from
`--add-dir` directories (E8), so the projection cannot carry settings; hooks from `--settings`
and the repo **merge** and both fire (E7); `--settings` wins for `defaultMode` and the statusLine
(E9, E10); a repo's `"disableAllHooks": true`, even in `settings.local.json`, silences every
hook including ours unless `--settings` sets it to `false` (E11, E12); `--setting-sources user`
also drops the repo's `CLAUDE.md` and our skills (E13); `--add-dir` directories need no trust
(E15). Repository hooks therefore run in seats alongside ours; binding a repo implies trusting it.
A Claude Code upgrade can change any of this, so the end-to-end test is the upgrade gate.

## Alternatives considered
- **Settings in `<projection>/.claude/settings.json`**: never loaded (E8).
- **`--setting-sources user`** to ignore repo settings: also drops the repo's guidance and the
  projected skills (E13).
- **Write settings into the checkout or `~/.claude/settings.json`**: violates ADR 0015 or
  leaks between seats.
