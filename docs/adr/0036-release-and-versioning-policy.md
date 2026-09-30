---
id: 0036
title: Releases are tagged from main with approval; versions are 0.<milestone>.<patch>
status: accepted
date: 2026-10-01
---

# 0036 — Releases are tagged from main with approval; versions are 0.<milestone>.<patch>

## Context
The bootstrap rule (ADR 0008) makes a release the thing the team runs, so releases must be
deliberate, reproducible and traceable to a milestone's acceptance. The CLI resolves rig files and
the orchestrator stores the result, so a CLI and an instance of different versions can disagree
about the resolved rig. From [spec 0007](../specs/0007-cli-and-released-instance.md) (R27,
R52–R55, D11–D13).

## Decision
- **Versions** are SemVer `0.<milestone>.<patch>` until 1.0: M1 is `0.1.0`, fixes are `0.1.x`,
  release candidates are `0.<m>.0-rc.<n>`. The version comes from the tag, not the project file.
- A release is cut by pushing a tag `v*` on a commit of `main`. The release workflow builds and
  tests, packs, waits for **maintainer approval** in the `release` environment, pushes to nuget.org
  with **trusted publishing** (OIDC, no long-lived key), and creates a GitHub Release with the
  package and its checksums.
- A milestone's release is cut when its implementation issues are merged and the manual demo
  passes; the milestone's acceptance then runs on that release, and fixes ship as patches.
- **Compatibility**: commands that change anything require the CLI and the instance to have the
  **same major.minor**; read commands work across versions with a warning. A patch never changes
  the resolved rig or the meaning of the API.
- CI packs and smoke-tests the tool on every pull request.

## Consequences
The version tells which milestone's acceptance a build passed. Upgrading across minors is an
explicit stop, start and pin change; patches upgrade in place. Downgrading after a release has
migrated the database is not supported, and release notes say when a release adds a migration.
The nuget.org trusted-publishing policy and the `release` environment are repository settings the
maintainer keeps.

## Alternatives considered
- **Continuous publishing from `main`**: the team would drift onto untested builds, against
  ADR 0008.
- **Calendar or build-number versions**: say nothing about which milestone passed.
- **A long-lived NuGet API key as a secret**: a standing credential; kept only as the fallback if
  trusted publishing is unavailable (spec 0007 RK9).
- **Any CLI version against any instance**: a CLI with a different loader could store a rig the
  orchestrator reads differently.
