# Story review: slice 11-2

Reviewed at commit d7fd9a6. Stories: 8. Check: ok.

## Findings

- [x] S6 (judgment-gap): after a harness exits at once, R14 leaves a labelled dead pane and a `starting` entry with null handle fields, and R19 then answers every later start with `NotFound` because that entry cannot match SessionId/PaneId/PanePid, so the seat cannot be started again in this slice although R14's error says Retryable=true and spec 0004 R13/R32 treat a `starting` entry matched by session name as managed and replace the dead pane. Fix: give R19 and E19 the exact result for a dead target whose entry is `starting` with the same launch label (removal, and whether `PaneExited` is published and with which PaneStartTime), and ask `lead` if the start time of a never-identified pane needs a maintainer decision.
- [x] S6 (judgment-gap): R19 writes `Reported=true` before calling `publish`, so when the callback throws the start fails with the pane kept, and the retry finds `Reported=true`, publishes nothing and removes the pane: the exit is never reported. Fix: state in R19 and E19 that a failed publication leaves or restores `Reported=false` (or that the flag is written only after `publish` returns).
- [x] S3 (judgment-gap): R9 and E9 fix camelCase property names for the registry file, while spec 0004 "Registry entry" gives snake_case (`seat_id`, `pane_start_time`, `observed_at`) for this durable format that 11-5 reconciliation reads. Fix: change R9 and E9 to the spec's snake_case names, listed exactly.
- [x] S2 (context-gap): R8 does not say whether the six `set-option -g` calls are issued when `show-environment -g` answers no-server, and a scripted runner needs one result or seven. Fix: state in R8 and E8 the exact command sequence for the no-server case.
- [x] S5 (context-gap): R17 says "immediately before any post-creation mutation" but E16 lists only the seven mutations, so an implementer can verify once or seven times and the scripted command sequence differs. Fix: state in R16/R17 and E16 that `display-message` runs before each of the seven commands (or once), with the full command order.
- [x] S5 (judgment-gap): R17 and E17 (owned by S5) require the forbidden-character rejection "at StartAsync's preflight", and E6 (owned by S2) expects "subsequent start throws Unavailable", but `StartAsync` exists only in S6; R17 also does not name which spec fields are "opaque labels". Fix: move the forbidden-character clause and its E17 variant to R18/E18 with the fields named, and word E6 for `RunAsync`.
- [x] S2 (judgment-gap): R21 (owned by S6) makes `TmuxNames` reject an invalid instance/home with `ArgumentException`, which is behaviour of `SocketName` and `ConfigPath` that S2 introduces under R5. Fix: move that sentence to R5/E5 so S6 does not change S2's helpers.
- [x] S2 (context-gap): R5 says options errors throw `ArgumentException("Invalid tmux host options.")` but not whether the constructor or `InitializeAsync` throws, and R7 says "Options/config write failures yield unavailable", which reads as the opposite for options. Fix: name the throwing member in R5 and remove "Options/" from R7.
- [x] S6 (judgment-gap): S6 owns six rules over the start lock and listing, dead-pane replacement, registry transitions, attach commands and telemetry added to both the starter and S2's client, plus real-tmux tests and the integrated E11/E13/E14 assertions, which is more than one run for a Sonnet-level implementer. Fix: move R21/E21 and R22/E22 into their own story or stories that depend on S6, with T6 divided to match.
- [x] S1 (context-gap): R23 reclassifies only a "broken-pipe/EPIPE" failure and says other IO failures are not reclassified, but .NET gives the stdin write, flush and close one `IOException` type on every OS, so the implementer must choose how to tell them apart (message text, HResult, errno) and what the other failures return. Fix: state in R23 either that any `IOException` from writing, flushing or closing stdin of a started child is this case, or the exact test on Linux and Windows and the result for the rest.

## Not checked

- The immediate-exit boundary the author asked about: failing R14 without a guessed start time is consistent with rule 3; the first finding is about what follows it, not about R14 itself.
- tmux behaviour was not run: the R7 semicolon escape, `-F` output with TAB under `-u`, and the no-server stderr texts in R4 were read, not tried.
- Acceptance tests are written before the code, so variants that need an internal seam (unsupported OS in E5, crash before rename in E9) can only be held by the permanent T items; I did not check that this is enough.
- S2 (six rules, six outputs) was judged to fit one run; no split was asked for.
- 11-1 code was read only for the record shapes in `SessionHost.cs` and `TmuxNames.cs`.
- A dead pane whose labels were only partly written (a label command failed, then the harness died) still gets `NotFound` from R18 and waits for 11-5 reconciliation; spec 0004 R32 would call it managed by its `starting` entry. Not raised: it needs two failures and nothing is removed or lost.
- Whether `tools/story.sh analysis-pr` accepts the spec 0004 edit that this branch carries beside the brief.

Author revision: findings 2–9 addressed in R5–R9/R16–R19/E5–E9/E16–E19 and T6–T8, with attach and diagnostics separated from lifecycle. The initially pending finding 1 was subsequently resolved by the maintainer decision recorded below. No unknown identity value was invented.

Finding 1 resolution: maintainer decision 2026-10-06 via lead qitem-20261005215420-064ab307 narrows
spec R13. R19/E19 replace a verified matching dead starting launch with no returned handle without
an event/guessed start time; registry recovery sidecar preserves full prior identity and exit
until replacement running is committed. Spec R13 and Changes after acceptance amended in the
same analysis. R9/E9 include the sidecar's persistent format/API; T3/T6 guard it.

Architect, round 1: the diff 9e04218..ff264aa was read, including the spec 0004 R13 amendment, and the nine resolutions are accepted; no findings are open. The maintainer decision was read in lead's qitem-20261005215420-064ab307 and the brief and spec text match it.

Architect, amendment for #181: the diff e434e52..11aeefa (R23, E23, T9 on S1; status and index lines) was read; stories S2 to S8 are unchanged. One finding above is open.

Author amendment resolution: R23 now classifies any IOException from started-child stdin write, flush or close as early closure, using exception type only; no platform-specific message/HResult/errno decision remains. E23 states the same classification. stdout/stderr and process-start errors remain outside this rule, and R2 timeout/cancellation precedence is unchanged.

Architect, amendment round 1: the resolution was read at 65a6442 and is accepted; no findings are open.

Architect, re-cut: the branch was rebuilt on main as d7fd9a6; its brief, items and stories are byte-identical to 65a6442 and it differs from main only in these four files. No findings are open.
