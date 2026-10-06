---
id: 14-5
title: "#14 slice 5 — Claude Code projection plans"
issue: 14
status: approved
route: impl
paths: [src/Aiakos.Spec/, tests/Aiakos.Spec.Tests/]
date: 2026-10-06
---

# Brief: #14 slice 5 — Claude Code projection plans

Part of #14. Self-contained. **Do not read `docs/specs/` or `docs/adr/`.** Read existing
Spec source/tests for style. Where silent, preserve existing behavior, choose the smallest
implementation and record the choice in the commit body.

## Goal

Produce an in-memory Claude Code projection plan for every successfully resolved agent seat:
generated guidance, copied skills, exact descriptors, content-addressed bytes and a projection
hash. Plan creation uses already embedded snapshots. Node installation and harness launch are
later consumers, not work in this slice.

Issue #14 lists risk 0003-RK2: identity mentioned in projected text carries no authority.
The generated golden explicitly states that authority is the environment's per-seat token.
This slice checks that text; it does not create or validate tokens.

## Files to create or touch (nothing else)

| Path | What |
|---|---|
| `src/Aiakos.Spec/` | Pure projection helpers, projection records, additive seat property and loader integration |
| `tests/Aiakos.Spec.Tests/` | Committed exact output, boundary, snapshot and load tests |

No new packages/projects, Version on PackageReference, NoWarn, pragma warning disable or
SuppressMessage. No process, network, git, database, harness or node access.

## Public surface (exact names)

Namespace `Aiakos.Spec`. Preserve positional constructors of existing resolved records.
The surface below is owned by the indicated rule; do not stub another story's surface.
Tests can call internal helpers through the existing friend assembly.

```csharp
// R1/R2: renderer; returns UTF-8 without BOM.
internal static class ClaudeGuidanceRenderer
{
    internal static byte[] Render(ResolvedRig rig, string seatId, string specHash);
}
// R3: embedded skill mapping; returns fresh arrays.
internal static class ClaudeSkillProjection
{
    internal static IReadOnlyList<EmbeddedFile> Map(IReadOnlyList<ResolvedSkill> skills);
}
// R4/R5/R6: complete plan; files include CLAUDE.md, not only skills.
public sealed record ProjectionFile(string Path, string Sha256, int Bytes, string Mode);
public sealed record ResolvedProjection(string Root, string Hash,
    IReadOnlyList<ProjectionFile> Files, IReadOnlyDictionary<string, byte[]> Contents);
internal static class ProjectionPlanBuilder
{
    internal static ResolvedProjection? Build(string root,
        IReadOnlyList<EmbeddedFile> files, IReadOnlyList<string> checkoutPaths,
        ReferenceSource source, ICollection<Diagnostic> diagnostics);
}
// R7: add to existing ResolvedSeatParameters record body:
public ResolvedProjection? Projection { get; init; }
```

`ClaudeGuidanceRenderer` and `ClaudeSkillProjection` need only merged 14-3 records and the
merged 14-4 canonical writer. The plan builder needs neither of those two helpers. Loader
integration requires 14-4-3 to be merged: on this analysis base, `RigLoader.Load` still ends
with `ResolvedRigAssembler.Assemble`, without hash finalization. Do not implement that missing
predecessor here. Stop and report its absence if integration is assigned before it exists.

## General rules

G1. These helpers are pure: no file/directory reads or writes, no streams, environment lookup,
    time, random values or node path expansion. Their internal inputs are non-null snapshots
    satisfying the existing resolved-record invariants (renderer seat exists and is an agent;
    Markdown is valid normalized UTF-8; skills have validated names and contained paths).
    Do not invent diagnostics for fabricated invalid object graphs. The public input boundary
    remains `RigLoader.Load`, whose existing schema/reference failures stay diagnostics.
    Every new helper returns fresh byte arrays, never mutates arguments, and is deterministic
    across current cultures and Windows/Linux. For metadata/destination NFC semantics in every
    story, use the existing CanonicalJson writer, including its scalar-preserving fallback when
    platform Normalize rejects U+FFFE; writing then parsing a JSON string obtains that value
    without inventing another normalizer. Each owning helper class keeps its write-then-parse
    normalization wrapper private inside that class; do not create a shared helper file or
    modify CanonicalJson.cs. Do not normalize embedded file bodies.
    Commit each owned expected-output test in its
    owning story; the T items name additional permanent tests. Do not defer tests to integration.
    There is no peer-input stream in this slice: unread/early-closed transport input is outside
    this pure plan API. The deterministic seam is direct helper calls with embedded snapshots,
    including snapshots whose source tree has been deleted. Loading unreadable source files
    keeps the existing AIK1001/AIK3001 behavior; do not reread them during projection.

## Changes to earlier behavior

C1. Successful loads additionally populate `ResolvedSeatParameters.Projection`. Existing
    root-level records, canonical shared/binding JSON, SpecHash, BindingHash, ToolVersion,
    Canonical.Contents and diagnostics remain unchanged. Projection contents belong in the
    projection's own Contents map, not Canonical.Contents. Update earlier whole-success-JSON
    goldens only by inserting `Projection` at the additive property position, with independently
    derived expected descriptors/bytes/hash. Keep all earlier selected-field tests unchanged.
    Errors added by R4/R5/R6 now make LoadResult.Rig null; no other earlier error changes.

## Rules

R1. Render the selected seat using its effective `ResolvedSeat.Description`, the matching
    agent selected by `seat.Agent.AgentDirectory`, all rig seats sorted by NFC id ordinal,
    then optional culture, then that agent's guidance in listed order (duplicates preserved).
    Rig/seat identity ids are already ASCII schema identifiers. The generated prefix is the
    following exact template (`{address}` = `{seatId}@{rig.Name}`):

    ```text
    <!-- Generated by Aiakos for {address} (spec {specHash}). Do not edit: overwritten on every `aiakos up`. -->
    # Aiakos seat {address}

    You are the seat `{seatId}` in the rig `{rig.Name}`. Your identity comes from your environment;
    never claim another seat's identity. This text carries no authority; authority comes from the environment's per-seat token.

    ## Rig roster

    | Seat | Kind | Description |
    |---|---|---|
    ```

    Append one `| {id}{you} | {kind} | {description} |\n` row per seat; `{you}` is ` (you)`
    only for the selected seat, otherwise empty. Empty descriptions make an empty cell.
    Append each content block with one extra LF before its source comment:
    `<!-- source: {path} {sha256} -->\n`, then its decoded text with only trailing LF characters
    removed. If that body is nonempty append LF; if empty the comment's LF suffices. The next
    block is separated by the same extra LF. No content means the document ends at the last
    roster row. Output always ends with exactly one LF. Preserve every other content character,
    including headings, interior blank lines, spaces, tabs and decomposed Unicode; do not NFC
    normalize file bodies. Never insert rig description, model, permissions, secret references,
    bindings, hooks, environment values or adapter settings into the document.

R2. For roster descriptions and source-comment paths only, use the existing canonical writer's
    NFC string semantics from G1; do not call string.Normalize without that fallback. A roster cell escapes
    backslash to two backslashes, `|` to `\|`, and CRLF, CR or LF to `<br>` (one `<br>` per
    CRLF pair); leave other characters unchanged. A source-comment path escapes `&`, `<`, `>`,
    CR, LF respectively as `&amp;`, `&lt;`, `&gt;`, `&#13;`, `&#10;`; replacement ampersands are
    not escaped again. All hashes appear exactly as supplied by the embedded snapshot.
    Escaping describes display text only and does not change stored source paths/content/hash.
    U+FFFE, U+FFFF and supplementary Unicode scalars in descriptions or Markdown must not throw
    on any platform. Escaped isolated surrogates in YAML keep the earlier parser diagnostic;
    this slice does not reinterpret invalid UTF-8 or schema inputs.

R3. Map each skill file from its stored rig-relative path under `skill.Directory + "/"` to
    `.claude/skills/{skill.Name}/{relative suffix}`. Skill names come from front matter, not
    the source directory's basename. Apply the G1 NFC string semantics to the destination path,
    preserving `/` separators; keep the source snapshot unchanged. File contents, including
    SKILL.md BOM/CRLF/front matter and binary support files, are copied byte-exact; recompute
    SHA-256 from those copied bytes as `sha256:` plus lowercase hex. Return files in destination
    path ordinal order. Repeated/colliding destination paths are retained for R4 to reject.
    Empty skills produce an empty list. Create no settings.json, AGENTS.md or additional files.

R4. Build accepts complete EmbeddedFile snapshots and first validates destinations. Use NFC
    destination strings with G1 semantics; each must be a nonempty relative `/` path with no
    leading slash, backslash, NUL, CR, LF, colon, empty component, `.` or `..` component.
    Do not reject other Unicode scalars, spaces or dotfiles. Duplicate NFC paths are errors,
    even when their bytes match. Return null and append exactly one error at `source`:
    Code `AIK3002`, Message `invalid projection file path`, Hint null. Do not echo the path.
    Do not compute hashes or size diagnostics when this validation fails. Safe files are sorted
    ordinal by normalized path; descriptors use the normalized path and mode `0644` for every
    file, regardless of source filesystem permissions. Recompute hashes from content; ignore
    incoming EmbeddedFile.Sha256. Contents includes one fresh copied byte array per distinct
    content hash, keys in ordinal order; equal bytes at different paths retain both descriptors.

R5. After R4 succeeds, reject lexical overlap between `root` and any `checkoutPaths` entry:
    either path equals or contains the other as a whole slash-delimited path prefix. For
    comparison only, strip repeated `/`, trailing `/`, `.` components and resolve `..` with
    a stack (at anchor root extra `..` stays at root). Absolute `/` and `~/` are distinct anchors;
    compare ordinal/case-sensitive and do not NFC normalize node paths. Return root exactly
    as supplied in a successful plan. No home expansion, symlink probing or host Path.GetFullPath.
    On any overlap return null and append exactly one AIK3002 error at source, Message
    `projection root overlaps a repository checkout`, Hint null; do not echo node paths.
    Skip size and hash construction on overlap. Component siblings (`repo`/`repo-extra`) do
    not overlap. Physical alias/symlink checks and comparison of expanded `~` with `/home/...`
    require the node filesystem and are explicitly deferred to the node installer; a lexical
    plan is never permission to write into a checkout.

R6. After R4/R5 succeed, sum Content.Length across every destination (long arithmetic; equal
    bytes installed twice count twice). Exactly 2097152 bytes is allowed, above that returns
    null and one AIK3005 error at source, Message `projection exceeds 2 MiB`, Hint null.
    Count generated CLAUDE.md and all skill files; do not count JSON descriptors, root or hash.
    Success returns a ResolvedProjection with root unchanged and no appended diagnostics.
    Hash is SHA-256 of UTF-8 without BOM and without a final LF of the compact canonical JSON
    array of descriptors, in Files order. Each descriptor has exactly these keys in this order:
    `{"bytes":<integer>,"mode":"0644","path":<JSON string>,"sha256":<JSON string>}`.
    Use CanonicalJson for escaping/NFC. Prefix lowercase digest with `sha256:`. Hash excludes
    root, content map, spec_hash, binding_hash, version and itself. Empty file array hashes `[]`.

R7. Once 14-4-3's successful-load hash finalization exists, build projections after that
    finalization, using the finalized SpecHash in R1. For each agent seat in rig declaration
    order, generate EmbeddedFile `CLAUDE.md` from R1 and concatenate R3's mapped files; call R4–R6
    once with its parameter ProjectionRoot. checkoutPaths contains every Binding.Repos.Path
    plus every ResolvedCheckout.Path of every seat parameter with the same Node as this seat
    (not just this seat's selected repo). Bindings apply on every node, so include all bindings.
    Find the seat's original YAML mapping via its id; source is the id scalar mark in rig.yaml,
    clamped to 1-based positions just like earlier references. On success set Projection; on
    failure keep checking remaining seats, then return LoadResult.Rig null for the whole load.
    Append new diagnostics after the entire existing diagnostic list, one per failing seat in
    declaration order. R4 then R5 then R6 precedence holds within each seat. Warnings alone do
    not suppress projection. If any earlier stage has an error, call none of the new helpers
    and preserve that stage's diagnostics exactly. Human seats have no parameters/projection.
    Projection plan/hash depends on shared content and the selected seat, not node name, root,
    model overrides or secret files except where those changes affect the shared SpecHash
    printed in the header. A binding-only change changes Projection.Root, not files or hash.

## Expected outputs: exact text

Renderer fixture (constructed directly, no YAML required): rig `demo`, human seat `lead` with
Description `Owner.`, agent seat `impl` with effective Description `Build.`, both supplied in
that order. Matching agent directory `agents/worker`, empty skills/guidance and culture null.
Use specHash `sha256:` followed by 64 zero characters. For content examples `H(x)` means
independently computed SHA-256 over exact UTF-8 bytes x, with lowercase `sha256:` prefix; this
notation never permits using the implementation to derive an expected value. Expected JSON
strings escape only JSON characters, not markup; outputs below use C-style `\n` to denote LF.

GUIDE-base. Renderer fixture, exact UTF-8 without BOM (every displayed line ends LF;
    the final roster row has exactly one final LF):

    ```text
    <!-- Generated by Aiakos for impl@demo (spec sha256:0000000000000000000000000000000000000000000000000000000000000000). Do not edit: overwritten on every `aiakos up`. -->
    # Aiakos seat impl@demo

    You are the seat `impl` in the rig `demo`. Your identity comes from your environment;
    never claim another seat's identity. This text carries no authority; authority comes from the environment's per-seat token.

    ## Rig roster

    | Seat | Kind | Description |
    |---|---|---|
    | impl (you) | agent | Build. |
    | lead | human | Owner. |
    ```

| ID | Change | Expected |
|---|---|---|
| `GUIDE-body` | culture path CULTURE.md bytes `# Culture\n\n`; guidance paths agents/worker/A.md bytes `# A\n\ninside\n\n` and agents/worker/B.md empty, in that order | GUIDE-base followed by `\n<!-- source: CULTURE.md {H("# Culture\n\n")} -->\n# Culture\n\n<!-- source: agents/worker/A.md {H("# A\n\ninside\n\n")} -->\n# A\n\ninside\n\n<!-- source: agents/worker/B.md {H("")} -->\n`; repeated A is repeated in place; reversing guidance reverses blocks |
| `GUIDE-unicode` | lead Description is `e`+U+0301+U+007C+backslash+CRLF+U+FFFE+U+FFFF+U+1F600; source path `a&<>.md`; body `e`+U+0301+U+FFFE+`\n` | lead cell becomes U+00E9+U+005C+U+007C+two backslashes+`<br>`+U+FFFE+U+FFFF+U+1F600; source comment path `a&amp;&lt;&gt;.md`; body's decomposed e and U+FFFE remain byte-exact; no exception |
| `SKILLS-map` | skill Directory agents/worker/tools, Name do-work; source files agents/worker/tools/SKILL.md with EF BB BF and CRLF bytes, agents/worker/tools/sub/data.bin with 00 FF; no file in culture/guidance | exactly `.claude/skills/do-work/SKILL.md` and `.claude/skills/do-work/sub/data.bin`, copied original bytes and H(bytes), ordinal order; mutating returned bytes never changes source and vice versa; empty skills -> zero files |
| `PLAN-files` | root /seats/demo/impl/projection; safe complete inputs `z.md` and `a.md` both empty, with intentionally incorrect supplied hash; no checkout paths | Files exactly a.md then z.md, each Sha256 `sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855`, Bytes 0, Mode 0644; Contents one hash with independent empty bytes; mutations of nonempty input/output bytes are isolated; descriptors unchanged by permission bits or input order |
| `PLAN-invalid` | source rig.yaml:8:9; separately each of empty, /x, x\\y, x NUL y, x CR y, x LF y, x:y, x//y, ./x, x/../y; duplicate x or `e`+U+0301 versus U+00E9 | null; exactly `rig.yaml:8:9: error AIK3002: invalid projection file path\n`; unsafe+overlap+over-limit still only this diagnostic; safe space/dotfile/U+FFFE paths accepted |
| `PLAN-overlap` | source rig.yaml:8:9; root /s/demo/impl/projection and checkout /s, same root, /s/demo/impl/projection/nested or /s//demo/impl/./repos/../projection; separately ~/s/projection and ~/s | null; exactly `rig.yaml:8:9: error AIK3002: projection root overlaps a repository checkout\n`; overlap+over-limit still only this diagnostic; /s/demo/impl/projection-extra, /S, or other anchor accepted; root string returned unchanged |
| `PLAN-size` | source rig.yaml:8:9; disjoint root; valid files sum 2097152 then 2097153 bytes, including two descriptors pointing to equal bytes; separately empty files | boundary accepted; over null, exactly `rig.yaml:8:9: error AIK3005: projection exceeds 2 MiB\n`; empty Files/Contents and Hash `sha256:4f53cda18c2baa0c0354bb5f9a3ecbe5ed12ab4d8e11ba873c2f11161202b945` |
| `PLAN-hash` | PLAN-files | hash H(exact UTF-8 string `[{"bytes":0,"mode":"0644","path":"a.md","sha256":"sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"},{"bytes":0,"mode":"0644","path":"z.md","sha256":"sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"}]`); different root/input order unchanged, changed bytes/path changes hash |
| `LOAD-plan` | valid existing fixture with culture/guidance/skills, at least human+two agent seats; bind shared checkout /checkout/repo, seat_root /seats; also test seat-worktree | each agent parameter Projection.Root equals ProjectionRoot, generated CLAUDE.md uses finalized SpecHash, exact skill paths/bytes per R3; Files/descriptors/hash/Contents per R4/R6; no human parameter, no settings file; shared/binding hashes and Canonical.Contents equal independent prior 14-4 values; a binding-only root/node/secret-reference change preserves projection files/hash |
| `LOAD-errors` | valid fixture with two agent seat id scalars at rig.yaml:8:9 and 11:9; oversized projected snapshots for both; separately root lexical overlap with unselected bound clone or another seat checkout | LoadResult.Rig is null; first case appends exactly `rig.yaml:8:9: error AIK3005: projection exceeds 2 MiB\nrig.yaml:11:9: error AIK3005: projection exceeds 2 MiB\n`; overlap case appends one PLAN-overlap error per affected seat at its own id position; earlier errors remain byte-identical and no projection error is appended; earlier warnings precede new errors |

## Tests

Use `tests/Aiakos.Spec.Tests/`, existing xUnit style and temporary rig fixture helpers. Committed
helper tests use in-memory snapshots. Goldens use independently derived bytes/descriptors,
not a second call to the helper under test. SHA-256 from standard framework APIs is permitted
for expected values over literal goldens; never hash output from the implementation for the
sole equality oracle. Output tests additionally compare actual content hashes to their bytes.

T1. Commit a renderer golden containing the literal authority disclaimer from R1, source
    comments, effective descriptions, a human seat and selected `(you)` row. Assert the exact
    whole UTF-8 bytes, LF/BOM/final newline, no settings/secret/binding insertions, GUIDANCE
    order and embedded-byte immutability. Repeat under en-US and tr-TR. Include GUIDE-unicode
    and empty/missing culture/guidance, body ending in zero/one/multiple LF and empty body.

T2. Commit skill mapping cases with front-matter name differing from directory basename,
    nested dotfiles/binary support, BOM/CRLF SKILL.md, Unicode NFD support filename, file order
    permutations and copy isolation. Expected NFD destination filename is NFC; raw file bytes
    are unchanged. Delete source trees after embedding and assert pure helper outputs still
    match; there must be no dependency on the continued existence of source paths.

T3. Commit R4/R5/R6 tests with direct builder snapshots: exact file-list JSON hash, NFC
    duplicate rejection, all invalid path classes, 2 MiB inclusive limit, repeated-content
    destinations counted separately, error precedence, both overlap directions, dot/parent
    normalization, component sibling and anchor distinction. Exercise U+FFFE in a destination
    string without an exception and run under en-US/tr-TR; returned arrays and maps must not
    alias the input. Existing incoming diagnostics remain and each failure appends exactly one.

T4. Commit load-level projection goldens and R7/C1 regressions. Assemble legal source fixtures
    whose individual guidance files remain <=256 KiB and skills <=1 MiB but whose aggregate
    projected bytes exceed 2 MiB; compute their size including literal generated guidance.
    Check both affected seats and diagnostics positions/order. For exact 2 MiB boundary use
    builder tests T3, not invalid oversized single-file load fixtures. Check lexical overlap
    with an unselected bound clone, existing error suppression, warning-only success, root/
    node binding sensitivity and no filesystem modifications (before/after source tree bytes
    identical; node paths need not exist). Missing/deleted/unreadable inputs retain existing
    source diagnostics and never return a projection; do not introduce a stream API just to
    emulate peer EOF. Public Load must not throw for YAML description containing escaped
    U+FFFE/U+FFFF, decomposed Unicode and supplementary scalars that the earlier parser accepts.
    Freeze shared/binding canonical goldens independently and only update C1's additive JSON.

## Definition of done

- `dotnet build -c Release`: 0 warnings, 0 errors.
- `dotnet test --project tests/Aiakos.Spec.Tests -c Release`: all green. No Docker tests needed.
- After the second failed acceptance gate, stop and report per workflow; no third attempt.
- All files LF, UTF-8 without BOM, final newline.
- One commit on the story branch, subject `feat(spec): <story title> (#14)`.
  Body lists choices where silent and which part of 0003-RK2 is checked. Do not push/open a PR.

## Out of scope (do not implement, do not stub)

Actual node writes/cleanup, physical path/symlink authorization, harness adapter flags/settings/
trust/token installation, projection transmission/chunking, DB persistence, CLI dry run and
harness-neutral projections. No source re-reading, Markdown heading rewrite or credentials in
fixtures. Preserve source reference cap/skill validation and 14-4 shared/binding hash contracts.
