# Story review: slice 11-4

Reviewed at commit 32f262b. Stories: 8. Check: ok.

## Findings

- [x] S5 (judgment-gap): R10 rescans only "before each signal round", and R1 adds session members only while the matching root is present, so a child the harness forks during Grace (default 10 s) and leaves behind when it exits on SIGTERM is reparented, is in no snapshot taken while the root lived, and is never signalled; spec 0004 R25 step 5 says processes forked during the grace period are included. Fix: in R10 take a snapshot and Expand on every grace poll, retaining the identities, and add that case to E10 and T5 (root has no child at stop, forks one during Grace, exits on TERM: the child is signalled and gone).
- [x] S4 (judgment-gap): R8 recovers the socket only for a server identity remembered from an earlier successful poll, so a watcher that starts after the socket file was deleted (node restart) publishes SessionVanished for panes whose roots are alive; spec 0004 R30 sends SIGUSR1 whenever a tmux server for this socket name still exists. Fix: either follow R30 for the unremembered case, or state in R8 and E8 that with no remembered server and a registered root identity still alive no vanish is published (degraded instead), and name the narrowing of R30 as an item so the maintainer sees it.
- [x] S4 (context-gap): R7 does not say whether a tick goes on after one ObserveAsync fails, so one handle with a mismatching registry (R5 NotFound on every tick) can keep every later handle of the tick from ever getting its exit or vanish event. Fix: R7 states that the remaining observations of the tick are still observed, the tick counts as degraded, and E7/T4 get a two-handle case.
- [x] S3 (context-gap): R5 names "a matching complete registry handle (or no entry for a read-only handle)" and "a mismatching registry", but not a handle with ReadOnly=false and no registry entry (file deleted), for Exited. Fix: say whether that is NotFound without event or the in-memory path of a read-only handle, and add it to E5.
- [x] S4 (context-gap): R8 does not say on which ticks GetServerAsync runs (every successful poll, or only while no identity is remembered), while T4 asserts complete argv with no unexpected command. Fix: one sentence in R8 that fixes when the `display-message -p "#{pid}"` call is made.
- [x] S4 (context-gap): R7 "TickAsync never overlaps another tick" does not say what a second concurrent call does (waits and then runs, or returns without polling). Fix: say which in R7.
- [x] S5 (judgment-gap): the story is the whole stop state machine (R9–R12: validation, three grace paths, the partial-report boundary, leftovers, dead cleanup with reverify) and a T5 of about 25 cases; that is more than one run of a Sonnet-level implementer, and it splits. Fix: split into the not-running path (R9, R12, E9, E12, their part of T5) and the live path (R10, R11, E10, E11, the rest of T5) that depends on it.
- [x] S6 (judgment-gap): the story joins the starter change C1/T6 with the whole real-tmux proof T7 (five scenarios, AC7/8/10/16), where defects of the merged S1–S5 first show, and it has one retry. Fix: split T7 into a story of its own after the C1 story, or, if a story may not own only a test item, move the watcher and socket scenarios of T7 that need no starter lock to it with the rule they prove.
- [x] S5 (context-gap): round 2. The story owns E9/E12/T5 but delivers only "internal helpers" with no name or signature, and public StopAsync arrives in S6, so the acceptance tests (written first, from the story text alone) have no member to call; R9 also says "acquire the lifecycle name lease" while T5 says the helper is "called under an existing lifecycle lease" and still asserts "name-lock release on every exit". Fix: spell the internal member(s) S5 delivers in "Public surface" (exact name, parameters, return type, and whether the caller or the helper holds the lease), and move the lease acquisition of R9 and the lock-release case of T5 to whichever story owns it.
- [x] S2 (context-gap): round 2. T2 tests GetServerAsync ("exact #{pid} command"), but its argv (`display-message,-p,"#{pid}"`), the match of that positive PID in a same-user snapshot and the null result are stated only in R8, which S4 owns and `story.sh show 11-4 2` does not print. Fix: move that sentence into a rule S2 owns (R4 or a new rule), say what a failed or unparsable invocation returns, and leave in R8 only when the watcher calls it.

## For the maintainer

- C2 narrows spec 0004 R30 (a socket deleted before the watcher first runs is not recovered; an unremembered server with a live root stays degraded, with no SIGUSR1 and no vanish). The brief states it as a proposal. It is a change to a rule of a spec: merging this analysis approves it, and spec 0004 R30 then needs the amendment.
  Decided 2026-10-08 by the maintainer (through lead, qitem-20261008202417-2b4d075f20be4d9c): the narrowing is accepted. Spec 0004 R30 still needs the amendment; this slice does not write it.

## Not checked

- The 11-2 starter (11-2-5, 11-2-6) does not exist on this baseline; C1 was read against the brief's description of it only.
- The R3/R4 format strings and `display-message -p "#{pid}"` against a real tmux 3.4 or 3.6.
- Zombies: ProcessSnapshot lists a zombie with its PID and start time, so R10/R11 count an unreaped tracked process as a survivor; not tried.
- R11's "Killed with a non-null Error" for a partial stop: accepted as consistent with spec 0004 ("report instead of throwing once the pane has changed"); how 11-5 and the proto map it was not read.
- Whether the gate runs S6 with AIAKOS_TEST_TMUX=1.
- No build and no test was run; this is a review of documents.
- Round 2: the six other round-one findings were read against the changed R5, R7, R8, R10, E5, E7, E8, E10, T3, T4, T8 and the S5–S8 blocks only; the unchanged rules were not read again line by line.
- Round 2: whether R7 logs the degraded Warning on every failed tick or once per episode, and whether a failed GetServerAsync marks the tick degraded, are left to "choose the simplest".
- Round 2: the brief says the branch starts at 28902f7; the worktree also holds 05793c2 (11-3-2). Not checked whether that changes anything S1–S6 read.
- Round 3: only the two round-two findings were read, against the changed Public surface, R4, R8, R9, R10, R12, T2, T5, T8, the S5 and S6 blocks and `story.sh show 11-4 2` and `5`; both are accepted. Nothing else was read again.
- Round 3: what the watcher does with its remembered identity when GetServerAsync returns null or throws on a reachable poll is not stated in R8 and no expected output depends on it; left to "choose the simplest".
- Round 3: R12's "internal non-locking helper" for publishing under the held lease is not a named member; whether S3's merged SessionLifecycle will offer one or S5 adds it was not checked.

## Author resolution, round 1

- Grace polls snapshot/Expand before status; E10/T8 cover a child discovered while the root lives and retained after root exit. Between-snapshot detached forks remain the stated limit.
- C2 explicitly proposes the R30 narrowing for maintainer approval. R8/E8/T4/T7 defer vanish for an unremembered server with a matching live root, without guessing a server to signal.
- R7/E7/T4 continue after individual observation failure and mark the tick degraded; concurrent TickAsync waits then runs.
- R5/E5/T3 fix writable/no-entry Exited as NotFound without event.
- R8/E8/T4 fix GetServerAsync to once per reachable poll containing Alive, before terminal observation.
- S5 owns shared validation and internal not-running cleanup helpers; S6 adds the public live state machine. T5/T8 split along that boundary. No temporary Alive stub is requested.
- S7 owns starter wiring; S8 owns explicit real-verification requirement R13 and T7.

Verification: story.sh split-done and check pass (8 stories, 38 items); git diff --check clean.
No build, unit test, real tmux run, or acceptance trial read. Architect must re-review; C2 is not an approved spec change.


## Author resolution, round 2

- S5's Public surface now names ValidateRequest, ObserveUnderLeaseAsync and CleanupUnderLeaseAsync
  with exact parameters/return types and behavior. ValidateRequest takes no lease and does no IO;
  both UnderLease helpers require, retain and never reacquire the caller-held lease. S5's T5
  calls them directly through the existing friend assembly. R10 (S6) now owns public StopAsync's
  lease acquisition/disposal; the release-on-every-exit case stays in S6's T8.
- R4 (S2) owns GetServerAsync's exact argv, positive PID and same-user snapshot match, null cases,
  fixed propagated errors and cancellation. R8 keeps watcher scheduling and remembered identity.
  T2 explicitly covers the R4 cases, visible in S2's generated story.
- C2 and its maintainer proposal are unchanged. The architect must move Reviewed at commit after
  reading this resolution; this author has not approved the review or the spec narrowing.

Verification: story.sh check passes (8 stories, 38 items); generated S2/S5/S6 story text
contains the moved rules and named helpers; git diff --check is clean.
No build, unit test, real tmux run, or acceptance trial read.
