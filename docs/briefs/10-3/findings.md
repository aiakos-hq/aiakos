# Story review: slice 10-3

Reviewed at commit 0b36217. Stories: 6. Check: ok.

## Findings

- [ ] S2 (context-gap): `INodeLinkApplication` (S2) takes `NodeIdentity`, the type that `NodeTokenRegistry.Authenticate` (S1) returns, and S1 and S2 do not depend on each other, so two parallel runs both create it. Fix: add S1 to the `depends` of S2, or say in the `notes` of both which story creates `NodeIdentity`.
- [ ] S5 (context-gap): the S5 note tells the implementer to rewrite the health-based tests in `OrchestratorConnectionTests.cs`, but the change item that allows it (C1) and its output (E13) are owned by S6 and are not in the S5 story text, and after S5 the E13 test already passes on `main`, so S6 has no failing baseline for it. Fix: move C1 and E13 to S5.
- [ ] S3 (context-gap): the S3 note "timer/replacement messages must remain processable so a blocked callback cannot stall supersession or liveness" is a rule that exists only in `stories.md`, has no expected output, and leaves open whether `StateChangedAsync` may run while `ReceiveAsync` is still awaited. Fix: move it into R7 with an E7 case (application blocked on one event, then the liveness timeout and a superseding stream), or delete it.
- [ ] S3 (context-gap): R4 to R7 put authentication, the handshake, atomic replacement, the single writer, host shutdown, the liveness timer and back-pressure of callbacks into one run, with about 25 cases in E4 to E7; that is more than one concern and more than a Sonnet-level run. Fix: split into a handshake story (R4, R5, E4, E5) and an ownership and liveness story (R6, R7, E6, E7) that depends on it.
- [ ] S4 (context-gap): R8 does not say whether the stored ceiling is bounded: "advances independently of error category" read literally doubles without limit, which overflows `TimeSpan` after about 41 consecutive failures (0.5 s × 2^41), and the sequence "ten `Unavailable`, then one `Unauthenticated`" draws from 60 s or from 300 s depending on the reading. Fix: state in R8 the bound of the stored ceiling after doubling (for example 300 s) and add that mixed sequence to E8.
- [ ] S6 (context-gap): the tracing half of R12 changes the send and receive loops that S3 and S5 build, and names neither the `ActivitySource` nor the activity, so the E12 test, which is written first, cannot know what to listen for. Fix: make tracing its own rule with the source and activity names for both sides, in its own story that depends on the server and node stream stories, and leave registration, limits and keepalive in R12.
- [ ] S6 (context-gap): the startup half of R3, which S6 activates, has no expected output (E3 tests only `IsAllowed` and `Validate`; E12 and E13 start only loopback hosts), and R3 does not say from where and when the addresses are read (configured URLs before binding, or bound addresses after start). Fix: state the source and the moment in R3, and add an S6 output: Kestrel with `http://0.0.0.0:<port>` fails to start with the fixed error, and a `TestServer` host starts.
- [ ] S5 (context-gap): E10 "three auth errors then success … counter increases once" contradicts R10 "preserve reconnect counter behavior" when the success is the first connection of that `OrchestratorConnection`: today the counter moves only after an earlier `Connected` (`OrchestratorConnection.cs:78`). Fix: say in E10 that the connection was `Connected` before the three errors, or give the count for a first connection as zero.
- [ ] S5 (context-gap): R10 says the heartbeat carries `SentAt` from `TimeProvider` and R11 (S2) describes the default `CreateHeartbeat` without it, so neither story is told to set it and neither E10 nor E11 checks it. Fix: say in R10 that the connection sets `SentAt` on the message the source returns, and name it in E10.

## Not checked

- Conformance of the brief to spec 0002 beyond its rules R6 to R11 and the stream status table; the ADRs were not read.
- That the address Aspire passes for the `grpc` endpoint in `ASPNETCORE_URLS` passes R3 (expected `http://localhost:<port>`); the dev stack was not run.
- That the fake clock the tests need can be written without a new package (no test project references a fake `TimeProvider`, and the `.csproj` files are outside `paths`).
- The WSL end-to-end test (`AIAKOS_E2E_WSL`): between the merge of S5 and of S6 the node on `main` cannot connect to the orchestrator on `main`; not run.
- `artifacts/trials/` (acceptance tests), by role.
