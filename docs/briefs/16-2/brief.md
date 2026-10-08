---
id: 16-2
title: "#16 slice 2 — release pin and rig compatibility"
issue: 16
status: approved
route: impl
paths: [.config/dotnet-tools.json, .github/workflows/rig-compat.yml, rigs/aiakos-dev/README.md, tests/Aiakos.Spec.Tests/]
date: 2026-10-08
---

# Brief: #16 slice 2 — release pin and rig compatibility

Part of #16. Self-contained. **Do not read docs/specs/ or docs/adr/.** Where silent choose the
simplest behavior and record the choice in the commit body. Do not implement missing neighbors.

## Goal and baseline

Pin the released M1 Aiakos as a local .NET tool and make the real aiakos-dev rig prove its
compatibility with that release on CI. Supply the operator's exact pin/upgrade instructions.
Covers spec0008 R3/R11–R14, AC2 released-tool path, AC7 and ADR0037;16-1 already owns the rig,
permissions, prerequisite script, loader/projection tests and prospective README.16-3 owns the
live M1 acceptance and Stage B declaration. No manifest or rig-compat workflow exists on main.

EXTERNAL READINESS PREREQUISITE: the first stable M1 release 0.1.0 must be published with a
restorable NuGet Aiakos tool providing up --dry-run. Router must verify availability before
readying S1. At analysis time gh release list returned []; only the placeholder is documented.
Do not pin placeholder0.0.1-preview.1, a prerelease, local package, a development build or a
made-up future version to bypass this dependency. A later first stable version requires a brief
amendment before implementation. Analysis can be approved before the prerequisite exists.

The compatibility job uses a Windows runner because the released instance is a Windows tool;
dry-run does not launch an instance/node, require WSL, Claude, Docker, a real clone or credentials.
Local loader tests already demonstrate the example binding's planned paths; CLI dry-run and
release packaging are #15 prerequisites, not operations to invent inside this slice.
Risks checked: bootstrap separation and0008-RK9 release failure handling. Live upgrade, deny
rules, context and user login checks remain16-3; no risk is closed by these static instructions.

## Files to create or touch (nothing else)

| Path | What |
|---|---|
| .config/dotnet-tools.json | Local exact-version tool manifest |
| .github/workflows/rig-compat.yml | Released-tool dry-run check |
| rigs/aiakos-dev/README.md | Pin, compatibility status, ordered upgrade procedure |
| tests/Aiakos.Spec.Tests/ | AiakosDevPinTests.cs, AiakosDevCompatibilityTests.cs and affected AiakosDevRunbookTests.cs |

No CLI/loader/tool packaging changes, packages, Version on PackageReference, NoWarn, pragma
warning disable or SuppressMessage. Do not alter rig.yaml/env/agents/culture or existing CI.
Tests locate the live repository root by walking parents to Aiakos.slnx, like
AiakosDevRunbookTests; do not duplicate the rig source into fixtures.

## Public surface

No new C# API. Local command name is dotnet aiakos; workflow/job check name is rig-compat.
Nothing starts seats, changes GitHub branch protection, creates releases or upgrades this rig.
The manifest JSON and workflow YAML are the consumer interfaces.

## General rules

G1. All operations in the compatibility job are read-only with respect to user instances and
    seats. Use only released local tool; no dotnet run, pack, global tool install/update, custom
    feed, local nupkg or fallback. Never log secrets or introduce credentials, personal bindings,
    user HOME files or live instance access. Restore failure and loader failure fail the job;
    no continue-on-error, ||true, condition that skips restore/load or development fallback.

G2. Text LF/UTF8/noBOM/final LF. Earlier tests remain unchanged except C1's targeted README
    expectations; new tests use xUnitv3 and existing serializer/YAML utilities, no new dependency.
    Pure committed regression tests must run without network/tool restore/released CLI; actual
    released-tool verification is the compatibility job and acceptance gate, never simulated
    success. Do not run a package install/build in the main checkout.

## Changes to earlier behavior

C1. README's pending16-2 statement becomes completed pin/compatibility information only after
    its files land. Preserve the statement "does not mean that M1 acceptance has run",16-3 and
    Stage B limits. Change upgrade prose that currently says format changes go in the pin PR:
    release-dependent rig changes go in a subsequent PR after the pin moves (ADR0037/spec0003
    R32). Extend AiakosDevRunbookTests to assert this corrected ordering and pin section instead
    of treating any mention of16-2 as proof it is still pending. Other16-1 outputs stay intact.

## Rules

R1. Create .config/dotnet-tools.json exactly as the golden below: manifest version1, isRoot=true,
    tool key aiakos, exact version0.1.0, commands=["aiakos"]. No rollForward/additional tools,
    runtime source path, global install or floating version. dotnet tool restore from repository
    root must resolve the published tool; dotnet aiakos --version reports0.1.0. This pin is the
    first M1 release, not a claim that the current source/released instance has that version.

R2. Create workflow name rig-compat, job key rig-compat, runs-on windows-latest. Events:
    pull_request branches[main] with no path filter; push branches[main] with no path filter;
    workflow_dispatch. Workflow permissions contents:read; concurrency group
    rig-compat-${{ github.ref }}, cancel-in-progress:true. Steps ordered checkout@v7,
    setup-dotnet@v6 with global-json-file global.json, run "dotnet tool restore", run exactly
    "dotnet aiakos up --dry-run rigs/aiakos-dev --env rigs/aiakos-dev/rig.env.example.yaml".
    No environment secret, instance initialization/start, Docker/WSL provisioning, build or test
    of development CLI. Keep native command exit-code propagation of the default Windows shell.
    The job is not gated on a release-presence check; missing package is a failure.

R3. The real dry-run success is exit0, zero errors/warnings, planned impl/review node wsl-local,
    model opus, checkout seat-worktree; lead remains human. Verify using published0.1.0 in an
    isolated temporary copy of manifest and entire rig directory. In another temp copy, add
    top-level "unknown_compat_field: true" to rig.yaml and run the exact same command: nonzero
    exit with AIK2002 diagnostic. Never mutate committed rig for the negative test, never use
    user's ignored rig.env.yaml, never up without --dry-run. Capture release version and both
    exit codes/outputs as gate evidence. The job only runs the positive command; negative proof
    is in the pre-merge acceptance gate, and static regression coverage keeps command immutable.

R4. README gains heading "## Release pin and compatibility" with exact pin0.1.0, manifest link,
    and commands "dotnet tool restore", "dotnet aiakos --version", and R2's dry-run line. It says
    lead commands run from Windows main checkout, tool restore uses repository-local manifest,
    and "Never run the team with dotnet run --project src/Aiakos.Cli." It names check rig-compat
    on every PR targeting main and every push to main. Marking it required is a maintainer GitHub
    setting only after the first successful main run; this PR changes no repository settings.
    Keep local binding, prerequisites, seat permissions and operating table unchanged except
    Upgrade row may point to the corrected section. Status names16-2 as supplying pin/check and
    says live acceptance is pending16-3, rather than announcing Stage B or completed M1.

R5. README Upgrade gives ordered steps for a released patch upgrade0.1.0->0.1.1:
    (1) reviewed pin PR changes manifest exact version, optional compatible rig edits only;
    format-requiring rig edits follow after this PR merges;
    (2) merge then git pull and dotnet tool restore from Windows main checkout;
    (3) dotnet aiakos instance status to compare CLI/host versions;
    (4) dotnet aiakos down --all, dotnet aiakos instance stop, dotnet aiakos instance start;
    (5) dotnet aiakos up rigs/aiakos-dev, dotnet aiakos ps, verify resumed seats/no drift.
    Released host remains running from its cached runtime while tool restore changes CLI; patch
    commands work against old host because major.minor is equal. Explain a minor-version change
    requires stopping seats/instance with the old pinned CLI BEFORE pulling/restoring new pin;
    never claim new-minor CLI can mutate old host. Do not run global tool update. Link spec0007
    upgrade runbook, retain no-development-build/fix-release-pin-retry path; downgrade after new
    migrations unsupported. Development testing uses separate rig name aiakos-dev-test and
    seat root ~/aiakos/test-seats, never team's released instance/worktrees (R14).

## Expected outputs: exact text

Manifest golden (final LF):

```json
{
  "version": 1,
  "isRoot": true,
  "tools": {
    "aiakos": {
      "version": "0.1.0",
      "commands": [
        "aiakos"
      ]
    }
  }
}
```

| ID | Change | Expected |
|---|---|---|
| `E1` | manifest | exact golden bytes R1; one aiakos tool, command aiakos, exact0.1.0; local released tool version0.1.0 |
| `E2` | workflow parsed structure | exact R2 triggers/job/permissions/steps/command; no development-build path or credentials; failed restore/load propagates failure |
| `E3` | released dry-run real rig and unknown-field temp copy | text-mode assertions specified below; positive exit0/zero diagnostics; negative nonzero plus AIK2002; recorded release0.1.0, source files unchanged |
| `E4` | README status/pin/check | R4 heading/commands/manifest/check link; exact sentence Never run the team with dotnet run --project src/Aiakos.Cli.;16-3/Stage B remain pending |
| `E5` | patch/minor upgrade and development isolation | exact command order R5; format PR after pin, old CLI stop before minor restore, separate aiakos-dev-test and ~/aiakos/test-seats, no global tool update |

E3. Use the default text form of the exact R2 command (no --json). Match stdout physical
    lines using the slice 15-1 text renderer contract; assert exactly one line per seat.
    The lead line must equal:
    `seat: "lead" node: null harness: null model: null checkout: null workdir: null`
    The impl line must begin with the exact prefix:
    `seat: "impl" node: "wsl-local" harness: "claude-code" model: "opus" checkout: "seat-worktree" workdir: `
    The review line must begin with the exact prefix:
    `seat: "review" node: "wsl-local" harness: "claude-code" model: "opus" checkout: "seat-worktree" workdir: `
    For each agent, parse the entire remaining workdir value as one nonempty JSON string;
    do not fix its machine-dependent path or the spec_hash/binding_hash values. Assert the
    exact stdout line `rig: "aiakos-dev"`. On the positive run stderr must contain no
    diagnostic with severity error or warning; informational notices may occur. The negative
    run must exit nonzero and stderr must include diagnostic code AIK2002. Record both streams
    and exit codes, plus --version output 0.1.0; this is real released-tool evidence per R3.

E4. README tests assert the R4 heading, commands and these exact sentences (compare raw Markdown
    after removing backticks and folding each whitespace run to one ASCII space).
    Use the relative manifest link `[local tool manifest](../../.config/dotnet-tools.json)`
    and workflow link `[rig-compat](../../.github/workflows/rig-compat.yml)`.
    - `Slice 16-2 supplies the release pin and compatibility check.`
    - `Live M1 acceptance and its release evidence are pending 16-3.`
    - `This does not mean that M1 acceptance has run or that Stage B has begun.`
    - `The repository-local manifest pins Aiakos to version 0.1.0.`
    - `The lead runs these commands from the Windows main checkout.`
    - `dotnet tool restore uses the repository-local manifest.`
    - `Never run the team with dotnet run --project src/Aiakos.Cli.`
    - `The rig-compat check runs on every pull request targeting main and every push to main.`
    - `Only the maintainer can make rig-compat required in GitHub, after its first successful run on main.`
    - `This PR changes no repository settings.`

E5. README tests assert R5's commands in its ordered five-step patch procedure, the upgrade
    runbook link `[spec 0007 upgrade procedure](../../docs/specs/0007-cli-and-released-instance.md#upgrading-the-released-instance)`,
    and these exact sentences, using the same whitespace folding as E4:
    - `For a patch upgrade from 0.1.0 to 0.1.1, use the following steps in order.`
    - `The reviewed pin PR changes the manifest to the exact released version; it may include only rig edits compatible with the old pin.`
    - `Rig edits that require a newer format follow in a subsequent PR after the pin PR merges.`
    - `After merge, run git pull and dotnet tool restore from the Windows main checkout.`
    - `Run dotnet aiakos instance status to compare the CLI and host versions.`
    - `Run dotnet aiakos down --all, then dotnet aiakos instance stop, then dotnet aiakos instance start.`
    - `Run dotnet aiakos up rigs/aiakos-dev, then dotnet aiakos ps; verify that seats resumed and no drift remains.`
    - `The released host keeps running from its cached runtime while tool restore changes the CLI.`
    - `Patch commands work against the old host because major.minor is equal.`
    - `For a minor-version change, stop all seats and the instance with the old pinned CLI before pulling main or restoring the new pin.`
    - `A new-minor CLI cannot mutate the old host.`
    - `Do not run global tool update.`
    - `Downgrade after new migrations is unsupported.`
    - `Development testing uses the separate rig name aiakos-dev-test and seat root ~/aiakos/test-seats, never the team's released instance or worktrees.`
    Preserve the existing Verification limits fix-release-pin-retry paragraph verbatim;
    its development-build prohibition and issue/fix PR/new release/version pin/upgrade/restart
    sequence remain part of E5. These sentences supply R4/R5's exact test markers; do not
    infer additional exact wording from R4/R5's explanatory prose.

## Tests

Use existing Aiakos.Spec.Tests live-root conventions and independent expected literals. Network
restore and published-tool subprocess are acceptance gate operations, not normal dotnet test.
Do not add a fake CLI that reports success instead of the pinned release.

T1. Commit manifest byte-golden and parsed exact pin/command/root-key tests for E1/G1/G2.
    Restore availability/version evidence belongs to acceptance, not a networked unit test.
T2. Commit YAML-structure tests for E2: unconditional main PR and push (no path filters), Windows runner,
    action versions, global.json, restore-before-dryrun, exact command, no skip/fallback/secret/
    extra development steps. Use existing YamlDotNet reference in Spec; no new package.
    Permanent live-rig loader test inserts unknown field in a temporary copied rig and asserts
    AIK2002 through current loader; that regression is supplementary, not E3's released proof.
T3. Commit README contract tests E4/E5/C1: pending acceptance preserved, local manifest commands,
    patch ordering/minor-version old-CLI gate and subsequent format PR. Existing sixteen-slice
    runbook tests retain all prior seat/binding/permission/manual-evidence assertions.

## Definition of done

Release build0warnings/errors; Spec.Tests and earlier tests stay green. Acceptance gate uses
published pin for E1/E3 (not development CLI). One local commit docs(rig): <story title> (#16),
body names choices and limits. Do not push/open PR or apply branch protection. No live seats run.

## Out of scope (do not implement, do not stub)

#15 release production, CLI/dry-run changes, host runtime/instance APIs;16-1 rig files/guidance/
permission changes/prereq script;16-3 live M1 run/evidence/stageB/risks updates; GitHub required
check setting, upgrade of running owner environment, global tool, package feeds, queue/messaging.
