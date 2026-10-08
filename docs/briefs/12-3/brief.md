---
id: 12-3
title: "#12 slice 3 — hook normalizer and screen classifier"
issue: 12
status: approved
route: impl
paths: [src/Aiakos.Node/Harnesses/ClaudeCode/, tests/Aiakos.Node.Tests/Harnesses/ClaudeCode/, tests/Aiakos.Node.Tests/Fixtures/claude-code/2.1.284/]
date: 2026-10-08
---

# Brief: #12 slice 3 — hook normalizer and screen classifier

Part of #12. Self-contained. **Do not read docs/specs/ or docs/adr/.** Where silent, choose the
simplest behavior and name it in the commit body. Do not create a missing neighboring service.

## Goal and baseline

Provide pure, fixture-driven Claude payload normalization and screen classification. Covers
spec 0005 R21–R24 mapping, R27 rotation, AC1, and the pure classification part of R26. Transport
coalescing is owned by 12-2; readiness outcomes and driver composition by 12-4. Only 10-1's
merged generated protocol is required. Existing HarnessEventLimits is available on main from
10-2 and is reused; no unmerged 11-2 story, session host, ingest or driver is required.

The later driver feeds each launch's events in received order and retains the returned state.
The normalizer neither sorts source_seq nor deduplicates transport deliveries. Seat/launch IDs,
source_seq and receive time belong to the enclosing NodeEvent and remain the driver's concern.
The 12-2 IngestedPayload can be adapted by passing Name and Body; this slice never references
that pending type. The optional deliveryId argument is evidence already matched by the later
confirmer, not a value read from untrusted payload JSON. No marker registry is built here.

Risks exercised by permanent fixtures: 0005-RK7 payload compatibility, 0005-RK9 and 0006-RK2
parallel permission pairing, 0006-RK10 absence of Escape evidence. Fixtures document the M1
ambiguity of identical parallel tools; they do not claim it is solved or run a SeatActor.
Live upgrades, Escape behavior and real-Claude observations remain 12-5.
Dependency boundary for the separate 11-4 slice: this baseline has 11-2's process runner,
private client, launch registry and process snapshots. It does not have 11-2-5's launch commands
or 11-2-6..8's start lifecycle, attach facade and start diagnostics. 11-4 may assume the former
existing components, but not the latter capabilities until their stories merge; its planned
11-2 dependency is not discharged by this brief. None is a prerequisite for 12-3.

## Files to create or touch (nothing else)

| Path | What |
|---|---|
| src/Aiakos.Node/Harnesses/ClaudeCode/ | ClaudeHookNormalizer, ClaudeNormalizationState, ClaudeNormalizationResult, ClaudeScreenClassifier |
| tests/Aiakos.Node.Tests/Harnesses/ClaudeCode/ | xUnit v3 tests and fixture loader |
| tests/Aiakos.Node.Tests/Fixtures/claude-code/2.1.284/ | Sanitized JSON payloads, expected JSON and screen text |

No new package or project reference, Version on PackageReference, NoWarn, pragma warning disable
or SuppressMessage. No changes to project files: fixture tests read files via a solution-root
finder (walk ancestors from AppContext.BaseDirectory to Aiakos.slnx), not copied output assets.

## Public surface (exact names)

```csharp
// namespace Aiakos.Node.Harnesses.ClaudeCode
// HarnessEvent is Aiakos.Contracts.Node.V1.HarnessEvent.
public sealed class ClaudeNormalizationState
{
    public ClaudeNormalizationState(string currentSessionId);
    public string CurrentSessionId { get; }
    // All other state is private/internal; immutable snapshots, no public payload/tool inputs.
}
public sealed record ClaudeNormalizationResult(
    HarnessEvent Event, ClaudeNormalizationState State);
public static class ClaudeHookNormalizer
{
    public static ClaudeNormalizationResult Normalize(ClaudeNormalizationState state,
        string nativeName, ReadOnlyMemory<byte> body, string? deliveryId = null);
}
public static class ClaudeScreenClassifier
{
    public static string Classify(string capture);
    public static string ClassifyExitReason(string capture);
}
```

Later 12-4 and tests compile against these names. No IHarnessDriver, LaunchContext or ingest
interface is introduced. The state initially needs only CurrentSessionId; later stories extend
its internals without changing this surface. Until a story owns a mapping, that native name uses
the OTHER fallback; implement only owned rules and the context of dependencies.

## General rules

G1. Pure APIs: no file/process/network/environment access, timers, logging, state actors,
    keys, outcomes, sessions or secret storage. No static mutable launch state. Never log or
    expose raw/prompt/tool-input text through ToString of state/result (override result's
    generated ToString to return exactly "ClaudeNormalizationResult"). Event.Raw is deliberately
    returned as evidence; it is not a logging surface. State.ToString returns exactly
    "ClaudeNormalizationState". Input state and returned events from earlier calls remain
    unchanged, including when one state is used for two independent branches. Each call returns
    its own mutable protobuf event and copies raw bytes; mutating caller bytes or one returned
    event cannot change another result or retained pairing state.

G2. Additive APIs preserve every earlier result. Null state/nativeName/currentSessionId/capture
    throws ArgumentNullException with that parameter name; empty strings are allowed. Invalid
    payloads never throw or embed values in errors: malformed UTF-8/JSON, non-object roots,
    excessive JSON depth (>64), empty bytes use OTHER with no attributes/usage/session ID and
    unchanged state. Validate all JSON property names and string values recursively before
    extracting fields or changing state, including unknown fields and strings nested in
    tool_input. Any undecodable string (unpaired surrogate escape or invalid UTF-8 bytes) makes
    the whole payload malformed with that same OTHER/empty-fields/unchanged-state result;
    retain the R1 raw envelope. Do not treat just that field as absent. Valid objects ignore
    unknown fields. Expected scalar string fields accept strings only; booleans use lowercase true/false; numeric attribute fields accept JSON numbers
    and retain their number text. Missing/null/wrong-type fields are absent, never "null" or
    invented zero. Duplicate JSON properties use the last occurrence. Names/IDs compare ordinal,
    numbers use invariant culture. No UUID validation belongs in these pure fixture APIs.
    Every story adds permanent tests of its owned rules and applicable G1/G2 boundaries.
    Interim OTHER fallbacks for mappings owned by later stories are not permanent contracts;
    permanent fallback tests use only the unknown native name Mystery of E1.

## Changes to earlier behavior

None. NodeProgram, the node link, session host and existing helpers remain unchanged.

## Rules

R1. Every call returns one HarnessEvent, never null: Harness="claude-code", NativeName=argument
    (ignore body.hook_event_name), Origin=Live, RawContentType="application/json". Raw is original
    body, RawSize is original byte count, NativeSessionId is string session_id or empty. Parse
    and extract from the full body first, then call existing HarnessEventLimits.Apply to cut raw
    to 262144 bytes and each attribute value to 1024 UTF-8 bytes at a character boundary. Raw may
    end inside JSON/UTF-8; it is evidence bytes, not reparsed. RawTruncated is false at/below the
    limit, true above. Unknown names or unknown SessionStart source map OTHER with empty
    attributes and no Usage, retaining a valid string session_id. No INPUT_RESOLVED emitted.

R2. SessionStart source startup/resume/fork => SessionStarted with source, model, session_title
    string attributes; resume additionally gets numeric context_tokens and
    seconds_since_last_response. PreCompact => CompactionStarted with string trigger.
    Source compact => Compacted with only source=compact. Ordinary starts and compact do not
    overwrite CurrentSessionId: mismatches are left for the driver. SessionEnd except clear
    => SessionEnded with reason string, even if its ID differs (orphan event is never dropped).

R3. UserPromptSubmit => PromptSubmitted: turn_id from prompt_id, permission_mode from its string
    field, delivery_id only when caller supplies a nonempty deliveryId (do not infer it from
    prompt text or body.delivery_id). PreToolUse => ToolStarted: tool_name, tool_use_id, turn_id
    from prompt_id. PostToolUse/PostToolUseFailure => ToolFinished: tool_name, tool_use_id,
    numeric duration_ms; failure adds failed=true. Stop => TurnEnded: turn_id from prompt_id,
    boolean stop_hook_active. StopFailure => TurnFailed: turn_id from prompt_id and error from
    last_assistant_message, cut by R1 to 1024 UTF-8 bytes. Omit missing values, never copy prompt,
    tool_input, tool_response, cwd, transcript_path or arbitrary fields into attributes.

R4. Notification always preserves string notification_type. Types other than permission_prompt
    (including idle_prompt) => Other. PermissionRequest and permission_prompt are initially
    Other until the pairing story owns R7–R9; do not synthesize input resolution on absent hooks.

R5. statusLine => Telemetry. Usage uses proto optional presence: context_used_percent from
    context_window.used_percentage only if an integer fits uint32; context_window_tokens from
    context_window.context_window_size if integer fits ulong; context_used_tokens is checked sum
    of context_window.current_usage.input_tokens + cache_creation_input_tokens +
    cache_read_input_tokens, only when all three are present integer ulongs and sum fits ulong.
    For all integer usage fields, integer means a JSON number token with no fraction or
    exponent: 16 is eligible, 16.0 and 1e2 are absent even though mathematically integral.
    Ignore output_tokens. cost_usd from cost.total_cost_usd if finite nonnegative number;
    model_id from model.id string or empty. Invalid/negative/fractional/out-of-range integer
    values are absent, not clamped or rounded. Set Usage only if at least one optional value
    exists or model_id is nonempty. Explicit zero remains present. null current_usage makes
    used tokens absent independently of the other fields. Hooks never get Usage.

R6. statusLine attributes: rate_limit.five_hour.used_percent and .resets_at from
    rate_limits.five_hour.used_percentage/resets_at, same for seven_day; numbers retain JSON
    text. claude.version from version, session_name from session_name (strings),
    exceeds_200k_tokens from boolean. Omit null/wrong types and all other fields. No throttling,
    scheduling or sequence loss detection here: each statusLine input returns one Telemetry.

R7. Retain open PreToolUse entries in immutable launch-local state: string nonempty tool_use_id,
    string tool_name, object/array/scalar non-null tool_input. A repeated ID replaces its entry
    and becomes most recent. PostToolUse or PostToolUseFailure removes its matching ID; unrelated
    completions remove nothing. PermissionRequest => InputRequested with tool_name if present
    and request_id of the most recent still-open entry with the same tool_name and canonical
    tool_input, or "*" if none/missing/invalid fields. Never trust a payload.request_id.
    Matching does not close a tool. Two identical open tools choose the latest, an explicit M1
    limitation, not an invented disambiguation strategy.

R8. Canonical equality recursively sorts object keys ordinal, preserves array order, compares
    decoded strings/booleans/null by value and numeric tokens by original text (1 differs from
    1.0). Whitespace and object property order do not matter; duplicate keys use last occurrence
    recursively. Missing/null top-level tool_input cannot pair. Nested null is valid. Snapshot
    canonical data while parsing; no retained JsonElement pointing at a disposed document.

R9. State has a permission-echo flag, initially false. Every InputRequested sets it true;
    PromptSubmitted and ToolStarted reset it false. A permission_prompt Notification when false
    => InputRequested with notification_type=permission_prompt, request_id="*", sets flag true;
    when true => Other with only notification_type=permission_prompt. PermissionRequest always
    emits InputRequested even when flag is already true. Other events, completions and elapsed
    silence do not reset the flag; no fake Escape/timeout resolution or input-resolved event.

R10. SessionEnd reason clear => Other with reason=clear and payload's old ID. Keep current ID
    until a SessionStart source clear with nonempty string session_id arrives. That start =>
    SessionStarted with source=clear, previous_session_id equal to state's CurrentSessionId
    (even if initially empty), NativeSessionId new ID; returned state sets CurrentSessionId to
    the new ID and clears tool-pairing/echo state. Later rotations use the latest ID. A clear
    start with missing/empty ID => Other with empty attributes, unchanged state. A clear start
    requires no prior clear end; do not drop it when arrival order varies.

R11. No other event changes CurrentSessionId or attributes an absent session_id to it. A late
    orphan SessionEnd for an old ID is still SessionEnded and cannot roll the current ID back.
    No SessionObserved, registry/probe mutation or resume decision is performed by this API.

R12. Classify returns the first label whose case-insensitive ordinal substring matches, in this
    exact priority: trust-dialog for "Is this a project you trust?" or "Is this a project you
    created or one you trust?"; resume-picker for "Resume session"; login-required for "Not
    logged in"; api-key-dialog for "Detected a custom API key"; settings-error for "Error
    parsing settings" or "Invalid settings"; otherwise unrecognized. Strip ANSI CSI sequences
    (ESC [ parameters/intermediates final byte @ through ~) before matching; do not interpret
    terminal control actions. Empty/arbitrary captures => unrecognized. No broader keywords.

R13. ClassifyExitReason after the same ANSI stripping returns RESUME_SESSION_NOT_FOUND if it
    contains "No conversation found", otherwise SESSION_ID_IN_USE if it contains "is already
    in use", otherwise HARNESS_EXITED; case-insensitive ordinal, first match wins. This API only
    returns a reason string; it neither knows whether a pane died nor sets an outcome. Driver
    12-4 applies it only after pane death. Classify never returns these exit reason strings.

## Expected outputs: exact text

Base payload B is UTF-8 {"session_id":"session-a","prompt_id":"turn-a"}; initial state
CurrentSessionId="session-a". In rows below merge stated fields into B unless explicit bytes
are supplied. Kind names below omit the generated enum prefix; map keys/values are exact.
For payload rows, unlisted attributes and Usage are absent and the R1 envelope/raw fields apply.
Screen rows use literal capture strings and return only classification strings.

| ID | Input/change | Expected |
|---|---|---|
| `E1` | Mystery name with B; mismatched hook_event_name; invalid JSON/UTF8, [], null, empty body; otherwise valid objects containing session_id="\ud800", raw FF FE bytes inside a string, an undecodable property name/unknown string, or nested tool_input string "\ud800" | Other; valid B retains session-a, invalid/nonobject/undecodable-string payload has empty ID; empty attributes/no Usage; state unchanged; ToString exactly ClaudeNormalizationState / ClaudeNormalizationResult; null arguments per G2 |
| `E2` | SessionStart startup/resume/fork, model="haiku", session_title="impl", context_tokens=42, seconds_since_last_response=2; compact; PreCompact trigger=manual; SessionEnd reason=other with session_id=old | starts SessionStarted source=<source>, model=haiku, session_title=impl; resume also context_tokens=42, seconds_since_last_response=2; compact Compacted source=compact only; PreCompact CompactionStarted trigger=manual; orphan SessionEnded ID=old reason=other; current ID unchanged |
| `E3` | prompt permission_mode=default; caller deliveryId=delivery-a; pre/post tool_name=Bash, tool_use_id=tool-a, duration_ms=331; failure; Stop stop_hook_active=false; StopFailure last_assistant_message="Invalid API key" | PromptSubmitted turn_id=turn-a, permission_mode=default, delivery_id=delivery-a; ToolStarted tool_name=Bash, tool_use_id=tool-a, turn_id=turn-a; ToolFinished tool_name=Bash, tool_use_id=tool-a, duration_ms=331; failure also failed=true; TurnEnded turn_id=turn-a, stop_hook_active=false; TurnFailed turn_id=turn-a, error=Invalid API key |
| `E4` | idle_prompt/unknown Notification; 300 KiB valid PostToolUse (large tool_response), multibyte 1100-byte error; raw lengths 262144/262145 | notifications Other notification_type=<type>; post ToolFinished with extracted attributes E3, RawSize=307200, Raw.Length=262144, RawTruncated=true; error <=1024 bytes with complete final character; raw boundary false/true; no tool_response/prompt in attributes |
| `E5` | statusLine: used_percentage=16, size=200000, current_usage input=8/cache_creation=202/cache_read=32366/output=32, total_cost_usd=0.034351, model.id=haiku | Telemetry; Usage HasContextUsedPercent=true value16, HasContextWindowTokens=true value200000, HasContextUsedTokens=true value32576, HasCostUsd=true value0.034351, ModelId=haiku |
| `E6` | null used_percentage/current_usage; explicit zero; negative/fractional/overflow fields, integer fields with tokens 16.0 or 1e2, or ulong sum overflow; all fields null | null makes HasContextUsedPercent/HasContextUsedTokens=false; explicit0 remains present; invalid numeric field only absent; entirely empty usage => Usage=null; one status input yields one event, no hook usage |
| `E7` | statusLine version=2.1.284, session_name=impl, exceeds_200k_tokens=false; five_hour used_percentage=43/resets_at=1790684400, seven_day used_percentage=11/resets_at=1790823600 | claude.version=2.1.284, session_name=impl, exceeds_200k_tokens=false, rate_limit.five_hour.used_percent=43, rate_limit.five_hour.resets_at=1790684400, rate_limit.seven_day.used_percent=11, rate_limit.seven_day.resets_at=1790823600; null fields absent |
| `E8` | pre tool-a Bash input={"command":"echo ok","nested":{"b":2,"a":1}} then PermissionRequest Bash input reordered with spaces; unmatched name/input; matching tool finished before request | request InputRequested tool_name=Bash, request_id=tool-a; unmatched/finished request_id=*; canonical equality ignores object order/spacing, preserves array order, distinguishes 1/1.0 and missing/null input |
| `E9` | two identical open tools tool-a then tool-b; request; finish tool-b; request; failed finish tool-a; request; branch from earlier state | request IDs tool-b, tool-a, *; each event emitted; no earlier state mutated; two separate launches/branches cannot pair with one another; repeated tool-a ID becomes latest |
| `E10` | PermissionRequest then permission_prompt echo; another permission request; prompt then notification twice; pre tool then notification; idle_prompt/silence/completion | InputRequested then Other; another request still InputRequested; prompt resets then InputRequested request_id=* then Other; pre resets then InputRequested; idle_prompt Other; silence emits nothing and completion does not reset echo flag |
| `E11` | SessionEnd clear old session-a; SessionStart clear session-b; another clear session-c; missing-ID clear; late orphan end session-a | Other ID=session-a reason=clear/current=session-a; SessionStarted ID=session-b source=clear previous_session_id=session-a/current=session-b; next previous_session_id=session-b/current=session-c; missing clear Other/unchanged; orphan SessionEnded ID=session-a/current=session-c; rotated state has no old pairing/echo |
| `E12` | screen strings: trust phrases; Resume session; Not logged in; Detected a custom API key; Error parsing settings/Invalid settings; empty/generic prompt | respectively trust-dialog, resume-picker, login-required, api-key-dialog, settings-error, unrecognized; CSI/color/case variants same; mixed trust+resume => trust-dialog; no input/process/outcome effect |
| `E13` | No conversation found with session ID: example; Error: Session ID example is already in use.; unrelated/empty; both phrases | respectively RESUME_SESSION_NOT_FOUND, SESSION_ID_IN_USE, HARNESS_EXITED, RESUME_SESSION_NOT_FOUND; CSI/case variants same; Classify of first two => unrecognized |

## Tests

xUnit v3 as BackoffTests.cs. Fixture layout: <name>-<case>.json and <name>-<case>.expected.json;
expected files describe kind/native ID/complete attributes/optional Usage presence. Compare
contents, not just counts. Use /home/user paths and ordinary synthetic session/tool IDs.
S1 owns the internal static ClaudeFixtureLoader in
    tests/Aiakos.Node.Tests/Harnesses/ClaudeCode/ClaudeFixtureLoader.cs, namespace
    Aiakos.Node.Tests.Harnesses.ClaudeCode, with internal static string ReadText(string fileName)
    and internal static byte[] ReadBytes(string fileName). Both resolve fileName beneath
    tests/Aiakos.Node.Tests/Fixtures/claude-code/2.1.284/ via the ancestor solution-root finder
    described above; ReadText uses UTF-8, ReadBytes preserves exact bytes. The finder is private
    to this type. Later stories reuse these methods and do not define another loader or finder.
Every mapping owned by the current story gets a fixture; across the slice this includes orphan
ends, compact/clear, idle_prompt and unknown names. Screen stories use text/expected-label fixtures. Reconstruct abbreviated spike samples as valid JSON; identify synthetic cases in tests
(PermissionRequest, PostToolUseFailure, clear and classifier needles), never call them recorded
full captures. The rows above are the authoritative closed contract, no external spike reading
required. No automatic golden regeneration or new fixture tooling dependency.

- T1. Permanent envelope/fallback/null/invalid-field tests E1, all R2 source/end cases E2 and
    prompt/tool/turn fixtures E3. Prove caller delivery evidence cannot come from JSON/prompt.
    S1 creates ClaudeFixtureLoader with the solution-root finder and both shared read methods.
    Permanent OTHER fallback assertions use only Mystery (E1); do not pin the interim mappings
    of PermissionRequest, permission_prompt, SessionStart source clear, SessionEnd reason clear
    or statusLine. R2 source/end tests cover only startup/resume/fork/compact and non-clear ends.
- T2. Permanent Notification and truncation/snapshot/UTF8 tests E4, parse full 300 KiB fixture
    before cutting raw; absent scalar/null behavior and input/result state immutability G1/G2.
    Apply the same permanent-fallback restriction as T1; idle_prompt and unknown Notification
    types retain R4's final Other mapping and remain covered.
- T3. Permanent statusLine fixtures E5–E7 (before turn, after turn, post-compact zero, all-null,
    incomplete/invalid current_usage, overflow and rate limit omissions), asserting proto Has*
    presence independently. Same inputs yield byte-equivalent events and immutable state.
- T4. Permanent sequence tests E8–E10: nested canonical equality, arrays, duplicate keys,
    malformed nested tool_input strings per G2/E1 (Other, no state mutation), latest identical
    parallel tool, unrelated/failed completion, wildcard fallback, echo reset,
    launch isolation and independent branches. No events during silence; retain the known Escape
    gap without running a clock/actor or fabricating resolution.
- T5. Permanent rotation tests E11 including clear without preceding end, repeated rotation,
    orphan old ends, missing IDs, differing normal start ID, and clearing old tool/echo state.
- T6. Permanent classifier text fixtures E12/E13 for every label/reason, ANSI variants,
    priorities, empty/null/unknown input; compile-time API usage returns strings only.

## Definition of done

Release build at solution root: 0 warnings/errors. Node tests and acceptance green; earlier
results preserved. Tests need no Claude, tmux, listener, login or Docker. LF UTF8/noBOM/final LF.
One local commit feat(claude): <story title> (#12); body records silent choices and checked risks.
No push/PR. Gate and retry follow story.sh; stop/report a repeated failure after three attempts.

## Out of scope (do not implement, do not stub)

12-1 orchestrator/settings/profile; 12-2 relay/ingest/coalescing/loss tracking; 12-4 capability,
prepare/trust/readiness/outcomes/driver/confirmer/orphan-probe/registry wiring; 12-5 live Claude;
11-4 watcher/stop; SeatActor, NodeProgram/node link/DI, session ID validation, projection,
protocol changes, user config, CI, new packages, auth, secret filtering of returned raw evidence.
