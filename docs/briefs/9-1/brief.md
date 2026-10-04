---
id: 9-1
title: "#9 slice 1 — deterministic orchestrator host disposal"
issue: 9
status: draft
route: impl/senior
paths: [tests/Aiakos.Orchestrator.Tests/, src/Aiakos.Orchestrator/Program.cs, src/Aiakos.Orchestrator/OrchestratorTelemetry.cs]
date: 2026-10-04
---

# Brief: #9 slice 1 — deterministic orchestrator host disposal

Part of #9; repairs the failure reported in #103. Read StartupTests.cs, OrchestratorFactory.cs,
DatabaseFixture.cs, AssemblyInfo.cs, the orchestrator Program.cs and OrchestratorTelemetry.cs,
and ServiceDefaultsExtensions.cs / TelemetryShutdownService.cs for diagnosis. Do not assume
which layer causes the race. Where silent, choose the simplest behavior and state the choice
in the commit body. No implementation may change a file outside the allowed paths.

## Goal

Find and fix the intermittent tracer-provider disposal exception while retaining the actor
startup/shutdown test's real assertions. Show the same stress gate failing before the fix and
passing afterwards, while the other orchestrator tests run concurrently through xUnit.

The reported CI failure is on PR #101, run 37186592874, job 111389699477:
System.InvalidOperationException: Collection was modified; enumeration operation may not execute.
Stack: List<T>.Enumerator.MoveNext -> TracerProviderSdk.Dispose -> Host.DisposeAsync ->
WebApplicationFactory.DisposeAsync -> StartupTests.ActorSystemRunsAfterStartAndTerminatesAfterStop.
This is historical evidence, not a claimed local reproduction or a known root cause.

## Files to create or touch (nothing else)

| Path | What |
|---|---|
| tests/Aiakos.Orchestrator.Tests/StartupTests.cs | Keep/repair lifecycle assertions and shutdown ordering |
| tests/Aiakos.Orchestrator.Tests/Infrastructure/ | Repair test-host lifetime if the diagnosed cause is there |
| tests/Aiakos.Orchestrator.Tests/*.cs | Focused regression/synchronization fixture if the diagnosed cause needs it |
| src/Aiakos.Orchestrator/Program.cs, OrchestratorTelemetry.cs | Host-local fix only if diagnosis shows it is necessary |

Do not change packages, project files, ServiceDefaults production code, CI or other projects.
The wider tests directory in paths permits test fixture support, not unrelated test edits.
Public application APIs remain unchanged. Do not prescribe a speculative fix in advance.

## Rules and changes

R1. Diagnose the failing lifetime before changing behavior. Identify the concurrent mutation
   and disposal (or evidence that the race is inside the pinned OpenTelemetry dependency),
   with source file/line references and the reproduction command. Write the cause and why the
   fix removes the race in the commit body under literal headings `Cause:` and `Evidence:`.
   Keep the failed before log and successful after log local in artifacts/trials/9-1-1/;
   QA owns these gate runs, and lead uses the commit body in the PR. An unproved hypothesis
   must be called unknown; it is not a diagnosed cause. If the needed production change is
   outside allowed paths, stop and route the missing scope to lead, without modifying it.
   TelemetryShutdownService.cs is intentionally read-only under the maintainer's narrow
   authorization, even if it is the diagnosed cause. In that case record the source line and
   required change, park the queue item on a scope decision, and hand the diagnostic evidence
   to lead for maintainer-authorized scope amendment. The story is blocked on that decision,
   not fixed or done; do not substitute an unrelated test workaround or consume a retry to
   try one. Resume only after the brief permits the required file or lead records a different
   in-scope fix that addresses the evidenced lifetime boundary.

R2. Fix the diagnosed cause in the allowed host or test lifetime. If the dependency itself
   races, make the host/test setup or shutdown deterministic at the actual conflicting
   boundary. No retry, skip, swallowed disposal exception, arbitrary delay, removal of actor
   assertions, disabling all test parallelism, or disabling tracing/instrumentation as a
   substitute for a lifetime fix. Do not add a forced exception solely to make baseline red.

C1. ActorSystemRunsAfterStartAndTerminatesAfterStop still starts a real OrchestratorFactory,
   resolves the real ActorSystem, asserts Name is `aiakos` and WhenTerminated is incomplete
   after startup, disposes the factory, then awaits WhenTerminated with its existing 30-second
   bound and asserts IsCompletedSuccessfully. A cause-driven change may adjust explicit
   start/stop ordering or test-host ownership, but must retain all these observable assertions.
   Other orchestrator tests keep their behavior and remain green.

R3. The acceptance stress gate runs the entire orchestrator test project 200 consecutive
   times in Release against real Postgres, with the project's normal xUnit class parallelism
   enabled each time. Thus every iteration includes the actor lifecycle test concurrently
   with the other orchestrator classes. Build once; subsequent iterations use --no-build.
   No iteration is retried. Fail immediately on any nonzero test run, exception or timeout.
   Print `STRESS iteration <n>/200` before each run; after 200 successful runs print exactly
   `STRESS: pass (200/200)`. Preserve raw runner output so zero executed tests, skips of the
   lifecycle test, infrastructure failures and the actual disposal race are distinguishable.
   Each invocation has a five-minute external deadline; timeout is failure, not success.
   A demonstrable runner infrastructure failure (Docker daemon unavailable, container launch
   failure, or Postgres unavailable before the test host can run) prints exactly
   `STRESS: infrastructure failure (iteration <n>)` and exits 2, retaining raw diagnostics.
   A real test assertion/host-disposal failure exits 1. Do not classify ambiguous errors,
   application migration failures or exceptions after host startup as infrastructure merely
   to avoid a red test. QA assesses the failure from the runner trace and hands an infrastructure
   report to lead; it is neither a reproduced baseline nor evidence that the fix failed.
   Baseline infrastructure assessment says exactly `BASELINE: infrastructure failure`.
   After infrastructure is restored, lead routes a fresh verification; never retry an iteration
   inside the stress gate. tools/story.sh done counts any failed run that reached the build;
   this brief does not waive/reset that count. If infrastructure interrupts such a run, report
   the recorded attempt count to lead before a rerun, rather than quietly spending another
   attempt or modifying the helper. Preflight Docker/Postgres availability before invoking
   done to avoid known infrastructure failures.
   The baseline must fail with the reported disposal race, or a diagnosed equivalent at the
   same lifetime boundary, not a compiler/analyzer error or unavailable Docker/Postgres.
   If the first 200-run baseline stays green, QA records `BASELINE: not reproduced (200/200)`
   in a separate baseline assessment and hands to lead for a reproduction decision before
   ready. Do not mark that green run as a failing baseline or accept historical CI as a local
   gate failure. No statistical guarantee is claimed from a finite run. A cause-driven
   deterministic regression may be added within the same behavior contract and included in
   every stress iteration, but its baseline failure must still demonstrate the real race.

## Expected outputs: exact text

| ID | Input | Expected |
|---|---|---|
| `E1` | diagnosed failure and fix commit | commit body includes `Cause:` and `Evidence:`, concrete competing operations/source references and reproduction command; no unsupported known-cause claim |
| `E2` | disposal under the reproduced competing operation | disposal completes without Collection was modified, without retry/skip/catch-and-ignore or disabling instrumentation/parallelism |
| `E3` | actor lifecycle test | Name `aiakos`; not terminated after startup; terminated successfully after factory disposal within 30 seconds; earlier orchestrator tests pass |
| `E4` | identical stress gate on before and after commits | before: runner failure from the disposal race with raw trace; after: 200 successful iterations and final exact line `STRESS: pass (200/200)`; green before-state is explicitly not reproduced and goes to lead |

| `E5` | an iteration cannot execute because Docker/container/Postgres infrastructure is demonstrably unavailable | exact line `STRESS: infrastructure failure (iteration <n>)`, exit 2 and raw cause; baseline assessment `BASELINE: infrastructure failure`; no reproduced-race or successful-fix claim, no hidden iteration retry, recorded done attempt count retained and reported to lead |

## Tests

T1. Local acceptance gate under artifacts/trials/9-1-1/gate.sh implements R3 and records the
   baseline commit in main-before.txt through tools/story.sh baseline. QA checks the actual
   failure cause; historical CI evidence alone does not satisfy ready. The author writes
   acceptance before implementation and QA runs it. A compiled focused regression, if needed
   to expose the race deterministically, must demonstrate R2/C1 without adding another rule.

## Definition of done

- Identical baseline/after gate with E4 evidence, all earlier tests green, build zero warnings/errors.
- One local commit: `fix(orchestrator): make host disposal deterministic (#9)`.
- Commit body includes R1 evidence and `Risks: #9 has no open risk assigned; this fixes #103's host-disposal regression.`
- LF, UTF-8 without BOM, final newline. No push/PR. Use tools/story.sh and its one-retry limit.
- Lead creates the ready sub-issue as 9-1-1 under #9 and closes #103 as superseded.

## Out of scope

Package upgrades, application features, global telemetry redesign, changes to ServiceDefaults,
other test projects, migrations, CI rerun policies, and claims that 200 passes prove races impossible.
