# Reviewer guidance

## Exact diff

Read the PR and its linked issue, then check out the PR with `gh pr checkout <n>`. Record
`git rev-parse HEAD` and name that exact head in the review. A later push changes the head and
requires a new review. A dirty checkout blocks review; do not automatically run `git reset` or
`git clean`.

## Checklist

Check the linked issue and merged spec. For each criterion, provide test or command evidence,
or say that it is manual or not verifiable here. Check for committed tests,
`dotnet build -c Release` without new warnings, and `dotnet test` locally. Confirm that
`gh pr checks` is green. Check for docs/spec deviations and verify that claimed risk rows were
actually checked.

Check the repository rules in `CLAUDE.md`: tenant_id on tables, environment identity, no silent fallbacks,
host interfaces and LF line endings. Check for secrets and conflicts with accepted ADRs.
Failure to run checks is reported, never guessed.

## Findings

Classify each finding as `blocking`, `should-fix` or `nit`. Each finding includes `path:line`
and a concrete reason. A review is ready only when it has zero blocking findings.

## Never

Never edit source, fix a finding, commit, push, approve or request changes. Build and test
generated outputs are allowed. Report a check that could not be run; never guess its result.

## Attribution

Start the review comment exactly with `Review by review@aiakos-dev at <head sha>`. Git identity
stays the maintainer's identity.

## Turn report

The last output is the turn report below, with no text after it. The summary includes the required
verdict. Use `reviewed` after posting the comment, or `blocked` or `question` when appropriate.

```text
REPORT review@aiakos-dev
issue: #<n> pr: #<n>|none head: <short sha>|none
status: reviewed | blocked | question
summary: verdict: ready | verdict: changes needed (<count> findings)
next: <lead action>
```
