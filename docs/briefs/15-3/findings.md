# Story review: slice 15-3

Reviewed at commit 834945a. Stories: 12. Check: ok.

## Findings

- [x] S2 (context-gap): E2 expects the incompatible-version stderr with exit 4 and the `Seats: …` line, but S2's surface is `CliVersionPolicy.Compatible` (bool) and `CliSeatResolver.Resolve` (string), neither of which requests `/v1/version` or writes stderr, and R2 does not say whether each handler or the S10 wiring sends the single version GET, which decides the request sequence of every fake-HTTP test in S4–S9. Fix: name in R2 the public member that performs the version check and writes both messages, say which component calls it, and state how the ambiguous addresses leave `Resolve`.
- [x] S1 (judgment-gap): `PostAsync<TRequest,TResponse>` returns `CliApiReply<TResponse>` with one `TResponse`, but `up` answers 200 `AlreadyUpResponse` or 202 `AcceptedResponse` and `down` 200 `NoOpResponse` or 202 `CommandAcceptedResponse`, so the exact signature cannot carry what R1 describes. Fix: state the type arguments the callers use for `up` and `down`, or change the signature in the public surface to carry the two cases.
- [x] S1 (context-gap): R1 refers to "the exact 15-2 routes" and to problem responses without giving the paths of up, down, send, capture, registration and launch or the fields of the problem document, although the brief says implementers do not read other documents. Fix: add the route table and the problem JSON shape to R1.
- [x] S11 (context-gap): R12 prints `request_id` from `X-Request-Id`, but `CliApiException` in the exact surface has no member that carries it out of the client, and neither brief 15-2 nor spec 0007 defines that response header. Fix: add the member to the S1 surface and name the slice that sends the header, or remove `request_id` from R12 and E12.
- [x] S3 (context-gap): R3 gives exit codes and messages for `CliBodyReader.ReadAsync`, which returns `Task<string>`, without saying what it throws, and S3 depends on S1 with no tie named; the same holds for the R2 resolver failures. Fix: state in R3 and R2 that these throw `CliApiException` with the exact `Reason` and `Retryable` values, and name that tie in the notes of S3.
- [x] S6 (context-gap): R6 does not say whether `down` posts every stop before it polls or posts and polls seat by seat, nor whether the 60 s is per seat or for the command, although T6 records request order; R8 has the same gap for `up` and its 45 s. Fix: state the order of posts and polls and the scope of the deadline in R6 and R8.
- [x] S8 (context-gap): R8 prints the guidance note on every resume, while spec 0007 R40 prints it only when the projection hash differs from the one the conversation started with, so the brief changes the spec. Fix: add the matching edit of spec 0007 R40 to this analysis branch so that merging the analysis approves it.
- [x] S4 (context-gap): R4 rejects `ps --json --watch` with exit 2, while spec 0007 R48 lists `[--watch] [--json]` together without a restriction. Fix: add the matching edit of spec 0007 R48 to this analysis branch, or define the output of JSON watch in R4.
- [x] S8 (context-gap): the story holds the git source reader (three bounded processes, failure cases, a private repository in tests) and the whole `up` command (local validation, registration, launch polling, drift, guidance note, text and JSON), all in the single rule R8, which is more than one run of a Sonnet-level implementer. Fix: split R8/E8/T8 into a source-reader rule and an `up` rule and make them two stories, the second depending on the first.

## Not checked

- `bash tools/story.sh show 15-3 <n>` for each story: I read brief.md and stories.md directly.
- Field names of the 15-2 records that R4–R8 use (`Member`, `Kind`, `SpecDrift`, `ActivityDetail`, `WorstSeverity`, `OpenFindings`, `Context`, `Decision`, `OutcomeReason`): I confirmed the route list, the up/down/send/capture response types and `SeatRegistrationResponse.Drifted` in brief 15-2, nothing else.
- The 15-4 surfaces R11 names: I only confirmed that brief 15-4 mentions each name (`InstanceLayout`, `ConnectionStore`, `ReadDevAsync`, `IInstancePlatform`, `InstanceConfigurationJson`, `IInstanceCommands`, `Aiakos.Wsl.IWslProcessRunner`, `OtlpEndpoint`), not their signatures.
- Whether the 15-1 parser accepts every flag R4–R10 rely on (`--wide`, `--watch`, `--lines`, `--timeout`, `--write`, `--no-wait`).
- R7 `--wait turn`: whether the seat's activity can still read `idle` from before the turn at the moment the delivery is confirmed; that depends on the actor's ordering in slice 13-4, which I did not read.
- The exact rejection explanations in R9 against spec 0007's table beyond `SEAT_WORKING`.
- Existing code in src/Aiakos.Cli and src/Aiakos.Core/TmuxNames.cs.
- Sizes of S4, S9 and S10: large but I did not ask for a split.

## Author resolution, round 1

All nine findings above are addressed on this analysis branch; architect verification is pending.

1. R2/public surface now names CheckAsync: each handler calls it once, it owns the version GET and diagnostics; the application does not repeat it. Resolver failures carry a sorted immutable candidate snapshot through CliApiException, rendered by the handler boundary.
2. R1 specifies JsonElement response type arguments for up/down and status-based generated DTO deserialization, including no-content down requests.
3. R1 includes every route, request/response type and the exact problem JSON shape.
4. R12 drops request_id; verbose errors print only the root trace_id. No undocumented response header is required.
5. R2/R3 specify CliApiException reasons/retryability and S3 notes name its exception dependency on S1.
6. R6/R8 specify sequential POST-then-poll per seat and per-accepted-seat deadlines, starting at the POST response.
7. Spec 0007 R40 now agrees with the conservative guidance note on every resume; approval remains the analysis merge.
8. Spec 0007 R48 now agrees with local rejection of JSON watch before discovery.
9. Source metadata is R13/E13/T13 in S8; up remains R8/E8/T8 in S9, depending on S8. Later stories are renumbered.

Verification: split-done/check passed (12 stories, 44 items); all 12 story extracts generated; git diff --check passed. No implementation, build, acceptance tests or live instance/Windows checks were run. The architect's original unchecked areas remain unchecked here except existing CLI/Core source inspection; this amendment does not claim API/neighbor-slice integration verification.

## Architect, round 2

The diff 344a3b6..834945a was read: brief.md, items.tsv, stories.md and spec 0007 R40 and R48. Eight of the nine resolutions are accepted as written: the response union and the route table (checked against brief 15-2's route list and its `UpRequest`, `SendRequest`, `ProblemResponse` and `CaptureTimeoutResponse` records), `request_id` removed, the exception reasons of R3, the sequential order and per-seat deadlines of R6 and R8, both spec edits, and the split into S8 (R13) and S9 (R8). The first resolution leaves one gap, below.

- [x] S2 (context-gap): R2 and G2 say "the command boundary" returns the exit code and "the handler boundary" writes `Seat address is ambiguous.` and the `Seats:` line, but no rule says whether each handler's `RunAsync` catches `CliApiException` (writes stderr, returns the exit code) or lets it reach the S11 wiring, and no S2 type renders those two lines although E2 expects them, so the tests of S2 and of every handler story (S4–S7, S9, S10) cannot know whether to expect a return value or an exception. Fix: state once in G2 which component is the boundary, and give the member that renders a `CliApiException` to stderr (including the `Seats:` line) an exact name in the public surface, owned by S2.

Not checked in round 2: `story.sh show` output beyond story 8's item list; everything listed under "Not checked" above still stands.

## Author resolution, round 2

G2 now names each operational handler RunAsync as its own exception/cancellation boundary; lower-level components propagate, and S11 renders only failures before handler dispatch. R2 and the exact public surface name S2-owned CliCommandRendering.WriteError(CliApiException,TextWriter), including ambiguous-address lines and suppression of the already-written incompatible-version diagnostic. S2 tests call the renderer directly after catching component exceptions; handler tests expect an exit code and stderr. The split and item ownership are unchanged. Architect verification is pending, as requested in the round-2 handoff.

Verification: story check and S2/S11 extracts plus git diff --check are recorded in the handoff. No implementation, build, acceptance tests or live instance/Windows checks were run.
