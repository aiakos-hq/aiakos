---
id: 14-3
title: "#14 slice 3 — file references and the resolved rig"
issue: 14
status: draft
route: impl
paths: [src/Aiakos.Spec/, tests/Aiakos.Spec.Tests/]
date: 2026-10-04
---

# Brief: #14 slice 3 — file references and the resolved rig

Part of #14. Self-contained. **Do not read `docs/specs/` or `docs/adr/`.** Read the existing
Spec source/tests for node trees, semantic checks and test style. Where silent, preserve existing
behavior and choose the smallest implementation; record the choice in the commit body.

## Goal

Safely resolve shared references, embed guidance/culture and complete skill directories, then
return the resolved rig, binding and agent-seat parameters. Per-file SHA-256 belongs here.
Canonical JSON and aggregate hashes belong to 14-4; generated projection and its 2 MiB cap to
14-5. No placeholder hashes or projection files are added here.

## Files to create or touch

| Path | What |
|---|---|
| `src/Aiakos.Spec/` | Reference helpers, embedded/resolved records and Load integration |
| `tests/Aiakos.Spec.Tests/` | Unit/golden/generated tests; valid-result assertions at assembly |

No new packages/projects, `Version=` on PackageReference, NoWarn, pragma warning disable or
SuppressMessage. No process, network, git, database or node access.

## Surface by owning concern

Namespace `Aiakos.Spec`; helpers are internal (existing friend test assembly). Existing
LoadResult, Diagnostic, Severity and formatter stay unchanged. First three concerns test their
helpers directly; Load still returns null until the assembly concern replaces empty ResolvedRig.
Do not implement or stub another concern's helper. Records are JSON serializable plain values.

```csharp
// Path concern
internal enum SharedReferenceKind { File, Directory }
internal sealed record ReferenceSource(string File, int Line, int Column);
internal sealed record SharedPath(string AbsolutePath, string Path);
internal static class SharedReferencePaths
{
    internal static SharedPath? Resolve(string rigRoot, string ownerDirectory, string value,
        SharedReferenceKind kind, ReferenceSource source, ICollection<Diagnostic> diagnostics);
}
// Markdown concern
public sealed record EmbeddedFile(string Path, string Sha256, byte[] Content)
{
    public int Bytes => Content.Length;
}
internal static class SharedContentReader
{
    internal static EmbeddedFile? ReadMarkdown(SharedPath file, ICollection<Diagnostic> diagnostics);
}
// Skill concern
public sealed record ResolvedSkill(string Directory, string Name, string Description,
    IReadOnlyList<EmbeddedFile> Files);
internal static class SharedSkillReader
{
    internal static ResolvedSkill? Read(string rigRoot, SharedPath directory,
        ReferenceSource source, ICollection<Diagnostic> diagnostics);
}
// Assembly concern
public sealed record ResolvedRepo(string Name, string Url, string DefaultBranch);
public sealed record ResolvedPermissions(IReadOnlyList<string> Allow, IReadOnlyList<string> Ask,
    IReadOnlyList<string> Deny);
public sealed record ResolvedHarnessSettings(string PermissionMode, ResolvedPermissions Permissions);
public sealed record ResolvedRequirements(string Sandbox, string Auth, IReadOnlyList<string> Secrets);
public sealed record ResolvedAgent(string Directory, string Name, string Description,
    string? DefaultHarness, string? DefaultModel, IReadOnlyList<EmbeddedFile> Guidance,
    IReadOnlyList<ResolvedSkill> Skills, ResolvedHarnessSettings HarnessSettings);
public sealed record ResolvedAgentSeat(string AgentDirectory, string Harness, string? Model,
    string Checkout, IReadOnlyList<string> Repos, string WorkdirRepo,
    ResolvedRequirements Requires, ResolvedHarnessSettings HarnessSettings);
public sealed record ResolvedSeat(string Id, string Kind, string Description, ResolvedAgentSeat? Agent);
public sealed record ResolvedPlacement(string Seat, string Node);
public sealed record ResolvedRepoBinding(string Name, string Path);
public sealed record ResolvedSecretSource(string Name, string File);
public sealed record ResolvedBinding(string SeatRoot, IReadOnlyList<ResolvedPlacement> Placement,
    IReadOnlyList<ResolvedRepoBinding> Repos, IReadOnlyList<ResolvedSecretSource> Secrets);
public sealed record ResolvedCheckout(string Repo, string Policy, string Url, string SourcePath,
    string Path, string? Branch, string? BaseRef);
public sealed record ResolvedSeatSecret(string Name, string File, string DeliverAs);
public sealed record ResolvedSeatParameters(string Seat, string Rig, string Node, string Harness,
    string? Model, string Auth, string Sandbox, string SeatDir, string Workdir,
    string ProjectionRoot, IReadOnlyList<ResolvedCheckout> Checkouts,
    ResolvedHarnessSettings HarnessSettings, IReadOnlyList<ResolvedSeatSecret> Secrets);
public sealed record ResolvedRig(string Name, string Description, EmbeddedFile? Culture,
    IReadOnlyList<ResolvedRepo> Repos, IReadOnlyList<ResolvedAgent> Agents,
    IReadOnlyList<ResolvedSeat> Seats, ResolvedBinding Binding,
    IReadOnlyList<ResolvedSeatParameters> SeatParameters);
```

ownerDirectory is rig-relative (`""` or `agents/impl`). SharedPath.Path is normalized
rig-relative `/` spelling; AbsolutePath is only for internal reads. No aggregate hash, tool
version, canonical JSON, generated projection or runtime field exists in these records yet.

## General rules

G1. Preserve earlier validation, damaged-container suppression and credential redaction. Order:
   rig diagnostics, culture, then agents in first-use order (each agent's YAML diagnostics,
   guidance in list order, skills in list order with files sorted ordinal by relative path),
   then env. Within one file order line, column, code, stable discovery order for ties.
   Run independent checks despite errors elsewhere; read only schema/semantic-valid values.
   Human and invalid-kind seats are never traversed for agent_ref. Any error prevents assembled
   Rig; warnings do not. Helpers return null for their own failed result without clearing earlier
   diagnostics. Expected argument/path/I/O/access failures produce stated diagnostics, never
   exception text or an exception to the caller. Do not catch catastrophic runtime failures.

G2. Never read a target before its path checks pass. Supplied rig root is the trusted boundary;
   its ancestry is not checked, every component below it is. Read only rig files and the selected
   env file. Node paths, seat_root and secret source files are never inspected locally. Load
   writes nothing. Temporary test roots may contain generated files/links.

G3. Credential-like reference values/discovered relative paths must not become output filenames
   or payload paths. Before path processing use the existing detector and report AIK4020 at
   the referring scalar (at the skill scalar for discovered filenames), skip traversal. Use
   existing kind precedence/message/hint; suppress other errors at that position. No message,
   hint or snapshot path echoes credentials, absolute rigRoot or exception text. If different
   files have credential-like names, attribute their error to the owning reference without
   displaying the unsafe discovered filename.

## Changes to earlier behavior

C1. A safe local agent_ref with missing/unreadable directory or agent.yaml becomes AIK3001 at
   the reference scalar instead of agent-file AIK1001. Paths rejected by R1 that were formerly silently skipped become AIK3002. A contained
   `..` in agent_ref is now normalized and loads the agent rather than being silently skipped. Agent-file syntax/envelope/byte-limit errors retain existing AIK100x/200x
   once path checks pass. Missing rig/env AIK1001 and all other earlier golden text are unchanged.
   Human/invalid-kind agent_ref traversal stops; their earlier applicable diagnostics remain.

C2. At final assembly change ValidTests.LoadsMinimalRigWithoutDiagnostics and
   LoadsFullRigWithoutDiagnostics from Assert.Null(Rig) to non-null with the resolved assertions
   below. Error results remain null. No other earlier expectation changes except C1.

C3. Harden the already merged C1/R2 implementation as R14 specifies. Special files no longer
   pass File-kind resolution; unreadable agent.yaml no longer reports file-level AIK1001.
   This correction is story S6; existing story IDs and their scope remain unchanged.

## Rules

R1. Resolve rejects empty paths, `/`/`//` absolute, drive forms like `C:/x`, `~/x`, backslashes,
   NUL, normalization outside root, or any symlink/reparse point below root including dangling
   links. Error AIK3002 at source: Message `invalid shared path`; Hint
   `use a relative / path inside the rig root without symbolic links`. Return null without read.
   Normalize `.`, repeated/trailing `/` and `..` that stays within root. Compare containment by
   components with host case semantics, not prefix. Return canonical relative `/` spelling with
   no leading/trailing slash or dot components; preserve case and Unicode spelling.

R2. File kind means an ordinary regular file only (R14 adds the explicit special-file checks).
   Safe missing/wrong-kind entry or inspection/access failure gives AIK3001 at source, Message
   `referenced file or directory not found`, Hint null. Link error wins over missing/type error.
   Integrate agent discovery: check directory then agent.yaml using same source; at most one
   error per reference. Load canonical agent directory once across aliases. Existing invalid
   scheme/kind/value is skipped by reference checks and retains earlier diagnostics.

R3. ReadMarkdown accepts a checked regular file (no extension/Markdown syntax check). Raw-byte
   cap including BOM is 262144 inclusive. Check file length before allocating/reading its content;
   a length above the cap immediately produces AIK3005, even for a multi-GiB sparse file. Bound
   the subsequent read to cap+1 bytes to detect growth without unbounded allocation; growth
   above the cap produces the same AIK3005. A removed/unreadable file remains the R3 I/O case. Exceeded: AIK3005 at file.Path 1:1, Message
   `referenced file is larger than 256 KiB`, Hint null. Invalid UTF-8: AIK1004 there, Message
   `file is not valid UTF-8`, Hint null. Read/access failure: AIK3001 there with R2 text. Remove
   one leading UTF-8 BOM, convert CRLF and remaining CR to LF, preserve all other text including
   final-newline presence. Encode UTF-8 without BOM; Content is those bytes, Bytes their length,
   Sha256 `sha256:` plus lowercase 64-hex SHA-256 of Content. Own errors return null.

R4. Scan decoded Markdown and valid UTF-8 skill files line by line using the five existing
   SemanticValidator credential regexes and precedence; share detector logic, do not duplicate
   patterns. One AIK4020 per matching line: first regex kind then first match of that kind.
   File is relative actual file; line 1-based after BOM/CR normalization, column is 1-based
   UTF-16 match index. Message `credential-like value (<kind>)`; Hint
   `never put secrets in rig files; name the secret and bind it in rig.env.yaml`. No matched
   text echoed; no usable snapshot with any match. Invalid UTF-8 skill support files are binary
   and preserved without text scan; SKILL.md must be UTF-8 under R6. Scan before exposing content.

R5. Read recursively includes all regular files, including dotfiles, but not empty directories.
   Never follow nested links; each link error is AIK3002 at original skill scalar with R1 text,
   skip it and continue other safe entries. Coalesce nested path failures within each skill
   declaration: at most one AIK3002 for any number of links/unsafe paths, one AIK3001 for any
   number of enumeration/read failures, and one AIK4020 for unsafe discovered filenames. For
   AIK4020, select the first offending filename in ordinal relative-path order and use its
   first matching detector kind; it suppresses other errors at that same scalar per G3. Errors
   for scanned file text/front matter stay on their own files and are not coalesced across files.
   Missing top-level SKILL.md is AIK3004 at skill scalar,
   Message `skill directory has no SKILL.md`, Hint null (no additional AIK3001 for that file).
   Enumeration/read failure is AIK3001 at skill scalar with R2 text. Any error prevents result.
   Do not return early for missing SKILL.md or another collected error until the metadata cap
   pass finishes or exceeds a cap: R7 determines the sole diagnostic in the over-limit case.

R6. SKILL.md must be valid UTF-8; optional BOM then exactly `---` on its first line and a later
   exact `---` closing fence (CRLF accepted). Front matter is one mapping with nonempty scalar
   name and description, name matching `^[a-z0-9][a-z0-9-]{0,63}$`. Reject duplicate keys, anchors,
   aliases, merge keys and custom tags; ordinary additional keys allowed. No envelope check.
   Invalid UTF-8/fence/YAML/required scalar/pattern gives AIK3004 at SKILL.md 1:1, Message
   `invalid skill front matter`, Hint null, no parser text. Valid name not equal to directory
   basename gives same position/code, Message `skill name does not match directory name`.
   Body is not parsed as YAML. Preserve raw SKILL.md bytes including BOM/CRLF.

R7. Skill cap is 100 regular files and 1048576 total raw bytes inclusive, counting SKILL.md and
   dotfiles. Count each regular directory entry once; no links count. Exceed either: one AIK3005
   at skill scalar, Message `skill exceeds 100 files or 1 MiB`, Hint null, result null. Enumerate
   regular entries and inspect their lengths before reading any content; stop enumeration at
   the 101st file or when summed lengths first exceed 1048576. Do not read large/sparse files
   to determine size or enumerate the remaining tree after the cap is exceeded. Return null
   immediately. When this skill read exceeds either cap (metadata or bounded content read),
   discard every diagnostic collected by that skill read and emit only its one AIK3005 at the
   declaration scalar. This includes missing SKILL.md, nested-link/unsafe-filename, I/O,
   front matter and text-scan failures inside that skill; unrelated earlier diagnostics are
   retained. This cap precedence overrides R5/G3 for entries discovered inside the skill;
   a credential-like top-level skill reference is still skipped by G3 before Read is called.
   Therefore an over-limit declaration's result is independent of enumeration order.
   If metadata passed,
   bound subsequent reads by the remaining aggregate allowance+1; growth that exceeds the
   allowance yields the same single AIK3005 and stops remaining reads. Individual
   skill files have no Markdown 256 KiB cap. Files sorted ordinal by canonical relative path;
   snapshots contain raw bytes and raw SHA-256 in R3 format. ResolvedSkill carries Directory,
   front matter Name/Description and Files. Input snapshots have no mode; projection adds 0644.

R8. Load discovers culture relative to root, guidance/skills relative to valid agent directory.
   Call helpers, collect independent errors even when semantic failures elsewhere prevent Rig.
   Skip files that failed parsing or have AIK2001; skip scalar references with earlier errors;
   skip guidance/skills list as a whole if any slice-1 diagnostic falls inside it. Do not traverse
   x- annotations. Per Load canonical-path cache reads content and emits its file diagnostics
   once; reference errors occur per distinct source scalar. Cache a failed skill's diagnostics
   too: replay scalar-level errors (AIK3001/3002/3004 for missing SKILL.md/3005/unsafe-name AIK4020)
   at each declaration's own scalar, preserving the per-scalar coalescing above. File-level
   front matter, UTF-8 and text-scan diagnostics appear once per canonical file. A failed skill
   still has no usable name, so R9 adds no duplicate-name error. Preserve repeated guidance entries
   and skill declarations in their original order; no cache survives Load.

R9. Repeated skill name in one agent's declarations gives AIK4003 at second/subsequent list
   scalar, Message `duplicate skill name '<name>'`, Hint `first defined at line <line>` from
   first scalar. Aliases count as duplicate declarations. Report once per declaration, not
   per seat sharing the agent. Different agents may each declare build; failed skill read has
   no usable name and causes no duplicate-name error.

R10. On no errors populate Rig Name, Description default empty, Culture null if absent, Repos
   in source order, Agents once per canonical directory in first-use order, Seats in source
   order. Repo.DefaultBranch defaults main. Agent metadata/defaults are from agent.yaml;
   guidance and skills preserve declaration order, skill Files use R7 order. Output embeds all
   content and contains no parser nodes, source positions, x- fields or absolute rig-root path.
   Result remains complete after deleting inputs; next Load sees edits.

R11. Human Kind human, Description or empty, Agent null, no parameters. Agent Kind agent;
   Description seat else agent; Harness seat else agent default; Model seat else agent default
   else null, verbatim. AgentDirectory canonical. Checkout defaults seat-worktree; Repos defaults
   all workspace names, explicit lists preserve order and deduplicate first occurrence;
   WorkdirRepo defaults first selected name before canonical sorting. Requires defaults optional,
   subscription, empty secrets; deduplicate secret names first occurrence. api-key appends
   anthropic_api_key when missing, retaining earlier AIK4012 warning. Settings default permission
   mode default and empty lists; permission lists retain order/repetitions. No live sandbox check.

R12. Binding SeatRoot defaults ~/aiakos/seats; Placement one node per agent in seat order,
   override else default, no human. Repos all known env repo bindings including unused, env
   order; Secrets env source order. Preserve node path strings verbatim (no expansion, host
   normalization, existence probes or secret read). Existing semantic checks own binding errors.

R13. SeatParameters follows agent-seat order, Seat=id, Rig=name, Node=placement, resolved
   Harness/Model/Auth/Sandbox/Settings. Join derived node paths with literal `/` after trimming
   trailing `/` from seat_root, root `/` yielding `/demo/impl`. SeatDir `<root>/<rig>/<seat>`;
   ProjectionRoot `<seat_dir>/projection`. Checkouts selected repo order, Policy/Url/SourcePath
   from seat/repo/binding. Shared Path=SourcePath verbatim, Branch/BaseRef null. Seat-worktree
   Path=`<seat_dir>/repos/<repo>`, Branch=`aiakos/<rig>/<seat>`, BaseRef=`origin/<default_branch>`.
   Workdir is selected workdir_repo checkout Path. Secrets follow Requires order with bound File
   and DeliverAs `file`. No runtime session ID, token, environment, hooks, statusLine,
   apiKeyHelper, tmux settings, executable path or secret value in any record.

R14. Before opening content, File-kind resolution must reject a FIFO/named pipe, Unix socket,
   character device or block device as wrong kind. Directory kind still accepts only directories.
   After R1 link checks, each rejected special file returns null and exactly one AIK3001 at
   ReferenceSource, Message `referenced file or directory not found`, Hint null. Never open,
   read or wait for a special-file writer. Apply this to agent.yaml discovery and direct helper
   calls. If an ordinary agent.yaml passes inspection but cannot be read (including Unix mode
   000 under a non-root user), Load emits exactly one AIK3001 at each referring agent_ref scalar
   with that same message and null Hint, and no agent-file AIK1001. Cache the failed canonical
   agent read once but replay the scalar diagnostic for each referring seat. Retain existing
   agent syntax, UTF-8, envelope and size diagnostics when bytes are readable; retain missing
   rig/env AIK1001. Any such failure leaves Rig null. Detection must not invoke external programs
   in production. When file classification or reading is denied, use the same AIK3001.

## Expected outputs

One test named by each output ID covers all its variants. Formatter text ends with LF; `\n`
means LF. Source positions below are helper inputs. Use temp roots, links and generated bytes.
Never commit credential-looking fixture values: concatenate separate fragments at runtime.

| ID | Input / variants | Exact expected output |
|---|---|---|
| `PATH-unsafe` | source agents/impl/agent.yaml:7:5, owner agents/impl; empty, absolute / and //, C:/x, ~/x, backslash, NUL, ../../../outside, existing/dangling final or ancestor link | null; `agents/impl/agent.yaml:7:5: error AIK3002: invalid shared path\n  hint: use a relative / path inside the rig root without symbolic links\n` |
| `PATH-normal` | existing ./../impl//GUIDANCE.md from agents/impl; agents/impl/./GUIDANCE.md from root; directory agents/impl/; sibling prefix escape | first two Path agents/impl/GUIDANCE.md, directory agents/impl, no diagnostic; sibling escape gets PATH-unsafe error, never read |
| `PATH-missing` | missing file/directory or wrong kind, source rig.yaml:10:16 | null; `rig.yaml:10:16: error AIK3001: referenced file or directory not found\n` |
| `PATH-agent` | minimal agent_ref unsafe ../../outside, missing agents/missing, linked agent.yaml, missing agent.yaml; second seat same canonical alias; local:agents/x/../impl with an existing ordinary agents/x directory | unsafe/link AIK3002 at rig.yaml:10:16 with PATH-unsafe text; missing dir/file PATH-missing text; malformed aliased agent produces its earlier diagnostics only once; contained parent traversal loads agents/impl/agent.yaml with no path diagnostic |
| `TEXT-normal` | GUIDANCE.md BOM+A CRLF B CR C LF; LF counterpart; A without final newline; empty | Content respectively UTF-8 A\nB\nC\n (Bytes 6) for first two, A (Bytes 1), empty (Bytes 0); normalized hashes equal; Path agents/impl/GUIDANCE.md; Sha256 sha256:+64 lowercase hex independently matching Content; no diagnostics |
| `TEXT-size` | GUIDANCE.md 262144/262145 ASCII bytes, 3 GiB sparse file, C3 28 invalid UTF-8, removed after path check | boundary accepted; over/sparse `agents/impl/GUIDANCE.md:1:1: error AIK3005: referenced file is larger than 256 KiB\n`; invalid `agents/impl/GUIDANCE.md:1:1: error AIK1004: file is not valid UTF-8\n`; removed AIK3001 at 1:1 with PATH-missing message; failures null |
| `TEXT-secret` | each five kinds at line 2 col 1 after safe LF; two kinds same line; lookalikes xghp_example, rotate the ghp_ token, sk-ant-, https://example.com/a@b; ssh://git@github.com/org/repo.git at line 2 col 1 | null; `agents/impl/GUIDANCE.md:2:1: error AIK4020: credential-like value (<kind>)\n  hint: never put secrets in rig files; name the secret and bind it in rig.env.yaml\n`; first kind priority only per line; the four named lookalikes have no diagnostic; ssh URL is AIK4020 (URL with user info) at 2:1 with the same hint, not exempted; no credential echoed |
| `SKILL-valid` | build/SKILL.md `---\nname: build\ndescription: Build things\n---\nBody\n`, support binary 00 FF 01, dotfile; BOM/CRLF variant | Directory agents/impl/skills/build, Name build, Description Build things, all files raw and ordinal-path ordered, correct byte counts/raw hashes, no diagnostics |
| `SKILL-missing` | missing SKILL.md, source agent.yaml:8:5 | null; `agents/impl/agent.yaml:8:5: error AIK3004: skill directory has no SKILL.md\n` |
| `SKILL-front` | every R6 invalid form; name other in build directory; extra ordinary metadata | invalid `agents/impl/skills/build/SKILL.md:1:1: error AIK3004: invalid skill front matter\n`; mismatch same position/code with `skill name does not match directory name\n`; both null; extra metadata accepted |
| `SKILL-links` | nested file/directory/dangling symlink, external target credential sentinel; two links in one skill; two unsafe credential-like filenames; failing linked skill declared at list scalars 8:5 and 9:5 | one link or two links: null, exactly one `agents/impl/agent.yaml:8:5: error AIK3002: invalid shared path\n  hint: use a relative / path inside the rig root without symbolic links\n`; two unsafe filenames: one AIK4020 at 8:5, no path echo; same linked skill declared twice: that AIK3002 text at 8:5 then 9:5, no file content diagnostic and no duplicate skill name; no target error/content |
| `SKILL-limits` | 100/101 files, 1048576/1048577 total bytes; SKILL.md or support >262144 within total cap; 3 GiB sparse support file; generated tree with more than 101 entries; 150 regular files plus a nested link, plus an unsafe filename, or with SKILL.md absent (separate cases, entries created in opposite orders) | boundaries/large individual accepted; exceeded null, exactly one `agents/impl/agent.yaml:8:5: error AIK3005: skill exceeds 100 files or 1 MiB\n` and no other skill diagnostic in every mixed/error/order case |
| `SKILL-secret` | credential in valid UTF-8 support at 2:3; invalid UTF-8 binary; credential-like filename | text null, AIK4020 support-path:2:3 with TEXT-secret message/hint; binary preserved without text scan; unsafe name null, AIK4020 at original skill scalar without path/value echo |
| `RES-content` | minimal + culture Team CRLF, guidance First LF then Second LF but declarations second/first, build skill; shared-agent alias seat; edit guidance between Loads; delete root after successful Load | non-null/no diagnostics, Culture Team LF, ordered guidance Second LF/First LF, one agent directory agents/impl, complete skill snapshots; next Load sees new hash; old result serializable/readable after deletion |
| `RES-duplicate` | skill entries skills/build and ./skills/build/ at lines 8,9 col 5; shared agent on two seats | duplicate exactly `agents/impl/agent.yaml:9:5: error AIK4003: duplicate skill name 'build'\n  hint: first defined at line 8\n`, Rig null |
| `RES-distinct` | separate agents each declare their own build skill; one valid skill reused by distinct agents | Rig non-null, two Agents, skill name build on each, no duplicate skill error; reused canonical files have identical bytes/hashes without duplicated content diagnostics |
| `RES-defaults` | existing minimal/full; override seat description/harness/model; null model; repeated selections | minimal demo/empty description/null Culture, app main, impl Implements issues/claude-code/null model/shared/[app]/app/optional/subscription/empty secrets; full ordered impl/review/pm and two parameters, pm.Agent null, review seat-worktree; overrides win; repetitions deduplicate except permission rules, settings allow order preserved |
| `RES-binding` | full default local + review override other; absent/explicit root; unused known repo binding; external env path | placement impl:local,review:other; root ~/aiakos/seats or explicit verbatim; env repo/secret paths and order preserved; external env replaces only binding, no node path probes |
| `RES-parameters` | full root /seats/, app branch dev; shared impl; root /; omitted api-key secret list with source bound; sandbox required | review dir /seats/demo/review, projection /seats/demo/review/projection, workdir /seats/demo/review/repos/app, branch aiakos/demo/review, base origin/dev; shared workdir/path /home/dev/app, Branch/BaseRef null; root /demo/impl; key added once with DeliverAs file, existing AIK4012 warning, Rig non-null; sandbox required retained without error |
| `RES-errors` | minimal: append culture_file: MISSING.md at rig line 12; append guidance: and list item MISSING.md at agent lines 7/8; env rig changed to other; then separate credential-reference/damaged-list variants | Rig null; exactly `rig.yaml:12:15: error AIK3001: referenced file or directory not found\nagents/impl/agent.yaml:8:5: error AIK3001: referenced file or directory not found\nrig.env.yaml:3:6: error AIK5001: rig 'other' does not match rig name 'demo'\n`; secret only AIK4020 at source/no read; damaged list only earlier list diagnostics, independent checks continue |

| `PATH-special` | File helper points to a generated FIFO, Unix-domain socket, /dev/null character device and a block device when available; minimal local agent has FIFO or socket agent.yaml | null helper; exactly one AIK3001 at supplied source (integration rig.yaml:10:16), Message `referenced file or directory not found`, Hint null; null Rig; no content open or wait for a writer |
| `PATH-unreadable` | minimal agent.yaml mode 000 as non-root; second seat aliases the same canonical directory; unreadable directory inspection | exactly one AIK3001 per agent_ref at its scalar, Message `referenced file or directory not found`, Hint null; no agent-file AIK1001; null Rig |

## Tests

xUnit v3 like existing tests; fixture negatives may use GoldenTests. Generated large/binary/link
cases use disposable roots with finally cleanup. Linux acceptance runs all link cases; Windows
without link privilege explicitly skips only those cases. No real credential literals committed.

T1. Path helper and agent integration cover all PATH rows, wrong kinds, directory aliases,
   sibling-prefix escape, contained parent traversal and link precedence. Assert no exception
   or escaped target read; contained-parent agent_ref loads normally; preserve missing rig/env AIK1001 and agent syntax/envelope errors.

T2. Markdown outputs cover exact normalized bytes, independent SHA256.HashData expectations,
   raw-byte boundary before normalization, sparse over-limit files without content allocation,
   BOM/CRLF/CR/empty/no-final-LF, scan position after BOM
   and all detector kinds; serialized diagnostics contain no planted value.

T3. Skill outputs cover raw hashes independently from source bytes, front matter invalid forms,
   inclusive counts/bytes, dotfiles/binary, links, unsafe filenames and scan positions. Skill
   errors yield no result while independent safe files are checked for additional errors unless a
   size cap has terminated that directory. Sparse over-limit files must give AIK3005, not an
   allocation error; enumeration stops at the first count/byte overage.

T4. Serialize resolved minimal/full and content fixtures after deleting inputs: snapshots remain
   complete, records contain no absolute rigRoot, parser nodes, x- keys or R13 runtime fields.
   Compare a normal Load with a Load while PATH is temporarily empty; restore in finally and
   disable parallelization for the environment-changing test. Assert no local secret read even
   for an existing secret-source file containing a runtime-built sentinel. Node binding paths
   are permitted. Spec AC9/AC10 are covered; 0003-RK2 is later projection text and is not checked.

T5. Load integration tests assert RES-errors and RES-duplicate, including null Rig for those
   invalid calls only. For valid calls assert diagnostics, not Rig (the existing ValidTests
   retain null assertions until C2 changes them at assembly). Exercise repeated failing skills: two declarations of one linked
   directory give one AIK3002 at each scalar; two declarations of one skill with invalid front
   matter give only one file-level AIK3004; both cause no duplicate-name diagnostic. A valid
   minimal/full Load has the earlier empty diagnostics; do not assert its temporary null Rig. Changed referenced
   files on a second Load are re-read: safe text then a runtime credential produces AIK4020.
   No new public assembly record is needed in this story; wire a per-call internal catalog for
   the following assembly story to consume rather than re-reading files.

T6. Cover PATH-special and PATH-unreadable. On Linux generate FIFO with mkfifo and socket with
   a bound Unix-domain Socket; leave FIFO without a writer and require the child Load probe to
   terminate within five seconds (kill and fail on timeout). Direct helper probes use supplied
   source probe.yaml:7:9; agent integration uses rig.yaml:10:16. /dev/null covers character device;
   test a block device only if one exists and is accessible, otherwise explicitly skip that case.
   Mode-000 tests run under a non-root user and restore permissions in finally; explicitly skip
   permission-denial cases under root. Platforms without Unix special files explicitly skip those
   cases. Assert a regular file still resolves, a directory remains wrong File kind, link rejection
   still wins, readable malformed agent retains its existing diagnostics, missing rig/env retain
   AIK1001, and no special file content is opened. Do not introduce a new diagnostic code.

## Definition of done

- `dotnet build -c Release`: 0 warnings/errors.
- `dotnet test --project tests/Aiakos.Spec.Tests -c Release`: all green, earlier goldens unchanged except C1.
- Authored text LF, UTF-8 without BOM, final newline.
- One commit subject `feat(spec): <exact story title> (#14)`; body silent choices and
  `Risks: this story checks none of #14's listed open risks.` No push/PR by implementer.
- Separate QA/reviewer seats check implementation per docs/workflow.md.

## Out of scope

Aggregate/canonical hashes, NFC, tool version, storage; generated projection, its hash and 2 MiB
cap; disk modes; node trust/launch/secret delivery/sandbox checks; git worktrees; CLI tracked-env
warning; spec/ADR/docs changes by implementer; new packages. Later slices consume these records.
