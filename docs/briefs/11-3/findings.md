# Story review: slice 11-3

Reviewed at commit 1cf2bff. Stories: 5. Check: ok.

## Findings

- [ ] S1 (judgment-gap): E2's mutation half (read-only or mismatched handle -> NotFound, no later pane mutation) cannot be observed in S1, which has no public mutating method and no named helper, so its acceptance tests cannot be written first. Fix: split R2/E2 so the mutation-verification part is owned by S2, or give the helper an exact name and signature in the brief.
- [ ] S2 (judgment-gap): E6 expects a competing `SendKeysAsync` to throw Busy, but that method arrives in S3 (R12), so an S2 output depends on a later story. Fix: move the keys clause of E6 to E12.
- [ ] S4 (context-gap): R13 says to reuse the existing child tmux spans/counters, but those are 11-2 R22 (story S8) and are not in `src/Aiakos.Node` at 5bb5ce3 (only `NodeTelemetry.Reconnects` and `node-link.receive` exist). Fix: name 11-2 S8 as a merge prerequisite in S4's notes, as S5 does for the starter.
- [ ] S2 (judgment-gap): R10 makes an already-cancelled request throw OperationCanceledException with no work, while the 11-1 contract test `ReportsTheBufferLoadedStageForAnAlreadyCancelledRequest` (`tests/Aiakos.Node.Testing/SessionHostContractTests.cs:266`) expects a returned report with stage BufferLoaded, and 11-5 runs that suite on this component. Fix: align R10/E10 with the contract, or add a `C` item that changes the contract test and the fake.
- [ ] S2 (context-gap): R10 gives no answer for `CancelDelivery` (as opposed to caller cancellation) arriving after the gate is taken and before the load has succeeded. Fix: state the result for that order of events in R10 and E10.
- [ ] S1 (context-gap): R3 rejects a cursor outside the size as NotFound, but tmux reports `cursor_x` equal to `pane_width` after a character is written in the last column (pending wrap), so a live pane with a full-width line would be "not found" (from tmux behaviour as I know it; not run). Fix: state the bound as 0..width inclusive for x, or say what an out-of-range cursor returns instead of NotFound, and add the case to E3.
- [ ] S3 (context-gap): R11 does not say what a failed `ResubmitAsync` (identity mismatch or tmux failure) throws to the confirmer, nor what the report is when the confirmer throws OperationCanceledException while the delivery token is not cancelled. Fix: give both exact results in R11 and E11.
- [ ] S3 (context-gap): R12 does not say whether an empty key list takes the gate, so its result while a delivery holds the gate (Busy or no-op) is the implementer's choice. Fix: state it in R12 and E12.

## Not checked

- Story sizes beyond a reading: S1 (runner tail retention plus capture) looked feasible in one run and no split is asked for.
- Whether the fake's stage promotion for an empty lead (it always reaches LeadTyped) conflicts with R8 in any contract test.
- That `pane_pid` and `@aiakos-launch` are still reported for a dead pane under remain-on-exit; R15 has no real dead-pane capture.
- Whether tmux `load-buffer` with empty stdin exits 0 and creates no buffer.
- How the gate runs S5's opt-in tests (`AIAKOS_TEST_TMUX=1`), and the state of 11-2 stories S6 and S8 (no issue found in a 20-row search; 11-2-5 is open and `blocked`).
- Spec 0004 and the ADRs were not read; nothing was built or run.
