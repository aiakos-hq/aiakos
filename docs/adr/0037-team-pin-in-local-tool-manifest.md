---
id: 0037
title: The team's release is pinned in the repository's local tool manifest
status: accepted
date: 2026-10-01
---

# 0037 — The team's release is pinned in the repository's local tool manifest

## Context
ADR 0008 says the `aiakos-dev` rig runs on the last release, "installed as a pinned `dotnet
tool`", but not where the pin lives. A pin that exists only on the maintainer's machine can drift
silently, CI cannot check the rig files against it (spec 0003 R32), and the team's tool is easy to
confuse with the development build of the same repository. From
[spec 0008](../specs/0008-aiakos-dev-rig.md) (R11–R13, D3).

## Decision
- The pinned release is recorded in **`.config/dotnet-tools.json`** at the repository root (tool
  `aiakos`, exact version). The lead runs the team with **`dotnet aiakos …`** from the Windows
  checkout of `main`; `dotnet run --project src/Aiakos.Cli` is the development build and is never
  used for the team.
- **Upgrading the team is a PR** that changes the manifest. Rig files that use a new format feature
  follow in the PR after the pin moves.
- A CI job **`rig-compat`** restores the pinned tool and loads `rigs/aiakos-dev` with
  `up --dry-run` on every PR that touches the rig or the pin, so the rig files always load with the
  release the team runs.

## Consequences
`dotnet tool restore` gives every machine and CI the same version; the upgrade is reviewable and
visible in history; `dotnet aiakos` and `dotnet run --project src/Aiakos.Cli` cannot be mistaken
for each other. Commands must run inside the repository tree. Because the manifest lives in the
repository the seats change, a seat could propose a pin change; it takes effect only after review,
merge and the lead's deliberate upgrade. This refines ADR 0008 without changing it.

## Alternatives considered
- **A global tool plus a version file**: nothing enforces the file, and the installed version can
  drift from it.
- **A global tool with the version only in the README**: no CI check is possible.
