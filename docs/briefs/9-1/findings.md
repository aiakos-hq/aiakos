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

Author ticks record the amendments.

## Re-review of the diagnosis-only amendment, round 4

Reviewed at commit 936d271. Stories: 1. Check: ok.

The three findings of round 3 are resolved: R4 places the diagnosis as read-only work in the
analysis worktree with its record at `artifacts/diagnoses/9-1-1.md` (checked: the path is
git-ignored) and the `escalation` line now separates it from the fix; the stress command no
longer carries baseline or attempt wording and the deterministic gate has its own infrastructure
result (E7); the check that the test observes the diagnosed boundary is done by `qa` in the
baseline assessment before ready.

New findings: None.

Noted, not a finding; the brief states it: the diagnosis is a step by the `senior` seat that
`docs/workflow.md` does not have, routed by `lead` by hand, and it runs in the analysis worktree
of this slice.

Not checked in this round: nothing was run or diagnosed; the list of round 3 still applies.


## Shutdown-service scope amendment (review pending)

Maintainer authorization dated 2026-10-04, lead queue qitem-20261004211403-de3aa475: 9-1-1 may edit TelemetryShutdownService.cs. R1 removes its read-only/scope-stop condition, paths permit that file only, and C2/E8 require the diagnosed ownership fix at StoppedAsync/provider disposal on success/timeout/error while preserving provider timeoutMs (default 2 seconds) and lifecycle timeoutMs+500 (default 2.5 seconds). No detached provider access or unbounded join is accepted; an evidenced incompatibility goes to lead, rather than silently weakening the decision. R4(b) deterministic acceptance follows approval. Supporting diagnosis source inspected; no implementation or test run by author.

## Review of the shutdown-service scope amendment, round 5

Reviewed at commit a30b4bc. Stories: 1. Check: ok.

Read: the amendment commit (paths, R1, C2, E8, the S1 block), `TelemetryShutdownService.cs`
and spec 0001 R37 with its design note. The widening of `paths` to that one file, the removal of
the scope-stop from R1 and the prohibition of detached workers, provider disposal in the service
and longer bounds are consistent with each other.

### Findings

- [x] S1 (context-gap): for the timeout path C2 and E8 ask for two results that cannot both hold: when a provider's `Shutdown` has been entered and is still running at `timeoutMs + 500`, either `StoppedAsync` completes at the bound and the host disposes the provider under the running call, or it waits and the bound is exceeded; C2 answers this with "stop and hand to lead", so the E8 case "shutdown held by a barrier, lifecycle timeout advanced, no overlap, bound kept" cannot pass for any implementation and the story ends in the stop it was amended to avoid. Fix: split the timeout case in C2 and E8 into (a) a worker that has not entered the provider at the bound, which must never touch the provider afterwards, and (b) a call already inside the provider at the bound, with the result the maintainer chooses for it (which guarantee yields), so that every E8 case has one satisfiable expected output.
- [x] S1 (context-gap): `TelemetryShutdownService.cs` is shared with the node through `AddAiakosServiceDefaults`, and C2 changes when a node's stop completes as well, but the brief's gate and its "earlier tests" are the orchestrator test project only, and no test anywhere covers this service today. Fix: state in C2 that the node's stop behaviour changes with it and which test projects the after gate runs (at least `tests/Aiakos.Node.Tests`), or state that the node is deliberately not checked.

## Not checked, round 5

- `artifacts/diagnoses/9-1-1.md`: R4 names `lead`, the author and `qa` as its readers, so it was not read; C2's account of the cause (lines 54 and 67-68) was compared with the source only.
- The maintainer's authorization of the wider scope (lead queue item named in R1).
- Whether a deterministic regression for E8 compiles and fails on `main` without a seam in the service; nothing was built or run.


## Shutdown-service review resolution in progress

The shared-node finding is addressed in C2/E8/T1 and S1 notes: node stop changes too, focused shared-service lifecycle tests live in the allowed orchestrator directory, and the after gate runs full orchestrator and node Release suites. Author tick records the text change.

The timeout finding remains open for a maintainer choice via lead: queued-not-entered workers must be prevented from entering after the deadline; entered and uncompleted provider calls cannot satisfy both bounded lifecycle return and disposal-after-completion. Proposed safe choice is to retain ownership and await already-entered calls even beyond the lifecycle wait bound, preserving the configured provider timeout and documenting that the lifecycle bound then limits admission/wait observation rather than final return. Alternative bounded-return choice would require another permitted ownership/termination design and evidence that no provider can be disposed under a still-running call. No choice is authorized or silently implemented here.

## Maintainer timeout decision resolution

Lead queue qitem-20261004212216-58da4ef3 records the maintainer decision dated 2026-10-04: timeoutMs+500 now bounds admission/observation only; queued-not-entered work is atomically barred from late provider access; already-entered work must complete before DI disposal even beyond the deadline. The prior no-unbounded-join/hard lifecycle-total bound is explicitly relaxed, provider timeoutMs is preserved, and a nonreturning entered call may hold shutdown pending. C2/E8 and S1 notes now state satisfiable separate cases. Shared-node coverage at 83c7ed1 remains. Author tick records the change.

## Re-review of the shutdown-service scope amendment, round 6

Reviewed at commit 2ad0e64. Stories: 1. Check: ok.

Both findings of round 5 are resolved. C2 and E8 now give each timeout case one satisfiable
result under the maintainer's decision: work that has not entered its provider at
`timeoutMs + 500` is barred from entering later, and a call already inside the provider is
awaited before disposal even beyond that deadline, with no finite bound on the total. C2, E8
and T1 state that node stop changes too and that the after gate runs the orchestrator and node
test projects with focused tests of the shared service.

### Findings

- [x] S1 (context-gap): the decision changes what spec 0001 R37 promises ("a stop during which the dashboard is already gone still exits in about 3 s"): an entered provider call that does not return now holds the stop of the orchestrator and of the node without a bound, but the brief does not name R37, and `docs/specs/` is outside `paths`, so the story would merge code that deviates from an accepted spec with no item that records it. Fix: `author` names the R37 deviation in C2, and says who records it in spec 0001 and when (for example `lead`, as a "changes after acceptance" entry in the pull request of this amendment).

## Not checked, round 6

- The maintainer's decision itself (lead queue item named in C2); taken from the author's text.
- `artifacts/diagnoses/9-1-1.md`, as in round 5; nothing was built or run.


## R37 deviation resolution

C2 names spec 0001 R37's changed stop guarantee and requires lead to record the 2026-10-04 maintainer decision and both-host effects in Changes after acceptance, correcting conflicting R37 shutdown wording in the same amendment PR before approval. Spec edits are lead's analysis/approval work, not implementation scope. Author tick records this responsibility/time assignment.

## Re-review of the shutdown-service scope amendment, round 7

Reviewed at commit db1691b. Stories: 1. Check: ok.

The finding of round 6 is resolved: C2 names the deviation from spec 0001 R37 and assigns its
record to `lead`, as a "Changes after acceptance" entry with corrected R37 wording, in the pull
request of this amendment and before approval.

New findings: None.

Open action outside the brief, for `lead`: spec 0001 does not carry that entry yet; C2 makes it
part of the amendment pull request.

Not checked in this round: the lists of rounds 5 and 6 still apply; nothing was built or run.

