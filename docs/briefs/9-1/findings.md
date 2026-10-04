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

