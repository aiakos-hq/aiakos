---
id: <issue>-<n>
title: "#<issue> slice <n> — <short title>"
issue: <issue>
status: draft            # draft | approved | implemented | superseded
route: impl/opencode     # or impl/sonnet; a story can override it in stories.md
paths: [src/<project>/, tests/<project>.Tests/]   # the gate rejects changes outside these
date: <yyyy-mm-dd>
---

# Brief: #<issue> slice <n> — <short title>

<!--
Location: docs/briefs/<issue>-<n>/brief.md, next to items.tsv. Add a row to docs/briefs/README.md.
The flow and its definitions are in docs/workflow.md. Delete these comments.

A brief is a closed description: a splitter sorts its items into stories and an implementer gets
its story as this text with the other stories' items removed. So:

- Every rule, change to earlier behaviour, expected output and test is an **item** with an ID,
  and is listed in items.tsv (id, type, scope, needs; tab-separated).
- An item is marked by its ID at the start of a line ("R3. …", "- C1. …", "- T2. …") or in the
  first column of a table ("| `E1` | … |"). Lines that continue an item are indented.
- IDs: R<n> rules, C<n> changes to earlier behaviour, T<n> tests, G<n> rules that apply to every
  story (scope "all"); expected outputs by a short name. Never renumber an approved brief.
- Do not put a sentence between items that only makes sense with one of them: it stays in every
  story.
- Everything outside the items (goal, files, public surface, definition of done, out of scope)
  is given to every story unchanged. Write it so that it is true for each of them.

Lessons from the first slices:
- A rule that is stated only in prose gets missed. Give every rule one expected output with the
  exact text; the check refuses a rule that no output or test needs.
- The implementer invents behaviour where the brief is silent. Say what to do when it is silent.
- Earlier tests are part of the contract: every earlier result that changes is a C item, and one
  sentence says why the others will not change.
- Never put token-like strings in fixtures; GitHub push protection blocks the commit.
-->

Part of #<issue>. Self-contained. **Do not read `docs/specs/` or `docs/adr/`.** <!-- name the one exception, with line numbers, if a table must be copied -->
Where the brief is silent, pick the simplest behaviour and say so in the commit body.

## Goal

<!-- Two or three sentences: what exists after this slice, and what deliberately does not yet. -->

## Files to create or touch (nothing else)

<!-- The same list as "paths" in the front matter, with what goes where. -->

| Path | What |
|---|---|
| | |

No `Version=` on `PackageReference`, no `NoWarn`, no `#pragma warning disable`, no
`[SuppressMessage]`. <!-- add what this slice must not reference -->

## Public surface (exact names)

```csharp
```

<!-- State who compiles against it (a later slice, the acceptance tests). "Unchanged" is a valid answer. -->

## General rules

<!-- Scope "all" in items.tsv: defensive input, order, what runs when an earlier check failed. -->

G1.

## Changes to earlier behaviour

<!-- One C item per earlier result that changes, with the test or fixture that has to change. -->

- C1.

## Rules

<!-- One R item per behaviour: condition, result, exact message, position. -->

R1.

## Expected outputs: exact text

<!-- A base fixture, then one row per case: the single change and the exact expected output.
Check line and column numbers by hand. Each row is an item of type "output" that needs its rule. -->

| ID | Change | Expected |
|---|---|---|
| `<name>` | | |

## Tests

<!-- Test project, fixture layout and the style file to imitate (not items), then one T item per
test that is not an expected output. -->

- T1.

## Definition of done

- `dotnet build -c Release` at the repo root: 0 warnings, 0 errors.
- `dotnet test --project <test project> -c Release`: all green. <!-- name what need not run -->
- If the same test still fails after three attempts, stop and report what you tried.
- All files LF, UTF-8 without BOM, final newline.
- One commit on the current branch, subject `<type>(<scope>): <the story's title> (#<issue>)`.
  The body lists every choice made where the brief was silent<!-- and the risks sentence, if any -->.
  Do not push, do not open a PR.

## Out of scope (do not implement, do not stub)

<!-- Name the neighbouring work explicitly: later slices, other projects, docs, CI. -->
