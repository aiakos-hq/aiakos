---
id: 15-1
title: "#15 slice 1 — CLI skeleton and local dry run"
issue: 15
status: approved
route: impl
paths: [src/Aiakos.Cli/, tests/Aiakos.Cli.Tests/, Directory.Packages.props, Aiakos.slnx]
date: 2026-10-06
---

# Brief: #15 slice 1 — CLI skeleton and local dry run

Part of #15, spec0007 R1–R5/R30–R33 and command reference. Self-contained; implementers
need not read specs or ADRs. Where silent, use the smallest private implementation; do not add
commands, side effects, output fields, public types, or fallback behavior. Record private choices.

## Goal and current prerequisites

Replace the published preview greeting with the real System.CommandLine command tree and a
fully local `up --dry-run`. This slice supplies parsing and validation for future commands,
not their API or host implementation. Several pure concerns can proceed in parallel after S1.

At reviewed main base c2a1bd5, `src/Aiakos.Cli/Program.cs` only prints its version or a preview greeting;
its package is Aiakos 0.0.1-preview.1. RigLoader.Load, DiagnosticFormatter and ResolvedRig are
on main. No local `/v1` API (15-2), CLI API client/operational handlers (15-3), detached released
instance host/configuration/secrets/connection discovery (15-4), or release payload/tag pipeline
(15-5) exists. Dry run must need none of them: no HTTP, token/connection file, database, Docker,
WSL, node, tmux, checkout creation or file projection. It reads rig/agent/env/reference files via
the existing loader and may execute only the read-only git tracking check specified below.
Do not reference Orchestrator, Node, Data, Hosting.Wsl, or AppHost. Do not invent an API or host.

Risks register #15 checked: 0007-RK1–RK9, 0004-RK5, 0005-RK3/RK5 remain open; this slice
checks only local validation/CLI packaging identity, not final bundled package limits, process
survival, ACLs, WSL keepalive, recovery, delivery semantics, or trusted publishing. PR names
that limited scope. Next analysis candidates: 15-2 can define API DTO/auth/address/revision
contracts independently of this CLI; actor-backed handlers need13-3/13-4. 15-4's configuration,
runtime copy and supervisor can be briefed against existing composition/runner ports, but its
healthy-start/API shutdown integration needs15-2 and node lifecycle. 15-3's pure renderers can
be briefed after API DTOs settle. Final15-5 payload/release smoke waits on host/runtime layout.

## Files

| Path | Purpose |
|---|---|
| src/Aiakos.Cli/ | command parser, local renderers, git checker, application and Program; README |
| tests/Aiakos.Cli.Tests/ | new xUnit v3 project using inherited test defaults; parser/renderer/git/application tests |
| Directory.Packages.props | one central System.CommandLine2.0.12 package entry |
| Aiakos.slnx | register Cli.Tests only |

S1 creates project references to Aiakos.Spec and the centrally managed System.CommandLine;
Cli.Tests references Cli and Spec. Later stories add source files/tests, not shared project setup.
Use current repo formatting/test conventions. No package Version on references, NoWarn,
warning suppressions or new test framework. Production CLI remains OS-neutral in this slice.

## Public surface (Aiakos.Cli namespace)

```csharp
public sealed record CliRequest(string Command, string Instance, bool Json, bool Verbose,
    IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, IReadOnlyList<string>> Options);
public sealed record CliParseResult(CliRequest? Request, string? Error, string? Help, bool Version);
public static class CliCommandLine
{
    public static CliParseResult Parse(string[] args, string? environmentInstance);
}
public static class CliRequestValidator
{
    public static string? Validate(CliRequest request);
}
public static class DryRunRenderer
{
    public static string Text(Aiakos.Spec.ResolvedRig rig);
    public static string Json(Aiakos.Spec.LoadResult result, IReadOnlyList<string> notices);
}
public sealed record GitCheckResult(IReadOnlyList<Aiakos.Spec.Diagnostic> Diagnostics,
    IReadOnlyList<string> Notices);
public sealed record GitInvocationResult(bool Started, int? ExitCode);
public interface ICliGitRunner
{
    Task<GitInvocationResult> RunAsync(IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);
}
public sealed class CliGitRunner : ICliGitRunner
{
    public Task<GitInvocationResult> RunAsync(IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);
}
public sealed class GitEnvChecker(ICliGitRunner runner)
{
    public Task<GitCheckResult> CheckAsync(string rigRoot, string envPath,
        string currentDirectory, CancellationToken cancellationToken);
}
public sealed class CliApplication(GitEnvChecker gitChecker)
{
    public Task<int> RunAsync(string[] args, string currentDirectory,
        string? environmentInstance, string version, TextWriter stdout,
        TextWriter stderr, CancellationToken cancellationToken);
}
```

All methods above are real production components, not a test-only command tree. Public parser
results use immutable snapshots (no castable mutable backing collections); command paths are
space-separated (`instance init`), option keys are long names without `--`, flags have an empty
value list, repeat seat values preserve order, defaults below are present in Options. Positional
Arguments are exactly consumed positionals; absent optional positionals are an empty list.
Each story implements only the types/methods needed by its owned rules; no empty placeholders
for other stories. No hidden `instance host` command, dynamic plugin commands or extra public dispatch contracts.

## General rules

G1. No argument/env/rig content, git stderr, delivery text, credentials or full exception text is
    echoed in a CLI-generated error. Loader diagnostics are emitted only through the existing
    loader/formatter (which already sanitizes credential evidence). Never serialize the resolved
    canonical file contents, secrets or bindings beyond the explicit summary fields. Values in
    summary text are JSON-quoted strings (null prints literal null), preventing line injection.
    No color/ANSI is emitted in this slice (plain text satisfies non-terminal/NO_COLOR behavior).
    Inputs are not mutated. Public null arguments are rejected with ArgumentNullException;
    process boundary catches anticipated IO/path/process failures into the fixed results below.
G2. LF output with final LF; JSON exactly one compact document plus LF, snake_case api:v1,
    deterministic field order as R5. Cancellation stops pending git, propagates from checker,
    and application returns1 with stderr `Command canceled.\n` and no new stdout. Any private
    disposable process is disposed; never kill any owner process, server, pane or git repository.

## Changes

C1. Replace preview greeting and placeholder README examples with command help and local dry
    run. `-v` becomes verbose, not a version alias; only `--version` is version. Keep PackageId
    Aiakos, ToolCommandName aiakos, AssemblyName aiakos, RollForward Major, PackAsTool true,
    IsPackable true and version0.0.1-preview.1. Remove placeholder description in csproj/README
    and say only local dry run is implemented; operational commands arrive in later slices.
    Earlier placeholder greeting expectation changes; no other project behavior changes.

## Rules

R1. Add System.CommandLine2.0.12 centrally and create the production command tree. Parse fresh
    per call, using System.CommandLine parsing, not a hand-written token parser. `--help`/`-h`
    for root and each command/group returns generated help; no args gives root help. `--version`
    only at root (no subcommand/positionals), optionally with global options, returns Version=true. Help/version require no instance/path lookup.
    Generated help must contain the exact command/option names below and an appended plain
    paragraph `Exit codes: 0 done; 1 unsuccessful outcome; 2 invalid input; 3 instance unavailable; 4 incompatible version; 5 authentication failed.`
    Do not golden-test library wrapping/localized boilerplate. Root description is
    `Aiakos: a file-defined control plane for teams of AI coding agents.` Command descriptions
    use the Purpose phrases in the grammar table below. Unsupported invocations do not succeed.
R2. Grammar (all names case-sensitive; System.CommandLine standard `--` end-of-options applies):
    recursive global options `--instance <name>`, `--json`, `-v|--verbose`; explicit instance
    wins over nonempty AIAKOS_INSTANCE, then release. Instance slug matches
    `[a-z][a-z0-9-]{0,62}`; invalid resolves to fixed usage error, no path access.
    One occurrence per scalar/flag option; repeated `--seat` allowed one value per occurrence.
    Numeric options invariant integers; timeout positive1..86400, port-base1..51535,
    lines1..10000. No abbreviation/unrecognized commands/options or response-file expansion.
    Missing/wrong/extra tokens, numeric bounds, duplicates and invalid instance produce
    CliParseResult(null,"Invalid command line.",null,false), never token-bearing parser errors.
    Valid result Request has Error/Help null and Version=false; help/version Request is null.

| Command | Positionals | Local options with defaults | Purpose |
|---|---|---|---|
| instance | none | none | Manage a released instance. |
| instance init | none | port-base optional, distro Ubuntu, node-id wsl-local, database-url-file optional, force flag | Initialize instance configuration. |
| instance start | none | timeout90 | Start the instance host. |
| instance stop | none | keep-seats, keep-database flags | Stop the instance host. |
| instance status | none | none | Show instance status. |
| instance run | none | none | Run the instance host in the foreground. |
| up | rig-dir optional | env optional, seat repeat, fresh, note optional, dry-run, no-wait | Validate a rig and launch seats. |
| down | seat zero or more | rig optional, all, no-wait | Stop selected seats. |
| send | seat required, text optional (including literal -) | file optional, force, wait delivery, timeout1800 | Deliver input to a seat. |
| capture | seat required | lines200 | Capture pane evidence. |
| ps | seat optional | rig optional, wide, watch | Show seat state. |
| attach | seat required | write | Attach to a seat pane. |

    Group `instance` with no child gives group help. Defaults are strings as shown; absent
    optional scalars/flags/repeat options have no dictionary key. Unknown nonempty environment
    values only affect parsing when no explicit instance overrides them. Null/empty env selects
    release. A syntactically valid help/version request bypasses instance-value validation:
    `--help` and `--version` with AIAKOS_INSTANCE=BAD return help/version respectively (exit0);
    `--instance BAD --help` and `--instance BAD --version` likewise return help/version (exit0).
    No resolved instance or Request is produced. Missing option values/unrecognized tokens still
    fail grammar; this exception ignores only the instance value, not arbitrary syntax errors.
    Empty option strings and empty required seat/text/file/rig/node/distro values fail.
    Implementation guidance for the information-option boundary (same behavior, no added rule):
    System.CommandLine2.0.12 RootCommand already contains HelpOption and VersionOption.
    Do not register a duplicate --version; the built-in VersionOption disallows other explicit
    options, and built-in HelpAction clears unrelated grammar errors. A validation tree may
    remove those defaults, register ordinary zero-arity bool --help/-h and root-only --version,
    and give root a no-op action so valid root information requests do not acquire the library's
    missing-subcommand error. Let System.CommandLine recognize tokens and reject unknown/extra
    tokens; inspect parsed option values and the selected command, not raw token presence.
    For this validation pass, positional minimum arities may be zero: enforce the R2 required
    positional counts after valid help/version has been handled, so capture --help remains valid
    but capture without a seat is invalid. Scalar option values must still be present/nonempty,
    duplicates and numeric bounds checked before returning information; only instance slug
    validation is bypassed. --version with a selected subcommand is invalid even if the flag
    occurred before it; tokens after -- remain positional. Render generated help only after this
    validation, using the library HelpAction/HelpBuilder rather than handwritten help. This is
    one verified private approach, not authorization for a handwritten token parser, Program
    wiring, information short-circuit before grammar validation, or changed public results.
R3. CliRequestValidator.Validate is pure and returns null or exactly `Invalid command line.`.
    `up --fresh` requires at least one seat; note requires fresh; no-wait/dry-run combination
    is accepted (no-wait has no local effect). Down requires exactly one nonempty selector:
    positional seat(s), rig or all. Send requires exactly one text-or-file source; positional
    `-` is stdin source, not another option. Wait is exactly delivery/turn/none; file and text
    cannot coexist. Capture uses parser's lines bound. Ps seat and rig cannot coexist.
    Json rejected for instance init/start/stop/run and attach; group/root help ignores Json.
    Instance dev is accepted for client commands including dry run, refused for init/start/stop/
    run with the same fixed usage error; instance status accepts dev (client read).
    This slice validates syntax only: no server address resolution, body-file/stdin reading,
    seat existence, fresh note recording, body validator, API version/auth or live polling.
R4. DryRunRenderer.Text uses rig.Name, SpecHash, BindingHash and Seats in original rig order.
    Exactly `rig: <quoted name>`, `spec_hash: <quoted hash>`, `binding_hash: <quoted hash>` lines,
    then one line per seat: `seat: <quoted id> node: <quoted node> harness: <quoted harness>
    model: <quoted model> checkout: <quoted policy> workdir: <quoted workdir>` (one physical
    line; spaces between labels). Agent fields come from matching SeatParameters and Agent;
    checkout is Agent.Checkout. Human seats include the same labels with all five values null.
    Every string uses System.Text.Json default string serialization; do not include addresses,
    file payloads, secret paths, repository URL or content. Null hashes remain null rather than
    fabricated. Text never sorts seats or normalizes node paths.
R5. DryRunRenderer.Json emits ordered top-level keys api("v1"), rig, spec_hash, binding_hash,
    seats, diagnostics, notices. Null Rig means null rig/hash fields and empty seats. Seats
    are ordered objects id,kind,node,harness,model,checkout,workdir with R4 values/nulls.
    Diagnostics preserve supplied order with keys severity(lowercase error/warning),code,file,
    line,column,message,hint (hint null allowed). Notices is the supplied ordered string array.
    Nothing else is serialized. Invalid load still produces one document; errors do not erase
    diagnostics. Do not call loader, filesystem, git or HTTP in either renderer.
R6. GitEnvChecker checks ONLY tracking, not commits/dirtiness or secret contents. Inputs are
    absolute resolved paths from caller. First runner argv is `-C`,rigRoot,`rev-parse`,
    `--is-inside-work-tree`; successful0 then argv `-C`,rigRoot,`ls-files`,
    `--error-unmatch`,`--`,envPath. Runner uses ProcessStartInfo FileName=git, ArgumentList,
    UseShellExecute=false, redirects/discards both output streams, never logs them, timeout10s.
    Missing executable (Started=false) yields no diagnostic and one notice exactly
    `Git is unavailable; skipped env tracking check.`; first nonzero means no git worktree
    (empty result). Second0 emits Warning AIK5010 with File=env path relative to currentDirectory
    normalized slash, Line1 Column1, Message=`rig.env.yaml is tracked by git; keep environment bindings local.`,
    Hint=null. Second nonzero (untracked/outside) produces empty result. Timeout/other expected
    process failure yields Started=true,ExitCode=null and notice
    `Git tracking check failed; continuing without tracking warning.`; checker stops there.
    Cancellation propagates rather than becoming a skip notice. Use no shell, interpolated command,
    git writes, filename echo or env content reads. Every returned collection is immutable.
R7. CliApplication.RunAsync handles parsing, validator, help/version, then dry run only.
    Parse/validation failure: stdout empty, stderr `Invalid command line.\n`, exit2. Help:
    stdout Help plus final LF, stderr empty, exit0. Version: stdout supplied version+LF,
    stderr empty, exit0. Program supplies assembly informational version (unknown if absent),
    Environment.CurrentDirectory, AIAKOS_INSTANCE, console writers and Ctrl+C cancellation.
    A recognized valid command other than up --dry-run has no operational implementation yet:
    stdout empty, stderr `Command is not implemented in this build.\n`, exit1. Do not resolve
    instance or simulate its failure/success. This is an explicit slice limit, removed by15-3/15-4.
R8. For up --dry-run, resolve optional rig-dir against currentDirectory (default that directory),
    env explicitly against currentDirectory, default rigRoot/rig.env.yaml. Only then call
    RigLoader.Load once. Invalid path syntax/anticipated path failure returns2 with fixed
    stderr `Invalid rig path.\n` and no stdout; loader's normal missing-file diagnostics remain
    its diagnostics, not replaced. Run git check even if loader returns errors; append its
    diagnostics after loader diagnostics; render diagnostic file paths relative to currentDirectory
    (resolve loader-relative paths against rigRoot; rooted override env stays correct), slash
    separators, without modifying original LoadResult. In text mode diagnostics go to stderr
    using DiagnosticFormatter.Format, notices each prefixed `info: ` with LF; summary stdout
    only when Rig exists and no Error. In json mode output R5 document for both success/error,
    stderr still the same diagnostics/notices. Exit2 if any Error or Rig null; otherwise0.
    --seat/fresh/note/no-wait are accepted per R3 but do not filter or mutate dry-run summary:
    every seat listed, no launch. Verbose adds no identifiers to local errors because no request
    or trace exists. Dry run never reads instance homes/tokens, writes files or opens network.
R9. Program is a thin asynchronous entry calling CliApplication with real CliGitRunner and
    GitEnvChecker. Dispose created resources. Smoke invocation of built CLI and temporary
    `dotnet pack` install must work cross-platform for help/version/dry run, never install/update
    the authenticated user's global tool. Pack into a private temporary package folder; create
    a temporary NuGet.Config with packageSources cleared and ONLY that folder as a source.
    Install with that --configfile, explicit --version 0.0.1-preview.1, temporary --tool-path and
    private NUGET_PACKAGES/NUGET_HTTP_CACHE_PATH/DOTNET_CLI_HOME directories in the subprocess
    environment. Do not use owner caches, fallback feeds, nuget.org or a published/cached same-ID
    package; smoke must execute exactly the just-packed payload. Restore the calling environment
    (prefer per-process variables); clean only owned temporary paths. README lists only supported local behavior and the
    honest not-implemented result. Package/runtime payload/release version remains15-5.

## Expected outputs: exact text

Fixture renderer rig demo, SpecHash s, BindingHash b, two seats in order: human owner; agent
impl, node local, harness claude-code, model null, checkout shared, workdir /repo. JSON below
is compact with final LF. Text expected R4 is exactly:
```text
rig: "demo"
spec_hash: "s"
binding_hash: "b"
seat: "owner" node: null harness: null model: null checkout: null workdir: null
seat: "impl" node: "local" harness: "claude-code" model: null checkout: "shared" workdir: "/repo"
```

| ID | Input | Exact expectation |
|---|---|---|
| `E1` | root/help/version and grammar/defaults matrix R1/R2 | recognized command paths/options/defaults exactly R2; help contains exact names/Purpose/exit paragraph; root version=true; help/version with invalid environment instance or --instance BAD => help/version exit0 without instance resolution; malformed parse error Invalid command line.; immutable independent snapshots |
| `E2` | cross-field validation R3 | fresh without seat; note without fresh; conflicting/missing down/send sources; invalid wait; ps seat plus rig; json unsupported; dev host mutation => Invalid command line.; other legal combinations => null |
| `E3` | renderer demo and Unicode/null/human/multiple seats | exact R4 text above; unchanged rig order and JSON-quoted strings; no file/secret payload |
| `E4` | renderer demo, empty/null load, diagnostics/notices | {"api":"v1","rig":"demo","spec_hash":"s","binding_hash":"b","seats":[{"id":"owner","kind":"human","node":null,"harness":null,"model":null,"checkout":null,"workdir":null},{"id":"impl","kind":"agent","node":"local","harness":"claude-code","model":null,"checkout":"shared","workdir":"/repo"}],"diagnostics":[],"notices":[]} plus LF; null load has null rig/hashes and empty seats; exact ordered diagnostic fields R5 |
| `E5` | fake git tracked/untracked/missing/outside/timeout/cancel paths | exact argv/ordering R6; tracked one Warning AIK5010 and exact message; missing/timeout exact notices; no commands after terminal outcome; cancellation propagated; immutable results |
| `E6` | application parse/help/version/unimplemented/canceled | exact stdout/stderr/codes R7/G2; -v no longer version; no argument/env/body leakage or instance access |
| `E7` | actual loader minimal and negative fixtures with fake git; env override/default cwd/JSON | text summary/JSON exact R4/R5; diagnostics relative cwd using unchanged formatter; loader error exit2 without text summary but with JSON document; success0; no IO writes/network/instance dependency |
| `E8` | built/packed isolated executable help/version/local dry run | package identity unchanged; preview greeting gone; command tree/help and dry run usable; isolated local-only fresh-package install with private NuGet/tool/home caches; operational invocation fixed not-implemented exit1 |

## Tests

Cli.Tests uses xUnit v3 inherited defaults. For integration fixtures COPY the existing
Spec.Tests Fixtures/valid/minimal and selected negative input into a private temporary tree;
do not edit existing fixtures. Pure renderer fixtures construct records directly with s/b;
real-loader summary hashes come from existing canonical fixture expectations, never invent
canonicalization. Fake git runner records argv and uses TaskCompletionSource for cancellation.

- T1. Commit parser golden names/defaults/errors/help invariants and collection immutability tests
    for E1; fresh parse calls cannot share mutated defaults. Register test project in solution.
- T2. Commit validation matrix E2 with all grammar boundaries, no file/body/instance effects.
- T3. Commit independent text/JSON golden E3/E4, Unicode escaping/null/human/order/diagnostic
    preservation and sentinel content omitted; compare exact bytes, not self-generated expected.
- T4. Commit fake-runner E5 cases plus private temporary real git repository (when git exists)
    proving tracked/untracked/outside and space/non-ASCII paths. No authenticated checkout writes.
- T5. Commit application E6/E7 golden streams/exit codes and executable E8 smoke; fake checker
    input capture, files-before/after comparison, env override and no instance/network requirement.
    Package smoke uses R9 temporary-only NuGet.Config/source/packages/http-cache/tool/home paths,
    explicitly proving fresh payload rather than published/cached placeholder; no global install. Earlier CLI/version identity
    checks are updated only for C1 and all Spec tests remain green.

## Done

Release build zero warnings/errors; Cli.Tests and Spec.Tests green; no owner tools/state altered.
One commit per story, LF UTF-8 no BOM, final newline; no push/PR (lead owns those). Acceptance
external gates written later by author, run by QA. Any missing neighbor is reported, never built.

## Out of scope

API endpoints/DTOs/auth, command HTTP dispatch/polling/rendering live outcomes, body input reading,
instance init/start/stop/status host logic, configuration/secret files/ACLs/connection discovery,
WSL/Docker/process host supervision, node payload publishing, NuGet/global installation,
release pipeline/tag/version change, telemetry exporters or new git/rig loader features.
