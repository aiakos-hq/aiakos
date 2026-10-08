---
id: 15-3
title: "#15 slice 3 — client commands"
issue: 15
status: draft
route: impl
paths: [src/Aiakos.Cli/, tests/Aiakos.Cli.Tests/, src/Aiakos.Core/, tests/Aiakos.Core.Tests/, Aiakos.slnx]
date: 2026-10-08
---

# Brief: #15 slice 3 — client commands

Part of #15, spec 0007 R26–R27, R34–R51, AC6–AC10, AC12, AC14, AC16 and the unit client/rendering test plan. Self-contained: implementers do not read specs or ADRs. Where silent, use the smallest private implementation, record the choice in the commit body, and add no commands, public types, retries, automatic starts or state writes.

## Goal and prerequisites

Make the existing command tree operate a running instance through the v1 API: up, down, send, capture, ps and attach. Preserve local dry run. The CLI is a client; it never starts an instance or changes database/actor state directly.

Analysis base already has the 15-1 parser, Core.InputValidator, Core.TmuxNames and Spec's resolved/canonical models. Remaining 15-1 stories provide CliRequestValidator, GitEnvChecker and CliApplication. 15-2 supplies Api.Contracts and its exact routes/records; 15-4 supplies instance configuration/discovery. These are external prerequisites, not implementation work in this slice. Each story's notes name its prerequisites. If a named surface is absent or changed at start, stop and hand the evidence to router; do not invent a substitute or build the neighboring slice.

The existing launch read record has spec_hash/binding_hash but no conversation-start projection hash. R8 deliberately emits a conservative guidance note on every resume; it does not claim to detect a guidance change. This covers changed guidance without a new persistence/API contract.

Risk scope: tests check the Escape explanation (0005-RK3), conservative guidance warning (0005-RK5) and recovery next steps (0007-RK7); they do not close real-harness guidance delivery, reboot recovery, host survival, WSL keepalive, packaging, ACL or publishing risks. No Windows manual run is required to declare a fake-API story done; the maintainer's spec0007 demo owns the live run.

## Files to create or touch (nothing else)

| Path | Purpose |
|---|---|
| src/Aiakos.Cli/ | API client, command handlers, renderers, input reader, source metadata and executable wiring; README scope |
| tests/Aiakos.Cli.Tests/ | permanent xUnit v3 fake-HTTP, fake-time, renderer and wiring tests |
| src/Aiakos.Core/TmuxNames.cs | additive exact attach argv helper shared with the node |
| tests/Aiakos.Core.Tests/, Aiakos.slnx | new xUnit v3 Core test project/reference and solution entry for additive helper tests |

No PackageReference Version, NoWarn, pragma warning disable or SuppressMessage. Add the CLI project references to Api.Contracts and Core in C2, without new packages. Use System.Net.Http/System.Text.Json and inherited test defaults. Do not reference Orchestrator, Node, Data, AppHost or Hosting.Wsl. Existing Core.InputValidator behavior is authoritative, including CR and CRLF normalization; do not implement a second validator.

## Public surface (exact names)

Types below are in Aiakos.Cli.Client unless noted. Each story creates only its owned types; no stubs. API payloads use Aiakos.Api.Contracts and its generated JSON context from 15-2. The response records mirror Data read records, but CLI never references Data.

```csharp
public sealed class CliApiException : Exception {
    public CliApiException(int exitCode,string reason,string detail,bool retryable, IReadOnlyList<string>? ambiguousAddresses = null,bool isServerProblem = false);
    public int ExitCode { get; }
    public string Reason { get; }
    public bool Retryable { get; }
    public bool IsServerProblem { get; }
    public IReadOnlyList<string> AmbiguousAddresses { get; }
} // Message is detail; no inner exception or request printer
public sealed class CliApiClient(HttpClient http,string cliVersion) {
    public Task<TResponse> GetAsync<TResponse>(string relativePath,CancellationToken ct);
    public Task<CliApiReply<TResponse>> PostAsync<TRequest,TResponse>(string relativePath,
        TRequest body,CancellationToken ct);
    public Task<TResponse> PutAsync<TRequest,TResponse>(string relativePath,TRequest body,CancellationToken ct);
}
public sealed record CliApiReply<T>(int StatusCode,T? Value);
public static class CliVersionPolicy {
    public static bool Compatible(string cliVersion,string serverVersion);
    public static Task<Aiakos.Api.Contracts.VersionResponse> CheckAsync(
        CliCommandContext context,bool changesState,CancellationToken ct);
}
public sealed class CliBodyReader {
    public Task<string> ReadAsync(Aiakos.Cli.CliRequest request,TextReader stdin,
        string currentDirectory,CancellationToken ct);
}
public sealed record CliSource(string Path,string? Commit,bool Dirty);
public sealed class CliSourceReader {
    public Task<CliSource> ReadAsync(string rigRoot,CancellationToken ct);
}
public static class CliSeatResolver {
    public static string Resolve(string input,IReadOnlyList<Aiakos.Api.Contracts.SeatStatusResponse> seats);
}
public sealed class CliCommandContext(CliApiClient api,TimeProvider timeProvider,string instance,
    string cliVersion,bool json,bool verbose,TextWriter stdout,TextWriter stderr) {
    public CliApiClient Api { get; }
    public TimeProvider TimeProvider { get; }
    public string Instance { get; }
    public string CliVersion { get; }
    public bool Json { get; }
    public bool Verbose { get; }
    public TextWriter Stdout { get; }
    public TextWriter Stderr { get; }
}
public static class CliCommandRendering {
    public static string Quote(string? value);
    public static string Rejection(string reason,string detail,string address);
    public static int WriteError(CliApiException error,TextWriter stderr); // owned by S2
}
public sealed class CliReadCommands {
    public Task<int> RunAsync(Aiakos.Cli.CliRequest request,CliCommandContext context,CancellationToken ct);
} // ps and capture only
public sealed class CliDownCommand {
    public Task<int> RunAsync(Aiakos.Cli.CliRequest request,CliCommandContext context,CancellationToken ct);
}
public sealed class CliSendCommand(CliBodyReader bodies) {
    public Task<int> RunAsync(Aiakos.Cli.CliRequest request,CliCommandContext context,
        TextReader stdin,string currentDirectory,CancellationToken ct);
}
public sealed class CliUpCommand(Aiakos.Cli.GitEnvChecker git,CliSourceReader sources) {
    public Task<int> RunAsync(Aiakos.Cli.CliRequest request,CliCommandContext context,
        string currentDirectory,CancellationToken ct);
}
public interface ICliAttachRunner {
    Task<int> RunAsync(string fileName,IReadOnlyList<string> arguments,CancellationToken ct);
}
public sealed class CliAttachRunner : ICliAttachRunner;
public sealed class CliAttachCommand(ICliAttachRunner runner) {
    public Task<int> RunAsync(Aiakos.Cli.CliRequest request,CliCommandContext context,
        string distro,string absoluteWslHome,bool isWindows,CancellationToken ct);
}
```

A generated-context generic request fails locally when its type is unsupported; never fall back to reflection serialization. Public collections are snapshots. The supplied HttpClient has an authenticated BaseAddress; the client does not own/dispose it. Context does not store a token or body. Beyond the named CliCommandRendering members, private renderer helpers are sufficient; public handler entry points above are the permanent test seam.

## General rules

G1. No delivery body, bearer token, secret value, canonical file contents, HTTP raw payload or full exception is printed, logged, tagged or included in a generated exception. Only capture prints pane evidence and ps prints the explicitly allowed read fields. Errors never echo user arguments. Anticipated IO/HTTP/JSON/process failures use the fixed messages below. Never automatically retry a mutation, relaunch a seat, start/stop an instance or delete any home/worktree/session. Cancellation stops owned waits/processes only.
G2. Human results stdout; diagnostics/notices/errors stderr; plain text, no ANSI. Output uses LF and one final LF. JSON is one compact document plus LF, snake_case, top-level api:"v1", declared field order below, explicit nulls; JSON-quote variable strings in human output (System.Text.Json default encoder). Quote(null) returns literal null. Never emit intermediate JSON or progress on stdout. API array order is preserved unless explicitly stated. Aggregate seat commands continue after a seat rejection but abort on unavailable/auth/version failures. Each operational handler RunAsync (CliReadCommands, CliDownCommand, CliSendCommand, CliUpCommand and CliAttachCommand) is its own command boundary: catch CliApiException from its complete invocation, call CliCommandRendering.WriteError(error,context.Stderr) once and return that result; do not let it propagate to S11 wiring. Lower-level components propagate exceptions. Preserve the per-seat rejection handling specified below before this outer catch. Cancellation propagates through lower-level components; each handler catches OperationCanceledException and returns1 with stderr `Command canceled.\n`, never reports success or sends further requests; ps --watch uses the R4 exit0/no-cancel-line exception. S11 catches failures during pre-handler discovery/local validation with the same WriteError member and cancellation mapping; it does not render again after a handler returns. R12 adds its verbose trace line once at the operational invocation boundary after a nonzero result. Empty selection is success with an empty result array.
G3. All waits use cancellable TimeProvider delays and a monotonic deadline. Poll immediately then every250ms, without a request after final outcome or deadline. Timeout does not resend or cancel a server command. Network/JSON failure is not an unknown-success fallback. Tests advance fake time; no wall-clock sleeps, owner homes, real seats, real instance or authenticated tool changes.

## Changes to earlier behaviour

C1. Replace only 15-1's operational `Command is not implemented yet.\n` result for up without dry-run, down, send, capture, ps and attach. Preserve parser grammar, local validation/help/version/dry-run bytes and package identity/version. Update only corresponding unsupported-command tests and CLI README scope. Instance commands retain 15-4's dispatch. All loader/Core/API/actor behavior remains unchanged except additive R10 helper.

C2. Add CLI references to Core and Api.Contracts without new packages. No execution wiring belongs to this foundation change.

## Rules

R1. CliApiClient uses HTTP/1.1, escaped path segments and query values, and generated contract serialization. Routes and payloads are the R1 table below. For up callers use PostAsync<UpRequest,JsonElement>; for down use PostAsync<object?,JsonElement> with null body (send no HTTP content and skip request serialization). These return the success envelope as a built-in JsonElement snapshot; handlers select the DTO by StatusCode and deserialize it through the generated context: up200 AlreadyUpResponse/up202 AcceptedResponse, down200 NoOpResponse/down202 CommandAcceptedResponse. Send uses PostAsync<SendRequest,CommandAcceptedResponse>, capture PostAsync<CaptureRequest,CaptureResponse>. JsonElement response handling does not change the API context. Each mutation includes X-Aiakos-Client-Version. 401=>exit5 `Authentication failed.`; unreachable/503=>exit3 `Instance is unavailable.`; 400=>exit2; 404/409/504=>exit1; VERSION_INCOMPATIBLE=>exit4. For statuses mapped to fixed diagnostics above (401/503), create local exceptions with IsServerProblem=false. For HTTP 504 from POST /v1/seats/{address}/capture, CliApiClient creates CliApiException(1,"CAPTURE_TIMEOUT","Capture timed out.",true) with IsServerProblem=false; it owns this fixed diagnostic mapping, so the capture handler uses its ordinary G2 outer catch and WriteError renders the local message. Other valid problem responses retain reason/detail/retryable and set IsServerProblem=true; malformed/unsupported response/status=>exit1 `Instance response is invalid.`. Never print raw response bytes; do not retry. Valid server error strings are JSON-quoted when rendered, preventing extra lines.

  R1 route table (all placeholders are escaped individually; omit rig query when absent):

  | Method/path | Request | Response |
  |---|---|---|
  | GET /v1/version | none | VersionResponse |
  | GET /v1/seats?rig=<rig> | none | SeatStatusResponse[] |
  | GET /v1/seats/{address} | none | SeatDetailResponse |
  | GET /v1/launches/{id} | none | LaunchResponse |
  | GET /v1/commands/{id} | none | CommandResponse |
  | GET /v1/nodes | none | NodeResponse[] |
  | PUT /v1/rigs/{rig} | RegisterRigRequest | 200 RegisterRigResponse |
  | POST /v1/seats/{address}/up | UpRequest | 200 AlreadyUpResponse or 202 AcceptedResponse |
  | POST /v1/seats/{address}/down | no content | 200 NoOpResponse or 202 CommandAcceptedResponse |
  | POST /v1/seats/{address}/send | SendRequest | 202 CommandAcceptedResponse |
  | POST /v1/seats/{address}/capture | CaptureRequest | 200 CaptureResponse or 504 CaptureTimeoutResponse |

  Problem JSON is exactly {"reason":<string>,"detail":<string>,"retryable":<bool>}; CaptureTimeoutResponse has the same fields. Missing/wrong-typed fields are invalid responses. No request-id header is required or consumed. CliApiException.AmbiguousAddresses is an immutable snapshot, empty by default; ToString never includes HTTP payloads.

R2. Each handler RunAsync calls CliVersionPolicy.CheckAsync exactly once after required local validation and before any other HTTP request; the application wiring does not repeat it. CheckAsync performs GET /v1/version, validates SemVer, writes the messages below to context.Stderr and returns VersionResponse. Incompatibility writes its diagnostic then throws CliApiException(4,"VERSION_INCOMPATIBLE","CLI and instance versions are incompatible.",false); WriteError returns its exit code without printing that diagnostic again. Invalid SemVer throws CliApiException(1,"INVALID_RESPONSE","Instance response is invalid.",false). Compatible means equal numeric major.minor, ignoring patch/prerelease/build; invalid SemVer is an invalid response. up/down/send refuse incompatible version before any seat lookup or mutation: exit4 stderr `CLI <quoted cli> and instance <quoted server> versions are incompatible.\n`. ps/capture/attach continue across versions with stderr `CLI and instance versions differ.\n` whenever complete version strings differ (patch differences also warn). Commands changing state work across patch/prerelease differences without warning. CliSeatResolver full addresses match exactly; short input matches Member. Resolve throws CliApiException for failures, never writes stderr. No match=>exit1 NOT_FOUND, Retryable=false/detail `Seat or rig was not found.`; multiple=>exit2 AMBIGUOUS_SEAT, Retryable=false/detail `Seat address is ambiguous.` with sorted full candidates in AmbiguousAddresses. CliCommandRendering.WriteError returns error.ExitCode. First, when error.IsServerProblem is true, it writes exactly `<reason>: <JSON-quoted error.Message>\n`, including for server VERSION_INCOMPATIBLE or AMBIGUOUS_SEAT, and does not apply the local branches below. The flag is set by the S1 client only for retained valid server problems under R1; all locally constructed exceptions use its default false. This generic server branch does not replace the per-seat R9 handling required by each handler before its outer catch. For local AMBIGUOUS_SEAT it writes the fixed detail plus LF, then stderr `Seats: <sorted JSON-quoted full addresses joined comma-space>\n`. For local VERSION_INCOMPATIBLE it writes nothing (CheckAsync already wrote the diagnostic); for all other local errors it writes error.Message as the fixed diagnostic specified in this brief, unquoted, with one final LF. It emits no stdout, trace IDs or raw exception text. S2 tests catch resolver/CheckAsync exceptions and call WriteError directly; later handler tests expect returned exit codes and rendered stderr, not propagated CliApiException. Resolve every explicit down address before sending any stop; deduplicate preserving first occurrence. Never guess an address.
R3. CliBodyReader consumes exactly the already-validated 15-1 source: text, --file relative to currentDirectory, or '-' using stdin. Strict UTF-8 file decoding (optional UTF-8 BOM stripped); no newline trimming; apply Core.InputValidator.CheckBody and use Normalized. Stream inputs stop once they cannot be accepted within the inclusive normalized1MiB limit (CRLF must not cause premature rejection); do not allocate an unbounded input. ReadAsync throws CliApiException on the following failures (Retryable=false): rejected control/unpaired surrogate/oversize/encoding=>exit2 Reason INVALID_BODY/detail `Delivery body is invalid.`; unreadable file/stdin=>exit2 Reason BODY_UNREADABLE/detail `Delivery body could not be read.`. No body appears in exception ToString, stderr, logs or spans. A trimmed body starting '/' without whitespace/newlines is a single slash command: only /compact accepted; otherwise exit2 Reason UNSUPPORTED_SLASH_COMMAND/detail `Only /compact can be sent in M1.`. Ordinary multiline/slash-containing prose remains text. Finish validation before instance discovery/version/HTTP; server remains authoritative.
R4. ps reads version, /nodes and /seats?rig=<escaped name> (omit query if absent); detail first resolves a short address from /seats then GET /seats/{address}. Header human text: `instance <quoted instance> <quoted server-version>\n`, then `node <quoted node> connected|disconnected\n` for each node. List uses unpadded delimiter ` | ` with exact header `SEAT | NODE | DESIRED | SESSION | ACTIVITY | RESUME | CTX | FINDINGS\n`. Addresses quoted with '*' appended when SpecDrift. Human seats render human SESSION and '-' for remaining columns except address; agent strings quoted, null node/desired '-'; null axes render `unknown (unspecified)`, unknown axes render `unknown (<quoted reason or "unspecified">)`; known activity with ActivityDetail renders `<quoted activity> (<quoted detail>)`. Null context '?' else invariant percentage+'%'; zero findings '-' else `<count> <quoted worst severity or "unknown">`. --wide adds columns `MODEL | LAST_EVENT` with quoted model or '?' and ISO8601 UTC date or '?'. JSON list fields api,instance,version,nodes,seats; nodes and seats are generated DTOs in contract declaration order (without rewriting axis values). Detail JSON fields api,instance,version,nodes,seat (SeatDetailResponse); human detail after header consists of `seat: <compact generated SeatStatusResponse JSON>\n`, `launch: <compact JSON or null>\n`, `transitions: <array JSON>\n`, `findings: <array JSON>\n`, `deliveries: <array JSON>\n`; preserve reasons/since and no command body. --watch refreshes nodes and list/detail every2s, same text per snapshot; Ctrl+C exits0 without cancel line. With --json --watch accumulate nothing: reject locally exit2 `JSON watch is not supported.\n` before discovery. A successful read exits0 even if an axis is unknown.
R5. capture resolves the address via list, then POST capture with HistoryLines (parser default200,1..10000). Text stdout is pane text unchanged except CRLF=>LF and ensure final LF; no header/interpretation. JSON fields api,text,truncated,pane_dead. 504=>exit1 `Capture timed out.\n`; SEAT_NOT_PRESENT=>exit1 rejection R9. Never infer state or send keys/input from text. The command does not change tmux or an axis.
R6. down explicit addresses use R2; --rig/--all lists and filters agent Kind only (never POST humans), preserving API order. Process seats sequentially: POST one selected agent and complete its polling/output before POSTing the next. The 60s deadline is per accepted seat, measured from its POST response;200 no_op renders not-running,202 polls /commands/{id} up to60s. Only stopped/killed/not-running are success; pending/null outcome polls, any other final outcome exits1 with its reason; expiry renders unknown with reason timeout and exits1. --no-wait renders accepted with command id and exits0 on202 (acceptance only); no-op still not-running. Human stdout one line `<quoted address> <outcome>` plus ` (<quoted reason>)` if present, LF. JSON fields api,seats; each row address,outcome,reason,command_id (null if no-op/rejected), in that order. Rejection row outcome rejected and reason server reason. Overall exit1 for any unsuccessful seat. Never remove seat files.
R7. send resolves one address, POST SendRequest(normalized body,force). Client never changes force based on state; server rejects working/needs-input even with force. --wait none renders accepted/id exits0 on202. Otherwise poll command for the selected --timeout seconds (default1800), terminal Outcome is confirmed/submitted-unconfirmed/not-delivered/failed/unknown; only confirmed exits0. confirmed without TurnId is invalid response. --wait turn after confirmation polls detail until Activity idle=>0, needs-input/unknown=>1 or deadline=>1 (same total deadline). Observe matching confirmation before idle can terminate, so a pre-submit idle does not succeed. Human line `<quoted address> <outcome>`, then ` turn <quoted turn-id>` when present, then ` activity <quoted final activity>` only for --wait turn; reason when present is ` (<quoted reason>)`, LF. JSON fields api,address,outcome,reason,command_id,turn_id,activity (nullable). Expiry has outcome unknown, reason timeout; confirmed-but-turn-timeout retains confirmed/turn-id, reason timeout, activity unknown and exits1. Rejections use R9, no stdout result. No resubmission or delivery body output.
R8. up loads the rig and git-env diagnostics exactly as 15-1 local dry run before discovery; loader errors=>exit2/no HTTP. Validate requested --seat IDs against loaded rig before discovery: unknown/human=>exit2 `Selected seat is not an agent seat.\n`; duplicates deduplicated, process loaded rig order. PUT loaded Canonical.ResolvedJson, Contents, hashes and tool version/source to registration; never recanonicalize or reread source file bytes. Human summary `rig <quoted name> revision <n> spec_changed <true|false> binding_changed <true|false>\n`. Launch selected agents sequentially in rig order: POST one seat and finish its polling/output before POSTing the next. The ready_timeout+30s deadline is per accepted launch, measured from its POST response; list human seats as `<quoted address> human, not launched\n`. Registration drifted agents never receive up even with fresh; render drifted, reason `run aiakos down <address> then aiakos up`. Non-drifted POST up(fresh,note);200 already-up renders already-up success;202 polls launch immediately/every250ms until Outcome nonnull or ready_timeout+30s deadline. M1 Claude ready_timeout15s =>45s; do not implement another harness. Outcome ready success, all other outcomes/timeout=>1; print Decision verbatim with Outcome and OutcomeReason. --no-wait accepted success, Decision null. Use R13 source metadata; when Commit is null emit stderr `Git source metadata is unavailable.\n` once before registration. Every resume/resume-unverified emits stderr `guidance changes apply to fresh sessions or after /compact\n`, conservatively even if guidance unchanged. Human row `<quoted address> <decision or -> <outcome>` plus parenthesized quoted reason when present. JSON fields api,rig,revision,spec_changed,binding_changed,seats; row fields address,kind,decision,outcome,reason,launch_id,command_id,guidance_note (bool), in that order, null absent IDs/reason/decision. Human row JSON outcome human-not-launched; drifted outcome drifted is informational success (no restart requested of an already running seat); rejection outcome rejected exits1. Never use fresh to restart a drifted seat automatically. JSON diagnostics remain on stderr, no dry-run summary document before registration result.
R9. Rejection stderr is `<reason>: <explanation>\n`; reason is stable server UPPER_SNAKE_CASE. Exact explanations: SEAT_WORKING=>`The seat is working on a turn; input now could interleave. Wait (--wait turn on your last send) or look with aiakos capture.`; SEAT_NEEDS_INPUT=>`The seat waits for a decision in its pane (permission or question). Answer it with aiakos attach --write. If it was already denied with Escape, Claude Code sent no event; type the next prompt in the pane.`; SEAT_ACTIVITY_UNKNOWN=>`Activity is unknown. Check with aiakos capture; if the seat is idle, --force sends anyway and records that decision.`; SEAT_NOT_PRESENT=>`No running harness. Run aiakos up.`; DELIVERY_IN_FLIGHT=>`A previous delivery has no outcome yet.`; SEAT_STATE_UNKNOWN=>`Session is unknown. Use aiakos capture to look, then aiakos down and aiakos up.`; RESUME_LOST=>`The conversation does not exist any more. Run aiakos up --fresh --seat <address> --note "restart".` (substitute validated full address); NODE_NOT_CONNECTED=>`The seat's node is not connected. Run aiakos instance status.`; SLASH_COMMAND_NOT_ALLOWED=>`Only /compact can be sent in M1.`. For other reasons print `<reason>: <JSON-quoted server detail>\n` unchanged in meaning, exit from R1. No response body or local exception fallback. JSON stdout stays empty for a whole-command failure; aggregate up/down includes rejected row and emits this stderr.
R10. Create tests/Aiakos.Core.Tests with inherited xUnit v3 conventions/reference to Core and add its solution entry (it does not exist at analysis base). Add Core.TmuxNames.AttachArguments(string instance,string absoluteHome,string seatAddress,bool readOnlyMode=true) returning exact argv tmux,-u,-f,absoluteHome.TrimEnd('/')+"/tmux/tmux.conf",-L,SocketName(instance),attach-session,optional-r,-t,`=`+SessionName(address). Existing AttachCommand stays byte-for-byte unchanged; helper reuses existing address/instance validators; absoluteHome must start with / and contain no NUL, CR or LF. Use Linux slash joining, never Windows Path.Combine/IsPathFullyQualified for this parameter. Invalid home throws ArgumentException `Invalid tmux host options.`. CLI attach resolves seat via list/detail; missing launch=>SEAT_NOT_PRESENT; human=>exit2 `Cannot attach to a human seat.\n`. Non-Windows=>exit2 `Attach requires Windows.\n` before process/config IO. Run wsl.exe argv -d,distro,--exec, plus helper arguments with inherited terminal streams; --write omits -r, first stderr `Typing in the pane can collide with deliveries and is invisible to Aiakos until a hook reports it.\n`. Return exact child exit code; start failure=>exit3 `Attach could not be started.\n`; cancellation stops only its own wsl child, never tmux/server/harness. Default instance config Ubuntu/.aiakos is not guessed: R11 supplies configured distro and resolved absolute WSL home. Never launch a shell or concatenate argv.
R13. CliSourceReader runs git -C <root> rev-parse --show-toplevel, rev-parse HEAD and status --porcelain with redirected streams/10s bound each; outside git, missing/failing git or a 10s process timeout=>Commit null,Dirty false; do not print git output. Commit successful HEAD is trimmed hex, dirty true for nonempty porcelain; Path absolute rig root. No diagnostics are written by ReadAsync; its nullable Commit communicates unavailable metadata to R8. Cancellation propagates and stops only owned git children.

R11. Wire the production CliApplication and Program only after C2 and operational handlers plus 15-1/15-4 integration prerequisites merge. Preserve original CliApplication(GitEnvChecker) and 15-4 overload(GitEnvChecker,IInstanceCommands); add overload(GitEnvChecker,IInstanceCommands,ICliOperationalCommands). ICliOperationalCommands.RunAsync(CliRequest,string currentDirectory,string cliVersion,TextWriter stdout,TextWriter stderr,CancellationToken ct) returns Task<int>. Implement CliOperationalCommands with an injectable HttpClient factory, ConnectionStore, IInstancePlatform, TimeProvider, TextReader stdin and ICliAttachRunner (constructor may be private/composed; inject all effects for tests). Reuse InstanceLayout.Resolve and ConnectionStore.ReadAsync (dev ReadDevAsync); Windows home/config is15-4-owned. Missing/dead/unreachable=>exit3 stderr `Instance is unavailable.\n`; up adds `Run aiakos instance start.\n`. Read only secrets/api-token strict UTF8; missing/unreadable/empty=>exit5 `Authentication failed.\n`; trim terminal CR/LF only. Set request Authorization Bearer, never HttpClient defaults shared across invocations. For attach read InstanceConfigurationJson and resolve WSL $HOME through Aiakos.Wsl.IWslProcessRunner with argv -d,distro,--cd,~,--exec,sh,-c,`printf '%s\n' "$HOME"`, timeout10s; trim final LF, require an absolute slash path (otherwise exit3 `WSL home could not be resolved.\n`), then slash-join configured relative home; do not duplicate preflight/install/management. Add Aiakos.Wsl project reference only in this wiring story if required for the existing home-resolution port; no new WSL helper. Pass cancellation to every operation and dispose only per-invocation owned HTTP/resources. Special local body/watch/platform validation happens before discovery; help/version/dry-run never discover. Parser/validator failures preserve15-1 bytes/codes. Errors use fixed CliApiException mapping, R9 problems, and no raw exception text. Dispatch instance commands solely to IInstanceCommands. Default constructors use real production implementations when prerequisite surfaces exist, not test-only dispatch.
R12. One root ActivitySource("Aiakos.Cli") activity cli.up/down/send/capture/ps/attach surrounds an operational invocation, including polls. Context creation/version precheck is inside this root. CliApiClient propagates that traceparent on every request (and keeps one root through watch/polls). Allow tags cli.command,http.route,api.reason only; no URL query/body/note/token/response text/user inputs. Verbose errors add stderr `trace_id: <quoted root trace-id>\n`; normal errors omit IDs. Configure OTLP only when discovered connection.OtlpEndpoint is present, using existing centrally managed OpenTelemetry OTLP export package references (no version changes); flush bounded to1s at disposal, even if export hangs/fails (no success altered). Dry run/help/version do not create an exporter. Do not add OTLP dependencies in the pure transport story: exporter production composition belongs to this story.

## Expected outputs: exact text

Fixtures use full address impl@demo, version0.1.0, fixed Guid 11111111-1111-1111-1111-111111111111, turn t1 and strict API DTO declaration order. Each output covers all cases explicitly listed in its rule; independent literals, not renderer-derived expected values. Absent HTTP effects are asserted with a recording fake handler.

| ID | Input | Expected |
|---|---|---|
| `E1` | transport success/errors/auth/malformed/routes | R1 codes/details exactly; mutation header0.1.0; response401 emits `Authentication failed.\n`, unreachable emits `Instance is unavailable.\n`; no second POST |
| `E2` | version/address matrix |0.1.0 vs0.1.9-rc.1 compatible;0.2.0 incompatible; mutation stderr `CLI "0.1.0" and instance "0.2.0" versions are incompatible.\n`, exit4 zero mutation; ambiguous impl addresses impl@aa,impl@bb: stderr `Seat address is ambiguous.\n` then sorted `Seats: "impl@aa", "impl@bb"\n`; valid server 404 `{"reason":"NOT_FOUND","detail":"x","retryable":false}` from GET /v1/seats (ps): IsServerProblem=true, WriteError stderr `NOT_FOUND: "x"\n`, exit1, empty stdout; local NOT_FOUND: IsServerProblem=false, stderr `Seat or rig was not found.\n`, exit1 |
| `E3` | text/file/stdin boundaries and slash | valid CRLF/CR normalize with existing validator; exact1MiB accepted, over/control/surrogate/bad UTF8 exit2 fixed R3 text; /compact accepted, /clear locally refused, no discovery; sentinel absent from ToString/output |
| `E4` | ps axis/human/drift/context/findings/wide/detail/watch | R4 exact columns/header/documents; unknown with reason, never null=>0%; arrays preserve order;2s refresh; cancellation watch0; json-watch exit2 before HTTP |
| `E5` | capture successful flags/no launch/timeout | text `hello\n`; JSON `{"api":"v1","text":"hello","truncated":true,"pane_dead":false}\n`; capture HTTP 504 body `{"reason":"CAPTURE_TIMEOUT","detail":"Seat capture timed out.","retryable":true}` becomes the R1 local exception (IsServerProblem=false), exit1, empty stdout, literal stderr `Capture timed out.\n`; zero inferred input |
| `E6` | down no-op/stopped/killed/failure/no-wait/expiry | no-op `"impl@demo" not-running\n`; accepted `"impl@demo" accepted\n`; timed out `"impl@demo" unknown ("timeout")\n`, exit1; JSON rows include null command id for no-op; no humans stopped |
| `E7` | all delivery outcomes/force/rejection/turn/timeout | confirmed text `"impl@demo" confirmed turn "t1"\n`; turn idle adds ` activity "idle"`; needs-input/unknown exit1; wait none accepted0; JSON golden below; no early idle success and no body output |
| `E8` | registration/select/human/drift/fresh/resume/poll/error | loaded rig order, canonical bytes/files copied exactly, invalid selection zero HTTP; fixed summary/rows R8; drift zero up;45s timeout unknown1; guidance stderr on both resume decisions; no fresh fallback |
| `E13` | source metadata | private clean repo yields absolute rig Path, trimmed hex HEAD, Dirty=false; edited tracked file Dirty=true; unavailable git yields Commit=null, Dirty=false and no diagnostics; cancellation propagates |
| `E9` | every known/unknown rejection | R9 exact lines; needs-input mentions Escape; RESUME_LOST includes validated address; arbitrary detail JSON-quoted; aggregate continues only for seat failures |
| `E10` | Core helper/attach argv/write/exit/platform | exact R10 argv incl -u,-f,-L,-r,-t,=demo_impl; --write warning before child; return child7 unchanged; non-Windows exit2 before effects; existing helper unchanged |
| `E11` | real application wiring with injected effects | all six commands use production handlers; missing connection exit3 and up next-step; malformed token5; dev ReadDevAsync only; dry-run/help/version unchanged with zero discovery; instance commands15-4 unchanged |
| `E12` | activity/export/verbose/cancellation | one root cli.send with identical trace-id on version/list/send/polls; no sentinel body/note/token in logs/spans/exception; disabled exporter absent; enabled flush<=1s; fixed verbose line R12, ordinary errors omit IDs |

E4 list golden with no nodes/seats is `{"api":"v1","instance":"release","version":"0.1.0","nodes":[],"seats":[]}\n`; corresponding text is `instance "release" "0.1.0"\nSEAT | NODE | DESIRED | SESSION | ACTIVITY | RESUME | CTX | FINDINGS\n`. For one human lead@demo append `"lead@demo" | - | - | human | - | - | - | -\n`. For agent impl@demo, Node=wsl-local, Desired=up, Session=present, Activity=unknown, ActivityReason=node-link-lost, Resumability=resumable, SpecDrift=true, Context=null,OpenFindings=1,WorstSeverity=warning append `"impl@demo"* | "wsl-local" | "up" | "present" | unknown ("node-link-lost") | "resumable" | ? | 1 "warning"\n`.

E6 no-op JSON is `{"api":"v1","seats":[{"address":"impl@demo","outcome":"not-running","reason":null,"command_id":null}]}\n`.
E7 confirmed JSON is `{"api":"v1","address":"impl@demo","outcome":"confirmed","reason":null,"command_id":"11111111-1111-1111-1111-111111111111","turn_id":"t1","activity":null}\n`.
E8 no seats registration JSON is `{"api":"v1","rig":"demo","revision":1,"spec_changed":true,"binding_changed":true,"seats":[]}\n`; text `rig "demo" revision 1 spec_changed true binding_changed true\n`.

## Tests

Use existing Cli.Tests conventions, fake HttpMessageHandler with queued independently specified contract JSON/statuses, temporary files, fake time and recording process ports. No live instance/owner state. A renderer golden must be an independent literal/document, not generated by the component under test. Keep all tests in repository; later acceptance gates are external.

T1. Commit E1 transport route/header/generated contract/problem/cancellation tests, asserting no retries or response bytes in exceptions.
T2. Commit E2 SemVer/full/unique/ambiguous/unknown address matrix, local-versus-server WriteError cases and version pre-mutation tests.
T3. Commit E3 strict input/source/1MiB/control/slash/no disclosure tests, including CRLF raw byte expansion and interrupted streams.
T4. Commit E4 complete list/detail golden matrices across all axis values, Unicode/quoting, zero/null context, humans/drift/findings/wide/watch, order and cancellation.
T5. Commit E5 capture flags/timeout/no-launch golden, parser lines bounds and zero text interpretation.
T6. Commit E6 down recording request order/dedup/selection/pre-resolution/no-op/no-wait/terminal/timeout/mixed failure matrices.
T7. Commit E7 send exact JSON/text goldens for each outcome/wait mode, force forwarding, confirmed turn-id validation, shared deadline, early idle and no resubmit.
T8. Commit E8 real-loader temporary fixture plus fake-API registration/load error/select/order/human/drift/fresh/resume/no-wait/timeout tests with per-seat deadlines and sequential request order.
T13. Commit E13 source-reader tests in a private git repository when git exists, never an owner checkout write; cover outside-git, missing/failing git, dirty/clean, timeout and cancellation without leaked process output.
T9. Commit E9 rejection matrix exact explanations and arbitrary reason/detail preservation with newline safety.
T10. Commit E10 Core additive helper tests and recording attach process/platform/config/write/cancellation tests, proving inherited terminal behavior via process start configuration.
T11. Commit E11 production application/Program composition tests and executable fake-API smoke using private home/config/token fixtures; no production test-only switches or global installation.
T12. Commit E12 ActivityListener/recording exporter/verbose/cancellation tests with sentinel text omitted; ensure no second root per request and bounded flush.

## Definition of done

Release build zero warnings/errors; Cli.Tests and Core.Tests pass, existing CLI local behavior tests unchanged except C1. Story acceptance gate defines done; do not add a manual Windows demo requirement. LF UTF-8 no BOM and final newline. One local commit per story, conventional feat(cli) subject mentioning #15; commit body lists choices and the limited risk scope. No push or PR by implementer.

## Out of scope (do not implement or stub)

15-1 parser/grammar or dry-run changes, API/actor/schema changes, instance discovery/config/host management supplied by15-4, WSL preflight/install or terminal host changes, release payload/version/pipeline and comprehensive docs/cli.md (15-5), real authenticated Claude/Windows acceptance demo (#16). No automatic reconciliation, restart, fresh fallback, input from capture, API keys command or shared state writer.
