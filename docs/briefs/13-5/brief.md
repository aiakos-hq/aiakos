---
id: 13-5
title: "#13 slice 5 — durable restart and integration evidence"
issue: 13
status: approved
route: impl
paths: [src/Aiakos.Orchestrator/Seats/, tests/Aiakos.Orchestrator.Tests/Seats/, tests/Aiakos.Orchestrator.Tests/Link/]
date: 2026-10-08
---

# Brief: #13 slice 5 — durable restart and integration evidence

Part of #13. Self-contained. Do not read specs or ADRs to fill in requirements.
Where silent, choose the simplest behavior and record it in the commit body.

## Goal and overlap

Close the remaining spec 0006 R35 process-start boundary and prove AC5/AC6/AC8 plus the restart
part of AC9 through real actors and Postgres. This is not another state machine, command store,
node proxy, migration or launcher. The source audit found that current SeatActor.EnsureLoadedAsync
loads the snapshot and sends SeatChildLoaded without applying OrchestratorRestarted. SeatRegion
already selects startup keys and holds external routing until children load. This slice adds
one durable process-start overlay before that readiness handshake completes.

Covered elsewhere: 13-1 pure overlay/epoch/gap/rotation transitions and property tests; 13-2
schema/queries; 13-3 transactional event writer, startup query, actor supervision and typed
reload handshake; 13-4 R11/R13/R14/R18 command uncertainty, timer restoration, reload capture
and evidence capture dispatch; 10-4 S6/S7 authenticated assignment/replay query and retained
NodeProxy re-forwarding. Use those contracts even when code is not merged. In particular
Welcome.replay and crash re-forwarding already belong to 10-4: no duplicate implementation here.

The remaining stories are permanent integration evidence over those implementations, not
acceptance-test files. Their prerequisites are named below; do not stub missing code or widen
this slice to implement it. AC7 resume-loss, most AC9 send admission and AC13 rotation persistence
are owned by 13-4; AC3/AC4/AC10 are earlier coverage; AC11/AC12 live Aspire/Claude/WSL demos remain
unrun until #11/#12/#15 and release acceptance are ready. No claimed completion of those demos.

Risks exercised: 0006-RK9 typed actor recovery plus real transport (owned implementation #10),
0006-RK7 new-instance uncertainty and 0006-RK6 event/result ordering across durable replay.
0006-RK1 compatibility is already recorded in tests/Seats/TestKitCompatibility.md; no new
compatibility spike. RK3 throughput/manual demo, RK4 property coverage and RK8 concurrency
measurement remain in their existing owners. Do not mark a risk closed without actual evidence.

## Files to create or touch (nothing else)

| Path | What |
|---|---|
| src/Aiakos.Orchestrator/Seats/ | Startup overlay coordination inside existing actor/region/host, no second actor system |
| tests/Aiakos.Orchestrator.Tests/Seats/ | Startup, same/new epoch, command recovery integration tests and reusable test-only fixture |
| tests/Aiakos.Orchestrator.Tests/Link/ | Real NodeProxy/re-forward/ack integration test using existing loopback fixtures |

No schema, proto, package, project, Core/state-machine, Data or production Link changes. No
warning suppression/reflection in src. If a prerequisite implementation violates its published
contract, stop and hand the ran failure/evidence to router; do not fix its behavior in this slice.

## Public surface (exact names)

Unchanged. Preserve all 13-3 constructors/ports and 13-4 overloads and command services.
Use existing SeatActor, SeatRegion, SeatRegionHost, SeatActorGateway, ISeatActorReader/Writer,
PostgresSeatActorReader/Writer and PostgresSeatCommandStore. Startup coordination messages or
constructor flags may be internal, not a new public startup API. Later code compiles against
existing ports. 10-4 supplies SeatNodeEventCommitter/EventNodeLinkApplication, and 13-4 supplies
SeatCommandServices; no substitute production provider is permitted.

## General rules

G1. Only the SeatActor writes seat_state/seat_transition/seat_event/seat_launch/seat_command or
    seat findings, through existing transactions. Identity is trusted SeatKey/authenticated
    NodeIdentity, never protobuf identity alone. No event-log replay, synthetic harness event,
    auto launch, delivery resend or state inferred from captured text. No secrets/raw bodies,
    tokens or SQL/peer exception detail in logs/spans/reports. No UPDATE/DELETE of event/transition.
G2. Tests use the existing Testcontainers postgres:18 fixture, migrations and xUnit-v3/TestKit
    adapter, isolated database/tenant/rig names and test profiles (ClaudeLike/OpenCodeLike).
    No real node process, tmux, native harness login, released instance or owner database.
    Await startup/commit/notification barriers and bound waits by test cancellation; no sleeps
    or polling to guess a commit. Fake time controls timers. A fixture must terminate and await
    both actor systems before disposing data sources/database; no orphan actors or global env edits.

## Changes to earlier behavior

C1. At process startup, startup-selected live seats now commit the orchestrator-restarted overlay
    before SeatChildLoaded/host-ready. Existing startup tests expecting a live reported value
    immediately on initial load must assert Unknown/orchestrator-restarted instead, retaining
    their known values and version evidence. Standalone actor tests and supervised child reloads
    do not acquire a process-start overlay merely because PreStart runs again. Earlier absent/
    exited/human/retired behavior, event commits, reload notifications and pure table results stay exact.

## Rules

R1. Process-start initialization. For each active nonretired agent key returned by GetStartupSeatsAsync, mark its first successful region child initialization as orchestrator process startup. Before acknowledging that child as ready, the actor loads its persisted snapshot and applies existing SeatStateMachine.Apply(state,new OrchestratorRestarted(),profile,TimeProvider.GetUtcNow()). If that step changes state/transitions/findings, commit with existing writer using a SeatAppliedInput with null Event/TraceParent, then reload the actual committed snapshot/version. It must finish before SeatChildLoaded readiness completes, queued public requests, timer effects or evidence capture. The region never writes the step itself. A successful process-start overlay does not request an R18 capture, even for an Unknown current launch: OrchestratorRestarted emits no RequestCapture effect. Startup alone inserts/sends no capture on connected or disconnected nodes and retains no deferred capture for reconnect. Later committed new-instance gap effects and supervised reload notifications request captures through 13-4 R18; no second capture executor is added here. If OrchestratorRestarted was already durably set, do not create duplicate transitions or increment version for a no-op. Missing profile/invalid state/load/commit failure follows existing safe startup failure handling, never marks the seat ready or publishes guessed state.

R2. Startup versus supervision. Use the existing startup query: agent, not retired, current launch present OR desired up. Live known session (Present/Starting/Unknown) gets reported Session=Unknown and Activity=Unknown, both reason orchestrator-restarted, Overlay=OrchestratorRestarted; KnownSession/KnownActivity/details/native session/current launch/cursors remain as Apply specifies. Absent/Exited stays unchanged and does not create a new launch even if desired up. Humans/retired/nonstartup idle absent seats are not eagerly created. Keep the startup marker for a failed initial load until that key becomes successfully ready; it is consumed only after success. A later supervised child restart in this same region does not reapply the process-start overlay whether the node link is live or lost; it reloads committed state and uses the existing SeatActorReloaded-before-queued-work handshake. A new actor system/region starts a new process-start boundary.

R3. Test fixture. Add test-only type Aiakos.Orchestrator.Tests.Seats.DurableRestartFixture in tests/Aiakos.Orchestrator.Tests/Seats/DurableRestartFixture.cs: a fixture that creates the existing migrated Postgres database, seeds two tenants and a rig with agent A, agent B, human H, retired agent R and an absent desired-down agent D. A has current launch L, node name n, node epoch I1 and native session native-a; B has desired up but no current launch. Seed seats/sessions/launches/state once as setup, then all scenario mutations use real gateway/actor stores. Reuse test profiles; no runtime Claude adapter is required. Provide one real region/gateway per actor system and production Postgres readers/writers. Command scenarios add real PostgresSeatCommandStore plus only test node port/connection/material providers as specified by 13-4. Reader/writer wrappers may record/barrier/fail one call but delegate all successful work to Postgres. No in-memory database or successful fake event committer substitutes. This fixture owns no production registration.

R4. Same-instance durable replay (AC5). For A seed the initial current launch in Starting, native-a, epoch I1, NextSeq=1, known Starting/Unknown not-ready, desired up. Submit 20 normalized events of L/I1 through the gateway: seq1=SessionStarted source startup native-a SourceSeq1; seq2=PromptSubmitted native-a SourceSeq2 with turn_id=t1; seq3..20=unknown/opaque body with SourceSeq0. These establish Present/Working/Resumable and NextSeq21. Stop and await the first entire actor system without stopping a pane or changing rows, start a second over the same DB. Before node attach assert the committed restart overlay and preserved known axes/cursors. Attach I1 inventory L/Running LastSeq25 through real SeatNodeEventCommitter.GetReplayAsync; Welcome checkpoint must be 21 from committed state, not inventory or an event-log scan. Re-deliver seq18..25 through that facade: duplicates18..20, unknown21..24, TurnEnded native-a SourceSeq25 seq25. Before25 report Unknown/orchestrator-restarted with CatchUpSeq25; after25 reported Present/Idle/Resumable, Overlay=null, NextSeq26, LastSourceSeq25. Exactly 25 seat_event rows for A/I1, no duplicate transitions from18..20, one stored row per unique seq. No StartSeat/DeliverInput dispatched by restart.

R5. New-instance recovery (AC6). Independently repeat through20, restart, then attach epoch I2 inventory same L/Running LastSeq0 via real facade. Committed state has NodeInstanceId=I2, NextSeq1, Session Present from inventory, Activity Unknown with observation-gap, Resumability Resumable unchanged from before restart, with no resumability transition, Overlay=null, one open observation-gap finding. With 13-4 command services, exactly one new CapturePane command for L is persisted before test port sees it (HistoryLines0), and no StartSeat or delivery is sent. Capture output text does not establish idle/activity; PaneDead evidence only follows pure rules. Repeated identical attach does not fabricate replay/events or another automatic capture while one is pending/sent. The different-instance checkpoint is 1. In an independent alternate starting from the same through20/restart setup with no pending/sent capture, attach I2 with a different inventory LaunchId: assert Session Unknown/inventory-missing, an open inventory-mismatch finding, Resumability Resumable unchanged with no resumability transition, NextSeq1 and Overlay=null; retain current launch L rather than adopting the untrusted inventory launch. The committed gap still opens one observation-gap finding and requests capture for current launch L: exactly one new CapturePane command for L/history0 is persisted before dispatch, zero captures for the untrusted launch, and no start/delivery. Repeating that mismatch attach while the capture is pending/sent adds no capture.

R6. Command recovery integration (AC9 restart portion). Use 13-4 actor commands to create a valid current-launch delivery and observe it persisted sent to I1, plus one already final command. Same-instance link loss/reattach preserves that delivery status/outcome/instance and original SentAt/deadline; the SeatActor never sends it again. New-instance I2 attach atomically marks sent nonfinal delivery status/outcome unknown and pending not-dispatched delivery failed/not-delivered, preserving final commands. Tests may seed pending command rows as fixture setup to model the persist-before-dispatch crash; do not send it on boot. Query rows through existing reader/queries, assert original command IDs and exactly one original dispatch for sent delivery, zero dispatch for seeded pending. Advance fake time to original uncertainty deadline and assert no resend and no success invented. Use actual 13-4 deadline handlers; do not create a second scheduler.

R7. Evidence capture and handshake ordering. Test with real Postgres stores and controlled reload barrier: no new command/send until startup overlay commit and successful initial ready; on supervised reload, no command decision/send before SeatActorReloaded notification. Restore timers from persisted timestamps per 13-4 R13/R14; an overdue deadline fires once after ready without granting a fresh interval. Process-start readiness alone requests zero captures. A successful supervised reload SeatActorReloaded notification for an Unknown current launch requests one existing R18 history0 evidence capture after notification; a committed new-instance gap RequestCapture effect requests one after commit/refresh per R5; pending/sent capture suppresses duplicate, absent launch creates none. Unavailable node creates no successful dispatch and no busy retry. Capture result remains evidence, never resolves the overlay/activity from text. This test verifies 13-4 behavior, not permission to reimplement it.

R8. Real transport crash proof (AC8). Run production NodeProxyActor/NodeLinkService/EventNodeLinkApplication/SeatNodeEventCommitter against the real region/gateway/Postgres via existing authenticated loopback test setup, with a scripted fake node stream. Subscribe before event forwarding as production 10-4 does. Make a recording writer wrapper throw once before committing one valid event, then block the restarted reader until the test releases it. Assert no EventAck before release/real transaction, one matching reload notification after successful reload, facade re-forwards same immutable event/seq/trace only then, and exactly one committed event row/transition set and one EventAck for the retained callback. Payload mutation of the original test object after forwarding cannot change committed data. After the typed recovery, an ordinary InvalidOperationException must terminate without matching reload retry/ack as 10-4 specifies. No duplicate retry implementation in tests/production. This story waits for 10-4 S6/S7 code and their wiring.

R9. Ambiguous commit proof. Writer wrapper commits via real store then throws SeatCommitFailedException to model a lost receipt. After notification and retained re-forward, the already committed seq dedupes; count remains one, version/transition counts do not increment for duplicate, one eventual EventAck releases the callback. Successful persistence alone is not an ack; test barrier proves ack follows the final facade receipt. Cancellation/supersession during reload wait disposes callback/subscription, prevents later ack/retry, and shutdown awaits the actor systems within existing budgets. Other tenant/seat reload notices cannot release this callback. These are 10-4/13-3 integration assertions, not another replay buffer.

R10. Isolation and diagnostics evidence. For each restart/replay/crash scenario assert all read/write joins remain trusted tenant/seat and untouched second tenant rows keep original counts/values; retired/human/unknown inventory yields no agent actor/replay or seat mutation, with node findings solely through existing 10-4 behavior. Listener tests observe committed seat.apply/seat.transition or relevant already-defined link spans, then show sentinel payload/native input/token/exception details absent from logs/tags. Evidence count checks explicitly name seat+epoch; total row counts across tenants are not substitutes. No UPDATE/DELETE statement for event/transition added.

## Expected outputs: exact values

| ID | Expected |
|---|---|
| `BOOT` | Required artifact: ProcessStartupIntegrationTests with all T1 scenario methods present and executed, zero skipped; Startup barrier held -> no ready/public success; after commit live rows unknown/orchestrator-restarted and known values retained; absent/exited unchanged; supervision alone no extra process-start overlay for either live or lost node link; existing reload/link-loss handling remains in force. |
| `FIXTURE` | Required artifact: DurableRestartFixture and SameInstanceRestartIntegrationTests with all T2 scenario methods present and executed, zero skipped; Two actor systems sequentially share real migrated DB with private tenant/rig; command services are optional and their capture-before-dispatch assertion belongs to NEW; teardown awaits both systems. |
| `SAME` | Required artifact: SameInstanceRestartIntegrationTests with all T2 scenario methods present and executed, zero skipped; Checkpoint21; overlay through24, cleared after25; NextSeq26; 25 unique A/I1 events; duplicate18..20 adds no transition; no start/deliver resend. |
| `NEW` | Required artifact: NewInstanceRestartIntegrationTests with all T6 scenario methods present and executed, zero skipped; I2 checkpoint1; Session Present, Activity Unknown/observation-gap, Resumability Resumable unchanged with no resumability transition; one open observation-gap and one persisted L/history0 capture before dispatch. Independent mismatch: Session Unknown/inventory-missing, inventory-mismatch plus observation-gap findings, Resumability Resumable unchanged, NextSeq1/Overlay=null, current L retained; exactly one persisted L/history0 capture before dispatch, zero untrusted-launch captures, no start/delivery; repeated attach while pending/sent adds none. |
| `COMMAND` | Required artifact: CommandRestartIntegrationTests with all T3 scenario methods present and executed, zero skipped; I1 reattach preserves sent; I2 sent->unknown, seeded pending->failed/not-delivered, final unchanged; IDs/SentAt/deadlines preserved and dispatch count does not grow. |
| `ORDER` | Required artifact: CommandRestartIntegrationTests with all T3 scenario methods present and executed, zero skipped; Zero startup-only captures; no timer/send ahead of startup readiness; supervised SeatActorReloaded triggers one history0 capture after notification, new-instance gap effect after commit/refresh; pending/sent capture, absent launch or disconnected node inserts/sends none; overdue original deadline fires once after ready, no fresh interval; capture text changes no axis. |
| `CRASH` | Required artifact: TransportRestartIntegrationTests with all T4 scenario methods present and executed, zero skipped; Precommit throw -> zero ack before reload release; eventual one ack, one durable row; original seq/body/trace unchanged; ordinary exception not retried. |
| `AMBIGUOUS` | Required artifact: TransportRestartIntegrationTests with all T4 scenario methods present and executed, zero skipped; Postcommit lost receipt -> one durable row/version change and eventual one ack; duplicate re-forward unchanged; wrong-key reload/cancel/supersession no release/late ack. |
| `ISOLATION` | Required artifact: RestartIsolationDiagnosticsTests with all T5 scenario methods present and executed, zero skipped; Second tenant values/counts unchanged; no actor/replay for human/retired/unknown; diagnostic listeners positively observed but contain no sentinels. |

## Tests

Use tests/Aiakos.Orchestrator.Tests/Seats and Link. Reuse PostgresSeatActorWriterTests,
SeatRegionTests, NodeLinkEventAckTests and existing TestKitCompatibility patterns.

T1. Required permanent xUnit class Aiakos.Orchestrator.Tests.Seats.ProcessStartupIntegrationTests in tests/Aiakos.Orchestrator.Tests/Seats/ProcessStartupIntegrationTests.cs; required scenario methods StartupCommitPrecedesReady, StartupNoOpPreservesVersion, FailedStartupRetriesBeforeReady, SupervisedReloadWithLiveOrLostLinkDoesNotReapplyStartup, StartupSelectionPreservesAbsentHumanRetired. Commit startup barrier/overlay/no-op/supervision tests, including already-set overlay, failed initial commit/load, two live agents and absent/human/retired cases. Update only the earlier live-startup expectation identified by C1; no weakening of writer/gateway tests.

T2. Required permanent xUnit class Aiakos.Orchestrator.Tests.Seats.SameInstanceRestartIntegrationTests in tests/Aiakos.Orchestrator.Tests/Seats/SameInstanceRestartIntegrationTests.cs; required scenario methods SameEpochReplayUsesCommittedCheckpoint, FixtureAwaitsBothActorSystems. Commit the R3 real Postgres fixture and R4 same-instance actor scenario with exact sequences/states/cursors/counts and snapshot checks between replay batches. Production facade checkpoint read must be used, not a test-computed replay value.

T6. Required permanent xUnit class Aiakos.Orchestrator.Tests.Seats.NewInstanceRestartIntegrationTests in tests/Aiakos.Orchestrator.Tests/Seats/NewInstanceRestartIntegrationTests.cs; required scenario methods NewEpochPersistsCaptureBeforeDispatch, RepeatedAttachSuppressesPendingCapture, MismatchedLaunchIsNotAdopted. Commit the R5 new-instance scenario with exact states/cursors/counts, persisted capture-before-send, duplicate attach suppression and mismatch evidence, using the R3 fixture and production facade checkpoint.

T3. Required permanent xUnit class Aiakos.Orchestrator.Tests.Seats.CommandRestartIntegrationTests in tests/Aiakos.Orchestrator.Tests/Seats/CommandRestartIntegrationTests.cs; required scenario methods SameEpochPreservesSentDelivery, NewEpochMarksNonfinalCommandsUncertain, StartupAndReloadOrderCaptureAndOriginalTimers. Commit real-store command recovery and readiness/timer/capture-order tests with injected node port and fake time. Assert dispatch count/command ID before and after restart/new epoch and no text-based activity classification.

T4. Required permanent xUnit class Aiakos.Orchestrator.Tests.Link.TransportRestartIntegrationTests in tests/Aiakos.Orchestrator.Tests/Link/TransportRestartIntegrationTests.cs; required scenario methods PrecommitCrashReforwardsAfterReload, LostReceiptDedupesBeforeAck, OrdinaryExceptionDoesNotRetry, CancellationSupersessionAndForeignNoticeDoNotRelease. Commit authenticated loopback real NodeProxy crash/lost-receipt/late-notice cancellation tests. Use barriers, real stores and production facade re-forwarding, never manually resubmit the event as a substitute for AC8.

T5. Required permanent xUnit class Aiakos.Orchestrator.Tests.Seats.RestartIsolationDiagnosticsTests in tests/Aiakos.Orchestrator.Tests/Seats/RestartIsolationDiagnosticsTests.cs; required scenario methods RestartReplayAndCrashPreserveOtherTenant, NonAgentInventoryDoesNotCreateActorsOrReplay, CommittedDiagnosticsExcludeSentinels. Commit tenant/filter/sentinel evidence within all integration scenarios; show production diagnostic emission before checking absence. Unchanged append-only earlier tests stay green.

T0. Named fixture/classes/methods in R3 and T1-T6 are required deliverables, not optional example names. Scenario methods are xUnit facts/theories that execute the owned real-actor/Postgres assertions; empty tests, skipped cases and substituted in-memory fixtures do not satisfy the outputs. Story acceptance must fail for an absent owned class/method/fixture even when prerequisites already implement the behavior, then execute those permanent scenarios and require zero skipped tests. Every story adds permanent tests of its owned rules; no acceptance trials read or copied. Test fixture and logs enforce private state and bounded awaited teardown.

## Definition of done

- tools/story.sh gate owns acceptance; authors write no acceptance tests.
- Implementation: dotnet build -c Release zero warnings/errors, dotnet test green including
  database tests; Docker required. Report skipped/unrun environments honestly.
- Conventional commit subject with story ID/#13, body lists choices and exercised risk IDs.
  One commit; no push/PR/merge. LF, UTF-8 without BOM, final newline.
- If same test fails three times stop/report. A named missing dependency blocks its story,
  not permission to implement a fake production provider or change an earlier brief.

## Out of scope

No production link facade/retry/transport changes (10-4), command lifecycle/timers/capture
implementation (13-4), migrations/queries/pure state-machine changes, real session host/harness,
CLI/release/WSL/Aspire manual demos or statusLine throughput measurements. No new state writer,
actor system, inbox, persistence plugin or event-log rebuild.
