# Story review: slice 13-1

Reviewed at commit 4ff7f3d (`main`), brief `status: draft`. Stories: 6. Check: ok.

Read for this review: the brief, `items.tsv`, `stories.md`, and spec 0006 lines 379–638 and
707–726. R8–R20, R28, R38 and R39 of the spec (lines 170–212, 247–249, 290–294) were read where
a finding depends on them.

In this file "story 1" to "story 6" are the stories of `stories.md` (`S1`–`S6` there), and
"row S1" to "row S16" are rows of the session table. "P1" to "P11" are the proposed stories.

## Findings

Closure and order:

- [x] F1 (context-gap) Brief is not closed: B2a, B2b and B2c point at the 40 rows of spec lines
  452–469, 500–517 and 555–564, and no row is an item. Fix: copy the three tables into the brief
  with one row per line marked `` | `S5` | … `` and list each row in `items.tsv` as type
  `output` with the needs of table 1 below.
- [x] F2 (context-gap) The launch mode decision table (spec 587–593, 5 rows) and the delivery
  table (spec 602–608, 5 rows) are in the same position: B2 says "as written" and their values
  are not in the brief. Fix: copy them in as output items too, needing B14 and B22.
- [x] F3 (judgment-gap) No row can be tested completely in the story it sits in (table 1): the
  rule that hands an event to a table, B6, is in story 5, and stories 1, 2 and 3 do not depend
  on it and `story.sh show 13-1 1` does not show it. Of 40 rows, 2 (S4, S9) have one variant
  that story 1 can test (`CommandDispatchFailed`, B18), 3 (S7, S8, U4) pass only if the
  implementer guesses the dispatch, and 35 need a rule of story 5 or 6. Fix: the re-split of
  table 2.
- [x] F4 (judgment-gap) The same holds for the scripts of story 2: GS4, GS5, GS8 and GS9 are
  event sequences and need B6 (and B8 for a readiness step), which story 2 does not depend on.
  Fix: table 2.
- [x] F5 (judgment-gap) Moving every row to the first story that has its rules, without
  splitting a rule, puts 25 rows in story 5 and 14 in story 6, against `max_outputs: 9`.
  Fix: split B6, B17 and one sentence of B19 as listed under table 2.
- [x] F6 (judgment-gap) T1, T2 and T3 each cover rows of several stories, so no story can own
  them. Fix: remove T1–T3 as items and make "one test per row, named by row id, every column"
  part of what a row item means; the test class is created by the first story that has a row
  of that table.
- [x] F7 (context-gap) The `needs` column under-declares 26 items (table 3), which is why the
  check passes on a split that cannot be built in order. Fix: correct the lines marked
  "breaks order" in table 3.
- [x] F8 (context-gap) The stored vocabulary has no rule: the strings are comments in the
  surface block and the `*_UNSPECIFIED` sentence (brief lines 158–160) has no ID, so T10 needs
  `-`. Fix: give that sentence a rule ID and let T10 need it.
- [x] F9 (judgment-gap) Story 4 (delivery, `depends: -`) uses `FindingChange` and the finding
  kind constants of `SeatVocabulary`, which story 1 creates; built in parallel, both define the
  same types. Fix: one first story creates the surface types and the delivery story depends on
  it (P1, P2).
- [x] F10 (judgment-gap) B21's clause "`CommandStatus` … in `DeliveryNotCompleted` → `Unknown`"
  is behaviour of `DeliveryStateMachine` (story 4) owned by story 1, and its clause
  "`SessionLifecycle` → `UNKNOWN`" repeats B12 (story 5). Fix: move the first into B22 and
  delete the second.
- [x] F11 (context-gap) Row A13 has no cells ("handled by S12 / S13"), but T2 asks for a test of
  every row A1–A16. Fix: A13 is not an item; say so in the brief.
- [x] F12 (context-gap) Several rules add a resolve to rows of other stories: B6's last
  paragraph (`activity-stale`, every harness row), B19 (`state-unknown-prolonged`, every row
  that leaves `unknown`), B15 (`orphan-harness` on S3), B17, B20. `SeatState` holds no open
  findings, so a resolve can only be emitted every time, and a row test that compares the whole
  `Findings` list is changed by a later story. Fix: say in B2 that a resolve is emitted whether
  or not the finding is open, and that a row test asserts the findings its row names and
  ignores other resolves.

Inputs for which the brief gives a wrong answer or none:

- [x] F13 (context-gap) B8 says a mismatching event "is never S11 or A1" and then that U8
  "applies A1"; when the rotation event arrives while known session is `unknown`, `starting` or
  `exited`, A1 gives activity `idle` and breaks B4. Fix: say that U8 applies A1 only while
  known session is `present`.
- [x] F14 (context-gap) B7 lets a late event apply U8, and U8 applies A1, while the activity
  table takes no late input (spec 476–477, R13). Fix: say whether a late U8 changes activity.
- [x] F15 (context-gap) B5 gives no activity for S5 from `starting` with `ReadinessSeen` true;
  it is reachable: `present` after readiness, `NodeAttached` with a new instance and inventory
  `LAUNCHING` (S15 → `starting`), then `LaunchResult READY`. Fix: name the activity for that
  case, or say that S15 into `starting` clears `ReadinessSeen`.
- [x] F16 (context-gap) An event with a new `NodeInstanceId` and `Seq > 1` meets B6 step 1 and
  step 3, so rule 11 applies twice (two `observation-gap`, two `RequestCapture`), and T11 says
  "applies rule 11 once". Fix: say that rule 11 applies at most once per input.
- [x] F17 (context-gap) B2 lists `R18` as a `SeatTransition.Rule` value, and the changes of
  rule 11 are rows A16 and U6. Fix: say which transitions carry `R18`, or drop it.
- [x] F18 (context-gap) `StopNotCompletedBody` with `REJECTED`, or with a status B21 would call
  unrecognized, has no row (S4 names FAILED and TIMED_OUT) and no clause in B21. Fix: add it to
  B21 (S4, or no change).
- [x] F19 (context-gap) B2 says `—` and `n/a` mean "no transition, no finding" and does not
  give the `EventDisposition`; the spec says `n/a` is "recorded as evidence" (421) and B4 says
  `Applied` for one case. Fix: name the disposition for both cells in B2.
- [x] F20 (context-gap) Row S10 has no `+F` in its cell and the findings table (712) says S10
  opens `launch-unconfirmed`; B2 allows "only findings that a cell or rule below names". Fix:
  say in B19 and B21 whether S10 opens it, for the timeout and for the watchdog.
- [x] F21 (context-gap) B3 gives reported activity the overlay's reason and B4 says, "for known
  and for reported values", that the reason is `session-unknown` when session is `unknown`.
  Fix: limit B4's reasons to known values.

Brief against the spec lines:

- [x] F22 (judgment-gap) B5 sets `unknown/observation-gap` for S5 from `unknown`; spec 424–427
  gives `unknown/sources-disagree` for "S5 without a readiness event". Fix: follow the spec, or
  list it as a deviation as B4 does.
- [x] F23 (judgment-gap) B6 step 4 applies an `ObservationGapBody` of any launch; spec 622
  makes every body of another launch `stale-launch`. Fix: list it as a deviation.
- [x] F24 (judgment-gap) B6 step 1 applies rule 11 for every differing instance when the state
  has one; spec 616–618 adds "and the seat expected events from the previous instance". Fix:
  list it as a deviation, or state the condition.
- [ ] F25 (context-gap) B7 keeps `TELEMETRY` and `OTHER` out of the stale guard; spec 624–625
  has no exemption. The brief gives the reason, and the spec is not amended. Fix: `lead` adds
  it to the spec amendment of #13, with F22–F24 as far as they stay.

Approval:

- [ ] F26 Brief: `status` is `draft`. Fix: the maintainer approves it after the re-cut.

## Table 1: rows, driving rules, stories

"Driven by" is the rule that turns the input into the row. "Also needs" is a rule that sets a
value, finding or effect of the row. The number in brackets is the story that owns the rule in
the current split. "First story" is the earliest story of the current split in which the whole
row can pass. B1–B4 apply to every story and are not listed.

| Row | Input | Driven by | Also needs | Story now | First story |
|---|---|---|---|---|---|
| S1 | `UpRequested` | B14 (6) | – | 1 | 6 |
| S2 | `DownRequested` | B15 (6) | – | 1 | 6 |
| S3 | `StopResultBody` | B6 (5) | B15 `orphan-harness` (6), B10 (2) | 1 | 6 |
| S4 | `StopNotCompletedBody`; `CommandDispatchFailed(Stop)`; unrecognized `StopOutcome` | B6 (5); B18 (1); B21 (1) with B6 | – | 1 | 5 (B18 variant: 1) |
| S5 | `LaunchResultBody` READY | B6 (5) | B5 (2), B8 `ReadinessSeen` (5), B17 (6) | 1 | 6 |
| S6 | `LaunchResultBody` FAILED | B6 (5) | B17 `unexpected-exit` (6) | 1 | 6 |
| S7 | `LaunchResultBody` UNKNOWN or unrecognized | B6 (5), B21 (1) | – | 1 | 5 |
| S8 | `StartNotCompletedBody` REJECTED, two reasons | B6 (5) | – | 1 | 5 |
| S9 | `StartNotCompletedBody` REJECTED other or FAILED; `CommandDispatchFailed(Start)` | B6 (5); B18 (1) | – | 1 | 5 (B18 variant: 1) |
| S10 | `StartNotCompletedBody` TIMED_OUT or unrecognized; `LaunchWatchdogFired` | B6 (5), B21 (1); B19 (6) | – | 1 | 6 |
| S11 | readiness event | B8 (5), B6, B7 (5) | B5 (2), B17 (6) | 1 | 6 |
| S12 | `SESSION_ENDED`, readiness seen | B6, B7 (5) | B8 (5), B17 `unexpected-exit` (6) | 1 | 6 |
| S13 | `SESSION_ENDED`, no readiness | B6 step 5 (5) | B8 (5) | 1 | 5 |
| S14 | `ProcessExitedBody` | B6 (5) | B17 `unexpected-exit` (6) | 1 | 6 |
| S15 | `NodeAttached`, new instance, same launch | B12 (5) | B11 (5), B5 (2), B17 for `EXITED` (6) | 1 | 6 |
| S16 | `NodeAttached`, new instance, seat missing | B12 (5) | B11 (5) | 1 | 5 |
| A1 | readiness event | B8 (5), B6, B7 (5) | B5 (2) | 2 | 5 |
| A2 | `PROMPT_SUBMITTED` | B6, B7 (5) | B10, B20 (2) | 2 | 5 |
| A3 | `ACTIVE` | B6, B7 (5) | – | 2 | 5 |
| A4 | `TOOL_STARTED` | B6, B7 (5) | B10 detail (2) | 2 | 5 |
| A5 | `TOOL_FINISHED` | B6, B7 (5) | B10 (2) | 2 | 5 |
| A6 | `INPUT_REQUESTED` | B6, B7 (5) | B10 (2) | 2 | 5 |
| A7 | `INPUT_RESOLVED` | B6, B7 (5) | B10 (2) | 2 | 5 |
| A8 | `COMPACTION_STARTED` | B6, B7 (5) | – | 2 | 5 |
| A9 | `COMPACTED` | B6, B7 (5) | – | 2 | 5 |
| A10 | `TURN_ENDED` | B6, B7 (5) | B10, B20 (2) | 2 | 5 |
| A11 | `TURN_FAILED` | B6, B7 (5) | B10 (2) | 2 | 5 |
| A12 | `RETRYING` | B6, B7 (5) | – | 2 | 5 |
| A13 | `SESSION_ENDED` | rows S12 and S13 | – | 2 | not a row (F11) |
| A14 | `TELEMETRY`, `OTHER` | B6 last paragraph, B7 (5) | B21 unrecognized kind (1) | 2 | 5 |
| A15 | `QuietTimeoutFired` | B19 (6) | B6 `LastEventAt` (5) | 2 | 6 |
| A16 | gap, `ObservationGapBody`, new instance | B11 (5), B6 steps 1, 3, 4 (5) | – | 2 | 5 |
| U1 | `UpRequested` with a new session | B14 (6) | – | 3 | 6 |
| U2 | conversation evidence | B9 (3), B6 (5) | B7 for the late case (5) | 3 | 5 |
| U3 | `LaunchResultBody` READY, mode RESUME | B6 (5) | B17 (6) | 3 | 6 |
| U4 | `LaunchResultBody` FAILED, `RESUME_SESSION_NOT_FOUND` | B6 (5) | – | 3 | 5 |
| U5 | `LaunchResultBody` FAILED, FRESH with reused ID | B6 (5) | B17 (6) | 3 | 6 |
| U6 | gap, new instance | B11 (5) | – | 3 | 5 |
| U7 | `SessionObservedBody` mismatch; readiness with another ID | B6 (5); B8 (5) | – | 3 | 5 |
| U8 | rotation | B8 (5) | row A1, B7 for the late case (5) | 3 | 5 |

## Table 2: the re-split

Stay inside the brief's caps (8 rules, 9 outputs): no cap is raised. The cut follows the input,
not the axis, because the input is what an implementer dispatches on and what a rule is about.

Changes to rules that the split needs, and no others:

1. B6 becomes three rules. B6a: step 4 (attribute) and step 7 (apply order, evidence bodies,
   dead pane). B6b: step 5 (orphan), the last paragraph (`LastEventAt`, `activity-stale`) and
   B21's "`HarnessEventKind` → `OTHER`". B6c: steps 1–3 (epoch, duplicate, gap). Step 6 is B7.
   Reason: the tables need B6a before any row, and steps 1–3 need the tables (rule 11).
2. B17 becomes two rules. B17a: the first and the last sentence (resolves on entering
   `present`; `unexpected-exit`). B17b: the three sentences on U3, U5 and a failed resume.
   Reason: the session rows of launch results come before the resumability rows.
3. The `LaunchWatchdogFired` sentence of B19 becomes its own rule (B19w below). Reason: it is
   the second half of row S10, whose first half is a command result.
4. B21 keeps the session clauses only (F10). B2a, B2b and B2c stop being `once` items, since
   the rows replace them. T1–T3 go (F6). The vocabulary gets a rule (F8, "V" below).

| Story | Title | Owns | Outputs | Tests | Depends |
|---|---|---|---|---|---|
| P1 | Surface types and vocabulary | V | – | T10 | – |
| P2 | Delivery table | B22 | 5 delivery rows | T6 | P1 |
| P3 | Session: command results and process exit | B6a, B5, B17a, B18, B21, B19w | S3–S10, S14 | – | P1 |
| P4 | Harness events: readiness, session end, rotation | B6b, B8 | S11, S12, S13, A1, A14, U7, U8 | – | P3 |
| P5 | Activity: prompt, tools, pending input | B10 | A2–A7, GS8 | – | P4 |
| P6 | Activity: turn end, failure, compaction, retry | B20 | A8–A12, GS4, GS5, GS9 | – | P5 |
| P7 | Resumability: evidence and launch results | B9, B17b | U2–U5, GS1 | – | P6 |
| P8 | Sequence, lost observation, overlays, node attachment | B6c, B11, B12, B13 | A16, U6, S15, S16 | T7 | P7 |
| P9 | Stale guard and order properties | B7 | – | T8, T11, T12 | P8 |
| P10 | `up` and `down` | B14, B15 | S1, S2, U1, 5 launch decision rows | T4 | P8 |
| P11 | `send`, timers, command scripts | B16, B19 | A15, GS2, GS3, GS6, GS7 | T5, T9 | P10 |

Sizes: at most 6 rules (P3) and 9 outputs (P3) in a story; every story owns a rule; 39 rows
(16 + 15 + 8), 9 scripts and 10 new table rows are each in one story.

Reasons for the cuts that are not obvious:

- P1 exists so that later stories only add behaviour to types that are there, and so that P2
  and P3 can run side by side without both creating `FindingChange` (F9).
- P3 is the largest story: it also writes the skeleton of `Apply` and the derivation of
  activity from session (B4). If its run fails on size, the cut is S3, S4, S14 (stop and exit)
  against S5–S10 (launch), with B6a in the first half.
- B8 stays whole in P4: U7 and U8 need only the resumability value of the state, and U8 needs
  A1, which is in P4.
- A10 and A11 are in P6 with B20, so GS9 (ends with `TURN_ENDED`) is in P6 and not in P5.
- B7 is in P9 and not in P4, so that the property story owns a rule, and CsCheck (two project
  files and a generator) is one story. Nothing before P9 asserts a late event; `notes` of P4–P8
  must say that their tests use `SourceSeq` 0 or increasing. The late cases of U2 and U8 are
  tested in P9.
- T8 ("each pipeline step") is in P9 because that is the first story with all of B6a, B6b, B6c,
  B7 and B12.
- P10 depends on P8 because B14 decides on reported values (overlays, B13) and resolves
  `node-not-connected` through B12.

The split was not run through `story-check.sh`; the items do not exist yet.

## Table 3: the `needs` column

"Breaks order": the missing rule is owned by a story that the item's story does not depend on.
"Order holds": missing, and its owner is the same story or one it depends on. Items not listed
are right as they are: B1–B4, B2a, B2b, B8, B12, B13, B15, B16, B18, B20, B22, T5, T6, T7, T9.

| Item | Needs now | Verdict |
|---|---|---|
| B2c | B2a | Not needed: resumability is independent of session (R10). Harmless. |
| B5 | B2a, B2b | Breaks order: B8 (`ReadinessSeen`) and B12 (S15) are in story 5. |
| B6 | B2a, B2b, B2c, B7, B11 | Order holds: B8 (readiness kind in step 4, `ReadinessSeen` in step 5). |
| B7 | - | Order holds: B2c (U2, U8), B8, B9. |
| B9 | B2c | Breaks order: B7 ("applies to late events too") is in story 5. |
| B10 | B2b | Order holds: B2a ("any change of session clears it"). |
| B11 | B2b, B2c | Order holds: B2a (reads known session). |
| B14 | B2a, B2c | Order holds: B13 (reported values); the launch decision table is not an item (F2). |
| B17 | B2a, B2c | Order holds: B14 and B15 (`Launch.Mode`, `Desired`, `StopRequested`), B6, B8. |
| B19 | B2a, B2b | Order holds: B6 (`LastEventAt`), B13 (reported session). |
| B21 | B2a | Breaks order: B6 and B12 (story 5), B22 (story 4). See F10. |
| GS1 | B2b, B2c, B8, B10 | Order holds: B6, B7, B9 (U2 at the prompt), B2a. |
| GS2 | B2a, B6, B14, B17 | Order holds: B8, B9 (the turn that makes it `resumable`). |
| GS3 | B14, B17 | Order holds: B6, B2c (U4). |
| GS4 | B20 | Breaks order: B6, B7 (story 5); also B2b. |
| GS5 | B2a, B2b | Breaks order: B6 (story 5). |
| GS6 | B8, B14, B15 | Order holds: B6, B9, B2c. |
| GS7 | B16 | Order holds: B2b (A2 from `needs-input`), B6. |
| GS8 | B10 | Breaks order: B6, B7 (story 5). |
| GS9 | B10 | Breaks order: B6, B7 (story 5); also B2b (A3 sticky). |
| T1 | B2a, B18, B21 | Breaks order: B5 (2); B6, B8, B12 (5); B14, B15, B17, B19 (6). |
| T2 | B2b, B5 | Breaks order: B6, B7, B8, B11 (5); B19 (6); also B10, B20. |
| T3 | B2c, B9 | Breaks order: B6, B8, B11 (5); B14, B17 (6). |
| T4 | B14 | Order holds: B2c; the launch decision table (F2). |
| T8 | B6, B7, B12 | Order holds: B11. |
| T10 | - | No rule to need (F8). |
| T11 | B2b, B2c, B6, B7, B11 | Order holds: B9, B10. |
| T12 | B7 | Order holds: B2b (A2, A8, A9). |

With rows as items, the needs of a row are its "Driven by" and "Also needs" cells of table 1.

## Checked and found consistent with the spec lines

B11 (R18), B12 (S15, S16, R17), B13 (R16), B14 and the launch decision table, B15 (S2, S3),
B17 (spec 471, 578–580, 712–722), B19 (R19, R39), B20 (findings table), B22 (delivery table),
the profile interface (spec 667–680), the two test profiles (spec 687–691), GS1–GS9 and the
four properties of T11 (the first holds because every generated turn ends with `TURN_ENDED`,
so it does not contradict T12).

## Not checked

- The spec outside the lines named at the top. In particular B16 (the order of the `send`
  rejections and their names) and B18 (`CommandDispatchFailed` → S9 and S4, which the tables do
  not list) were not compared with the spec.
- R38 says `turn-failed` stays open until acknowledged; the findings table (720) and B20
  resolve it on `TURN_ENDED`. The brief follows the lines it is allowed to read. The spec
  disagrees with itself; that is for the spec amendment, not for this brief.
- The proto enum names against `Aiakos.Contracts.Node.V1`.
- The initial state of scripts GS4, GS5, GS7, GS8 and GS9, which the brief leaves to the test.
- Nothing was built or run except `tools/story.sh check 13-1` and `show 13-1 1|2|3`.

## Author resolution: re-cut on docs/brief-13-1-fix

F1–F24 are addressed in this branch and await the architect's second read; checked boxes
record the author repair, not approval. F25 remains lead's #13 spec amendment; F26 remains
the maintainer's approval. The brief stays draft. No production code was read or changed.

- F1/F2/F6/F11: 39 axis outputs (A13 delegates), LD1–LD5 and DV1–DV5 are marked in the
  brief and indexed. B2 gives each row a named test for all columns; T1–T3/B2a–B2c retire.
- F3–F5/F7/F9: the eleven stories follow table 2, with a first surface/vocabulary story,
  independent delivery story, then input concerns. B6a/b/c, B17a/b and B19w are separate
  rules. Needs reference actual dispatch/derivation rules, not removed table wrappers.
  Late variants are deferred explicitly to T8/S9; direct fixtures avoid dependencies on
  future up/down/readiness inputs. Earlier tests ignore only additional resolves, not
  unexpected opens or changed axis values.
- F8: V1 defines exact stored vocabulary, unknown proto values and Duplicate rejection;
  T10 needs V1. It covers only documented ToStored overloads, not wire-only lifecycle/gap.
- F10: B21 handles session results only. Harness kinds move to B6b; inventory lifecycle
  stays B12; DeliveryNotCompleted unknown status is B22.
- F12: B2 makes resolves unconditional and tells row tests to ignore later extra resolves.
  S3 notes defer B15's orphan-harness resolve; S5 notes defer B20's delivery resolve.
- F13/F14: B8 applies rotation's A1 only while present; B7 forbids activity changes on late
  rotation. U8 covers session variants and T8 covers late U8. The late-rotation choice is
  recorded explicitly against the spec's internally conflicting activity/rotation text.
- F15: B5 states that S15 LAUNCHING clears ReadinessSeen; S15's row test covers a following
  READY result. S5 without readiness uses sources-disagree from starting or unknown.
- F16: B6c permits one B11 application per input across epoch/gap/body; T11 pins the case.
- F17: derived transition labels are R10/R16/R17; gaps label their changes A16/U6.
- F18: B21 specifies rejected/unknown stop status as S4 and completed as evidence.
- F19: B2 defines Applied/Evidence for dash/n/a cells and pipeline disposition precedence.
- F20: B21 and B19w explicitly open launch-unconfirmed for both S10 variants.
- F21: B4 limits the derived reason names to known values; B3 wins for overlay reasons.
- F22: B5 follows spec: S5 without readiness uses unknown/sources-disagree.
- F23/F24: B6a and B6c explicitly declare the existing foreign-launch ObservationGap and
  unconditional known-epoch-loss differences from spec pipeline steps 4 and 1.

The split requires no additional story or higher cap beyond table 2. GS1's original
nine inputs had only eight listed activity results; the missing prompt -> working result
is now written explicitly, without changing A2. Script fixtures in story notes state
initial session/activity/launch/native-ID values so their input paths are not guessed.
