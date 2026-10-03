## S1: Surface types and vocabulary
goal: The shared public records, enums and interfaces and exact stored vocabulary exist; no state-machine behavior is implemented yet.
depends: -
owns: V1
outputs: -
tests: T10
notes: Create the shared surface once so the delivery and seat machine stories cannot redefine FindingChange or vocabulary. Introduce Apply only in its behavior story. T10 is the acceptance for V1.

## S2: Delivery table
goal: Delivery inputs follow every column of the delivery table, with final-state absorption and honest unknown enum handling.
depends: S1
owns: B22
outputs: DV1, DV2, DV3, DV4, DV5
tests: T6
notes: Shared FindingChange and vocabulary come from S1. This concern can run independently of the seat event stories after S1.

## S3: Session command results and process exit
goal: The seat Apply entry point dispatches launch/stop results and process exit, derives activity from session, and handles dispatch failures and launch watchdog.
depends: S1
owns: B6a, B5, B17a, B18, B21, B19w
outputs: S3, S4, S5, S6, S7, S8, S9, S10, S14
tests: -
notes: This is the largest cut: six rules and nine row outputs around one session concern. Construct Desired, Launch, ReadinessSeen and node sequencing directly in row fixtures; do not require up/down or readiness inputs from later stories. Use SourceSeq 0 or increasing; no late tests here. B6a introduces event attribution/application; harness-specific, sequencing and gap branches are completed by their owning later stories. S3 does not assert the unconditional orphan-harness resolve added by B15 later (B2 permits additional resolves). If size proves excessive, the existing seam is stop/exit S3,S4,S14 versus launch S5-S10; no escalation is requested now.

## S4: Harness readiness, session end and rotation
goal: Current-launch harness attribution supports readiness, orphan session end, native-ID mismatch and rotation.
depends: S3
owns: B6b, B8
outputs: S11, S12, S13, A1, A14, U7, U8
tests: -
notes: Use SourceSeq 0 or increasing; B7 in S9 owns late U2/U8 variants. U8 tests every known-session value: A1 only when present, and no session/activity change otherwise. LastEventAt and activity-stale resolves are established here. Tests construct the supplied state rather than invoke future up/down commands.

## S5: Activity prompt, tools and pending input
goal: Prompt, activity level, tools and pending-input events apply their columns and parallel-tool script.
depends: S4
owns: B10
outputs: A2, A3, A4, A5, A6, A7, GS8
tests: -
notes: B6a/B6b provide dispatch, B8 readiness. Use SourceSeq 0 or increasing; do not assert late input behavior before S9. GS8 fixtures start present/idle with matching native ID and launch. The unconditional delivery-unconfirmed resolve on A2 is added by B20 in S6; earlier row tests permit it under B2.

## S6: Activity turn end, failure, compaction and retry
goal: The remaining turn activity rows and resolutions hold, including turn failure, missing session end, and OpenCode stickiness.
depends: S5
owns: B20
outputs: A8, A9, A10, A11, A12, GS4, GS5, GS9
tests: -
notes: Use SourceSeq 0 or increasing. GS4 starts present/idle, GS5 present/working, GS9 present/idle; each has a matching current launch/native ID. B10 supplies pending-input state; S3 supplies process exit. Compaction tests explicitly set the remembered value when testing A9 in isolation.

## S7: Resumability evidence and launch results
goal: Matching conversation evidence and mode-specific launch results follow U2-U5 and the full permission-turn script.
depends: S6
owns: B9, B17b
outputs: U2, U3, U4, U5, GS1
tests: -
notes: Use SourceSeq 0 or increasing; S9 owns late evidence. Set CurrentLaunch.Mode and ReusedNativeSessionId directly in the fixture so no up command is needed. GS1 starts starting/unknown/not-ready with a current launch, matching native ID and fresh-only resumability; readiness is its first input. Session result rules come from S3.

## S8: Sequence gaps, overlays and node attachment
goal: Epoch/duplicate/gap handling, lost observation, link overlays and inventory reconciliation update known and reported state.
depends: S7
owns: B6c, B11, B12, B13
outputs: A16, U6, S15, S16
tests: T7
notes: Use SourceSeq 0 or increasing; no stale-order assertions before S9. B6c completes sequencing before attribution; B11 is applied once per input, including simultaneous new epoch, Seq gap and explicit gap body. NodeAttached tests cover both instances and every lifecycle, including S15 LAUNCHING clearing ReadinessSeen before a later READY result.

## S9: Stale guard and event-order properties
goal: The complete event pipeline honors late events and the telemetry exemption; the property suite and pinned history limitation hold.
depends: S8
owns: B7
outputs: -
tests: T8, T11, T12
notes: This is the first story with every pipeline stage. Add CsCheck package references here only and the complete-turn generator. T8 tests late U2 and late U8 preserving activity while updating native ID/resumability; T11 tests gap handling exactly once. Earlier row assertions remain unchanged except for permissible extra resolve requests.

## S10: Up and down decisions
goal: Up/down update desired state, dispatch effects and follow all launch decision rows plus S1,S2,U1.
depends: S8
owns: B14, B15
outputs: S1, S2, U1, LD1, LD2, LD3, LD4, LD5
tests: T4
notes: Reported-state decisions require overlays and node attachment from S8. Tests construct resumability directly, then test both profiles and Fresh values. B15 adds the unconditional orphan-harness resolve on S3; the earlier S3 test ignores additional resolves per B2. SourceSeq 0 or increasing is sufficient; no dependency on S9 is needed.

## S11: Send, timers and command scripts
goal: Send rejects in the stated order, timers validate state/time, and complete lifecycle command scripts pass.
depends: S10
owns: B16, B19
outputs: A15, GS2, GS3, GS6, GS7
tests: T5, T9
notes: These scripts use SourceSeq 0 or increasing, so no dependency on S9 is needed. GS2/GS3 explicitly seed ready/present idle, Desired Up, StopRequested false and resumable after a completed turn; GS6 starts present/idle/resumable; GS7 starts present/needs-input with matching current launch/native ID. State-unknown-prolonged resolves added here are ignored by earlier row tests per B2.
