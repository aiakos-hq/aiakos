# Implementer guidance

## Inputs

Start with `gh issue view <n>`. Read the merged linked spec and its acceptance
criteria, the cited ADRs and plan sections, and the rows owned by the issue in `docs/risks.md`.
If a nontrivial unmerged spec is required, stop and ask the lead a question before changing
files.

## Worktree

The implementer worktree persists across issues. Check `git status` before starting. A dirty git status
blocks new work; do not automatically run `git reset`, `git clean` or `git stash`.
Run `git fetch origin`, then create the issue branch from `origin/main`. Use `feat/<n>-<slug>` for a
feature, `fix/<n>-<slug>` for a fix, or `docs/<n>-<slug>` for documentation.

A failed fetch caused by a shared-clone lock may be retried once. If it still fails, stop and
report the problem as blocked. Do not remove locks. Do not automatically retry other unrelated
failures.

## Definition of done

Meet the acceptance criteria and show criteria met with evidence for the issue. Include
committed tests with the change.
Run the Release build with no new warnings and run the relevant tests. Update docs when needed.
Put any deviation from the spec under “Changes after acceptance.” Use
`.github/pull_request_template.md` and identify the risk IDs checked or closed by the change.

## Not verified here

Do not claim that the Windows dev AppHost, WSL E2E tests or manual demos were verified unless
they were run. State what was not verified honestly in the PR.

## Bootstrap

Never run a development build against the `aiakos-dev` team instance. Tests use other rig names,
other homes and other ports so they cannot address that instance.

## Attribution

Every commit includes the trailer `Aiakos-Seat: impl@aiakos-dev`. The pull request says
`Implemented by impl@aiakos-dev`.

## Turn report

The last output of every completed, blocked or paused turn is a report in this shape:
Use `ready-for-review` or `changes-pushed` when appropriate. Use `blocked`, `question` or
`in-progress` for those states. The next line names the action for the lead.

```text
REPORT impl@aiakos-dev
issue: #<n>          pr: #<n> | none          head: <short sha> | none
status: ready-for-review | changes-pushed | reviewed | blocked | question | in-progress
summary: <one to three lines>
next: <lead action>
```
