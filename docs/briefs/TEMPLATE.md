---
id: <issue>-<n>
title: "#<issue> slice <n> — <short title>"
issue: <issue>
status: draft            # draft | approved | implemented | superseded
route: impl/opencode     # or impl/sonnet
date: <yyyy-mm-dd>
---

# Brief: #<issue> slice <n> — <short title>

<!--
File name: docs/briefs/<issue>-<n>-<slug>.md. Add a row to docs/briefs/README.md.
A brief is the whole task for an implementer that reads nothing else. It becomes the body of the
slice issue (title "<issue>-<n>: <short title>", first body line "Part of #<issue>."), with a
routing label impl/opencode or impl/sonnet. Aim for two pages. Delete these comments.

Lessons from the first slices:
- A rule that is stated only in prose gets missed. Give every rule one example with the exact
  expected text.
- The implementer invents behaviour where the brief is silent. Say what to do when it is silent.
- Earlier tests are part of the contract: say which earlier expectations may change, and why the
  others will not.
- Never put token-like strings in fixtures; GitHub push protection blocks the commit.
-->

Part of #<issue>. Self-contained. **Do not read `docs/specs/` or `docs/adr/`.** <!-- name the one exception, with line numbers, if a table must be copied -->
Where the brief is silent, pick the simplest behaviour and say so in the commit body.

## Goal

<!-- Two or three sentences: what exists after this slice, and what deliberately does not yet. -->

## Files to create or touch (nothing else)

| Path | What |
|---|---|
| | |

No `Version=` on `PackageReference`, no `NoWarn`, no `#pragma warning disable`, no
`[SuppressMessage]`. <!-- add what this slice must not reference -->

## Public surface (exact names)

```csharp
```

<!-- State who compiles against it (a later slice, the scoring test). "Unchanged" is a valid answer. -->

## Rules

<!-- Numbered. Each: condition, result, exact message, hint, position. Start with general rules
(defensive input, order, what runs when an earlier check failed), then changes to earlier
behaviour, then one rule per behaviour. -->

1.

## Expected outputs: exact text, one golden each

<!-- A base fixture, then one row per case: the single change and the exact expected output.
Check line and column numbers by hand. -->

| Fixture | Change | Expected |
|---|---|---|
| | | |

## Tests

<!-- Test project, fixture layout, test classes by name, the unit tests that cover prose-only
rules, and the style file to imitate. -->

## Definition of done

- `dotnet build -c Release` at the repo root: 0 warnings, 0 errors.
- `dotnet test --project <test project> -c Release`: all green. <!-- name what need not run -->
- If the same test still fails after three attempts, stop and report what you tried.
- All files LF, UTF-8 without BOM, final newline.
- One commit on the current branch, subject `<type>(<scope>): <subject> (#<issue>)`. The body
  lists every choice made where the brief was silent<!-- and the risks sentence, if any -->. Do
  not push, do not open a PR.

## Out of scope (do not implement, do not stub)

<!-- Name the neighbouring work explicitly: later slices, other projects, docs, CI. -->
