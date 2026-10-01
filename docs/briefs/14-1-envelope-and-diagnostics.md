---
id: 14-1
title: "#14 slice 1 — rig file envelope and diagnostics"
issue: 14
status: implemented
route: impl/opencode
date: 2026-10-01
---

# Brief: #14 slice 1 — rig file envelope and diagnostics

This brief is self-contained. **Do not read `docs/specs/` or `docs/adr/`.** Where the brief is
silent, pick the simplest behaviour and say so in the commit body.

## Goal

A new library `Aiakos.Spec` that reads the three kinds of rig files, checks their YAML envelope
and field structure, and reports **every** problem in one pass as diagnostics with exact text and
1-based line/column. Nothing is resolved yet: `Load` always returns `Rig = null`.

## Files to create or touch (nothing else)

| Path | What |
|---|---|
| `src/Aiakos.Spec/Aiakos.Spec.csproj` | `<Project Sdk="Microsoft.NET.Sdk">` with only `<PackageReference Include="YamlDotNet" />` |
| `src/Aiakos.Spec/*.cs` | The loader. File-scoped namespace `Aiakos.Spec`; non-public types `internal sealed` |
| `tests/Aiakos.Spec.Tests/Aiakos.Spec.Tests.csproj` | `ProjectReference` to `..\..\src\Aiakos.Spec\Aiakos.Spec.csproj` and `<None Update="Fixtures\**" CopyToOutputDirectory="PreserveNewest" />`. xUnit v3 and `using Xunit` come from `tests/Directory.Build.props` |
| `tests/Aiakos.Spec.Tests/**` | Tests and fixtures (below) |
| `Aiakos.slnx` | Add both projects to `/src/` and `/tests/`, alphabetical |
| `Directory.Packages.props` | New `<ItemGroup Label="Spec">` with `<PackageVersion Include="YamlDotNet" Version="18.1.0" />` |

Target framework, nullable, warnings-as-errors and analyzers are inherited from
`Directory.Build.props`; do not repeat them. No `Version=` on `PackageReference`, no `NoWarn`,
no `#pragma warning disable`, no `[SuppressMessage]`.

## Public surface (exact names; a shared scoring test compiles against them)

```csharp
namespace Aiakos.Spec;
public enum Severity { Error, Warning }
public sealed record Diagnostic(Severity Severity, string Code, string File, int Line, int Column, string Message, string? Hint);
public sealed record ResolvedRig;                       // placeholder, filled in slice 2
public sealed record LoadResult(ResolvedRig? Rig, IReadOnlyList<Diagnostic> Diagnostics);
public static class RigLoader { public static LoadResult Load(string rigRoot, string? envPath); }
public static class DiagnosticFormatter { public static string Format(IEnumerable<Diagnostic> diagnostics); }
```

- `envPath == null` means `<rigRoot>/rig.env.yaml`. `Load` never throws for bad input and only
  reads files (no git, network, environment variables or process calls).
- `Format` writes, per diagnostic, `<file>:<line>:<col>: <error|warning> <code>: <message>\n`
  and, if `Hint` is set, `  hint: <hint>\n`. LF only.
- `Diagnostic.File` is the path relative to the rig root with `/` separators (`rig.yaml`,
  `agents/impl/agent.yaml`, `rig.env.yaml`). An env file outside the rig root is shown as passed,
  with `\` replaced by `/`.

## Rules

1. **Files.** Load `<rigRoot>/rig.yaml` (kind `Rig`), then for each `seats[].agent_ref` of the
   form `local:<path>` the file `<rigRoot>/<path>/agent.yaml` (kind `Agent`; each directory once,
   in order of first reference), then the env file (kind `RigEnv`). Skip an `agent_ref` whose
   path is absolute or contains `..` or `\` (no diagnostic yet). A file that fails still lets the
   other files be checked.
2. **Bytes.** Missing file → AIK1001. More than 262144 bytes, or not valid UTF-8 → AIK1004. A
   UTF-8 BOM is accepted. After AIK1001/AIK1004 the file is not parsed.
3. **YAML.** Parse with YamlDotNet's event API (`YamlDotNet.Core.Parser`) into your own node
   tree that keeps the start `Mark` of every key and value. Do not use the deserializer or
   `YamlStream`: they stop at the first duplicate key and expand aliases. Syntax error, or a
   second document → AIK1002, and that file reports nothing else.
4. **Forbidden YAML features (AIK1003), all reported, parsing continues:** duplicate key in a
   mapping (at the second key; first value wins); anchor (`&a`) and alias (`*a`), each where it
   occurs; merge key `<<`; any explicit tag (`!x`, `!!str`). An entry whose value is an alias or
   tagged is skipped by rules 6–10 but counts as present.
5. **Envelope (AIK2001).** `apiVersion` must be `aiakos.dev/v1` and `kind` must match the file's
   role. Key order is not checked. After any AIK2001, rules 6–10 are skipped for that file. A
   top level that is not a mapping (including an empty file) is AIK2004 at 1:1,
   `file must contain a mapping`.
6. **`x-` keys.** A key starting with `x-` is ignored with its whole value, at any level (rule 4
   still applies inside it).
7. **Reserved (AIK2005)** fields and values, checked before rule 8. See the table below.
8. **Unknown field (AIK2002)** for any other key not in the schema. Hint
   `did you mean '<field>'?` when a schema field of the same mapping is at Levenshtein distance
   ≤ 2 (closest wins; ties: first in schema order). Field names are snake_case; `checkOut` is
   simply unknown.
9. **Required (AIK2003)**, reported at the start of the mapping that lacks the field.
10. **Type and value (AIK2004).** Any scalar is a string, except an empty plain scalar, `~` and
    `null`. Check string / list / mapping, enums and patterns as given in the schema.
    `workspace.repos` must have at least one item (`field 'workspace.repos' must have at least one item`).
11. **Positions** are 1-based. AIK2002/2005-field/1003: the key. AIK2004/2005-value/2001 with a
    value present: the value. AIK2003 and a missing envelope key: the containing mapping's start.
    AIK1001/1004: `1:1`. AIK1002: the parser's position.
12. **Context suffix** `<where>`: empty for a top-level field; ` in seat '<id>'` for a field
    directly in a `seats` item whose `id` is a valid seat ID; otherwise ` in <path>` with the
    dotted path of the containing mapping (`workspace.repos[0]`, `seats[1].requires`,
    `harnesses.claude-code.permissions`, `secrets.api_key`). A list item's field name is
    `<list>[<i>]` (`secrets[0]`).
13. **Order and completeness.** All diagnostics of all files in one `Load` call: files in the
    order of rule 1, then by line, then column. Never stop at the first error.

## Schema

`id` patterns: rig `^[a-z][a-z0-9-]{1,39}$`, seat `^[a-z][a-z0-9-]{0,23}$`,
repo `^[a-z][a-z0-9._-]{0,63}$`, agent `^[a-z][a-z0-9-]{0,39}$`, secret `^[a-z][a-z0-9_]{0,63}$`,
node `^[a-z0-9][a-z0-9.-]{0,62}$`. `*` = required. Schema order is the order written here.

- **rig.yaml**: `apiVersion`*, `kind`*, `name`* (rig id), `description`, `culture_file`,
  `workspace`* { `repos`* [ { `name`* (repo id), `url`*, `default_branch` } ] },
  `seats`* [ { `id`* (seat id), `kind` (`agent`|`human`), `description`, `agent_ref` (required
  unless `kind: human`), `harness` (`claude-code`), `model`, `checkout`
  (`shared`|`seat-worktree`), `repos` [repo id], `workdir_repo` (repo id),
  `requires` { `sandbox` (`optional`|`required`), `auth` (`subscription`|`api-key`),
  `secrets` [secret id] } } ]
- **agent.yaml**: `apiVersion`*, `kind`*, `name`* (agent id), `description`*,
  `defaults` { `harness` (`claude-code`), `model` }, `guidance` [string], `skills` [string],
  `harnesses` { `claude-code` { `permission_mode` (`default`|`acceptEdits`|`plan`|`auto`),
  `permissions` { `allow` [string], `ask` [string], `deny` [string] } } }
- **rig.env.yaml**: `apiVersion`*, `kind`*, `rig`* (rig id), `seat_root`,
  `placement` { `default_node` (node id), `seats` { `<seat id>`: { `node` (node id) } } },
  `repos` { `<repo id>`: { `path` } }, `secrets` { `<secret id>`: { `file` } }.
  Map keys shown as `<… id>` are checked against the pattern (AIK2004 `invalid key`), not as fields.
- **Reserved → AIK2005.** rig.yaml top level: `pods`, `edges`, `imports`, `profiles` (M2),
  `channels` (M4), `egress` (M6). Seat: `profile`, `uses`, `startup`, `pod` (M2). `checkout`
  values `shared-readonly` (M6), `task-worktree` (M8). `harness` values `opencode`, `codex` (M2),
  also in `defaults.harness`. agent.yaml top level: `imports`, `resources`, `profiles`, `startup`,
  `subagents` (M2); `harnesses.opencode`, `harnesses.codex` (M2). rig.env.yaml top level: `nodes`
  (M6), `channels` (M4); `secrets.<id>.store`, `secrets.<id>.env` (M6).
- **Deferred, no diagnostic in this slice** (later slices own them): `permission_mode:
  bypassPermissions`; keys `hooks`, `statusLine`, `apiKeyHelper`, `env` under
  `harnesses.claude-code`; `secrets.<id>.value`; any `agent_ref` string.

## Diagnostics: exact text, one golden each

Base fixture `valid/minimal` (must produce no diagnostics):

```yaml
# rig.yaml                               # agents/impl/agent.yaml        # rig.env.yaml
apiVersion: aiakos.dev/v1                apiVersion: aiakos.dev/v1       apiVersion: aiakos.dev/v1
kind: Rig                                kind: Agent                     kind: RigEnv
name: demo                               name: impl                      rig: demo
workspace:                               description: Implements issues  placement:
  repos:                                 defaults:                         default_node: local
    - name: app                            harness: claude-code          repos:
      url: https://github.com/example/app.git                              app:
seats:                                                                       path: /home/dev/app
  - id: impl
    agent_ref: local:agents/impl
    checkout: shared
```

Each invalid fixture is the base with one change (three separate files, no header comments):

| Fixture `invalid/…` | Change | `expected/diagnostics.txt` |
|---|---|---|
| `AIK1001-missing-env` | no `rig.env.yaml` | `rig.env.yaml:1:1: error AIK1001: file not found` |
| `AIK1002-syntax` | rig.yaml line 3 `name: [demo` | `rig.yaml:<l>:<c>: error AIK1002: YAML syntax error: <parser message>` |
| `AIK1003-duplicate-key` | rig.yaml add line 12 `    checkout: seat-worktree` | `rig.yaml:12:5: error AIK1003: duplicate key 'checkout'` |
| `AIK1003-anchor-alias` | line 3 `name: &n demo`, add line 12 `    description: *n` | `rig.yaml:3:7: error AIK1003: anchors and aliases are not supported` and the same text at `12:18` |
| `AIK1003-merge-key` | add line 12 `    <<: {model: opus}` | `rig.yaml:12:5: error AIK1003: merge keys ('<<') are not supported` |
| `AIK1003-tag` | line 3 `name: !!str demo` | `rig.yaml:3:7: error AIK1003: tag '!!str' is not supported` |
| `AIK1004-too-large` (built in the test) | rig.yaml padded with a comment to 262145 bytes | `rig.yaml:1:1: error AIK1004: file is larger than 256 KiB` |
| `AIK1004-not-utf8` (built in the test) | rig.yaml + bytes `C3 28` | `rig.yaml:1:1: error AIK1004: file is not valid UTF-8` |
| `AIK2001-apiversion` | rig.yaml line 1 `apiVersion: aiakos.dev/v2` | `rig.yaml:1:13: error AIK2001: unsupported apiVersion 'aiakos.dev/v2'` + `  hint: this version supports aiakos.dev/v1` |
| `AIK2001-missing-apiversion` | rig.env.yaml without line 1 | `rig.env.yaml:1:1: error AIK2001: missing apiVersion` + same hint |
| `AIK2001-kind` | agent.yaml line 2 `kind: Rig` | `agents/impl/agent.yaml:2:7: error AIK2001: expected kind 'Agent', found 'Rig'` (missing: `missing kind, expected 'Agent'`) |
| `AIK2002-typo` | rig.yaml line 11 `    chekout: shared` | `rig.yaml:11:5: error AIK2002: unknown field 'chekout' in seat 'impl'` + `  hint: did you mean 'checkout'?` |
| `AIK2002-no-hint` | rig.yaml add line 12 `colour: blue` | `rig.yaml:12:1: error AIK2002: unknown field 'colour'` |
| `AIK2003-name` | rig.yaml without line 3 | `rig.yaml:1:1: error AIK2003: missing required field 'name'` |
| `AIK2003-agent-ref` | rig.yaml without line 10 | `rig.yaml:9:5: error AIK2003: missing required field 'agent_ref' in seat 'impl'` |
| `AIK2004-enum` | rig.yaml add line 12 `    kind: robot` | `rig.yaml:12:11: error AIK2004: invalid value 'robot' for field 'kind' in seat 'impl'` + `  hint: allowed values: agent, human` |
| `AIK2004-pattern` | rig.yaml line 3 `name: Demo` | `rig.yaml:3:7: error AIK2004: invalid value 'Demo' for field 'name'` + `  hint: must match ^[a-z][a-z0-9-]{1,39}$` |
| `AIK2004-type` | rig.yaml line 8 `seats: none`, lines 9–11 removed | `rig.yaml:8:8: error AIK2004: field 'seats' must be a list` (also `a string`, `a mapping`) |
| `AIK2004-key` | rig.env.yaml line 7 `  App:` | `rig.env.yaml:7:3: error AIK2004: invalid key 'App' in repos` + `  hint: must match ^[a-z][a-z0-9._-]{0,63}$` |
| `AIK2005-field` | rig.yaml add line 12 `pods: []` | `rig.yaml:12:1: error AIK2005: reserved field 'pods' is not supported in this version (planned for M2)` |
| `AIK2005-value` | rig.yaml line 11 `    checkout: task-worktree` | `rig.yaml:11:15: error AIK2005: reserved value 'task-worktree' for field 'checkout' in seat 'impl' is not supported in this version (planned for M8)` |
| `many-errors` | typo + `pods` in rig.yaml, `kind: Rig` in agent.yaml, `App` key in env | the four diagnostics above, in file order |

`<where>` goes right after the field name in every message, as in the AIK2005-value row.

## Tests (`tests/Aiakos.Spec.Tests`)

- Layout: `Fixtures/valid/minimal/` and `Fixtures/invalid/<name>/` holding the input files plus
  `expected/diagnostics.txt` (exact `DiagnosticFormatter.Format` output, LF, trailing newline).
- `GoldenTests`: one `[Theory]` over every `Fixtures/invalid/*` directory asserting exact text
  (for `AIK1002-syntax` compare only up to `YAML syntax error: `), and asserting `Rig` is null.
- `ValidTests`: `valid/minimal` → no diagnostics; the same with a BOM, with `x-note: hi` at top
  level and inside the seat, and with an env file passed by explicit path.
- `GeneratedFileTests`: the two AIK1004 cases, written to a temp directory.
- Unit tests: Levenshtein distance; hint tie-break; `Format` with and without a hint; missing
  rig root (AIK1001 for `rig.yaml` and for `rig.env.yaml`); errors in a second seat use
  ` in seat '<id>'` and the right line/column; an invalid seat `id` falls back to `seats[<i>]`.
- Style: file-scoped namespace `Aiakos.Spec.Tests`, `public sealed class`, sentence-style method
  names (`ReportsDuplicateKeys`), as in `tests/Aiakos.Hosting.Wsl.Tests/WslConfigParserTests.cs`.

## Definition of done

- `dotnet build -c Release` at the repo root: 0 warnings, 0 errors.
- `dotnet test --project tests/Aiakos.Spec.Tests -c Release`: all green. (Other test projects
  need Docker and WSL; you do not need to run them, and must not change them.)
- All files LF, UTF-8 without BOM, final newline.
- One commit on the current branch, subject
  `feat(spec): rig file envelope and diagnostics (#14)`. Do not push, do not open a PR.

## Out of scope (do not implement, do not stub beyond what is named above)

Resolution and defaults, `ResolvedRig` content, hashing, canonical JSON, projection; checks on
file references, paths, skills, URLs, permission rules, secrets and node paths; cross-file checks
(rig name vs env `rig`, unknown repos/seats, duplicate IDs, reserved seat IDs, human-seat fields,
"at least one agent seat"); every code outside AIK1001–AIK1004 and AIK2001–AIK2005; CLI wiring;
`UPDATE_GOLDEN`; changes to `docs/`, `CLAUDE.md`, CI, or any existing project.
