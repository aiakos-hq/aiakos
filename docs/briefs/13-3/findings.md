# Story review: slice 13-3

Reviewed at commit 6188d1d. Stories: 9. Check: ok.

## Findings

- [x] S5 (judgment-gap): G1 and R9 reject an event with NUL in a known string as `INVALID_SEAT_EVENT` with no commit, while 10-4's node buffer accepts NUL (10-4 R4) and 10-4's transport closes the stream without an ack on any failure that is not `SeatCommitFailedException`, so the node sends that same event first after every reconnect and no later event of any seat on that node is ever committed. Fix: give R9, R6 and `ACTOR-reject` a committed result for such an event so that it is acknowledged (for example opaque evidence with the raw protobuf bytes, as for an unknown body, and a finding), or agree with the 10-4 author which side makes it terminal and name that rule here.
- [x] S4 (judgment-gap): R7 inserts a new `seat_session` for every `AdoptRotatedSession`, but the table has `UNIQUE (tenant_id, harness, native_session_id)`, so a rotation to a native id that already has a row (back to an earlier conversation, or an id another seat reported) fails the transaction, R10 answers with the retry type and a restart, 10-4 re-forwards the same event, and after ten restarts the actor stops with the event still uncommitted. Fix: state in R7 and `WRITE-session` the exact result when the native id already has a row, and state in R10 whether a constraint violation is answered with the retry type.
- [x] S7 (context-gap): R14 fails host startup with `SEAT_ACTOR_UNAVAILABLE` when one eagerly loaded seat is missing or corrupt, so one bad `seat_state` row in any tenant keeps the orchestrator down for every tenant and seat. Fix: state in R14 and `HOST-shell` that the failure stays with that seat (no child, an `actor-stopped` finding, the host starts), or record that the maintainer accepts a host that does not start.
- [x] S4 (judgment-gap): S4 owns the transaction and version check (R5), event and transition rows with usage (R6), findings and session rotation (R7) and command, delivery and launch outcomes (R8) with six outputs on real Postgres, which is more than one run for a Sonnet-level implementer. Fix: split it into consecutive stories on the same writer class, R5 with R6, then R7, then R8, each depending on the one before, with `WRITE-atomic` worded so the first does not need findings, and T4 divided to match.

Author resolution in the commit following this review:
- Finding 1: body NUL becomes opaque original protobuf evidence, IngestUnavailable gap and
  finding, committed/acknowledged once; malformed traceparent is null. 10-4 author agreed no
  rule change to its byte-evidence contract. T7 covers NUL then valid event through2.
- Finding 2: same-seat native row is reused unchanged; foreign ownership is terminal gap plus
  session-id-mismatch with unchanged native pointer. Lookup precedes Apply acceptance; a race
  rolls back and re-forward sees owner. Permanent check/not-null/FK failures are nonretry type.
- Finding 3: corrupt/missing startup key is isolated, best-effort actor-stopped, host continues.
- Finding 4: former S4 is consecutive S4 events/state, S5 findings/session, S6 outcomes; T4/T8/T9
  follow that split. Overall 9 stories/45 items. Gateway/actor work remains independent of SQL.

## Not checked

- The seam was compared with the 10-4 brief at 1d04702: the three ports, the three records and `SeatCommitFailedException` have the same signatures in both, the reload notice comes after a restart load and before queued work, and `HARNESS_PROFILE_UNAVAILABLE` is an ordinary failure there. Read, not compiled.
- Akka 1.5.71 is the core version restored for this repository, so `Akka.TestKit` 1.5.71 matches it; the adapter itself and the `Akka.TestKit.Xunit2` nuspec claim were not tried.
- The types the brief names as merged (`SeatStep`, `NodeAttached`, `AdoptRotatedSession`, `DeliveryStateMachine`, `IHarnessStateProfile`, the body records) were found in `src/Aiakos.Orchestrator/Seats/`; their members were not compared with R3 field by field.
- R4's derived properties (`ReusedNativeSessionId`, `StopRequested`) are not stored, so a reloaded state can differ from the one in memory before a restart; the schema gives no other source and this was not raised.
- S6 (three rules, four outputs: region, gateway, supervision, lifecycle) was judged to fit one run.
- Nothing in this slice applies `OrchestratorRestarted` at startup (spec 0006 R35); the brief leaves restart handling to 13-5 and I did not check that 13-5 will own it.

Architect, round 1: the diff 941f029..e0fb70e was read and the four resolutions are accepted; no findings are open. The ports 10-4 consumes are unchanged by it. Left unraised: a store rejection that repeats (`SEAT_COMMIT_REJECTED`, PostgreSQL 23514/23502/23503) is not acknowledged, so 10-4 closes the stream and the node sends the event again; I found no valid input that reaches it, only a defect in the machine or the schema would, and the brief answers it with a fixed error instead of a restart loop.

Architect, amendment (wire metadata and malformed body evidence): the diff of e89ccbe against its base 6193b35 was read (G1, G2, R5, R6, R8, R9, R10, `WRITE-evidence`, `ACTOR-reject`, T4, T5, T9 and the notes of S4, S6, S7). No findings are open. The amendment follows the rule already accepted for NUL: content the node buffer lets through and the store cannot format (a nested body timestamp outside the protobuf range) is committed as opaque evidence with a gap and acknowledged. Read in the contract: the only well-known types inside `SeatEvent` bodies are `Timestamp` fields (`observed_at`, `captured_at`, gap `from` and `to`), so no `Duration` reaches the JSON formatter there; `raw_size` is `uint32` on the wire and `int` in the table, which is why values above 2147483647 are stored as null. An invalid envelope `observed_at` stays a rejection without an ack; 10-4's node buffer (R1) and adapter (R11) already refuse it, so only a node that breaks the protocol reaches it. Not checked: the bounds in R5 against Google.Protobuf's own validation by running it, and the acceptance tests in `artifacts/trials/`.

Architect, after lead merged main into the branch (6188d1d): brief, items and stories of 13-3 are byte-identical to e89ccbe, and the branch differs from main only in the three 13-3 files. No findings are open.
