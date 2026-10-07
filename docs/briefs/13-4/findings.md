# Story review: slice 13-4

Reviewed at commit f497030. Stories: 6. Check: ok.

## Findings

Resolution commit: f573054 follow-up. The public seam, persistence owner, outbound port,
timeouts, reload signal, watchdog ownership, and named prerequisites are now stated in the brief;
all seven findings are addressed for architect re-review.

- [x] S1 (judgment-gap): the seam here and the seam the 15-2 brief was reviewed against disagree: 15-2 expects `Aiakos.Core.CallerContext`, an `ISeatCommandDispatcher.DispatchAsync(SeatEnvelope, CallerContext, CancellationToken)` and messages without a caller (`SeatUp(bool Fresh, string? Note)`, `SeatSend(string Body, bool Force)`), while this brief puts `CallerContext` in `Aiakos.Orchestrator.Seats` (its paths do not include `src/Aiakos.Core/`), carries the caller inside each message, has `SeatSend(Lead, Body, ExpectConfirmation, Force, Caller)`, names no dispatcher type at all, and has no reply for a `SeatDown` that is a no-op. Fix: write the one seam in the public surface (the project and namespace of `CallerContext`, the type and member the API calls, every reply including the no-op) and tell the 15-2 author which of its statements change.
- [x] S2 (judgment-gap): R2 persists "session, launch and command metadata atomically through the existing committed input seam", but 13-3's `ISeatInputCommitter.ApplyAsync` accepts six inputs and rejects the rest with `SEAT_INPUT_NOT_SUPPORTED`, and 13-3's writer never creates a command, launch or session row; this brief also says it "does not own command creation" while R2 and R6 create commands. Fix: state which component creates those rows and through which named member, with the rows and columns written for up, down, send and capture, and remove the sentence that disowns command creation.
- [x] S2 (context-gap): every rule "dispatches" StartSeat, StopSeat, CapturePane or a delivery, but no outbound port is named and none exists on main (node command sending is 10-5), and nothing says who builds the launch and delivery content (12-1's `BuildLaunch` and `BuildDelivery`, the native session id, the lead that `SeatSend` already carries). Fix: name the outbound port with its members as an external prerequisite or as part of this slice, and say which component calls the harness adapter and when.
- [x] S1 (context-gap): the rules give no exact text: R5's "fixed reasons" are not listed, R8 and R9 say "the specified reason" and "the specified structured log/metric/span names" without specifying them, the timeouts of capture, delivery and stop have no values, and the expected outputs are summaries (`fixed rejection`, `fixed timeout reply`). Fix: list every rejection reason with its condition, every timeout value, every log, metric and span name with its tags, and give each output its exact reply or state.
- [x] S5 (judgment-gap): R8 and `RELOAD` emit `SeatActorRestarted` "before any retained work is released", but 13-3 publishes `SeatActorReloaded`, and it already does so before queued work; the rule also restates "the 13-3 lifecycle contract" without saying what this slice adds to it. Fix: use 13-3's name and state only the behaviour this slice adds (which overlays and capture requests it issues, on which inputs).
- [x] S1 (judgment-gap): S1's goal is to publish the records, but it owns R1, the whole `SeatUp` decision, and its output is `UP-reject`, while S2 "Up lifecycle dispatcher" owns only R2; and 13-3 left automatic timer scheduling (quiet timeout, launch watchdog, unknown prolonged) to this slice, which no rule here mentions. Fix: give S1 a rule and an output for the records alone, move the up decision to S2, and add the timer rule with its story or say which slice owns it.
- [x] S2 (context-gap): the story notes name prerequisites by 13-3's old story numbers ("13-3 S5/S6 persistence stages", "S7 region retry", "S7/S8", "S9"), which do not say what must be merged, and the brief says those stories "are not all on main yet". Fix: name each prerequisite by its 13-3 story title or the type it delivers, per story.
- [x] S1 (judgment-gap), round 1: the new `Aiakos.Core.SeatEnvelope(object Command)` has the same name as the merged `Aiakos.Orchestrator.Seats.SeatEnvelope(Guid TenantId, Guid SeatId, object Message)` (`SeatProtocol.cs`), which the dispatcher implementation must also use, and neither the new envelope nor `DispatchAsync(SeatEnvelope, CallerContext, CancellationToken)` nor any command record carries the seat, so a command names no seat. Fix: give the Core type another name or reuse the merged one, and add the seat identity (the resolved seat UUID) to the call, with the reply types that `Task<object>` can return listed.
- [x] S2 (context-gap), round 1: `ISeatCommandStore.CommitAsync` and `ISeatCommandPort` are named without signatures: `CommitAsync` has no parameters or result and its rows are "tenant, seat, command id/kind, caller and payload hash" with no column names; `SeatLaunch` is undefined; `Deliver(Guid,string,string,bool)` does not say what its arguments are; no method has a return type. The brief also does not say how this store's rows and the `seat_state` change of the same decision commit in one transaction with 13-3's version check, which ADR 0032 requires of the actor. Fix: give both interfaces their full members and records, the columns written per command kind, and the one transaction that holds rows and state.
- [x] S1 (context-gap), round 1: R1 to R9, the expected outputs table, items and stories are unchanged in this commit, so the points of the fourth to seventh findings stand as written: R5's rejection reasons are not listed and the outputs are still summaries; R8 and `RELOAD` still say `SeatActorRestarted` while the new paragraph says `SeatActorReloaded`; S1 still owns the up decision; the story notes still name prerequisites by 13-3's old numbers; and the timers appear only as "quiet/activity watchdogs 10 minutes", with no rule for which timer is armed and cancelled when, although `IHarnessStateProfile` already carries `ReadyTimeout`, `ConfirmTimeout` and `QuietTimeout` per harness. Fix: rewrite the rules, the outputs table and the story blocks themselves instead of adding a paragraph beside them, and take the timeout values from the profile or say why they are constants here.
- [x] S2 (judgment-gap), round 2: the store's rows name columns that do not exist: migration 0002 has no `seat_session.state`, no `seat_launch.status`, and no `seat_command.user`, `sender` or `payload_hash`; and they omit columns that are NOT NULL there (`seat_session.session_id`, `harness`, `decision`; `seat_launch.session_id`, `mode`, `decision`, `decided_by`, `command_id`, `node_name`, `spec_hash`, `binding_hash`, `seat_token_hash`; `seat_command.status`, `payload`, `requested_by`), so the insert the brief describes fails, and nothing says where the per-launch seat token whose hash `seat_launch` requires comes from. Fix: write the rows from the three tables as they are in `0002_seat_model.sql`, with the value of every NOT NULL column per command kind, and name the owner of the seat token.
- [x] S2 (judgment-gap), round 2: `ISeatCommandPort` returns results directly (`Task<LaunchReceipt>`, `Task<PaneCapture>`, `Task<DeliveryReceipt>`), but in the merged 13-3 design a command's result arrives as a `CommandResult` seat event that the actor maps and the writer records (13-3 R3 and R8), so the brief now has two sources for one outcome and does not say which wins or how a result that arrives only as an event completes a waiting `SeatCapture`. Fix: make the port send-only (accepted or not) and state how each waiting reply is completed from the committed event, or state the rule that reconciles the two.
- [x] S1 (context-gap), round 2: `SeatLaunch`, `LaunchReceipt`, `DeliveryReceipt`, `CommitReceipt` and `SeatCommandTransaction` have no properties; S1 still owns R1, the whole up decision, with `UP-reject` as its output; the outputs `UP-reject`, `UP-resume`, `DOWN-stop` and `CAPTURE` are still summaries; and R9 arms "quiet/activity watchdogs at 10 minutes" without saying which input (`QuietTimeoutFired`, `LaunchWatchdogFired`, `UnknownProlongedFired`) each timer sends, when it is armed and cancelled, and why the values are constants when `IHarnessStateProfile` carries them per harness. Fix: define the five records, give S1 a rule and output for the records alone, give each output its exact reply and state, and write the timer rule per input.

## Not checked

- The command messages and replies in the public surface match the actor protocol table of spec 0006 (read); the rules were not compared with spec 0006 R21 to R31 one by one, because the brief has to carry that text itself (`docs/briefs/TEMPLATE.md`).
- The 15-2 brief I compared with is the one I accepted at 6802c16; it says its bridge follows the merged 13-4 contract, so the fix can be on either side.
- Size and order were not judged: the stories cannot be sized until the rules say what is built.
- Nothing was built.

Architect, round 1: the diff 37d514f..65aea71 was read. Accepted: `CallerContext`, the command records and the dispatcher port now live in `Aiakos.Core` with that project in `paths`, the messages no longer carry the caller, `SeatAlreadyDown` exists, the outbound port and the store are named, and the capture, stop and delivery timeouts have values. Three points remain, written above. The seven original boxes are left as they are; the three new boxes say what is still open.

Architect, round 2: the diff fec47e0..f497030 was read. Accepted: `SeatCommandEnvelope(Guid TenantId, Guid SeatId, object Message)` carries the seat and no longer collides with the merged envelope; the send rejection reasons are exact; R8 and `RELOAD` use `SeatActorReloaded`; the prerequisites are named by what they deliver. Three points remain, written above, two of them about things the brief states that do not match main (the table columns, and how command results arrive). This was the second round, so the slice goes to lead with them. Read in the source: `0002_seat_model.sql` (`seat_session`, `seat_launch`, `seat_command`).


## Author resolution: full rewrite, 2026-10-07

Replaced the old rules, public surface, expected-output tables, item map and story blocks;
no old rule is kept beside a contradicting clarification. Read migration 0002, spec 0006
R21–R32, spec 0002 R45, merged 13-3 protocol/pure machine/reader/writer and 15-2's approved
bridge seam. Merged origin/main 8d06d44 into the analysis branch before rewriting.

- Original/round-1 seam findings: R1 uses Core.CallerContext and distinct Core.SeatCommandEnvelope
  with tenant/seat UUIDs; exact command/reply properties are published. S1 owns protocol/admission
  only; S6/R6 owns every up decision. The 15-2 author receives the precise namespace/name change.
- Original/round-1 persistence findings and round-2 schema finding: R2 publishes every transaction
  property/signature. R3 lists all real NOT NULL insert columns, initial nullable fields and
  desired/state/history updates; R4 lists dispatch statuses. No nonexistent columns remain.
  R5 owns per-launch token generation, hash-only row storage and send-only secret delivery.
- Original/round-1 outbound and round-2 two-source finding: R1 port sends a protobuf Command and
  returns SeatCommandDispatch(Sent) only. R5 builds launches, R10 builds deliveries through the
  actual adapter signatures. R7 completes capture/internal outcome waits only from committed
  CommandResult followed by metadata refresh. Public up/down/send admission replies remain
  compatible with 15-2; a send acknowledgement is never a final outcome.
- Original/round-1 exact-output/timer findings and round-2 undefined-record finding: all records
  have properties; output rows pin reply types/reasons, state, payload and persistence values.
  R13 names each input, arm/cancel deadline and profile-driven value. Fixed command/capture/stop
  policy is distinguished from ReadyTimeout/ConfirmTimeout/QuietTimeout profile values.
- Original/round-1 reload and prerequisite findings: R14 consumes SeatActorReloaded and the
  existing region ready handshake. Every story notes the prerequisite by type/title; material,
  relay and production node port are explicit integration prerequisites, with no dummy provider.

All boxes are author-resolved for fresh independent architect review, not an architect verdict.
Verification: tools/story.sh split/split-done/check: ok, 13 stories/51 items; git diff --check.
No implementation, build, gate, push or PR for this analysis was performed by the author.
