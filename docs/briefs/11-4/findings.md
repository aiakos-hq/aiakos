# Story review: slice 11-4

Reviewed at commit d9d1d81. Stories: 6. Check: ok.

## Findings

- [ ] S5 (judgment-gap): R10 rescans only "before each signal round", and R1 adds session members only while the matching root is present, so a child the harness forks during Grace (default 10 s) and leaves behind when it exits on SIGTERM is reparented, is in no snapshot taken while the root lived, and is never signalled; spec 0004 R25 step 5 says processes forked during the grace period are included. Fix: in R10 take a snapshot and Expand on every grace poll, retaining the identities, and add that case to E10 and T5 (root has no child at stop, forks one during Grace, exits on TERM: the child is signalled and gone).
- [ ] S4 (judgment-gap): R8 recovers the socket only for a server identity remembered from an earlier successful poll, so a watcher that starts after the socket file was deleted (node restart) publishes SessionVanished for panes whose roots are alive; spec 0004 R30 sends SIGUSR1 whenever a tmux server for this socket name still exists. Fix: either follow R30 for the unremembered case, or state in R8 and E8 that with no remembered server and a registered root identity still alive no vanish is published (degraded instead), and name the narrowing of R30 as an item so the maintainer sees it.
- [ ] S4 (context-gap): R7 does not say whether a tick goes on after one ObserveAsync fails, so one handle with a mismatching registry (R5 NotFound on every tick) can keep every later handle of the tick from ever getting its exit or vanish event. Fix: R7 states that the remaining observations of the tick are still observed, the tick counts as degraded, and E7/T4 get a two-handle case.
- [ ] S3 (context-gap): R5 names "a matching complete registry handle (or no entry for a read-only handle)" and "a mismatching registry", but not a handle with ReadOnly=false and no registry entry (file deleted), for Exited. Fix: say whether that is NotFound without event or the in-memory path of a read-only handle, and add it to E5.
- [ ] S4 (context-gap): R8 does not say on which ticks GetServerAsync runs (every successful poll, or only while no identity is remembered), while T4 asserts complete argv with no unexpected command. Fix: one sentence in R8 that fixes when the `display-message -p "#{pid}"` call is made.
- [ ] S4 (context-gap): R7 "TickAsync never overlaps another tick" does not say what a second concurrent call does (waits and then runs, or returns without polling). Fix: say which in R7.
- [ ] S5 (judgment-gap): the story is the whole stop state machine (R9–R12: validation, three grace paths, the partial-report boundary, leftovers, dead cleanup with reverify) and a T5 of about 25 cases; that is more than one run of a Sonnet-level implementer, and it splits. Fix: split into the not-running path (R9, R12, E9, E12, their part of T5) and the live path (R10, R11, E10, E11, the rest of T5) that depends on it.
- [ ] S6 (judgment-gap): the story joins the starter change C1/T6 with the whole real-tmux proof T7 (five scenarios, AC7/8/10/16), where defects of the merged S1–S5 first show, and it has one retry. Fix: split T7 into a story of its own after the C1 story, or, if a story may not own only a test item, move the watcher and socket scenarios of T7 that need no starter lock to it with the rule they prove.

## Not checked

- The 11-2 starter (11-2-5, 11-2-6) does not exist on this baseline; C1 was read against the brief's description of it only.
- The R3/R4 format strings and `display-message -p "#{pid}"` against a real tmux 3.4 or 3.6.
- Zombies: ProcessSnapshot lists a zombie with its PID and start time, so R10/R11 count an unreaped tracked process as a survivor; not tried.
- R11's "Killed with a non-null Error" for a partial stop: accepted as consistent with spec 0004 ("report instead of throwing once the pane has changed"); how 11-5 and the proto map it was not read.
- Whether the gate runs S6 with AIAKOS_TEST_TMUX=1.
- No build and no test was run; this is a review of documents.
