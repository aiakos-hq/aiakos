# Story review: slice 10-3

Reviewed at commit 0b36217. Stories: 6. Check: ok.

## Findings

- [x] S2 (context-gap): `INodeLinkApplication` (S2) takes `NodeIdentity`, the type that `NodeTokenRegistry.Authenticate` (S1) returns, and S1 and S2 do not depend on each other, so two parallel runs both create it. Fix: add S1 to the `depends` of S2, or say in the `notes` of both which story creates `NodeIdentity`.
- [x] S5 (context-gap): the S5 note tells the implementer to rewrite the health-based tests in `OrchestratorConnectionTests.cs`, but the change item that allows it (C1) and its output (E13) are owned by S6 and are not in the S5 story text, and after S5 the E13 test already passes on `main`, so S6 has no failing baseline for it. Fix: move C1 and E13 to S5.
- [x] S3 (context-gap): the S3 note "timer/replacement messages must remain processable so a blocked callback cannot stall supersession or liveness" is a rule that exists only in `stories.md`, has no expected output, and leaves open whether `StateChangedAsync` may run while `ReceiveAsync` is still awaited. Fix: move it into R7 with an E7 case (application blocked on one event, then the liveness timeout and a superseding stream), or delete it.
- [x] S3 (context-gap): R4 to R7 put authentication, the handshake, atomic replacement, the single writer, host shutdown, the liveness timer and back-pressure of callbacks into one run, with about 25 cases in E4 to E7; that is more than one concern and more than a Sonnet-level run. Fix: split into a handshake story (R4, R5, E4, E5) and an ownership and liveness story (R6, R7, E6, E7) that depends on it.
- [x] S4 (context-gap): R8 does not say whether the stored ceiling is bounded: "advances independently of error category" read literally doubles without limit, which overflows `TimeSpan` after about 41 consecutive failures (0.5 s × 2^41), and the sequence "ten `Unavailable`, then one `Unauthenticated`" draws from 60 s or from 300 s depending on the reading. Fix: state in R8 the bound of the stored ceiling after doubling (for example 300 s) and add that mixed sequence to E8.
- [x] S6 (context-gap): the tracing half of R12 changes the send and receive loops that S3 and S5 build, and names neither the `ActivitySource` nor the activity, so the E12 test, which is written first, cannot know what to listen for. Fix: make tracing its own rule with the source and activity names for both sides, in its own story that depends on the server and node stream stories, and leave registration, limits and keepalive in R12.
- [x] S6 (context-gap): the startup half of R3, which S6 activates, has no expected output (E3 tests only `IsAllowed` and `Validate`; E12 and E13 start only loopback hosts), and R3 does not say from where and when the addresses are read (configured URLs before binding, or bound addresses after start). Fix: state the source and the moment in R3, and add an S6 output: Kestrel with `http://0.0.0.0:<port>` fails to start with the fixed error, and a `TestServer` host starts.
- [x] S5 (context-gap): E10 "three auth errors then success … counter increases once" contradicts R10 "preserve reconnect counter behavior" when the success is the first connection of that `OrchestratorConnection`: today the counter moves only after an earlier `Connected` (`OrchestratorConnection.cs:78`). Fix: say in E10 that the connection was `Connected` before the three errors, or give the count for a first connection as zero.
- [x] S5 (context-gap): R10 says the heartbeat carries `SentAt` from `TimeProvider` and R11 (S2) describes the default `CreateHeartbeat` without it, so neither story is told to set it and neither E10 nor E11 checks it. Fix: say in R10 that the connection sets `SentAt` on the message the source returns, and name it in E10.

## Not checked

- Conformance of the brief to spec 0002 beyond its rules R6 to R11 and the stream status table; the ADRs were not read.
- That the address Aspire passes for the `grpc` endpoint in `ASPNETCORE_URLS` passes R3 (expected `http://localhost:<port>`); the dev stack was not run.
- That the fake clock the tests need can be written without a new package (no test project references a fake `TimeProvider`, and the `.csproj` files are outside `paths`).
- The WSL end-to-end test (`AIAKOS_E2E_WSL`): between the merge of the node stream story (now S6) and of production registration (now S7) the node on `main` cannot connect to the orchestrator on `main`; not run.
- `artifacts/trials/` (acceptance tests), by role.

## Author resolution, round 1

1. S2 depends on S1; S1 explicitly owns NodeIdentity creation.
2. C1/E13 moved to client story S6 (formerly S5); the constructor/test change now travels with its item.
3. R7 and E7 explicitly define blocked ReceiveAsync, concurrent ordered state callbacks, supersession cancellation and prompt closure.
4. Former S3 split into S3 handshake (R4/R5) and S4 ownership/liveness (R6/R7).
5. R8 stores a ceiling capped at 300 seconds without unsafe doubling; E8 includes ten normal failures then auth and 1000 failures.
6. R13/E16 in separate S8 define both source names, the activity name/kind, explicit parents and sender fields.
7. R3 names pre-bind configured URL sources and the eligible resolved-endpoint check; E15 in S7 covers rejected production Kestrel, TestServer exemption and a separate health listener.
8. E10 distinguishes zero for first success from one for success following an earlier Connected state.
9. R10 assigns SentAt after CreateHeartbeat and E10 checks the exact fake-clock timestamp.

These ticks record author changes. The untested surfaces listed above remain untested.

## Re-review, round 2

Reviewed at commit ae50b22. Stories: 8. Check: ok.

The nine findings of round 1 are resolved in the brief and the split: the resolutions above were
read against R3, R7, R8, R10, R13, E7, E8, E10, E15, E16 and the eight story blocks, and the E8
arithmetic was recomputed (ten `Unavailable` failures store 300 s, so the `Unauthenticated` draw
at 0.5 is 150 s).

New findings: None.

Not checked in this round, in addition to the list above:

- That `ConfigureEndpointDefaults` gives a loopback `IPEndPoint` for a `http://localhost:<port>` URL, which R3 now relies on.

## Maintainer-authorized endpoint amendment (review pending)

Source: artifacts/trials/10-3-1/review.md first two backlog entries; lead queue qitem-20261004094038-19747b9a reports maintainer authorization.

R14/C3/E17 and S9 amend the already merged pure policy. C4/E18 add the same checks to startup S7 without an invalid forward dependency on S9. Literal forms accepted/rejected and port-zero choice are exact in the amended brief. Slice/index status is draft pending architect review. Existing resolved findings remain resolved; this amendment needs a fresh review.

## Review of the endpoint amendment, round 3

Reviewed at commit fbca99e. Stories: 9. Check: ok.

Read: R14, C3, C4, E17, E18, the S7 and S9 blocks, and the merged `NodeLinkEndpointPolicy` with
its tests (no merged test asserts an address that R14 now rejects, so C3 changes no merged
expectation). The R14 grammar and the E17 matrix agree.

### Findings

- [x] S7 (context-gap): C4 tells the S7 implementer to reuse the tightened policy "if S9 has already merged" and otherwise to write the same grammar a second time at startup, so the code depends on the merge order, and when S7 merges first the startup copy stays beside the policy for good because S9 is told to do no startup wiring. Fix: take C4 and E18 out of S7 into a last story that depends on S7 and S9, and reduce C4 to "startup passes each configured address string unchanged to the policy".
- [x] S7 (context-gap): E18 does not give `GrpcPort`, and R3 validates only the addresses "on that port" when it is set, without saying how the port of an address that fails the grammar is found: with `GrpcPort=5180` and a valid `http://127.0.0.1:5180` beside `http://127.0.0.1:0` or `http://127.0.0.1#@example.test`, the bad address is not on the port, is not validated and the host starts, where E18 expects a failure; "every malformed address is rejected" is not the answer either, because E15 needs the non-loopback health address to pass. Fix: say in C4 how eligibility is decided for an address the grammar rejects, and give the `GrpcPort` setting and the other configured addresses for the E18 cases.

## Not checked, round 3

- The maintainer's authorization of the amendment (reported by `lead`) and `artifacts/trials/10-3-1/review.md`, which the amendment cites; not read, by role.
- That `tools/story.sh check` forbids a dependency on a later-numbered story, as the author reports.
- What Kestrel itself does with each E18 address.


## Endpoint amendment resolution

- C4/E18 moved to S10 depending on S7 and S9. Startup passes eligible original strings to the one policy implementation; no second grammar and no merge-order implementation choice remain.
- C4 specifies eligibility separately, and E18 specifies GrpcPort unset for all malformed-address cases. Additional explicit-port-5180 cases with an excluded 5181 health listener verify filtered startup behavior. Port-zero and implicit-port cases no longer claim eligibility at 5180.

Author ticks record the revisions.

## Re-review of the endpoint amendment, round 4

Reviewed at commit a062a95. Stories: 10. Check: ok.

Both findings of round 3 are resolved: C4 and E18 are in S10, which depends on S7 and S9, startup
passes the original strings to the one policy, and C4 defines eligibility. Each "explicit-5180"
address of E18 was walked through C4: it is either selected by its parsed port and rejected by
the policy, or indeterminate and rejected, so the expected failure holds in both cases.

New findings: None.

Noted, not a finding: under C4 a configured URL that `Uri.TryCreate` cannot parse fails startup
even on another port, so a wildcard health URL such as `http://*:5181` next to a valid link
endpoint stops the host with the NodeLink error. The brief states this result (fail closed).

Not checked in this round: what `Uri.TryCreate` returns for each E18 address; nothing was run.


## S3 host dependency amendment (review pending)

Lead queue qitem-20261004113038-5f124168 reports context-gap: the acceptance host omitted NodeLinkRegistry/ActorSystem, so implemented S3 could not be constructed. C5/E19 specify the slim-host application dependency set, public registry constructor, host-owned Akka lifetime and explicit generated-service qualification. S3 owns the minimum handshake support; S4 retains its ownership/liveness scope. This is an amendment pending architect and maintainer approval; no third story attempt is authorized by it. Acceptance rewrite/baseline follows approval.

## Review of the S3 host dependency amendment, round 5

Reviewed at commit aa84578. Stories: 10. Check: ok.

Read: C5, E19, the changed public surface, the S3 and S4 blocks and `show 10-3 4`. C5 and E19
close the gap for S3: the registration set is complete for R4 and R5, the registry constructor
is fixed, and the host owns the `ActorSystem`.

### Findings

- [x] S4 (context-gap): C5 fixes the registration set and the two-parameter `NodeLinkRegistry` constructor for the S3 host only, while S4 adds host-shutdown `Goodbye` (R6) and the liveness timer (R7) and its E6/E7 also run on a slim host; nothing says that this same set must be enough for S4, so an S4 implementation that needs one more registration (for example a hosted service that sends `Goodbye` on stop) fails to construct in the acceptance host exactly as S3 did. Fix: state in R6/R7 or in C5 that the C5 registration set and constructor are also the complete host for E6 and E7 (shutdown observed through the host-provided `IHostApplicationLifetime`, time and options reaching the proxy through `NodeLinkService`), or list what the S4 host adds, and say so in the S4 note.

## Not checked, round 5

- The two failed attempts of story 10-3-3 and their gate output; the cause was taken from the author's note.
- That `AddAkka("aiakos-link-acceptance", _ => { })` on a slim host starts and stops an `ActorSystem` as C5 assumes; nothing was run.
- `artifacts/trials/` (acceptance tests), by role.


## S4 host dependency resolution

C5 now fixes the same complete host and two-parameter registry constructor for E6/E7, with no extra application registrations. It explicitly permits the framework-provided IHostApplicationLifetime for shutdown and routes proxy time/options through NodeLinkService. S4 notes repeat the cross-story dependency; E6/E7 traceability includes C5. Author tick records the amendment.

## Re-review of the S3 host dependency amendment, round 6

Reviewed at commit 1ffb73a. Stories: 10. Check: ok.

The finding of round 5 is resolved: C5 makes the same registration set and the two-parameter
registry constructor the complete slim host for S4 and E6/E7, with shutdown observed through
`IHostApplicationLifetime` and time and options reaching the proxy through `NodeLinkService`;
the S4 note names it and E6/E7 list C5 in `items.tsv`.

New findings: None.

Not checked in this round: nothing was run; the list of round 5 still applies.


## Server protocol-clock amendment (review pending)

Lead queue qitem-20261004212849-70e5a0f7 records maintainer authorization dated 2026-10-04 for explicit R4 injected-clock timeout and a limited further S3 fix. R4/E4 now require provider-backed Hello timing and timer cleanup. R5 UTC, R6 event-driven shutdown/replacement, R7 initial/reset liveness, R8 pure arithmetic, R9 absence of an added server deadline, C5/E19 real external teardown bound and R12 transport clocks are explicitly classified. S3 acceptance already advances the virtual clock and needs no code change; future S4 E7 tests must use the same provider assumption. No product edits or acceptance run by author.

## Review of the server protocol-clock amendment, round 7

Reviewed at commit 3d15fa1. Stories: 10. Check: ok.

Read: the amendment commit (R4, R5, R6, R7, R8, R9, C5, E4, E7, E19, the audit paragraph and
the S3 and S4 notes). The changes add no item and move none. R4 and R7 now say that the Hello
deadline and every liveness deadline are measured and scheduled by the injected `TimeProvider`,
and C5 and E19 keep host and actor-system teardown on real time; the two are stated apart and
do not contradict each other. The sentences added to R8 and R9 describe stories that are
already merged (S5, S6) and change no expected output of theirs: the merged
`NodeReconnectDelay` holds no clock or timer.

New findings: None.

Not checked in this round:

- The maintainer's authorization (lead queue item named in the author's note) and the failed attempts of story 10-3-3 (issue #116, `blocked`) that led to it.
- That a `TimeProvider` double without a new package can drive both `CancellationTokenSource(TimeSpan, TimeProvider)` and the proxy's timers as E4 and E7 need; nothing was built or run.
- `artifacts/trials/` (acceptance tests), by role.

## Review of the recut against merged S3, round 8

Reviewed at commit cdfd25c. Stories: 10. Check: ok.

Read: the amendment commit (the baseline paragraph, R7, C4, C5, C6, C7, E6, E7, E18, E20, E21
and the S3, S4, S7 and S10 notes) and the code merged by #145: `NodeLinkService.cs`,
`NodeLinkRegistry.cs`, `NodeProxyActor.cs`, `Program.cs` and the names of the tests in
`NodeLinkHandshakeTests.cs`. The description of `main` in the brief matches the code: the
receive loop, the `Connected` and final state callbacks, a registry that stops the previous
actor, the original instance-ID spelling passed on (`NodeLinkService.cs:55`), `TrimEntries` in
`Program.cs:60` and a default-only `NodeLinkOptions` registration (`Program.cs:38`). No merged
test asserts a non-canonical instance ID, so C6 changes no committed test.

### Findings

- [ ] S7 (context-gap): C7 binds all three `NodeLinkOptions` values from `Aiakos:NodeLink` and validates only `HelloTimeout`, so `LivenessTimeout=00:00:00`, a negative `HeartbeatInterval` or a heartbeat interval longer than the liveness timeout start the orchestrator; the first two give a `Welcome` that every node rejects as `FailedPrecondition` under R9 and, in S4, a liveness timer armed with a non-positive due time, the third a link that turns `Unknown` between heartbeats. Fix: state in C7 the accepted range of `HeartbeatInterval` and `LivenessTimeout` and their relation with the fixed failure text, and add the cases to E21; or state that these two are not bound from configuration.

Noted, not a finding: the baseline paragraph names `artifacts/trials/10-3-3/review.md` in the
brief text that implementers receive, a folder they are told not to read; the sentence says its
observations are not requirements.

## Not checked, round 8

- `artifacts/trials/10-3-3/review.md`, by role; the amendment's account of it was compared with the merged code only.
- Lead's request for the recut; taken from the author's text.
- The node side for S7 (`NodeProgram`, message sizes, keepalive) against R12; nothing was built or run.

