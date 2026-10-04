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

Author ticks record the revisions, pending architect re-review.
