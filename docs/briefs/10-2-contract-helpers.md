---
id: 10-2
title: "#10 slice 2 — contract helpers and contract tests"
issue: 10
status: draft
route: impl/opencode
date: 2026-10-01
---

# Brief: #10 slice 2 — contract helpers and contract tests

Part of #10. Self-contained. **Do not read `docs/specs/` or `docs/adr/`.** Slice 1 is merged: read
`src/Aiakos.Contracts/Protos/aiakos/node/v1/node_link.proto` and
`tests/Aiakos.Contracts.Tests/NodeLinkProtoTests.cs` first; they define the messages and the test
style. Where the brief is silent, pick the simplest behaviour and say so in the commit body.

## Goal

Small pure helpers next to the generated contract, used later by both the orchestrator and the
node: protocol negotiation, capability names, the error catalogue, size limits, path rules,
placeholder expansion and a command validator. Plus contract tests for round trips and forward
compatibility. No network, no service, no client.

## Files to create or touch (nothing else)

| Path | What |
|---|---|
| `src/Aiakos.Contracts/Node/*.cs` | The helpers. File-scoped namespace `Aiakos.Contracts.Node`. One public static class per file |
| `tests/Aiakos.Contracts.Tests/*.cs` | Tests (below). Do not change `NodeLinkProtoTests.cs` |

`Aiakos.Contracts` is AOT-compatible: no reflection, no `dynamic`, no `Enum.Parse` on user input
in `src/`. (Tests may use reflection.) No new packages. No `Version=` on `PackageReference`, no
`NoWarn`, no `#pragma warning disable`, no `[SuppressMessage]`. Do not edit the proto.

## Public surface (exact names; later slices compile against them)

```csharp
namespace Aiakos.Contracts.Node;
using Aiakos.Contracts.Node.V1;

public static class NodeProtocol
{
    public const uint Major = 1;
    public const uint Minor = 0;
    public static ProtocolVersion Current { get; }                 // a new instance per call
    public static ProtocolVersion? Negotiate(ProtocolVersion? node, ProtocolVersion? orchestrator);
}
public static class NodeCapabilities
{
    public const string HarnessClaudeCode = "harness.claude-code";
    public const string CommandSendKeys = "command.send-keys";
    public const string LaunchFork = "launch.fork";
    public const string SessionHostTmux = "session-host.tmux";
    public static string ForHarness(string harness);               // "harness." + harness
}
public static class ErrorReasons      // one const string per row of the catalogue below, PascalCase name
{
    public static Error Create(string reason, string message, IReadOnlyDictionary<string, string>? metadata = null);
}
public static class LaunchReasons     // const strings: ReadyTimeout = "READY_TIMEOUT", ResumeSessionNotFound,
{ }                                   // HarnessExited, SessionIdMismatch, Stopped (same UPPER_SNAKE spelling)
public static class ContractLimits
{
    public const int MaxMessageBytes = 4 * 1024 * 1024;
    public const int MaxDeliverBodyBytes = 1024 * 1024;
    public const int MaxStartSeatFilesBytes = 2 * 1024 * 1024;
    public const int MaxPaneCaptureBytes = 1024 * 1024;
    public const int MaxHarnessRawBytes = 256 * 1024;
    public const int MaxAttributeValueBytes = 1024;
}
public static class SendKeyNames
{
    public static IReadOnlyList<string> All { get; }               // Enter Escape Tab Up Down Left Right C-c C-d, this order
    public static bool IsAllowed(string key);                      // ordinal, case-sensitive
}
public static class SeatPaths { public static bool IsAllowed(string path); }
public static class Placeholders
{
    public const string SeatHome = "${AIAKOS_SEAT_HOME}";
    public const string Workspace = "${AIAKOS_WORKSPACE}";
    public static string Expand(string text, string seatHome, string workspace);
    public static ByteString Expand(ByteString content, string seatHome, string workspace);
}
public static class CommandValidator
{
    public static Error? Validate(Command command, IReadOnlyCollection<string> capabilities);
}
public static class HarnessEventLimits { public static void Apply(HarnessEvent harnessEvent); }
```

## Rules

1. **Negotiate.** Either argument null, or majors differ → `null`. Otherwise a new
   `ProtocolVersion` with that major and the lower minor.
2. **Error catalogue.** `ErrorReasons.Create` sets `reason`, `message`, `metadata`, and `code` and
   `retryable` from this table. An unknown reason gets `ERROR_CODE_UNSPECIFIED`, not retryable.

   | Reason | Code | Retryable |
   |---|---|---|
   | `SEAT_NOT_FOUND` | `NOT_FOUND` | no |
   | `SEAT_ALREADY_RUNNING` | `ALREADY_EXISTS` | no |
   | `SEAT_NOT_READY` | `FAILED_PRECONDITION` | yes |
   | `SEAT_BUSY` | `FAILED_PRECONDITION` | yes |
   | `SEAT_STOPPING` | `ABORTED` | no |
   | `INVALID_SESSION_ID` | `INVALID_ARGUMENT` | no |
   | `ORPHAN_HARNESS_DETECTED` | `FAILED_PRECONDITION` | no |
   | `UNSUPPORTED` | `UNIMPLEMENTED` | no |
   | `PATH_NOT_ALLOWED` | `INVALID_ARGUMENT` | no |
   | `PAYLOAD_TOO_LARGE` | `RESOURCE_EXHAUSTED` | no |
   | `SESSION_HOST_ERROR` | `INTERNAL` | yes |
   | `INPUT_NOT_ALLOWED` | `INVALID_ARGUMENT` | no |
   | `INVALID_LAUNCH` | `INVALID_ARGUMENT` | no |
   | `SESSION_HOST_UNAVAILABLE` | `FAILED_PRECONDITION` | no |
3. **Paths** (`SeatPaths.IsAllowed`). Split on `/`. Allowed only when the path is not empty, has
   no `\`, no NUL, no `:`, and no segment that is empty, `.` or `..`.
4. **Placeholders.** Replace every exact occurrence of the two tokens, in one pass over the
   input: text that comes from a replacement is never expanded again. Nothing else is touched:
   not `$AIAKOS_SEAT_HOME`, not `${OTHER}`, not other casing. The `ByteString` overload does the
   same on bytes (tokens and values as UTF-8) and leaves every other byte as it is, valid UTF-8
   or not.
5. **Command validator.** Returns the first failure in this order, or `null`. Sizes are UTF-8
   bytes. It checks nothing else (no IDs, no timeouts, no lead or body characters).
   1. No `body` case set → `UNSUPPORTED`.
   2. `SendKeys`: `command.send-keys` not in `capabilities`, or any key not in `SendKeyNames` →
      `UNSUPPORTED`. An empty key list is valid.
   3. `StartSeat`: `NodeCapabilities.ForHarness(harness)` not in `capabilities` → `UNSUPPORTED`;
      `mode` not `FRESH`, `RESUME` or `FORK` → `UNSUPPORTED`; `FORK` without `launch.fork` →
      `UNSUPPORTED`; a secret with no `target` set → `UNSUPPORTED`.
   4. `StartSeat`: a file whose `root` is not `SEAT_HOME` or `WORKSPACE`, a file `path` that is
      not allowed, or a secret `file_path` that is not allowed → `PATH_NOT_ALLOWED`.
   5. `StartSeat`: sum of file `content` lengths above `MaxStartSeatFilesBytes` →
      `PAYLOAD_TOO_LARGE`.
   6. `DeliverInput`: `body` above `MaxDeliverBodyBytes` → `PAYLOAD_TOO_LARGE`.
   7. `CapturePane`, `StopSeat`: always valid.
6. **Messages never carry payload.** `Error.message` is a short English sentence that may name
   the field, the capability, the limit and the size, and never contains a body, a lead, an argv
   element, an environment value, file content or a secret value. A rejected path may be shown.
7. **Harness event limits.** `raw_size` = the original `raw` length, always. `raw` longer than
   `MaxHarnessRawBytes` is cut to exactly that many bytes and `raw_truncated = true`; otherwise
   `raw` and `raw_truncated` are untouched. Every attribute value longer than
   `MaxAttributeValueBytes` in UTF-8 is cut to the longest prefix that fits and ends on a
   character boundary. Keys and all other fields are untouched.

## Expected outputs (one test case each)

`caps` = all four capability constants unless the row says otherwise. "valid start" = `StartSeat`
with harness `claude-code`, mode `FRESH`, one file `{SEAT_HOME, "aiakos/claude-settings.json", 3 bytes}`.

| Case | Input | Expected |
|---|---|---|
| N1–N5 | `Negotiate`: (1.0, 1.0); (1.3, 1.1); (1.0, 1.2); (2.0, 1.0); (null, 1.0) | 1.0; 1.1; 1.0; null; null |
| E1 | `Create("SEAT_BUSY", "x")` | code `FailedPrecondition`, retryable true |
| E2 | `Create("NOT_IN_CATALOGUE", "x")` | code `Unspecified`, retryable false, reason kept |
| E3 | every catalogue row | code and retryable as in the table (one theory) |
| P1 | allowed paths: `a`, `aiakos/claude-settings.json`, `.claude/skills/x/SKILL.md`, `a..b/c` | true |
| P2 | not allowed: empty, `/etc/x`, `../x`, `a/../b`, `./a`, `a/./b`, `a//b`, `a/`, `a\b`, `C:x`, a path with NUL | false |
| X1 | `Expand("${AIAKOS_SEAT_HOME}/a ${AIAKOS_WORKSPACE}", "/h", "/w")` | `/h/a /w` |
| X2 | `Expand("$AIAKOS_SEAT_HOME ${OTHER} ${aiakos_workspace} $${AIAKOS_SEAT_HOME}", "/h", "/w")` | `$AIAKOS_SEAT_HOME ${OTHER} ${aiakos_workspace} $/h` |
| X3 | `Expand("${AIAKOS_SEAT_HOME}", "${AIAKOS_WORKSPACE}", "/w")` | `${AIAKOS_WORKSPACE}` |
| X4 | bytes `FF` + token `${AIAKOS_WORKSPACE}` + `FE`, workspace `/w` | bytes `FF 2F 77 FE` |
| V1 | `Command` with no body | `UNSUPPORTED` |
| V2 | `SendKeys [Enter, C-c]` | null; with caps lacking `command.send-keys`: `UNSUPPORTED` |
| V3 | `SendKeys [F1]`; `SendKeys [enter]`; `SendKeys []` | `UNSUPPORTED`; `UNSUPPORTED`; null |
| V4 | valid start | null |
| V5 | valid start with harness `opencode` | `UNSUPPORTED` |
| V6 | valid start with mode `UNSPECIFIED`; mode `(LaunchMode)99`; mode `FORK`; `FORK` with caps lacking `launch.fork` | `UNSUPPORTED`; `UNSUPPORTED`; null; `UNSUPPORTED` |
| V7 | valid start, file path `../x`; file root `UNSPECIFIED`; secret `file_path` `/abs` | `PATH_NOT_ALLOWED` each |
| V8 | valid start, secret with `env_var` set; secret with no target | null; `UNSUPPORTED` |
| V9 | valid start, files totalling exactly 2 MiB; one byte more | null; `PAYLOAD_TOO_LARGE` |
| V10 | harness `opencode` **and** file path `../x` | `UNSUPPORTED` (order) |
| V11 | `DeliverInput` body of 1 MiB of `a`; one byte more; 524289 times `é` | null; `PAYLOAD_TOO_LARGE`; `PAYLOAD_TOO_LARGE` |
| V12 | `CapturePane`; `StopSeat` | null; null |
| V13 | the `PAYLOAD_TOO_LARGE` error of V11 with a body that starts with `SENTINEL-BODY` | `message` does not contain `SENTINEL-BODY` |
| H1 | `raw` of 262144 bytes | unchanged, `raw_size` 262144, `raw_truncated` false |
| H2 | `raw` of 262145 bytes | length 262144, `raw_size` 262145, `raw_truncated` true |
| H3 | attribute of 1024 `a`; of 1025 `a`; of 600 `é`; of 1023 `a` + `é` | unchanged; 1024 bytes; 512 `é`; 1023 `a` |

## Tests (`tests/Aiakos.Contracts.Tests`)

Every test class carries `[Trait("Category", "Contract")]`. One class per helper
(`NodeProtocolTests`, `ErrorReasonsTests`, `SeatPathsTests`, `PlaceholdersTests`,
`CommandValidatorTests`, `HarnessEventLimitsTests`) with the cases above, plus:

- `CapabilityAndKeyTests`: the four capability strings and the nine key names, exact text and
  order; `ForHarness("claude-code")` equals `HarnessClaudeCode`.
- `ForwardCompatibilityTests` (build the bytes with `CodedOutputStream`):
  - a `ConnectRequest` with an extra field 1000 (varint 7) parses, and `ToByteArray()` returns
    the same bytes;
  - a `HarnessEvent` with `kind` = 99 parses, `(int)Kind == 99`, `Enum.IsDefined(Kind)` is false;
  - a `Command` whose only body is field 99 (length-delimited, 2 bytes) parses with
    `BodyCase == None`, and `CommandValidator.Validate` returns `UNSUPPORTED`;
  - a `SeatEvent` whose only body is field 99 parses with `BodyCase == None` and re-serializes to
    the same bytes.
- `RoundTripTests`: for **every** message type in `NodeLinkReflection.Descriptor.MessageTypes`,
  fill an instance through the descriptor accessors (string → `"x"`, bytes → 3 bytes, bool →
  true, numbers → 7, enum → its first non-zero value, message → filled recursively to depth 4,
  repeated → one element, map → one entry), with one instance per `oneof` case, then assert it
  equals itself after `ToByteArray()` and `Parser.ParseFrom`. Fields declared `optional` must
  report presence after the round trip, also when set to zero.

Style as in `NodeLinkProtoTests.cs`: file-scoped namespace `Aiakos.Contracts.Tests`,
`public sealed class`, sentence-style method names.

## Definition of done

- `dotnet build -c Release` at the repo root: 0 warnings, 0 errors.
- `dotnet test --project tests/Aiakos.Contracts.Tests -c Release`: all green, and
  `dotnet test --project tests/Aiakos.Contracts.Tests -c Release --filter-trait "Category=Contract"`
  runs the same tests. (Other test projects need Docker and WSL; do not run or change them.)
- If the same test still fails after three attempts, stop and report what you tried.
- All files LF, UTF-8 without BOM, final newline.
- One commit on the current branch, subject
  `feat(contracts): contract helpers and contract tests (#10)`. The body lists every choice made
  where the brief was silent, and has the sentence "Risks: this slice checks none of #10's open
  risks (0002-RK1, 0002-RK2, 0006-RK6, 0006-RK9)." Do not push, do not open a PR.

## Out of scope (do not implement, do not stub)

`NodeLinkService`, any client, authentication, handshake, heartbeat, reconnect; event buffer,
acks, replay; executing commands; validation of lead and body characters, IDs or timeouts;
pane capture truncation; mapping enums to stored strings; changes to the proto, `buf.yaml`, CI,
`docs/`, `CLAUDE.md`, or any other project.
