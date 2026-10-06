---
id: 13-4
title: "#13 slice 4 — SeatActor lifecycle and delivery"
issue: 13
status: draft
route: impl
paths: [src/Aiakos.Core/, src/Aiakos.Orchestrator/, tests/Aiakos.Orchestrator.Tests/]
date: 2026-10-06
---

# Brief: #13 slice 4 — SeatActor lifecycle and delivery

Part of #13. This slice owns the transport-free command dispatcher and its messages. It consumes
the committed SeatActor ports from 13-3 and is consumed by 15-2's local API. It does not own HTTP,
gRPC, node transport, command creation, or database schema. 13-3 S5/S6, S7 retry, S8 and S9 are
not all on main yet; stories depending on them name that dependency. Where silent, preserve the
existing normalized state machine and return a fixed safe rejection.

## Files to create or touch (nothing else)

| Path | What |
|---|---|
| `src/Aiakos.Core/` | CallerContext, command messages/replies and dispatcher port |
| `src/Aiakos.Orchestrator/Seats/` | dispatcher implementation and lifecycle/delivery coordination |
| `tests/Aiakos.Orchestrator.Tests/` | command, dispatcher, timeout and delivery tests |

No transport adapter, schema/migration, harness-specific branch, CLI or 15-2 API project.

## Public surface (exact names)

```csharp
namespace Aiakos.Core;
public sealed record CallerContext(Guid TenantId, string User, string Sender);
public sealed record SeatEnvelope(object Command);
public interface ISeatCommandDispatcher { Task<object> DispatchAsync(SeatEnvelope command, CallerContext caller, CancellationToken ct); }
public sealed record SeatUp(bool Fresh, string? Note);
public sealed record SeatDown;
public sealed record SeatSend(string Body, bool Force);
public sealed record SeatCapture(int HistoryLines);
namespace Aiakos.Orchestrator.Seats;
public sealed record SeatCommandAccepted(Guid? LaunchId, Guid CommandId);
public sealed record SeatAlreadyUp(Guid LaunchId);
public sealed record SeatAlreadyDown;
public sealed record SeatCommandRejected(string Reason);
public sealed record SeatCaptureCompleted(Aiakos.Contracts.Node.V1.PaneCapture Capture);
public sealed record SeatCommandTimedOut(string Reason);
```

15-2 resolves tenant/user/sender and calls `ISeatCommandDispatcher.DispatchAsync` with a
`SeatEnvelope` and `CallerContext`; it changes its bridge to these Core types. 13-4 never reads
tokens or derives identity from a body. Existing `ISeatInputCommitter.ApplyAsync` remains the
committed-input port; 13-4 owns row creation through its atomic `ISeatCommandStore.CommitAsync`.

## General rules

G1. All command decisions use trusted `CallerContext`, reported state and node-link availability;
    no body, address spelling or transport metadata can change tenant identity. Bodies and tokens
    never appear in logs or exception text.

The outbound seam is `ISeatCommandPort`: `StartSeat(SeatLaunch)`, `StopSeat(Guid)`,
`CapturePane(Guid)`, and `Deliver(Guid,string,string,bool)`, each accepting a cancellation token.
13-4 calls 12-1's `BuildLaunch`/`BuildDelivery` before invoking that port; 10-5 supplies the
transport implementation. `ISeatCommandStore.CommitAsync` atomically writes `seat_session`,
`seat_launch`, and `seat_command` rows (tenant, seat, command id/kind, caller and payload hash)
before any port call. Stop/capture/send use the same store. Capture timeout is 5s, stop timeout
10s, delivery timeout 15s, and quiet/activity watchdogs 10 minutes.

The absent-seat down result is `SeatAlreadyDown`. A reload consumes 13-3's `SeatActorReloaded`,
marks launch and delivery overlays unknown, requests capture for gaps, and releases queued work
only after that notification. Structured names are log `aiakos.seat.transitions`, metric
`aiakos_seat_commands_total`, and spans `seat.apply`, `seat.up`, `seat.down`, `seat.send`,
`seat.capture`; tags contain IDs, kinds, outcomes and reasons only.

## Rules

R1. `SeatUp` records desired-up and starts one launch only from absent/exited; starting/present is
    a successful no-op. Unknown rejects `SEAT_STATE_UNKNOWN`; unavailable node rejects
    `NODE_NOT_CONNECTED`. Fresh is explicit and retains CallerContext.
R2. Before dispatching StartSeat, persist session, launch and command metadata atomically through
    the existing committed input seam; no command is sent before commit. Resume never silently
    falls back to fresh. Failed resume records lost and later rejects `RESUME_LOST` until Fresh.
R3. `SeatDown` records desired-down and dispatches one StopSeat for a current launch; absent is a
    no-op. Stop success makes session absent; failed/timed-out stop makes unknown with `stop-failed`.
R4. `SeatCapture` dispatches CapturePane whenever a launch exists, returns its PaneCapture or a
    fixed timeout reply, and changes no axis. A contradictory dead pane opens sources-disagree.
R5. `SeatSend` requires live node, present session, idle activity and no delivery in flight.
    Working/needs-input reject. Force permits unknown activity and records forced; all rejections
    use fixed reasons and no command row.
R6. Accepted send persists a deliver command and lead/body before dispatch, records one outcome
    (confirmed, submitted-unconfirmed, not-delivered, failed or unknown), and never reissues it.
    A lost node makes the delivery unknown; reconnect resend belongs to NodeProxy only.
R7. A confirmed delivery without matching prompt evidence opens sources-disagree after the next
    commit; activity follows harness events. Delivery bodies, tokens and raw evidence stay out of
    logs, traces and metrics.
R8. Restart/reload and node-link events preserve the 13-3 lifecycle contract: live values become
    unknown with the specified reason, gaps request capture and never fabricate events, and
    SeatActorRestarted is emitted before any retained work is released. Capture and delivery
    timeouts are bounded; cancellation never reissues or rolls back a committed command.
R9. Every transition/finding emits the specified structured log/metric/span names, with IDs,
    kinds, outcomes and reasons only. Command operations use seat.up/down/send/capture spans.

## Expected outputs: exact text

| ID | Change | Expected |
|---|---|---|
| `UP-reject` | unknown or disconnected up | `SEAT_STATE_UNKNOWN` or `NODE_NOT_CONNECTED`; no command |
| `UP-resume` | failed resume then ordinary up | `RESUME_LOST`; only explicit Fresh accepted |
| `DOWN-stop` | stop success/failure | absent on success; unknown/`stop-failed` on failure |
| `CAPTURE` | launch present / missing | PaneCapture returned; fixed timeout reply; no axis change |
| `SEND-reject` | working/needs-input/no-link/in-flight | fixed rejection; no deliver command |
| `SEND-outcome` | accepted delivery | one command and one of `confirmed`, `submitted-unconfirmed`, `not-delivered`, `failed`, `unknown`; no resend |
| `RELOAD` | restart/link gap | unknown overlay, capture request, `SeatActorRestarted` before queued work |
| `OBSERVE` | transition/finding | `aiakos.seat.transitions`, `seat.apply`/`seat.up`/`seat.down`/`seat.send`/`seat.capture`; no bodies/tokens |

## Tests

T1. Test CallerContext and all command/reply records, immutable fields and fixed rejection strings.
T2. Test up decisions, atomic pre-dispatch persistence, resume loss and explicit fresh attribution.
T3. Test down/capture outcomes, timeout bounds and no axis mutation.
T4. Test send acceptance/rejection, forced unknown, one delivery outcome, no reissue and node loss.
T5. Test restart/link overlays, gap capture, reload ordering, cancellation and queued work.
T6. Test structured logs/metrics/spans and absence of bodies/tokens/raw values.

## Definition of done

`dotnet build -c Release` and the orchestrator test project pass with no new warnings. All files
are LF UTF-8 with final newline. One conventional story commit; no transport/API/schema changes.

## Out of scope

13-3 persistence/region implementation, 13-5 restart/resync integration, 15-2 HTTP/API, 10-4
node authentication/replay, harness I/O, migrations, CLI, Akka remoting or clustering.
