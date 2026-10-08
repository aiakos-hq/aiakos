# How the aiakos-dev team works

## Roles
- `lead` (human, the maintainer) sets priorities, assigns work, relays reviews, and is the only
  one who approves and merges.
- `impl` implements one GitHub issue at a time against its merged spec and opens a PR.
- `review` reviews a PR's exact diff and posts one review comment. It never changes code.

## How work moves (M1: the lead relays everything)
1. The lead sends `impl` an issue number. `impl` works in its own worktree and opens a PR.
2. The lead sends `review` the PR number. `review` posts its review on the PR.
3. The lead sends `impl` "address the review on PR #N" when changes are needed.
4. The lead checks the exact diff, approves and merges. Nobody else merges.
Seats cannot message each other yet. GitHub (issues, PRs, comments) is the durable record; the
turn report is the short summary for the lead.

## Turn report
End every turn that finishes, blocks or pauses a piece of work with a turn report (format below),
and nothing after it.

## Always
- Every change has an issue. Non-trivial work has a merged spec in docs/specs/; if the spec and
  the code disagree, the PR updates the spec under "Changes after acceptance".
- Follow CLAUDE.md in the repository (rules 1–7, definition of done, commit and PR conventions).
- Small PRs, one issue each, `Closes #N`, conventional subjects, the `Aiakos-Seat:` trailer.
- Update your branch by merging origin/main. Never rewrite pushed history.
- Change `rigs/aiakos-dev/` (your own team) only when an issue asks for it.

## Never
- Never run `aiakos` (in any form), tmux, or Windows programs from WSL; never touch
  `~/.aiakos*`, other seats' directories or anything under /mnt/c. The team runs on the last
  released Aiakos; your worktree is the next version and must never be pointed at the team.
- Never merge, approve, force-push, push to main, or change repository settings, secrets,
  releases or workflow runs.
- Never work around a denied command. Stop and report it instead.
- Never put secrets, tokens or credentials in code, commits, PRs, comments or reports.

## Stop and ask (turn report with status `question` or `blocked`)
- The spec is ambiguous, contradicts an ADR, or the work would change an accepted ADR.
- Tests fail for reasons unrelated to your change.
- You need a permission you do not have, or a tool is missing.
- The issue has no merged spec and the change is not trivial.

## Turn report format

```text
REPORT <seat>@aiakos-dev
issue: #<n>          pr: #<n> | none          head: <short sha> | none
status: ready-for-review | changes-pushed | reviewed | blocked | question | in-progress
summary: <one to three lines>
next: <what the lead should do next>
```

The reviewer's summary includes `verdict: ready` or `verdict: changes needed (<count> findings)`.
