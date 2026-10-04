# Story review: slice 9-1

Reviewed at commit ed2f210. Stories: 1. Check: ok.

## Findings

- [x] S1 (context-gap): the only code of this repository that acts on the `TracerProvider` during shutdown is `src/Aiakos.ServiceDefaults/TelemetryShutdownService.cs` (`StoppedAsync` starts `tracer.Shutdown` with `Task.Run` and stops waiting after the timeout plus 500 ms, while the host goes on to dispose the provider); the brief has the implementer read it and forbids changing it, so if the diagnosis lands there, R1 stops the story before any fix, and R2 leaves only an ordering change in the test. Fix: add that file to `paths` under the same "only if the diagnosis shows it is necessary" condition as `Program.cs`, or state in R1 that the maintainer has excluded it and that this outcome ends the story as blocked.
- [x] S1 (context-gap): R3 fails the gate "on any nonzero test run, exception or timeout" and only asks that infrastructure failures be distinguishable, so a Postgres container that does not start in iteration 137 of the after-run counts as a failed attempt against a correct fix, and two of them stop the story under the one-retry rule. Fix: state in R3 the gate's result for an iteration that fails before any test ran (for example a distinct final line and exit code that `qa` reruns and does not count as an attempt).

## Not checked

- The cause of the race: the first finding names where the brief closes a door, not a diagnosis; nothing was run and the failure was not reproduced.
- The CI run and job cited in the brief; only issue #103 was read.
- How long one run of the orchestrator test project takes, and so whether 200 runs before and 200 after fit the time that `tools/story.sh baseline` and `done` allow.
- Whether the story could be split into a diagnosis and a fix; the `impl/senior` route and its `escalation` line were accepted as written.
- `artifacts/trials/` (acceptance tests), by role.

## Author resolution

1. R1 explicitly keeps TelemetryShutdownService read-only under the maintainer's narrow scope. A diagnosis requiring that file is reported/parked for lead and maintainer scope amendment, with no speculative workaround or completion claim.
2. R3/E5 separate known infrastructure failures (exit 2, explicit marker and raw trace) from real test failures (exit 1). Baseline infrastructure is not reproduction. Preflight availability before done; if a reached-build run fails, the existing script still counts it and QA reports the count to lead before rerun. This brief does not override the repository's attempt rule.

Ticks reflect author revision.

## Re-review, round 2

Reviewed at commit 5f144e2. Stories: 1. Check: ok.

Both findings of round 1 are resolved by stating the outcome in the brief: R1 keeps
`TelemetryShutdownService.cs` read-only on purpose and makes a diagnosis there a blocked story
with the evidence handed to `lead`; R3 and E5 give an infrastructure failure its own final line
and exit code 2, and say that the attempt count of `tools/story.sh done` is kept and reported.

New findings: None.

Noted, not findings; the brief states both results:

- If the cause is in `TelemetryShutdownService.cs`, this story cannot fix #103: it ends blocked and needs a scope decision by the maintainer.
- An infrastructure failure after the build still counts as an attempt in `tools/story.sh done`; `lead` decides about the rerun.

Not checked in this round: that the exclusion of `TelemetryShutdownService.cs` is the maintainer's decision (stated by the author); nothing was run.


## Maintainer diagnosis-only amendment (review pending)

Lead queue qitem-20261004202405-e06c9b51 authorizes replacement of reproduce-first acceptance after two unsuccessful baseline approaches. R1 requires resolved library/host/factory source evidence; R4/E4/T1 require deterministic direct or application-boundary regression. R3/E6 stress is supporting only. A diagnosis record returns through lead to author for acceptance before behavior changes. Single senior story and scope/attempt limits remain. BrokenMigrationTests contention failure is separate scope. Pending architect review and maintainer approval; local gates are not yet replaced.

## Review of the diagnosis-only amendment, round 3

Reviewed at commit f538ef5. Stories: 1. Check: ok.

Read: the amended goal, R1, R3, R4, E1 to E6, T1, the S1 block and `docs/workflow.md` (flow steps
6 to 10, definitions 4 and 7). Replacing the reproduce-first gate by a deterministic regression
with two evidence routes is consistent in R4, E2, E4 and T1.

### Findings

- [x] S1 (context-gap): R4 puts a diagnosis by the `senior` seat before the acceptance test exists, so before the story is ready, has an issue or a worktree (`tools/story.sh start` needs the ready issue, and no seat may build or test in the main checkout), and it does not say where the "diagnosis-only record" is written while R1 puts the cause in the commit body of a commit that does not exist yet; the `escalation` line still says diagnosis and fix "cannot be split", which R4 now does. Fix: state in R4 where the diagnosis runs (which checkout, read-only or not), the file the record is written to and who may read it, or make the diagnosis a spike with its finding under `docs/spikes/` and leave the fix as the story; then correct the `escalation` line.
- [x] S1 (context-gap): R3 now says the stress command is not run by `baseline` or `done`, but R3 and E5 still give it a baseline assessment (`BASELINE: infrastructure failure`) and attempt accounting of `done`, which no longer apply to it, while the deterministic gate of R4, which starts a real `OrchestratorFactory` against Postgres, has no stated result when Docker or Postgres is unavailable. Fix: state the infrastructure result for the R4 gate (final line and exit code), and remove the baseline and attempt wording from R3 and E5 or move it to R4.
- [x] S1 (context-gap): R4 has the reviewer decide that a test on "a disconnected helper or an invented fake exception does not qualify", but after the gate a reviewer can block only on the four kinds of `docs/workflow.md`, so a regression that passes while observing the wrong boundary is done and the objection becomes a backlog item. Fix: place that check where it can stop the story: in the baseline assessment before ready (definition of ready, point 3: fails on `main` for the right reason), naming who confirms that the observed boundary is the diagnosed one.

## Not checked, round 3

- The maintainer's authorization of the amendment (reported by the author through `lead`).
- The two baseline runs the amendment refers to (200/200 green, and the failure of `BrokenMigrationTests` at iteration 79) and their logs under `artifacts/trials/`; not read, by role.
- Whether a deterministic regression can be written in test-only code for the cause that will be diagnosed; nothing was run or diagnosed.


## Diagnosis-only amendment resolution

1. R4 defines read-only senior analysis in the tools/story.sh analysis worktree, with no product build/test/edit, and artifacts/diagnoses/9-1-1.md as the diagnosis record, readable by lead/author/QA. Fix commit evidence follows diagnosis. Escalation now distinguishes preparatory analysis from the indivisible implementation boundary fix/proof.
2. R3/E5 supporting stress no longer produces a baseline assessment or consumes a done attempt. R4/E7 give the deterministic gate its own infrastructure final line/exit 2, baseline-only assessment and unchanged done accounting.
3. QA confirms source-to-observer connection and exact failure boundary in the pre-ready baseline assessment; lead requires it before ready. Later reviewer has only the existing four blocking kinds.

Author ticks record the amendments pending re-review.
