---
id: 9-1
title: "#9 slice 1 — deterministic orchestrator host disposal"
issue: 9
status: approved
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
startup/shutdown test's real assertions. Diagnose the lifetime from source and prove the fix
with an identical deterministic before/after test. Full-suite stress supports the evidence;
it is not the readiness or completion gate.

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
   with source file/line references. Read the resolved OpenTelemetry TracerProviderSdk.Dispose
   implementation, Host.DisposeAsync and WebApplicationFactory.DisposeAsync, and both sides
   of the actual competing operation. Record the resolved library/package versions (not only
   the centrally requested versions), tag/commit or SourceLink reference and line ranges,
   the ownership and ordering path, and whether the mutation race is inside the library or
   in our code. The historical stack alone is not a cause. Record a deterministic test command
   and what observation distinguishes before and after; do not require a local probabilistic
   reproduction as a prerequisite. Write the cause and why the fix removes the race in the commit body under literal headings `Cause:` and `Evidence:`.
   Keep the deterministic failed-before log and successful-after log local in artifacts/trials/9-1-1/;
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

R3. The supporting stress command, separate from gate.sh, runs the entire orchestrator test
   project 200 consecutive times in Release against real Postgres, with the project's normal xUnit class parallelism
   enabled each time. Run without artificial CPU contention or overlapping copies of the suite.
   Thus every iteration includes the actor lifecycle test concurrently with the other orchestrator classes. Build once; subsequent iterations use --no-build.
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
   Supporting infrastructure results belong only in the supporting output log; do not write
   a BASELINE assessment or claim a tools/story.sh done attempt for this separate command.
   This command is supporting evidence only: neither a green run nor a statistical failure
   substitutes for R4. QA reports its result separately and routes any failure to lead; it is
   not invoked by tools/story.sh baseline/done and does not decide ready/done. Existing attempt
   accounting remains unchanged. No statistical guarantee is claimed from a finite run.

R4. Replace reproduce-first acceptance with a deterministic regression selected by the diagnosed
   boundary, using explicit barriers or a controllable callback/provider double instead of
   sleeps, repeated attempts or CPU contention. Bound all waits and cleanup by 30 seconds,
   with a five-minute external gate deadline; timeout fails. The same compiled test runs before and after;
   the before failure must be an assertion of the missing lifetime guarantee or the genuine
   diagnosed exception, never a compiler error, absent Docker, synthetic product exception,
   or unconditional assertion. Retain the real actor lifecycle check C1 and run the earlier
   orchestrator suite in the after gate. There are exactly two permitted evidence routes:
   (a) direct regression: exercise the actual competing operations with a controlled schedule;
   before fails at that boundary, after disposal succeeds with no overlap/exception;
   (b) library-boundary regression: when source diagnosis demonstrates the race lives inside
   the pinned OpenTelemetry version and the library's internal schedule cannot be controlled,
   remove the competing operation from our allowed code or enforce its completion before
   disposal. The deterministic test controls and observes that application boundary with a
   double/barrier: before it observes the competing operation still present during disposal
   or disposal entering while that operation is pending, after it observes the operation
   removed for the entire teardown or disposal waiting until completion. The assertion is
   that no conflicting operation is present/in flight at disposal, not that the internal
   library exception was reproduced. Keep instrumentation and the actor assertions active.
   State explicitly in Cause:/Evidence: which route applies; for route (b) state
   "Library race not deterministically reproduced; application lifetime boundary verified."
   Before ready, QA checks the pinned library source/version and concurrent collection access, the
   real host/factory ownership path, every application start/stop/dispose path reaching that
   operation, that the observed test boundary is the changed production/test-host boundary,
   and that no detached/background operation can reintroduce it. Checking a disconnected
   helper or an invented fake exception does not qualify. QA names the diagnosis source
   lines and the acceptance observer/assertion in baseline-assessment.txt and confirms the
   baseline failed at that exact boundary. Lead requires that assessment before ready.
   This proof-contract check happens before the implementation run; it adds no fifth blocking
   kind to the later diff review. The reviewer afterwards applies docs/workflow.md's four
   blocking kinds to the already fixed contract. The 200-run result is supplementary,
   not proof of the library fix. If neither route can demonstrate an in-scope boundary, leave
   the cause unknown and route to lead for a further maintainer decision; do not accept a
   source-only assertion, a green stress run or an unrelated workaround as done.
   Diagnosis precedes test design and is analysis work, not a premature implementation run.
   After this amendment is approved, lead queues a read-only diagnosis to senior. Run
   tools/story.sh analysis 9-1 and inspect source in its analysis worktree, never main;
   do not run start, edit product/test-host behavior, build, or run product tests in this phase.
   Source extraction into a temporary directory is permitted. Write the diagnosis record
   at main-checkout artifacts/diagnoses/9-1-1.md (git-ignored, outside artifacts/trials), with
   the inspected application commit, resolved versions/source references, competing operations,
   chosen evidence route, boundary observation, proposed in-scope fix and an explicit unknown
   section. This read-only phase creates no implementation commit or attempt. Senior hands
   the record durably to lead; lead and this slice's author and QA may read it. R1's Cause:/
   Evidence: commit text is written later in the fix commit from the validated diagnosis.
   Lead routes the record to author to write the deterministic acceptance test before behavior
   changes; QA runs baseline on main via tools/story.sh and checks the right boundary as above.
   Only then does lead authorize ready/start under the existing attempt policy. Diagnosis
   neither authorizes another attempt nor waives the retry count.
   The deterministic gate classifies a demonstrable Docker/container/Postgres failure before
   host startup as infrastructure: print exactly "REGRESSION: infrastructure failure", exit 2,
   preserve raw diagnostics, and write "BASELINE: infrastructure failure" to baseline assessment
   only on a baseline run. Ambiguous errors, migration failures or exceptions after startup
   remain failures (exit 1); no infrastructure label hides them. Before invoking done, QA
   preflights Docker/Postgres. Any failed done run reaching build retains its recorded attempt
   count, even for infrastructure; report it to lead before any rerun, never retry in the gate
   or reset helper state. Lead decides resumption after infrastructure restoration.

## Expected outputs: exact text

| ID | Input | Expected |
|---|---|---|
| `E1` | diagnosed failure and fix commit | commit body includes `Cause:` and `Evidence:`, concrete competing operations/source file/line and resolved library-version references, host/factory ownership path, library-versus-application classification and deterministic test command; selected R4 route; no unsupported known-cause claim |
| `E2` | disposal at the diagnosed competing-operation boundary | disposal completes without Collection was modified; R4 observes no conflicting operation present/in flight at disposal; no retry/skip/catch-and-ignore or disabling instrumentation/parallelism |
| `E3` | actor lifecycle test | Name `aiakos`; not terminated after startup; terminated successfully after factory disposal within 30 seconds; earlier orchestrator tests pass |
| `E4` | identical deterministic R4 test on before and after commits | before: assertion failure at the diagnosed application lifetime boundary or genuine diagnosed exception, with raw trace; after: that test and C1/earlier orchestrator tests pass; final exact line `REGRESSION: pass`; route (b) evidence includes exactly "Library race not deterministically reproduced; application lifetime boundary verified." |

| `E5` | an iteration cannot execute because Docker/container/Postgres infrastructure is demonstrably unavailable | exact line `STRESS: infrastructure failure (iteration <n>)`, exit 2 and raw cause; supporting output log only, no baseline assessment or done attempt claimed; no reproduced-race or successful-fix claim, no hidden iteration retry |

| `E6` | separate supporting R3 stress command | 200 successful iterations print exactly `STRESS: pass (200/200)`; failures retain raw trace and are reported separately; stress never establishes or substitutes for deterministic baseline/after acceptance |

| `E7` | deterministic R4 gate cannot execute because Docker/container/Postgres is demonstrably unavailable before host startup | final exact line `REGRESSION: infrastructure failure`, exit 2 and raw cause; baseline-only assessment `BASELINE: infrastructure failure`; no boundary reproduction or fix claim; if done reached build, its recorded attempt count is retained and reported to lead before rerun |

## Tests

T1. Local acceptance gate under artifacts/trials/9-1-1/gate.sh implements R4/E4 and
   records the baseline commit in main-before.txt through tools/story.sh baseline. Author
   writes it after source diagnosis and before behavior changes; QA confirms a compiled
   boundary failure on main and the after-run result. Supporting R3 stress has a separate
   entry point and output log. No gate may manufacture the library exception or require its
   probabilistic reproduction under route (b).

## Definition of done

- Identical deterministic baseline/after gate with E4 evidence and R1 source diagnosis, all earlier tests green, build zero warnings/errors.
- One local commit: `fix(orchestrator): make host disposal deterministic (#9)`.
- Commit body includes R1 evidence and `Risks: #9 has no open risk assigned; this fixes #103's host-disposal regression.`
- LF, UTF-8 without BOM, final newline. No push/PR. Use tools/story.sh and its one-retry limit.
- Lead creates the ready sub-issue as 9-1-1 under #9 and closes #103 as superseded.

## Out of scope

Package upgrades, application features, global telemetry redesign, changes to ServiceDefaults,
other test projects, migrations, CI rerun policies, and claims that 200 passes prove races impossible.

The contention reworked baseline saw BrokenMigrationTests fail once at iteration 79 with
listening=2 while unrelated processes overlapped on the machine. This did not reproduce the
reported disposal race. Diagnosis/fix of that failure is outside this story; lead files a
separate issue. The earlier baseline's 200/200 green result and that contention result remain
historical observations, not failing deterministic acceptance evidence.
