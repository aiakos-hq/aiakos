---
name: implement-issue
description: Implement one GitHub issue from its merged spec and open a PR.
---

1. Read the assigned issue with `gh issue view <n>`. Read its merged linked spec, acceptance
   criteria, cited ADRs and plan sections, and its rows in `docs/risks.md`. Stop with a question
   if a nontrivial unmerged spec is required.
2. Check `git status`. Stop if the worktree is dirty; do not reset, clean or stash it. Run
   `git fetch origin`, then create `feat/<n>-<slug>`, `fix/<n>-<slug>` or `docs/<n>-<slug>` from
   `origin/main` with `git switch -c <branch> origin/main`. A fetch failed by a shared-clone lock
   may be retried once; if it still fails, report blocked and do not remove the lock. Do not retry
   other unrelated failures automatically.
3. Implement the issue and its tests. Run `dotnet build -c Release` and `dotnet test`. Update
   documentation as needed; record any spec deviation under “Changes after acceptance.” Do not
   claim the Windows development AppHost, WSL end-to-end tests or manual demos unless verified;
   list unverified work honestly in the pull request. Never run a development build against the
   `aiakos-dev` team instance; tests use other rig names, homes and ports.
4. Create a conventional commit with the trailer `Aiakos-Seat: impl@aiakos-dev`.
5. Push the issue branch with `git push -u origin <branch>`, then open the PR with `gh pr create`
   using `.github/pull_request_template.md`. Include `Closes #<n>`, identify the risk IDs checked
   or closed, and attribute it with `Implemented by impl@aiakos-dev`. Check it with
   `gh pr checks <n>` and fix failures caused by this change.
6. End with the prescribed turn report from `CULTURE.md` and `GUIDANCE.md`, beginning
   `REPORT impl@aiakos-dev`.
