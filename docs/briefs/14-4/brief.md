---
id: 14-4
title: "#14 slice 4 — canonical form and hashes"
issue: 14
status: approved
route: impl
paths: [src/Aiakos.Spec/, tests/Aiakos.Spec.Tests/]
date: 2026-10-06
---

# Brief: #14 slice 4 — canonical form and hashes

Part of #14. Self-contained. Do not read docs/specs/ or docs/adr/. Read the existing Spec source
and tests for style. Where silent preserve earlier behavior, make the smallest implementation,
and record the choice in the commit body. No additional validation rule or diagnostic is implied.

## Goal and prerequisite

Produce exact canonical JSON for the shared spec and local binding, separate SHA-256 hashes,
and a self-contained content map. Attach those results and the loader version to successful
loads and propagate the hashes into seat parameters. The canonical shape below closes the
underspecified shape in spec 0003 and ADR 0017; it is a compatibility surface for future consumers.
Projection and its file hash belong to 14-5, database storage to the orchestrator.

At analysis base main `2c968ee`, 14-3-5 is not merged: `ResolvedRig` is an empty record and Load
returns null even for valid inputs. This is a context-gap in the available implementation, not
a task to implement resolution here. The JSON writer story can proceed independently. Start
the shared/binding tree and integration stories only after 14-3-5 is merged, providing its
complete ResolvedRig, ResolvedBinding, ResolvedAgent, ResolvedSeat, ResolvedSeatParameters and
all nested records, with defaults resolved and guidance/culture normalized, skills byte-exact.
Use that API as specified in docs/briefs/14-3/brief.md; do not recreate traversal or assembly.
14-3-6 owns special-file validation. This slice changes none of its path/reader rules.

## Files to create or touch

| Path | Purpose |
|---|---|
| `src/Aiakos.Spec/` | Canonical JSON writer, shared/binding mapping, hash/content result, Load integration, additive record properties |
| `tests/Aiakos.Spec.Tests/` | Committed canonical/hash tests and checked-in golden JSON/hash assets |

No new packages/projects, NoWarn, pragma warning disable or SuppressMessage. No process,
network, git, database, node access or file writes by the loader. No changes to other projects.

## Surface by owning concern

Namespace Aiakos.Spec. Helper names are exact, internal via existing friend test assembly:

```csharp
// JSON writer concern (no dependency on 14-3-5)
internal static class CanonicalJson
{
    internal static string Write(System.Text.Json.JsonElement value);
}
// Shared/binding concern (requires 14-3-5)
public sealed record CanonicalRigResult(string SharedJson, string BindingJson, string ResolvedJson,
    string SpecHash, string BindingHash, IReadOnlyDictionary<string, byte[]> Contents);
internal static class RigCanonicalizer
{
    internal static CanonicalRigResult Create(ResolvedRig rig);
}
// Integration concern: additive init-only properties on existing records
// ResolvedRig: string? SpecHash, BindingHash, ToolVersion; CanonicalRigResult? Canonical
// ResolvedSeatParameters: string? SpecHash, BindingHash
```

New properties default null on manually constructed/unfinalized records, preserving the
14-3 constructors. Load always fills them on success once the integration story is merged.
The helpers are introduced by their owning stories, never stubbed by another. CanonicalJson
accepts JSON objects, arrays, strings, booleans, null and integer numbers in signed Int64 range;
its caller produces only that subset with unique fixed schema keys. Unsupported inputs have
the exact R1 failure contract below; no general JSON-number standard is requested. Create
receives a valid assembled rig.

## General rules

G1. Preserve every earlier diagnostic, order, redaction, error-result null and warning behavior.
   Canonicalization reads only the supplied records/content bytes; never reopen input files,
   secret source files or node paths. Hash/tool fields never influence diagnostics. Do not mutate
   supplied records, lists or byte arrays, and do not change their original strings or order.

## Changes to earlier behavior

C1. Successful Load returns the same resolved content/parameters as 14-3 plus the new populated
   fields from R8. Earlier valid result tests stay valid; error results remain null. Update a
   prior serialization golden only for these declared added properties; never alter an earlier
   expected axis, path, diagnostic or content byte. Before changing any whole-result golden,
   read the merged 14-3-5 tests. If they compare only fields or same-run serialization, retain
   those assertions without inventing a whole-result golden. If they compare a checked-in full
   resolved.json, extend that golden using the R2–R4 mapping of its already-reviewed values:
   commit the corresponding shared.json and binding.json canonical text as reviewable expected
   assets, calculate their SHA-256 independently using sha256sum or Python hashlib (not Create,
   Write or a production hash helper), and paste those literal hashes into the golden. Construct
   expected Canonical from those exact two JSON texts, their R5 combined object, the literal
   hashes, and the existing embedded files' bytes grouped/sorted by their known hashes (R6).
   ToolVersion is asserted separately against assembly Version.ToString; in a whole-result
   golden use the placeholder `<assembly-version>` and replace only that expected field with
   the current assembly version in the test before comparison. No other runtime-derived expected
   field is allowed. Record the independent command and resulting full-fixture hashes in the
   commit body. The minimal hashes below remain fixed; no new full-fixture golden is required
   if the predecessor has none.

## Rules

R1. CanonicalJson.Write returns compact JSON without BOM or trailing newline, encoded as UTF-8
   when hashed. Sort object property names by StringComparer.Ordinal after NFC normalization;
   recursively NFC-normalize keys and string values. Preserve array order. Null is `null`, bool
   is `true`/`false`, integers use their Int64 value in invariant decimal without leading
   zero/plus; parsed `-0` is accepted and writes exactly `0`. String escaping:
   double quote and backslash become `\"` and `\\`; use `\b`, `\t`, `\n`, `\f`, `\r` for those
   controls and lowercase four-hex `\u00xx` for the remaining U+0000–001F. All other Unicode
   scalars, including non-ASCII, `<`, `>`, `&`, `/` and U+2028, appear literally. No HTML escaping.
   NFC acts on metadata/path strings only, never EmbeddedFile.Content or its Sha256. A valid
   surrogate pair is one Unicode scalar. Unsupported input anywhere in the tree throws exactly
   `new ArgumentException("unsupported canonical JSON value")`, with no parameter name or
   inner exception (Message exactly `unsupported canonical JSON value`). Unsupported means
   Undefined (including default(JsonElement)), a number whose raw JSON spelling is not
   `-?(0|[1-9][0-9]*)` or is outside signed Int64, a string/property name containing an isolated
   surrogate, or duplicate property names after NFC normalization. Check each object for
   normalized-key collisions before writing it. Malformed strings can arrive as parsed JSON
   escapes such as `"\ud800"`: if JsonElement.GetString or JsonProperty.Name throws
   InvalidOperationException decoding it, translate that failure to the stated ArgumentException;
   do not implement a raw-text string decoder. This is the internal helper's declared rejection
   contract, not an additional loader diagnostic. Production mapping uses fixed valid keys.
   `JSON-exact` unsupported variants (each tested independently): `default(JsonElement)`;
   parsed `1.5`, `1.0`, `1e0`, `9223372036854775808`; parsed `{"a":1,"a":2}`;
   parsed `{"é":1,"e\u0301":2}` (NFC-key collision); parsed `"\ud800"` and
   `{"\ud800":1}` (isolated surrogate value/key). All have the same exact exception above.
   The valid negative-zero input is parsed `-0`, output exactly `0`.
   The valid non-BMP input is parsed `"\ud83d\ude00"`, output exactly `"😀"`.
   These helper tests do not create or change YAML diagnostics.

R2. Map the shared tree with exactly the keys/values in the following schema. Every listed key
   is present, including nulls and empty arrays. Property order in this schema is explanatory;
   R1 sorts it. Snake_case keys are explicit, independent of global serializer options.
   Shared root: `name`, `description`, `culture`, `repos`, `agents`, `seats`.
   Repo: `name`, `url`, `default_branch` from ResolvedRepo.
   Agent: `directory`, `name`, `description`, `default_harness`, `default_model`, `guidance`,
   `skills`, `harness_settings` from ResolvedAgent.
   Seat: `id`, `kind`, `description`, `agent`; human agent is null. Agent-seat object:
   `agent_directory`, `harness`, `model`, `checkout`, `repos`, `workdir_repo`, `requires`,
   `harness_settings` from ResolvedAgentSeat. Requires: `sandbox`, `auth`, `secrets`.
   Settings: `permission_mode`, `permissions`; permissions: `allow`, `ask`, `deny` string arrays.
   Skill: `directory`, `name`, `description`, `files` from ResolvedSkill.
   EmbeddedFile descriptor: exactly `path`, `sha256`, `bytes`, where bytes is Content.Length;
   culture is that descriptor or null, guidance/files are descriptor arrays.
   No content byte/base64, binding, seat parameters, apiVersion/kind envelope, source position,
   extension annotation, tool version, aggregate hash or generated projection is in shared JSON.
   Agent and seat `kind` above is the resolved seat kind, not the YAML envelope kind.

R3. Canonical shared arrays: seats by NFC id; agents by NFC directory; workspace repos by NFC
   name; each seat's repos and requires.secrets lexically by NFC string; skills by NFC name
   then NFC directory; each skill's files by NFC path. Preserve guidance order and repetitions;
   preserve allow/ask/deny order and repetitions. Sorting never deduplicates: resolution already
   owns selected repo/secret deduplication. Compare sort keys ordinal, using the fully canonical
   JSON of the element as the final tie-breaker (for normalized spelling collisions); tied
   identical elements retain repetitions. Sorting changes only canonical arrays, not snapshots.
   Keep workdir_repo from resolution: reordering an omitted seat repo selection can change its
   default workdir_repo, and therefore may change the hash; do not claim order invariance when
   the resolved workdir actually changes. Tests for unordered list permutations fix workdir_repo.

R4. Binding root has exactly `seat_root`, `placement`, `repos`, `secrets` from ResolvedBinding.
   Placement elements: `seat`, `node`, sorted by NFC seat; repo bindings: `name`, `path`, sorted
   by NFC name; secret sources: `name`, `file`, sorted by NFC name. Final ties follow R3's
   canonical-element tie-breaker. Strings are verbatim binding values before R1's NFC;
   preserve `~`, trailing slash, case and separators, never expand/normalize filesystem paths.
   No rig/env filename, absolute input root, derived seat paths, secret values or placement
   in shared JSON. Include unused known repo bindings and all resolved secret sources.

R5. Create returns SharedJson/BindingJson from R1–R4 and ResolvedJson exactly the compact
   canonical object with `binding` holding that binding tree and `shared` that shared tree.
   SpecHash is `sha256:` plus lowercase 64-hex SHA-256 of UTF-8 SharedJson; BindingHash is the
   same over BindingJson. Neither includes a newline, content map, tool version, own/other hash
   or computed seat parameters. Repeated Create calls and calls after deleting inputs produce
   identical bytes/hashes. Equivalent resolved values are independent of host path/root.

R6. Contents contains exactly one entry per distinct embedded Sha256 across culture, every
   agent guidance declaration and every skill file. Keys are the existing lowercase prefixed
   hashes, values are copies of the corresponding byte arrays. Duplicates share one map entry;
   descriptors still retain their declarations. Sort insertion/enumeration ordinal by hash for
   deterministic default System.Text.Json serialization. Use metadata from the supplied valid
   snapshots; do not recompute or re-read. Changing the map's byte arrays cannot alter input
   snapshots, and changing an input byte array after Create cannot alter the map.

R7. Commit tests for all R1–R6 outputs below and load-level hash invariance/sensitivity from
   R8. No new property-test package: deterministic generated permutations suffice. Use the
   existing minimal/full valid fixtures as bases, with generated safe guidance/culture and
   skills; fixture bodies contain no token-like literals. Add checked-in canonical and hashes
   goldens for the minimal fixture using the exact bytes below, run on every platform with no
   OS skip. Golden JSON files may have one final LF as repository text; strip that one LF when
   comparing to Write/Create (their output has none). Hash text file format is exactly
   `spec_hash: <value>\nbinding_hash: <value>\n`; never regenerate expected hashes from the
   implementation in the test. No CI edits needed; existing tests run on Windows/Linux.

R8. After successful 14-3 assembly, call Create once and return a new ResolvedRig with Canonical,
   SpecHash and BindingHash populated. Each ResolvedSeatParameters carries the same SpecHash
   and BindingHash; preserve its previous content and order. Set ToolVersion to
   `typeof(RigLoader).Assembly.GetName().Version!.ToString()` (assembly identity, no wall clock,
   subprocess or environment variable). Version is informational and outside both hashes.
   On any error, return null rig and do not canonicalize; warnings still allow finalization.
   SharedJson excludes Binding and SeatParameters so rebinding cannot change SpecHash. Do not
   add hash properties to other records. Pure helpers do not finalize or mutate their inputs.

## Expected outputs

One committed xUnit test per output ID (hyphens removed from C# method names), all named variants.

| ID | Input/variants | Exact expected output |
|---|---|---|
| `JSON-exact` | JSON tree `{"z":null,"s":"e\u0301<&/","b":true,"a":[2,1]}`; controls/quote/backslash; nested reordered properties; non-BMP pair; parsed `-0`; unsupported variants listed below | `{"a":[2,1],"b":true,"s":"é<&/","z":null}`; specified short/control escapes, literal non-BMP character; `-0` writes `0`; unsupported variants throw ArgumentException with Message `unsupported canonical JSON value`; no BOM/newline/whitespace outside strings |
| `CAN-shared` | resolved minimal fixture below; full fixture with culture/guidance/binary skill, human pm | exact minimal SharedJson below; full JSON has exactly R2 keys including null human agent, descriptors with path/hash/byte count and no Content/base64; snapshots unchanged |
| `CAN-order` | permute seats, agents, workspace repos, seat repos, secrets, skills and files with explicit workdir_repo; reverse guidance or permission order; NFC colliding path spelling with differing file descriptors | unordered permutations identical SharedJson; reversed distinct guidance/permissions different SharedJson; normalization ties sorted by canonical element JSON; input arrays unchanged |
| `CAN-binding` | minimal fixture; shuffled binding declarations; changed node/path/root/secret source; unused known repo binding | exact minimal BindingJson below; permutations identical, each changed binding value changes BindingJson; shared tree unchanged; node path tilde/trailing slash/case preserved apart from NFC |
| `HASH-minimal` | minimal resolved fixture | SpecHash `sha256:3a8966f81f40f6f1eaef639a5a396370418186fabfc4d34aba6d6790e11f910d`; BindingHash `sha256:07a1f519bd79eaa463dd39b8cf6ae1c37aa99527c5d4de65e599da268ebc3870`; ResolvedJson exactly `{"binding":<BindingJson>,"shared":<SharedJson>}` with trees substituted, no newline |
| `HASH-content` | repeated guidance/file hashes; binary and LF-normalized text; mutate result/input byte arrays after Create; delete inputs | one Contents entry per unique Sha256 with exact byte copies in ordinal key order; declaration descriptors retained; mutations never cross between input and map; repeated pre-mutation Create/deleted-input result identical |
| `LOAD-stable` | minimal/full content fixture copied to another root; YAML key/comment/x- changes; guidance/culture LF vs BOM/CRLF/CR; composed/decomposed description; deterministic YAML permutations | same SpecHash and canonical shared text; BindingHash also same when resolved binding equivalent; non-null complete rig; source metadata and embedded content retain predecessor behavior |
| `LOAD-change` | change safe guidance byte, skill support byte, permission string/order, model, or explicit workdir_repo; change only env node/repo path/root/secret source; mutate only a secret-source file value | shared changes alter SpecHash, not BindingHash; env changes alter BindingHash, not SpecHash; secret-source value change alters neither; raw skill CRLF vs LF changes SpecHash; guidance order changes SpecHash |
| `LOAD-result` | minimal/full; existing error fixture; api-key implicit-secret warning fixture | successful rigs have non-null Canonical and both hashes, seat parameter hashes equal rig hashes, ToolVersion equals assembly Version.ToString; error Rig null with identical diagnostics; warning Rig finalized with existing AIK4012; no added diagnostics |

The minimal fixture is `tests/Aiakos.Spec.Tests/Fixtures/valid/minimal` from 14-3, resolved as
specified by 14-3-5. Exact canonical shared bytes (one line, without final LF):

```json
{"agents":[{"default_harness":"claude-code","default_model":null,"description":"Implements issues","directory":"agents/impl","guidance":[],"harness_settings":{"permission_mode":"default","permissions":{"allow":[],"ask":[],"deny":[]}},"name":"impl","skills":[]}],"culture":null,"description":"","name":"demo","repos":[{"default_branch":"main","name":"app","url":"https://github.com/example/app.git"}],"seats":[{"agent":{"agent_directory":"agents/impl","checkout":"shared","harness":"claude-code","harness_settings":{"permission_mode":"default","permissions":{"allow":[],"ask":[],"deny":[]}},"model":null,"repos":["app"],"requires":{"auth":"subscription","sandbox":"optional","secrets":[]},"workdir_repo":"app"},"description":"Implements issues","id":"impl","kind":"agent"}]}
```

Exact minimal canonical binding bytes:

```json
{"placement":[{"node":"local","seat":"impl"}],"repos":[{"name":"app","path":"/home/dev/app"}],"seat_root":"~/aiakos/seats","secrets":[]}
```

## Tests

Plain xUnit v3 in Aiakos.Spec.Tests, no fixture requiring network/Docker. Direct writer tests do
not require 14-3-5; Create and load-level tests do. Use existing temp-root cleanup style.

T1. Keep a deterministic generated permutation test for at least 24 YAML property/list-order
   variants with explicit repos/workdir_repo, fixed safe files and binding, plus ignored x-
   annotations/comments. Compare shared/binding canonical bytes and hashes to the baseline.
   Include repeated Load, copied root, NFC metadata and normalized guidance/culture variants;
   skills stay raw. This guards spec AC2/AC3 and does not promise host path normalization.

T2. Serialize complete finalized rig/parameters after deleting input roots. Canonical descriptors
   never include Content or source/root/runtime identity values; the Contents map still has every
   exact byte. Change an existing node-local secret-source file containing a runtime-built
   sentinel; it never enters output and neither hash changes. Use an existing warning/error
   fixture to prove canonicalization doesn't change diagnostics or resolve an error. No new
   secret detection patterns/diagnostics or projection behavior are introduced.

## Definition of done

- `dotnet build -c Release`: zero warnings/errors.
- `dotnet test --project tests/Aiakos.Spec.Tests -c Release`: all green, earlier tests preserved
  except C1's declared added serialization fields.
- LF, UTF-8 without BOM, final newline on authored repository text.
- One commit subject `feat(spec): <exact story title> (#14)`; body silent choices and
  `Risks: this story checks none of #14's listed open risks.` No push or PR by implementer.
- Separate QA and reviewer check per docs/workflow.md; one retry, then lead.

## Out of scope

14-3 path checks, reader caps, content/seat assembly; generated projection, projection hash,
file modes and 2 MiB cap (14-5); DB storage/migrations, runtime session/token/hooks, CLI apply
behavior, node sandbox checks, CI/other projects, updates to spec or ADR by implementer.
Open risk 0003-RK2 concerns projected identity text, so this slice does not check or close it.
