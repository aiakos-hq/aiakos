# Story review: slice 13-5

Reviewed at commit 3701f1b. Stories: 5. Check: ok.

## Findings

- [x] S2, S3, S4, S5 (context-gap): these stories add only tests over behaviour that already exists once S1 and the named 10-4/13-4 stories are merged, so acceptance tests written from `SAME`, `NEW`, `COMMAND`, `ORDER`, `CRASH`, `AMBIGUOUS` and `ISOLATION` pass on `main` and `tools/story.sh baseline` records `BASELINE: pass`, which keeps the story from ever being ready (`docs/workflow.md:137`); the fixture of R3 and the permanent test classes have no exact names a gate could find missing instead. Fix: give the R3 fixture type and each story's permanent test class (and the scenario method names) exact names in the brief and the outputs, so the gate fails on `main` for their absence.
- [x] S2 (judgment-gap): the story holds the two-system Postgres fixture (R3), the 25-event same-instance replay (R4) and the new-instance scenario with command services, capture-before-dispatch and the launch-mismatch alternate (R5), with three outputs and two prerequisite slices, which is more than one run; and `SAME` needs only 10-4 S6 but waits for 13-4 because `NEW` is in the same story. Fix: move R5/`NEW` to a story of its own that depends on S2 and carries the 13-4 prerequisite alone.
- [x] S1, S3 (context-gap): R1 says the overlay commits "before ... evidence capture" and R7/`ORDER` expect "one history0 capture" for an Unknown live launch, but 13-4 R14 requests that capture only after a supervised reload's SeatActorReloaded notification, and no rule here says whether the new process-start boundary requests it too, so either S1's implementer decides or S3's test has no production owner. Fix: state in R1 whether a successful process-start overlay requests the R18 capture (and that a disconnected node inserts nothing), and name in R7/`ORDER` which event produces each expected capture.
- [x] S1 (context-gap): R2 says a supervised child restart does not reapply the process-start overlay "while the node link is live", which leaves the result for a restart with the link lost to the implementer. Fix: drop the condition, or state the result for a lost link in R2 and `BOOT`.

## Not checked

- The exact states of R4 and R5 (Present/Working/Resumable after seq 1..20, CatchUpSeq 25, the overlay clearing at seq 25, NextSeq 1 and the observation-gap values for I2, `orphan-or-running` on a launch mismatch) were not replayed against `SeatStateMachine`; only `ApplyOverlay` was read (it leaves a known Absent/Exited session unchanged, as R2 says).
- The source audit behind S1 was read and holds at 5bb5ce3: `SeatActor.EnsureLoadedAsync` loads and sends `SeatChildLoaded` without applying `OrchestratorRestarted`; `SeatRegion` spawns the startup keys and tracks `InitialLoadComplete`.
- Which existing tests C1 turns red was not looked up; nothing was built or run.
- The 10-4 S6/S7 and 13-4 contracts the stories consume (`SeatNodeEventCommitter.GetReplayAsync`, `SeatCommandServices`, `PostgresSeatCommandStore`) are not in `src` at 5bb5ce3; only 13-4 R13, R14 and R18 were read, and no 13-4 story issue exists yet.
- Whether S1 merging before the 13-4 stories turns their not-yet-written tests red (a seeded live seat now starts Unknown).
- Spec 0006 and the ADRs were not read.

Resolution of finding 4: R2 and BOOT now cover both live and lost node links without reapplying the process-start step.

Resolution of finding 3: R1 explicitly requests no startup-only capture. R7/ORDER name supervised SeatActorReloaded and committed new-instance gap effects as capture triggers; disconnected nodes insert/send nothing.

Resolution of finding 2: new S3 owns R5/NEW/T6 and depends on S2. S2 owns only fixture/SAME and waits only for 10-4 S6; later stories renumbered S4-S6.

Resolution of finding 1 (now S2-S6): R3 names DurableRestartFixture; T1-T6 name exact permanent class paths and scenario methods. Every output requires its named artifact and executed scenarios, zero skipped. T0 explicitly requires acceptance failure for absent deliverables even when behavior exists on main.
