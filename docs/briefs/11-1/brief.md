---
id: 11-1
title: "#11 slice 1 — session host interface, validators and the fake"
issue: 11
status: draft
route: impl/sonnet
paths: [src/Aiakos.Node/Sessions/, src/Aiakos.Core/, tests/Aiakos.Node.Testing/, tests/Aiakos.Node.Tests/, Aiakos.slnx, Directory.Packages.props]
date: 2026-10-01
---

# Brief: #11 slice 1 — session host interface, validators and the fake

Part of #11. Self-contained. **Do not read `docs/specs/` or `docs/adr/`**, with one exception:
`docs/specs/0004-tmux-session-host.md` lines 428–528 (the C# block of the interface and its
types). Read only those lines. Read `src/Aiakos.Node/`, `src/Aiakos.Core/Seams.cs` and
`tests/Aiakos.Node.Tests/` first for the conventions. Where the brief is silent, pick the
simplest behaviour and say so in the commit body.

## Goal

`ISessionHost` and its types exist in the node, with the validation rules and an in-memory
`FakeSessionHost` that behaves like the real host will. A shared contract suite pins that
behaviour; in this slice it runs against the fake, later against real tmux. No tmux, no process
is started anywhere in this slice.

## Files to create or touch (nothing else)

| Path | What |
|---|---|
| `src/Aiakos.Node/Sessions/*.cs` | The spec block's interface and types (namespace `Aiakos.Node.Sessions`), the extra types below, `LaunchValidator` |
| `src/Aiakos.Core/InputValidator.cs`, `src/Aiakos.Core/TmuxNames.cs` | Lead and body rules; session name from a seat address |
| `src/Aiakos.Core/Seams.cs` | Remove the placeholder `ISessionHost` and its comment line. Leave the others |
| `tests/Aiakos.Node.Testing/Aiakos.Node.Testing.csproj` | New class library, **not** a test project (see rule 1) |
| `tests/Aiakos.Node.Testing/*.cs` | `FakeSessionHost`, `ISessionHostRig`, `FakeSessionHostRig`, abstract `SessionHostContractTests` |
| `tests/Aiakos.Node.Tests/Aiakos.Node.Tests.csproj` | `ProjectReference` to the library |
| `tests/Aiakos.Node.Tests/Sessions/*.cs` | `FakeSessionHostContractTests`, validator tests |
| `Aiakos.slnx`, `Directory.Packages.props` | The new project; the two xUnit packages of rule 1 |

No `Version=` on `PackageReference`, no `NoWarn`, no `#pragma warning disable`, no
`[SuppressMessage]`. `Aiakos.Node` is AOT-compatible: no reflection in `src/`.

## Public surface

Copy the spec block as written, with these changes and additions (all in
`Aiakos.Node.Sessions` unless a namespace is given):

```csharp
// Change: SessionSpec gains one parameter after WorkingDirectory.
//   string SeatHome,                                 // absolute; needed by the *_FILE rule (rule 4d)

public sealed record TerminalSize(int Columns, int Rows) { public static TerminalSize Default { get; } }  // 160x45
public enum SubmitKey { CtrlM, None }
public enum ConfirmationOutcome { Confirmed, Unconfirmed, NotRequested }
public enum StopOutcome { Stopped, Killed, NotRunning }
public enum SessionHostAvailability { Available, Unavailable }
public sealed record SessionHostInfo(string Kind, string? Version, SessionHostAvailability Availability,
    string? Reason, IReadOnlyList<string> Capabilities);
public enum SessionHostErrorCode { Unavailable, InvalidArgument, InputNotAllowed, PayloadTooLarge, AlreadyRunning,
    OrphanDetected, NotFound, Busy, TmuxTimeout, TmuxFailed, LeftoverProcesses }
public sealed record SessionHostError(SessionHostErrorCode Code, string Message, bool Retryable,
    IReadOnlyDictionary<string, string>? Metadata = null);
public sealed class SessionHostException : Exception { public SessionHostError Error { get; } public SessionHostErrorCode Code { get; } }
public sealed record SessionLabels(int Schema, string Instance, string SeatId, string SeatAddress, string LaunchId, string Harness);
public sealed record RegistryExit(int? Code, int? Signal, DateTimeOffset ObservedAt, bool Reported);
public sealed record RegistryEntry(int Schema, string Instance, string State, string SeatId, string SeatAddress,
    string LaunchId, string Harness, string Socket, string SessionName, string? SessionId, string? PaneId,
    int? PanePid, ulong? PaneStartTime, DateTimeOffset CreatedAt, RegistryExit? Exit,
    IReadOnlyDictionary<string, string> Attributes);

public interface ILaunchFiles { bool ExecutableExists(string path); bool DirectoryExists(string path); }
public static class LaunchValidator { public static SessionHostError? Validate(SessionSpec spec, ILaunchFiles files); }

namespace Aiakos.Core;
public enum InputProblem { None, NotAllowed, TooLarge }
public sealed record LeadCheck(InputProblem Problem, string? Reason);
public sealed record BodyCheck(InputProblem Problem, string? Reason, string Normalized, bool LineEndingsNormalized, int Bytes);
public static class InputValidator
{
    public const int MaxLeadBytes = 1024;
    public const int MaxBodyBytes = 1024 * 1024;
    public static LeadCheck CheckLead(string lead);
    public static BodyCheck CheckBody(string body);
}
public static class TmuxNames
{
    public static bool TryParseAddress(string seatAddress, out string seat, out string rig);
    public static string SessionName(string seatAddress);    // "impl@aiakos-dev" -> "aiakos-dev_impl"; throws ArgumentException when invalid
}

namespace Aiakos.Node.Testing;
public sealed class FakeSessionHost : ISessionHost
{
    public FakeSessionHost(TimeProvider? time = null);
    public FakeLaunchFiles Files { get; }                               // everything exists until Remove(path)
    public void MakeUnavailable(string reason);
    public byte[] Received(SessionHandle session);                      // every byte the pane was sent, in order
    public void Print(SessionHandle session, IReadOnlyList<string> lines);   // appends screen lines for capture
    public void Exit(SessionHandle session, int? exitCode, int? signal);     // the pane's process ends
    public void Vanish(SessionHandle session);                          // the session disappears
    public bool StatusUnresponsive { get; set; }                        // GetStatusAsync answers Unknown
    public bool IgnoresGracefulStop { get; set; }                       // StopAsync then reports Killed
}
public interface ISessionHostRig : IAsyncDisposable
{
    ISessionHost Host { get; }
    SessionSpec NewSpec(string seat = "impl", bool ignoresGracefulStop = false);   // valid, rig "demo", new LaunchId
    string MissingDirectory { get; }
    Task<byte[]> ReceivedAsync(SessionHandle session);
    Task PrintAsync(SessionHandle session, IReadOnlyList<string> lines);
    Task ExitPaneAsync(SessionHandle session, int exitCode);
}
public abstract class SessionHostContractTests { protected abstract Task<ISessionHostRig> CreateRigAsync(); }
```

`Retryable` by code: only `Busy`, `TmuxTimeout` and `TmuxFailed` are retryable.

## Rules

R1. **The library project.** `tests/Directory.Build.props` makes every project under `tests/` an
   xUnit test executable. In `Aiakos.Node.Testing.csproj` set `<IsTestProject>false</IsTestProject>`
   and `<OutputType>Library</OutputType>`, remove the inherited package references
   (`<PackageReference Remove="xunit.v3" />`, same for `Microsoft.Testing.Extensions.TrxReport`),
   and reference `xunit.v3.extensibility.core` and `xunit.v3.assert` (add both to
   `Directory.Packages.props` with the version `xunit.v3` has). If it does not build clean this
   way, stop and report; do not work around it.
R2. **Harness-neutral.** No type or member names a harness, a hook or a screen.
R3. **Address and names.** A seat address is `<seat>@<rig>` with seat `^[a-z][a-z0-9-]{0,23}$` and
   rig `^[a-z][a-z0-9-]{1,39}$`. The session name is `<rig>_<seat>`.
R4. **`LaunchValidator`** returns the first failure in this order, or null. Messages name the
   field and never contain an environment value.
   a. `SeatAddress` invalid, `SeatId`, `LaunchId` or `Harness` empty, `SeatHome` not absolute
      (`/…`) → `InvalidArgument`.
   b. `Argv` empty; `Argv[0]` not absolute or `files.ExecutableExists` false; an element with
      NUL; an element ending in `;` → `InvalidArgument`.
   c. `WorkingDirectory` not absolute or `files.DirectoryExists` false → `InvalidArgument`.
   d. Environment: name not matching `^[A-Z_][A-Z0-9_]*$`; value with NUL, LF or CR; name `TMUX`,
      `TMUX_PANE` or starting with `AIAKOS_NODE_`; name containing `TOKEN`, `SECRET`, `PASSWORD`
      or `API_KEY` → `InvalidArgument`. One exception to the last: a name ending in `_FILE`
      whose value is an absolute path below `SeatHome` (starts with `SeatHome + "/"`, no `..`
      segment) is accepted.
   e. `Size` outside 80–500 columns or 24–200 rows → `InvalidArgument`.
   f. Packed size above 12288: the sum of (UTF-8 bytes + 1) over `Argv`, plus (name bytes + 1 +
      value bytes + 1) over `Environment` → `PayloadTooLarge`.
   g. `Attributes`: the sum of key and value UTF-8 bytes above 4096 → `PayloadTooLarge`.
R5. **Lead** (`CheckLead`). `NotAllowed` when: more than 1024 UTF-8 bytes; any character in
   U+0000–U+001F (TAB and LF included), U+007F or U+0080–U+009F; an unpaired surrogate; or the
   lead ends with `\;`. A lead ending in `;` is allowed. An empty lead is allowed.
R6. **Body** (`CheckBody`). First turn CRLF and lone CR into LF (`LineEndingsNormalized` says
   whether anything changed). Then `NotAllowed` when any character is in U+0000–U+001F other
   than LF and TAB, is U+007F, is in U+0080–U+009F, or is an unpaired surrogate. Then `TooLarge`
   when the normalized text is above 1048576 UTF-8 bytes. `Bytes` is that size.
R7. **Reasons never echo input.** `Reason` names the rule and the position (`control character
   U+001B at index 12`), never the text.
R8. **Fake: availability.** `Info` is kind `fake`, `Available`, capabilities
   `["session-host.tmux"]`. After `MakeUnavailable`: `Unavailable` with the reason, no
   capabilities, and every operation throws `Unavailable`.
R9. **Fake: start.** `LaunchValidator` with `Files`; a failure is thrown. A live session for the
   same seat → `AlreadyRunning` with metadata `launch_id` of the running launch. A dead one is
   replaced: its `PaneExited` is published first if it was not yet. The orphan probe is not
   called. The handle has `SessionName` from rule 3, increasing `SessionId` `$1…`, `PaneId`
   `%1…`, a fake `PanePid`, `ReadOnly` false. `StartAsync` sends nothing to the pane.
R10. **Fake: stale handles.** Every operation first checks that the handle's `LaunchId` and
    `PaneId` are the seat's current ones; otherwise `NotFound`.
R11. **Fake: delivery**, in this order: `CheckLead` and `CheckBody` (`InputNotAllowed` or
    `PayloadTooLarge` thrown, nothing received); an empty lead with an empty body is
    `InputNotAllowed`; dead pane → `NotFound`; take the input gate, or throw `Busy` at once;
    stage `BufferLoaded`; append the lead's UTF-8 bytes (`LeadTyped`); if the body is not empty
    append `ESC [ 2 0 0 ~`, the normalized body, `ESC [ 2 0 1 ~` (`BodyPasted`); wait
    `SubmitDelay`; for `SubmitKey.CtrlM` append `0x0D` (`Submitted`); call the confirmer, or use
    `NotRequested` when there is none; release the gate; return the report. Cancellation is
    observed between steps only; the report then has the stage reached.
R12. **Fake: resubmit.** `DeliveryContext.ResubmitAsync` appends one more `0x0D` and counts in
    `Resubmits`. A second call in the same delivery throws `InvalidOperationException` and
    appends nothing. `DeliveryContext.CaptureAsync` works while the gate is held.
R13. **Fake: keys.** More than 32 keys or an undefined enum value → `InvalidArgument`. Takes the
    gate (`Busy`). Appends: Enter `0D`, Escape `1B`, Tab `09`, Up `1B 5B 41`, Down `1B 5B 42`,
    Right `1B 5B 43`, Left `1B 5B 44`, CtrlC `03`, CtrlD `04`.
R14. **Fake: capture.** `HistoryLines` outside 0–10000 or `MaxBytes` outside 1–1048576 →
    `InvalidArgument`. The text is the last (`Size.Rows` + `HistoryLines`) printed lines, each
    with trailing whitespace trimmed, joined with LF, trailing empty lines dropped. Above
    `MaxBytes`: drop the oldest lines until it fits, but never a line of the last `Size.Rows`
    lines, and set `Truncated`. Works on a dead pane (`PaneDead` true). Never takes the gate.
R15. **Fake: status.** `Alive`; `Exited` with the code and signal given to `Exit` (null stays
    null, never 0); `Missing` after `Vanish` or after a stop; `Unknown` with a reason while
    `StatusUnresponsive`.
R16. **Fake: stop.** Dead or missing pane → `NotRunning` with the recorded exit status.
    Otherwise it cancels a delivery in flight, waits for nothing, and reports `Stopped`, or
    `Killed` when `IgnoresGracefulStop` (or the spec from `NewSpec(ignoresGracefulStop: true)`).
    The session is removed afterwards. Stop never takes the gate.
R17. **Fake: events.** `WatchAsync` yields `PaneExited` exactly once per launch when the pane dies
    without a stop, and `SessionVanished` once after `Vanish`. A stop publishes neither.
R18. **Fake: list, adopt, attach.** `ListAsync` returns one `Managed` listing per existing
    session (labels filled, `Registry` null). `AdoptAsync` returns the current handle for the
    listing and is idempotent. `GetAttachCommand` returns `["fake-attach", "-r", "=<session
    name>"]`, without `-r` when `readOnly` is false.
R19. **The fake never sends input on its own.** Bytes reach `Received` only through rules 11–13.

## Contract suite (`SessionHostContractTests`, one `[Fact]` each, names as given)

| ID | Test | Asserts |
|---|---|---|
| `K1` | `StartReturnsAHandleNamedAfterTheSeat` | `SessionName` `demo_impl`; status `Alive`; nothing received |
| `K2` | `StartRejectsAMissingWorkingDirectory` | `InvalidArgument`; `ListAsync` empty |
| `K3` | `StartRejectsASecondLaunchOfALiveSeat` | `AlreadyRunning`; the first session still `Alive` |
| `K4` | `StartReplacesADeadPane` | after `ExitPaneAsync(7)`, a new start works and one `PaneExited{7}` was published |
| `K5` | `DeliversLeadThenOneBracketedPasteThenSubmit` | received = lead ‖ `ESC[200~` ‖ body ‖ `ESC[201~` ‖ `CR`, for a one-line body and for a body with quotes, backticks, `$HOME`, a TAB, Greek text, `✓` and a blank line |
| `K6` | `KeepsALeadEndingInSemicolon` | the lead's bytes arrive with the `;` |
| `K7` | `NormalizesCrLfInTheBody` | LF received; report says normalized |
| `K8` | `RejectsEscapeInTheBody`, `RejectsANewlineInTheLead` | `InputNotAllowed`; nothing received |
| `K9` | `RejectsABodyOverOneMebibyte` | `PayloadTooLarge`; nothing received |
| `K10` | `ReportsNotRequestedWithoutAConfirmer` | stage `Submitted`, `NotRequested`, 0 resubmits |
| `K11` | `ReportsTheConfirmersTurnId` | `Confirmed` with the turn ID |
| `K12` | `AllowsOneResubmit` | one extra `CR`; `Resubmits` 1; the second call throws |
| `K13` | `RefusesASecondDeliveryWhileOneIsUnconfirmed` | the second throws `Busy`; received holds only the first |
| `K14` | `CaptureWorksWhileADeliveryWaits` | no `Busy` |
| `K15` | `SendsNamedKeys` | `Enter`, `Up`, `CtrlC` → `0D 1B 5B 41 03` |
| `K16` | `RejectsMoreThanThirtyTwoKeys` | `InvalidArgument` |
| `K17` | `CaptureDropsOldestLinesFirst` | 500 printed lines, `MaxBytes` 4096: `Truncated`, the last `Size.Rows` lines all present, the first line absent |
| `K18` | `CaptureWorksOnADeadPane` | `PaneDead` true |
| `K19` | `ReportsExitCodeOnce` | status `Exited` 7; exactly one `PaneExited` |
| `K20` | `StopReportsStopped`, `StopReportsKilledWhenGracefulStopIsIgnored`, `StopOfADeadPaneReportsNotRunning` | the outcome; status `Missing` afterwards; no event published |
| `K21` | `StopCancelsADeliveryInFlight` | the delivery returns with stage `Submitted`; the stop still completes |
| `K22` | `AStaleHandleIsNotFound` | after stop and a new start, the old handle throws `NotFound` on deliver, keys, capture and stop |

## Tests (`tests/Aiakos.Node.Tests/Sessions/`)

- T1. `FakeSessionHostContractTests : SessionHostContractTests` with `FakeSessionHostRig`.
- T2. `LaunchValidatorTests`: one case per clause of rule 4, including: `AIAKOS_SEAT_TOKEN_FILE` =
  `<seat home>/aiakos/seat-token.header` accepted; `AIAKOS_SEAT_TOKEN` rejected;
  `AIAKOS_SEAT_TOKEN_FILE` = `/etc/passwd` rejected; `…_FILE` with `<seat home>/../x` rejected;
  `AIAKOS_NODE_TOKEN` rejected; 79 and 501 columns rejected, 80 and 500 accepted; packed size
  12288 accepted and 12289 rejected; a message for a rejected value does not contain the value.
- T3. `InputValidatorTests`: one case per clause of rules 5–7, including: lead of 1024 and 1025
  bytes; lead with TAB; lead ending `;` and `\;`; body with ESC, with DEL, with U+0085, with a
  lone high surrogate; CRLF, lone CR, mixed; body of exactly 1048576 bytes and one more; a
  multi-byte body that is under the limit in characters and over it in bytes; a reason that
  does not contain the offending text.
- T4. `TmuxNamesTests`: `impl@aiakos-dev` → `aiakos-dev_impl`; invalid: `impl`, `Impl@demo`,
  `impl@d`, `impl@demo@x`, `im_pl@demo`.
- T5. `FakeUnavailableTests`: rule 8.
- T6. The existing `ArchitectureTests` must pass unchanged.

Style: file-scoped namespaces, `public sealed class`, sentence-style method names, as in
`tests/Aiakos.Node.Tests/BackoffTests.cs`.

## Definition of done

- `dotnet build -c Release` at the repo root: 0 warnings, 0 errors.
- `dotnet test --project tests/Aiakos.Node.Tests -c Release`: all green.
- `git grep -n "ISessionHost" -- src/Aiakos.Core` prints nothing.
- If the same test still fails after three attempts, stop and report what you tried.
- All files LF, UTF-8 without BOM, final newline.
- One commit on the current branch, subject
  `feat(node): session host interface, validators and fake (#11)`. The body lists every choice
  made where the brief was silent, and has the sentence "Risks: checks 0004-FU1 (the validator
  accepts AIAKOS_SEAT_TOKEN_FILE under the seat home and rejects AIAKOS_SEAT_TOKEN)." Do not
  push, do not open a PR.

## Out of scope (do not implement, do not stub)

Anything that runs tmux or any process; `TmuxSessionHost`, the registry on disk, the watcher,
process trees and signals; the socket and config parts of `TmuxNames`; the orphan probe's use;
mapping errors to the gRPC contract; `IHarnessDriver`; changes to `docs/`, `CLAUDE.md`, CI, or
any project not listed above.
