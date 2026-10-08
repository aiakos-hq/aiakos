# Story review: slice 15-3

Reviewed at commit 2b8b5c8deed350bf774d352907223f6125fe9a34. Stories: 11. Check: ok.

## Findings

- [ ] S2 (context-gap): E2 expects the incompatible-version stderr with exit 4 and the `Seats: …` line, but S2's surface is `CliVersionPolicy.Compatible` (bool) and `CliSeatResolver.Resolve` (string), neither of which requests `/v1/version` or writes stderr, and R2 does not say whether each handler or the S10 wiring sends the single version GET, which decides the request sequence of every fake-HTTP test in S4–S9. Fix: name in R2 the public member that performs the version check and writes both messages, say which component calls it, and state how the ambiguous addresses leave `Resolve`.
- [ ] S1 (judgment-gap): `PostAsync<TRequest,TResponse>` returns `CliApiReply<TResponse>` with one `TResponse`, but `up` answers 200 `AlreadyUpResponse` or 202 `AcceptedResponse` and `down` 200 `NoOpResponse` or 202 `CommandAcceptedResponse`, so the exact signature cannot carry what R1 describes. Fix: state the type arguments the callers use for `up` and `down`, or change the signature in the public surface to carry the two cases.
- [ ] S1 (context-gap): R1 refers to "the exact 15-2 routes" and to problem responses without giving the paths of up, down, send, capture, registration and launch or the fields of the problem document, although the brief says implementers do not read other documents. Fix: add the route table and the problem JSON shape to R1.
- [ ] S11 (context-gap): R12 prints `request_id` from `X-Request-Id`, but `CliApiException` in the exact surface has no member that carries it out of the client, and neither brief 15-2 nor spec 0007 defines that response header. Fix: add the member to the S1 surface and name the slice that sends the header, or remove `request_id` from R12 and E12.
- [ ] S3 (context-gap): R3 gives exit codes and messages for `CliBodyReader.ReadAsync`, which returns `Task<string>`, without saying what it throws, and S3 depends on S1 with no tie named; the same holds for the R2 resolver failures. Fix: state in R3 and R2 that these throw `CliApiException` with the exact `Reason` and `Retryable` values, and name that tie in the notes of S3.
- [ ] S6 (context-gap): R6 does not say whether `down` posts every stop before it polls or posts and polls seat by seat, nor whether the 60 s is per seat or for the command, although T6 records request order; R8 has the same gap for `up` and its 45 s. Fix: state the order of posts and polls and the scope of the deadline in R6 and R8.
- [ ] S8 (context-gap): R8 prints the guidance note on every resume, while spec 0007 R40 prints it only when the projection hash differs from the one the conversation started with, so the brief changes the spec. Fix: add the matching edit of spec 0007 R40 to this analysis branch so that merging the analysis approves it.
- [ ] S4 (context-gap): R4 rejects `ps --json --watch` with exit 2, while spec 0007 R48 lists `[--watch] [--json]` together without a restriction. Fix: add the matching edit of spec 0007 R48 to this analysis branch, or define the output of JSON watch in R4.
- [ ] S8 (context-gap): the story holds the git source reader (three bounded processes, failure cases, a private repository in tests) and the whole `up` command (local validation, registration, launch polling, drift, guidance note, text and JSON), all in the single rule R8, which is more than one run of a Sonnet-level implementer. Fix: split R8/E8/T8 into a source-reader rule and an `up` rule and make them two stories, the second depending on the first.

## Not checked

- `bash tools/story.sh show 15-3 <n>` for each story: I read brief.md and stories.md directly.
- Field names of the 15-2 records that R4–R8 use (`Member`, `Kind`, `SpecDrift`, `ActivityDetail`, `WorstSeverity`, `OpenFindings`, `Context`, `Decision`, `OutcomeReason`): I confirmed the route list, the up/down/send/capture response types and `SeatRegistrationResponse.Drifted` in brief 15-2, nothing else.
- The 15-4 surfaces R11 names: I only confirmed that brief 15-4 mentions each name (`InstanceLayout`, `ConnectionStore`, `ReadDevAsync`, `IInstancePlatform`, `InstanceConfigurationJson`, `IInstanceCommands`, `Aiakos.Wsl.IWslProcessRunner`, `OtlpEndpoint`), not their signatures.
- Whether the 15-1 parser accepts every flag R4–R10 rely on (`--wide`, `--watch`, `--lines`, `--timeout`, `--write`, `--no-wait`).
- R7 `--wait turn`: whether the seat's activity can still read `idle` from before the turn at the moment the delivery is confirmed; that depends on the actor's ordering in slice 13-4, which I did not read.
- The exact rejection explanations in R9 against spec 0007's table beyond `SEAT_WORKING`.
- Existing code in src/Aiakos.Cli and src/Aiakos.Core/TmuxNames.cs.
- Sizes of S4, S9 and S10: large but I did not ask for a split.
