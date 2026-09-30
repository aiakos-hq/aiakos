---
id: 0026
title: Minimum tmux version 3.4; below it the session host is unavailable
status: accepted
date: 2026-09-30
---

# 0026 — Minimum tmux version 3.4; below it the session host is unavailable

## Context
The session host relies on tmux behaviour that differs between versions: `#{pane_dead_status}`
and `#{pane_dead_signal}` for exit reporting, argument parsing (`--`, a trailing `;`,
single-argument commands run through `sh -c`), and `paste-buffer -p`. Ubuntu 22.04 ships tmux
3.2a, Ubuntu 24.04 (the CI runner) ships 3.4, and the development distro has 3.6. Claiming support
for a version no test runs on would turn version differences into silent misbehaviour. From
[spec 0004](../specs/0004-tmux-session-host.md) (R5, Q9).

## Decision
- The minimum supported tmux version is **3.4**.
- At node start the session host runs **`tmux -V`**, parses `tmux <major>.<minor>[suffix]` and
  records the version. A missing binary, an unparseable version or a version **below 3.4** makes
  the host **`Unavailable`**: it does not advertise the `session-host.tmux` capability, every
  operation fails with `Unavailable` naming the found and required versions, and **the node keeps
  running** (rule 3; API-driven seats from M2 do not need tmux).
- **Policy for the minimum:** it is the **oldest tmux version CI runs the session host
  integration tests on**. Raising it is a deliberate change made together with the CI image
  (for example when the runner moves to a newer Ubuntu), noted in the release notes; lowering it
  requires adding CI coverage for the older version first. The tmux binary path is configurable
  and logged at start.

## Consequences
Every supported version is a tested version. Users on Ubuntu 22.04 must install a newer tmux (or
use a newer distro) for terminal-driven seats; they get a precise message instead of broken
seats. Newer versions are accepted without a check; a regression there is caught by the opt-in
integration tests on the developer's distro.

## Alternatives considered
- **Support 3.2**: needs a second CI image and fallbacks for format variables that may be
  missing, for one distro release.
- **Refuse to start the node**: would also block seats that do not need tmux.
- **No version check**: failures would appear later as wrong exit codes or mangled arguments.
