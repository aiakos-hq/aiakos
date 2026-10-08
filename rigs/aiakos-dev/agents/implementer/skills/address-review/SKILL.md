---
name: address-review
description: Address each finding on the existing PR without rewriting history.
---

1. Read the review and discussion with `gh pr view <n> --comments`, then read the exact diff
   with `gh pr diff <n>`. List findings so each one gets a response.
2. Address each finding with a code change, or give a reason in a PR comment for leaving it
   unchanged. Do not silently omit a finding.
3. Run `git fetch origin`. If the branch is behind, merge `origin/main` with
   `git merge origin/main`; do not rewrite existing history.
4. Run `dotnet build -c Release` and `dotnet test` after the changes. Fix failures caused by
   this change.
5. Make a conventional commit with the trailer `Aiakos-Seat: impl@aiakos-dev`, then push
   the existing branch with `git push`.
6. Comment on the existing PR with `gh pr comment <n>`, beginning `Addressed at <sha>:` and
   listing one line for each finding. Use `--body-file` for multiline GitHub content; never
   shell interpolate report or review text.
7. End with the prescribed turn report from `CULTURE.md` and `GUIDANCE.md`, beginning
   `REPORT impl@aiakos-dev`, with status `changes-pushed`.

Follow the stop rules in `CULTURE.md` and the implementer `GUIDANCE.md`.
