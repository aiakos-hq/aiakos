---
name: review-pr
description: Review the PR at its exact head and post one comment review.
---

1. Read the PR with `gh pr view <n>`. Identify its linked issue and merged spec, then read the
   acceptance criteria and relevant risk rows.
2. Check `git status`. Stop if the checkout is dirty; do not reset or clean it. Check out the PR
   with `gh pr checkout <n>` and record the exact head with `git rev-parse HEAD`. A later push
   changes the head and requires a new review round.
3. Read the exact changes with `gh pr diff <n>`. Check every criterion using test or command
   evidence, or mark it manual or not verifiable here. Check committed tests,
   `dotnet build -c Release` with no new warnings, `dotnet test`, green `gh pr checks <n>`, docs/spec deviations,
   claimed risk rows, repository rules, secrets and accepted ADRs. Report checks that could not
   be run; do not guess their result.
4. Prepare one review comment. Begin exactly with `Review by review@aiakos-dev at <head sha>`.
   Include the verdict, a criterion/evidence table, and findings with severity (`blocking`,
   `should-fix` or `nit`), `path:line` and a concrete reason. Use one temporary body-file outside the checkout.
   Writing that file with shell is allowed; do not interpolate report or review
   text into a shell command or bypass a denied tool. Remove the temporary file after submission.
5. Post the comment review with
   `gh pr review <n> --comment --body-file <file>`. Never approve or request changes. A review is
   ready only when there are zero blocking findings.
6. Perform one review per round. If a new commit is pushed, review the new exact head in a new
   round.
7. End with the report defined in `CULTURE.md` and reviewer `GUIDANCE.md`, beginning
   `REPORT review@aiakos-dev`; use status `reviewed` after posting the comment, or `blocked` or
   `question` when appropriate. It is the last output and nothing follows it.

If `git fetch origin` fails because of a shared-clone lock, retry once. If it still fails, stop and
report blocked; do not remove the lock. Do not retry unrelated failures automatically.
