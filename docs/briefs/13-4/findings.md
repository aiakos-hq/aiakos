# Story review: slice 13-4

Reviewed at commit f573054. Stories: 6. Check: ok.

## Findings

Resolution commit: f573054 follow-up. The public seam, persistence owner, outbound port,
timeouts, reload signal, watchdog ownership, and named prerequisites are now stated in the brief;
all seven findings are addressed for architect re-review.

- [ ] S1 (judgment-gap): the seam here and the seam the 15-2 brief was reviewed against disagree: 15-2 expects `Aiakos.Core.CallerContext`, an `ISeatCommandDispatcher.DispatchAsync(SeatEnvelope, CallerContext, CancellationToken)` and messages without a caller (`SeatUp(bool Fresh, string? Note)`, `SeatSend(string Body, bool Force)`), while this brief puts `CallerContext` in `Aiakos.Orchestrator.Seats` (its paths do not include `src/Aiakos.Core/`), carries the caller inside each message, has `SeatSend(Lead, Body, ExpectConfirmation, Force, Caller)`, names no dispatcher type at all, and has no reply for a `SeatDown` that is a no-op. Fix: write the one seam in the public surface (the project and namespace of `CallerContext`, the type and member the API calls, every reply including the no-op) and tell the 15-2 author which of its statements change.
- [ ] S2 (judgment-gap): R2 persists "session, launch and command metadata atomically through the existing committed input seam", but 13-3's `ISeatInputCommitter.ApplyAsync` accepts six inputs and rejects the rest with `SEAT_INPUT_NOT_SUPPORTED`, and 13-3's writer never creates a command, launch or session row; this brief also says it "does not own command creation" while R2 and R6 create commands. Fix: state which component creates those rows and through which named member, with the rows and columns written for up, down, send and capture, and remove the sentence that disowns command creation.
- [ ] S2 (context-gap): every rule "dispatches" StartSeat, StopSeat, CapturePane or a delivery, but no outbound port is named and none exists on main (node command sending is 10-5), and nothing says who builds the launch and delivery content (12-1's `BuildLaunch` and `BuildDelivery`, the native session id, the lead that `SeatSend` already carries). Fix: name the outbound port with its members as an external prerequisite or as part of this slice, and say which component calls the harness adapter and when.
- [ ] S1 (context-gap): the rules give no exact text: R5's "fixed reasons" are not listed, R8 and R9 say "the specified reason" and "the specified structured log/metric/span names" without specifying them, the timeouts of capture, delivery and stop have no values, and the expected outputs are summaries (`fixed rejection`, `fixed timeout reply`). Fix: list every rejection reason with its condition, every timeout value, every log, metric and span name with its tags, and give each output its exact reply or state.
- [ ] S5 (judgment-gap): R8 and `RELOAD` emit `SeatActorRestarted` "before any retained work is released", but 13-3 publishes `SeatActorReloaded`, and it already does so before queued work; the rule also restates "the 13-3 lifecycle contract" without saying what this slice adds to it. Fix: use 13-3's name and state only the behaviour this slice adds (which overlays and capture requests it issues, on which inputs).
- [ ] S1 (judgment-gap): S1's goal is to publish the records, but it owns R1, the whole `SeatUp` decision, and its output is `UP-reject`, while S2 "Up lifecycle dispatcher" owns only R2; and 13-3 left automatic timer scheduling (quiet timeout, launch watchdog, unknown prolonged) to this slice, which no rule here mentions. Fix: give S1 a rule and an output for the records alone, move the up decision to S2, and add the timer rule with its story or say which slice owns it.
- [ ] S2 (context-gap): the story notes name prerequisites by 13-3's old story numbers ("13-3 S5/S6 persistence stages", "S7 region retry", "S7/S8", "S9"), which do not say what must be merged, and the brief says those stories "are not all on main yet". Fix: name each prerequisite by its 13-3 story title or the type it delivers, per story.

## Not checked

- The command messages and replies in the public surface match the actor protocol table of spec 0006 (read); the rules were not compared with spec 0006 R21 to R31 one by one, because the brief has to carry that text itself (`docs/briefs/TEMPLATE.md`).
- The 15-2 brief I compared with is the one I accepted at 6802c16; it says its bridge follows the merged 13-4 contract, so the fix can be on either side.
- Size and order were not judged: the stories cannot be sized until the rules say what is built.
- Nothing was built.
