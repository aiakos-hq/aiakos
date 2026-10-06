---
id: 12-1
title: "#12 slice 1 — Claude orchestrator adapter"
issue: 12
status: approved
route: impl
paths: [src/Aiakos.Orchestrator/Harnesses/, src/Aiakos.Orchestrator/Program.cs, src/Aiakos.Orchestrator/Aiakos.Orchestrator.csproj, tests/Aiakos.Orchestrator.Tests/Harnesses/]
date: 2026-10-06
---

# Brief: #12 slice 1 — Claude orchestrator adapter

Part of #12. Self-contained. **Do not read docs/specs/ or docs/adr/.** Read current resolved
records in Aiakos.Spec, the generated Node contract, Core.InputValidator and existing
Orchestrator.Seats.IHarnessStateProfile first. Where silent choose the simplest behavior,
record the choice in the commit body; never add a fallback or a missing neighbouring operation.

## Goal and baseline

Main06d4930 has resolved rig/seat records, pure seat state machine, empty Core.IHarnessAdapter
marker, node StartSeat/DeliverInput/SeatFile contracts and Core input validation. There is no
Claude adapter/profile, node driver/normalizer/relay/ingest, SeatActor shell, projection plan,
CallerContext API or command dispatch. AnalysisPR206 describes projection, not merged code.
This slice supplies a pure orchestrator adapter/profile/settings/launch/delivery API and its
DI registrations. It does not run Claude, generate guidance, copy skills, execute a relay,
persist native IDs, authenticate callers or dispatch commands.

BuildLaunch accepts caller-supplied projection and relay files. Later14-5 supplies projection
bytes, 12-2 supplies the actual relay, and 13-4/10-5 assemble the input and persist native IDs
before dispatch. This is an explicit application seam, not a default relay or assumed writer.
Maintainer decision of 2026-10-06 (qitem-20261006123052-cc0bd3ce): safe ~/ api-key
source references are accepted and expanded by the node's runtime HOME in a fixed helper
command; the orchestrator never looks up its own HOME. This analysis also clarifies spec0005
R36 in the same PR, which must be marked **read this one** for maintainer review.

Tests use named opaque file fixtures. No story needs those absent implementations to compile
or prove the pure adapter. Profile is independently useful to 13-3 as soon as it merges.

Risks checked:0005-RK8 generated disableAllHooks false and deny rule,0005-RK12 rotation
classification, and0006-RK5/0006-RK7 profile semantics; real state/liveness integration stays#13.
All other #12 risk-row items (0004-RK6/RK7,0005-RK1-RK7/RK9-RK11,0006-RK2/RK10,
0008-RK8,S0001-1 and M6-RK13) need node mechanics, real Claude or later end-to-end; this slice
does not close them. Api-key settings are specified here, but real api-key auth remains outside
M1 acceptance. No upstream Claude lookup is needed: the approved spec targets2.1.284.

## Files and public surface

All new interfaces/records live in Aiakos.Orchestrator.Harnesses; implementations in
Aiakos.Orchestrator.Harnesses.ClaudeCode. Add exactly one ProjectReference from Orchestrator to
existing Aiakos.Spec for resolved input types. Core marker stays unchanged; the richer local
interface inherits it. No Node reference, new package, proto/migration/pure-state changes,
Version on PackageReference, NoWarn or warning suppression. Tests use xUnitv3 conventions.

```csharp
namespace Aiakos.Orchestrator.Harnesses;
public interface IHarnessAdapter : Aiakos.Core.IHarnessAdapter
{
    string Harness { get; }
    Aiakos.Orchestrator.Seats.IHarnessStateProfile Profile { get; }
    LaunchSpec BuildLaunch(Aiakos.Spec.ResolvedSeatParameters seat,
        Aiakos.Contracts.Node.V1.LaunchMode mode, NativeSession session,
        IReadOnlyList<Aiakos.Contracts.Node.V1.SeatFile> suppliedFiles);
    DeliverySpec BuildDelivery(string authenticatedSender, string body, Guid commandId);
}
public sealed record NativeSession(string Id);
public sealed record LaunchSpec(IReadOnlyList<string> Argv, IReadOnlyDictionary<string,string> Env,
    IReadOnlyList<Aiakos.Contracts.Node.V1.SeatFile> Files, TimeSpan ReadyTimeout,
    Aiakos.Contracts.Node.V1.TerminalSize Terminal);
public sealed record DeliverySpec(string Lead, string Body, bool ExpectConfirmation,
    TimeSpan ConfirmTimeout);
// Namespace Aiakos.Orchestrator.Harnesses.ClaudeCode:
public sealed class ClaudeCodeStateProfile : Aiakos.Orchestrator.Seats.IHarnessStateProfile
{
    public ClaudeCodeStateProfile();
}
public sealed class ClaudeCodeSettings
{
    public ClaudeCodeSettings();
    public byte[] Build(Aiakos.Spec.ResolvedSeatParameters seat);
}
public sealed class ClaudeCodeDelivery
{
    public ClaudeCodeDelivery(ClaudeCodeStateProfile profile);
    public DeliverySpec BuildDelivery(string authenticatedSender, string body, Guid commandId);
}
public sealed class ClaudeCodeAdapter : IHarnessAdapter
{
    public ClaudeCodeAdapter(ClaudeCodeStateProfile profile, ClaudeCodeSettings settings);
}
```

Acceptance calls these APIs directly with resolved immutable snapshots. SuppliedFiles is the
harness-neutral file bundle seam, not a Claude hook member. A caller may serialize LaunchSpec
but must persist Profile.NewNativeSessionId before issuing StartSeat. Authentication supplies
the sender string; this API never reads sender identity from a delivery request body.

## General rules

G1. All builders/profile checks are pure: no file/process/stream/environment/network access,
    actual projection writing, secret reads, token production, timer or command dispatch.
    No logs/spans/metrics of body, settings, permissions, file bytes, peer detail, native config
    or credentials. No credentials in returned argv/env/settings/files; node-local secret paths
    are references, not values. This slice introduces no stream, so unread/early-close behavior
    belongs to the node/dispatch slices. A deterministic seam is direct builder calls with
    snapshots whose source tree is absent; no dependence on platform filesystem or live Claude.

G2. Resolved records follow the existing RigLoader invariants; do not invent exhaustive graph
    diagnostics. Validate explicit native ID/mode/auth/path/supplied-file/body boundaries below
    before returning a result. Never mutate inputs; returned collections/byte arrays/protos are
    independent snapshots. Null top-level argument throws ArgumentNullException using its
    parameter name. Other fixed exceptions below have no supplied values or arbitrary inner
    exceptions. No culture-sensitive parsing/sorting or platform string.Normalize. NUL/control
    and unpaired surrogate inputs in settings strings are fixed INVALID_LAUNCH; well-formed
    Unicode, including U+FFFE and non-BMP, remains intact through JSON escaping. Model is passed
    verbatim as one argv item except NUL/unpaired surrogate rejected INVALID_LAUNCH; no shell
    quoting or command-string joining. Permanent tests for each story include odd inputs and
    immutability, not only ASCII happy cases. Earlier behavior remains unchanged.

## Changes to earlier behavior

No earlier result changes. The marker stays valid and the richer interface is additive. Existing
fake profiles/state tests remain unchanged. New DI adds a Claude profile/adapter; absent node
capability never becomes advertised by an orchestrator registration.

## Rules

R1. Define the interface/records above and ClaudeCodeStateProfile. Harness="claude-code".
    NewNativeSessionId returns Guid.NewGuid lowercase D UUIDv4; IsValidNativeSessionId matches
    exactly [0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}, length36,
    no surrounding whitespace. Null/empty/uppercase/nonv4/invalid variant => false, never throws.
    FreshRelaunchReusesSessionId=true; EmitsInputResolved=false; ReadyTimeout15s,
    ConfirmTimeout5s, QuietTimeout10min. No random generation in BuildLaunch.

R2. Profile.IsReadiness true only for SessionStarted with source exactly startup/resume/fork/clear.
    IsConversationEvidence true only for PromptSubmitted/TurnEnded/TurnFailed, independent of
    attributes; the caller owns matching the event's native ID to the current session.
    IsSessionRotation true only for SessionStarted, source exactly clear and previous_session_id
    accepted by IsValidNativeSessionId. Missing/null attributes, unknown enums, compact/idle/tool/
    input events return false unless listed; comparisons ordinal/case-sensitive, no parsingraw
    payload, synthesized InputResolved or speculative idle detection. Do not modify state machine.

R3. ClaudeCodeSettings.Build uses resolved HarnessSettings; require Harness="claude-code",
    Auth subscription/api-key and PermissionMode one of default/acceptEdits/plan/auto,
    else InvalidOperationException("INVALID_LAUNCH"). Preserve allow/ask/deny strings/order and
    duplicates, append Bash(tmux:*) to deny only when not already exactly present. Retain empty
    arrays. Produce UTF-8/noBOM JSON with two-space indentation, LF line breaks and one final LF;
    default System.Text.Json encoder, no normalization. Exact property order: disableAllHooks,
    permissions(defaultMode,allow,ask,deny), hooks, statusLine, optional apiKeyHelper last.
    disableAllHooks=false. No additional settings key, wrapper, repo/user merge or file I/O.

R4. Hooks property order is SessionStart,UserPromptSubmit,PreToolUse,PermissionRequest,
    PostToolUse,PostToolUseFailure,Notification,Stop,StopFailure,PreCompact,SessionEnd. Each has
    one registration containing hooks=[{type:"command",command:"${AIAKOS_SEAT_HOME}/aiakos/bin/aiakos-hook-relay hook <name>",timeout:5}].
    PreToolUse/PermissionRequest/PostToolUse/PostToolUseFailure only have matcher:"*" before
    hooks; all others have no matcher. statusLine is {type:"command",command:
    "${AIAKOS_SEAT_HOME}/aiakos/bin/aiakos-hook-relay status statusLine"}, without timeout.
    No projected .claude/settings.json; all settings go in the --settings file.

R5. Subscription has no apiKeyHelper and no credential added. For api-key require exactly one
    ResolvedSeatSecret named anthropic_api_key. File must start with / or ~/ followed by a
    nonempty ASCII path using only [A-Za-z0-9._/-], no empty/./.. segment; otherwise
    InvalidOperationException("INVALID_LAUNCH"). Reject ~user/, relative paths, spaces,
    non-ASCII, quote, dollar, backtick, backslash and controls. Absolute source parsed helper
    is exactly "cat '<source File>'". For ~/ source remove that anchor and emit parsed helper
    exactly `cat "$HOME/<rest>"`; no orchestrator environment lookup/expansion. Node shell
    expands its HOME when Claude invokes the helper. Only that fixed syntax adds a dollar/quotes;
    supplied rest cannot inject them. For /keys/anthropic.key the final JSON property line is
    two ASCII spaces then `"apiKeyHelper": "cat \u0027/keys/anthropic.key\u0027"` and LF (no comma).
    For ~/.config/aiakos/secrets/anthropic_api_key it is two ASCII spaces then
    `"apiKeyHelper": "cat \u0022$HOME/.config/aiakos/secrets/anthropic_api_key\u0022"` and LF.
    These are exact file bytes; decoded helper quotes are not literal quote bytes in JSON.
    Never read/copy secret contents or create a SeatSecret proto, env credential, key value or
    secret fixture. Other secret references remain outside this adapter's result. Node12-4
    checks actual existence/mode; this pure API does not guess it.

R6. BuildLaunch checks session.Id first: invalid per R1 ->
    InvalidOperationException("INVALID_SESSION_ID"). Then permit Fresh/Resume; Fork ->
    InvalidOperationException("LAUNCH_MODE_NOT_SUPPORTED"), Unspecified/unknown ->
    InvalidOperationException("INVALID_LAUNCH"). Require resolved Harness=claude-code, safe ASCII
    node SeatDir/Workdir/ProjectionRoot anchored by / or ~/ (remaining characters
    [A-Za-z0-9._/-], no empty/./.. segment), ProjectionRoot=SeatDir+/projection,
    and seat/rig ASCII schema IDs, otherwise INVALID_LAUNCH. Tilde is retained verbatim;
    only the node knows its HOME. A loader-valid path containing space or non-ASCII is
    INVALID_LAUNCH here, matching the node shell-safe launch requirement, never silently quoted
    or normalized. These paths never replace placeholders in argv/settings. No native-ID mint/persist/fallback.
    Fresh argv exactly [claude,--session-id,<id>,-n,<seat>@<rig>,--settings,
    ${AIAKOS_SEAT_HOME}/aiakos/claude-settings.json,--add-dir,${AIAKOS_SEAT_HOME}/projection];
    Resume replaces --session-id with --resume and contains no --session-id. If Model nonnull
    append --model,<verbatim model>, including empty model as an empty single argv argument.
    Never use --continue,permission bypass,--bare,--safe-mode,--setting-sources or permission-mode.
    Env exactly AIAKOS_SEAT=<seat>@<rig>, CLAUDE_CODE_ADDITIONAL_DIRECTORIES_CLAUDE_MD=1,
    DISABLE_AUTOUPDATER=1. ReadyTimeout=Profile.ReadyTimeout, Terminal=160columns/45rows.

R7. SuppliedFiles contains exactly one projection/CLAUDE.md, zero or more
    projection/.claude/skills/<skill>/<relative path>, exactly one aiakos/bin/aiakos-hook-relay,
    and nothing else. Require root SeatHome, valid slash-separated relative path (no backslash,
    empty/./.. segments, control/unpaired surrogate, leading slash/drive), unique path ordinal,
    and nonnull bytes; otherwise INVALID_LAUNCH. Reject supplied settings/user/repo files, do
    not invent missing projection/relay bytes. Copy each content byte-exact, even non-UTF8.
    Output files path-ordinal sorted: projection files mode0644/Expand=false; relay0755/false;
    add generated aiakos/claude-settings.json mode0644/Expand=true from R3-R5. All roots SeatHome.
    Snapshot content/metadata independent of caller mutation; no checkout/workspace writes.
    Compose ClaudeCodeAdapter from the existing profile/settings and its owned ClaudeCodeDelivery
    helper; adapter.BuildDelivery delegates to that helper with identical results/errors.

R8. ClaudeCodeDelivery.BuildDelivery requires nonempty authenticatedSender without CR/LF/TAB/NUL/control or
    unpaired surrogate, nonempty Guid commandId; otherwise
    InvalidOperationException("INVALID_DELIVERY"). Apply Core.InputValidator.CheckBody;
    TooLarge -> InvalidOperationException("INPUT_TOO_LARGE"), NotAllowed ->
    InvalidOperationException("INVALID_INPUT"). Return its normalized Body (CRLF/CR=>LF,
    TAB retained, empty body permitted). For normal body lead is exactly
    "[aiakos from <sender> #<first8 lowercase Guid-D hex>] " (trailing one space);
    validate final lead through Core.CheckLead (<=1024bytes), else INVALID_DELIVERY.
    ExpectConfirmation=true, ConfirmTimeout=Profile.ConfirmTimeout. Never compare/persist body.

R9. Slash classification: after body normalization, trim only ASCII space/TAB/LF for classification.
    If first remaining character is '/', the entire trimmed text must be exactly /compact,
    otherwise InvalidOperationException("SLASH_COMMAND_NOT_ALLOWED"). This rejects /clear,
    /exit,/resume,/login,/compact arguments or multiple slash lines; no automatic escaping or
    fallback to normal delivery. Accepted /compact has Lead="", ExpectConfirmation=false,
    ConfirmTimeout5s; retain normalized original Body including its surrounding whitespace.
    A slash later in ordinary prose is ordinary text. Body validation R8 runs before slash rule.

R10. Program DI registers concrete profile/settings/adapter as singletons; IHarnessStateProfile
    and local IHarnessAdapter resolve their exact same concrete instances, adapter.Profile is
    the registered profile. Use TryAdd conventions to preserve explicit concrete/service test
    overrides; profile enumerable registration uses TryAddEnumerable descriptor so future other
    harnesses coexist, no duplicate Claude registration. Register only the local adapter seam,
    not an empty Core marker factory and not a node capability/driver. Dependency on existing
    Aiakos.Spec is a ProjectReference without package/version/warning changes. Builds do not
    depend on14-5/12-2: caller file snapshots are the application seam R7.

## Expected outputs: exact text

Base: resolved claude-code agent impl@aiakos-dev, subscription, modelnull, default permissions
allow=[Bash(dotnet build:*)], ask=[], deny=[Bash(git push --force:*)], seatDir=/srv/seats/impl,
workdir=/srv/seats/impl/repos/app, projectionRoot=/srv/seats/impl/projection. Native ID
11111111-1111-4111-8111-111111111111; commandId12345678-1234-4123-8123-123456789abc,
sender operator. Supplied files projection/CLAUDE.md bytes "guidance\n", skill
projection/.claude/skills/probe/SKILL.md bytes "skill\n", relay bytes "relay-fixture\n".
No source tree, caller API, real node or Claude exists in this fixture.

| ID | Change | Expected |
|---|---|---|
| `E1` | generated native IDs; lowercasev4/uppercase/nonv4/variant/whitespace/null checks | generated IDs match exact R1 regex; only lowercasecanonicalv4 accepted; no invalid-input exception; Harness claude-code, reuse true, inputResolved false, timeouts15s/5s/10min |
| `E2` | every HarnessEventKind/source and valid/missing previous_session_id | readiness only SessionStarted startup/resume/fork/clear; conversation only PromptSubmitted/TurnEnded/TurnFailed; rotation only clear with valid previousID; unknown/compact/idle/tool/input false; attributes untouched |
| `E3` | base settings and Unicode permissions; duplicate tmuxdeny; invalid mode/auth/harness/string | exact ordered LF/UTF8 JSON R3 with disableAllHooks false, preserved permission lists and exactly one appended tmux rule; valid Unicode preserved after parse; fixed INVALID_LAUNCH for invalid inputs, no platform-normalization exception |
| `E4` | settings hooks/status | exactly11 ordered hooks, only4 matcher*, timeout5, exact placeholder relay commands R4; statusLine command exact withouttimeout; noextra key |
| `E5` | subscription/api-key; absolute and ~/ source; missing/duplicate/unsafe reference | subscription has no helper/credential; /keys/anthropic.key decoded helper is cat '/keys/anthropic.key', exact final JSON line is two spaces then "apiKeyHelper": "cat \u0027/keys/anthropic.key\u0027" and LF; ~/.config/aiakos/secrets/anthropic_api_key decoded helper is cat "$HOME/.config/aiakos/secrets/anthropic_api_key", exact final JSON line is two spaces then "apiKeyHelper": "cat \u0022$HOME/.config/aiakos/secrets/anthropic_api_key\u0022" and LF; no host HOME lookup/file read/value; ~user/relative/dot-segment/space/non-ASCII/quote/dollar/backtick/backslash/control paths and missing/duplicate source fixed INVALID_LAUNCH |
| `E6` | Fresh/Resume; modelnull/empty/Unicode; invalidsession/Fork/path | exact R6 argv/env/terminal160x45/ready15s; Resume has no session-id, model single verbatimarg; invalidID beforeother checks INVALID_SESSION_ID, Fork LAUNCH_MODE_NOT_SUPPORTED, invalidmode/path INVALID_LAUNCH; /srv and ~/aiakos/seats anchors accepted unchanged with placeholder argv, loader-valid space/non-ASCII path fixed INVALID_LAUNCH; no credential |
| `E7` | projection/relay supplied files shuffled/mutated; missing/duplicate/unsafefile | exact bytes preserved, pathsorted roots SeatHome, projection0644false/relay0755false/settings0644true; snapshotimmutable, no checkout IO; malformed bundle INVALID_LAUNCH before result |
| `E8` | body CRLF/CR/TAB/noncharacters/control/surrogate/maxbytes; sender/commandid | normal lead exactly [aiakos from operator #12345678] plus one space; normalizedbody with TAB preserved, confirmationtrue5s; exact R8 INVALID_DELIVERY/INVALID_INPUT/INPUT_TOO_LARGE, no body echoed in error |
| `E9` | /compact,/clear,/exit,/resume,/login, slash arguments, multiline and ordinary prose | only trimmed /compact accepted: empty lead, confirmationfalse5s, original normalized body preserved; disallowed slash fixed SLASH_COMMAND_NOT_ALLOWED; ordinary prose normal R8 behavior |
| `E10` | production/test DI registration and resolution | exactly one Claude profile/adapter/settings singleton and matching adapter.Profile; explicit overrides preserved, other harness profile not replaced; no node capability and no missingrelay/projection dependency |

## Tests

Permanent tests in tests/Aiakos.Orchestrator.Tests/Harnesses/ClaudeCode, xUnitv3. No live Claude,
node/tmux/process/cwd/userhome access. Exact settings/launch fixtures are manually authored,
not generated by production implementation; sourcebytes intentionally absent from filesystem.

- T0. Each story commits input-snapshot/oddUnicode/no-secret regression tests for G1/G2;
    build earlier Orchestrator tests unchanged. Do not add real token-shaped fixture strings.
- T1. Commit E1/E2 profile matrix, strictnative-ID cases and newID v4/variant checks; integrate
    one ready/rotation/conversation example through existing pure state machine with this
    profile to prove current-session ID ownership remains caller-controlled (no state changes).
- T2. Commit byte-exact settings golden for E3-E5, including property/hook order, final LF,
    duplicate deny preservation, subscription and absolute/~/ API-path references, exact escaped
    helper bytes and no host HOME lookup. Test U+FFFE/nonBMP and fixed invalid
    surrogate/control rejection. Verify no environment/key content is read or returned.
- T3. Commit E6/E7 Fresh/Resume argv/env/files golden and malformed bundle cases. Bytes may
    be arbitrary opaque projection/relay evidence, never interpreted/executed. Input and output
    mutations cannot affect another call/result; two calls with same inputs byte-equal.
- T4. Commit E8/E9 delivery table with byte-limit boundary, senderlead overflow and Core body
    validation ordering, slash classification and one-character marker/space assertions.
- T5. Commit E10 service composition test with explicit pre-registered overrides and another
    fake profile; default adapter.Profile referenceequals registered profile. Earlier startup
    tests and dependency-direction tests remain green; no external provider fabricated.

## Definition of done

Release build0warnings/errors, affected Orchestrator tests and acceptance pass, earlier tests
stay green. LF/UTF8/noBOM/finalnewline. One local commit feat(claude): <story title> (#12),
body records silent choices and risk checks/remaining; use story.sh gate/retry. No push/PR.

## Out of scope

14-5 projection renderer/hash/copying;12-2 shellrelay/ingest;12-3 normalizer;12-4 node driver,
trust seeding, readiness, confirmer, executable/version/curl/flock checks;12-5 realClaude/E2E;
10-5 command dispatch/token creation;13-3 actor persistence;13-4 lifecycle/native-ID persistence;
apiCallerContext/authentication;Fork launch, node capabilities, autoadopt/restart, transcript
reading, personal ~/.claude.json/settings, checkout writes, M2OpenCode, CI/proto. No stubs.
