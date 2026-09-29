---
id: 0008
title: "The bootstrap rule: the team runs on the last release"
status: accepted
date: 2026-09-29
---

# 0008 — The bootstrap rule: the team runs on the last release

## Context
From M1, Aiakos is developed by an Aiakos-managed rig. If the team ran on the code it is
changing, a broken change would take down the team that has to fix it.

## Decision
The development rig (`rigs/aiakos-dev`) always runs on the **last released** Aiakos, installed as
a pinned `dotnet tool` with its own home directory and port. Seats develop the next version in
their worktrees. Releases are cut when a milestone's acceptance passes; the team upgrades
deliberately.

## Consequences
The CLI must be packaged as a .NET tool (hence the NuGet ID) from M1. Instances must be fully
isolatable by home directory and port.
