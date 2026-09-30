---
id: 0008
title: The aiakos-dev rig and the M1 acceptance
status: draft            # draft | accepted | implemented | superseded
issue: https://github.com/aiakos-hq/aiakos/issues/16
milestone: M1
owner: "@bsakel"
---

# 0008 — The `aiakos-dev` rig and the M1 acceptance

## Context

Issue [#16](https://github.com/aiakos-hq/aiakos/issues/16) asks for `rigs/aiakos-dev`: `rig.yaml`,
the agent definitions and `CULTURE.md`, for an implementer and a reviewer seat with the maintainer
as lead. It is also **M1's acceptance test**: the rig, running on the released M1 tool, implements
an M2 issue through a reviewed PR ([plan §10](../plan.md#10-roadmap-each-milestone-mvp-complete)).
After that, the repository leaves stage A (a human with one agent session) and enters stage B
([plan §11](../plan.md#11-how-we-build-it--aiakos-builds-aiakos)).

Spec 0003 already shows the rig as its worked example and proves that the format can express it.
This spec turns that example into the real team: what each seat is told, what it may do, how work
moves between the lead and the seats without the M2 MCP server or the M3 queue, what the machine
needs, how the release is pinned, and the exact procedure that closes M1.

Decisions this spec implements (not reopened here):

- [ADR 0008](../adr/0008-bootstrap-rule.md): the team runs on the last released Aiakos, never on
  the working tree it changes. The rig files live in the repository the seats change (spec 0003
  R32: they must stay loadable by the pinned release).
- [ADR 0009](../adr/0009-work-management.md): tickets in GitHub Issues, specs and ADRs in the
  repository, all changes through PRs.
- [ADR 0006](../adr/0006-claude-code-first-harness.md): Claude Code seats in M1.
- [ADR 0015](../adr/0015-projection-outside-checkouts.md) and
  [ADR 0027](../adr/0027-claude-projection-and-settings.md): guidance and skills are projected
  outside the checkout, next to the repository's own `CLAUDE.md`, which still applies.
- Spec 0003 D2: the reviewer uses `seat-worktree` plus deny rules, not `shared-readonly`.
- CLAUDE.md "Definition of done" (a reviewer checked the exact diff; the maintainer approves and
  merges) and rules 1–7.

### Depends on earlier specs

- **Spec 0003**: the file format, the worked example (`rig.yaml`, both `agent.yaml` files, the
  `implement-issue` skill, `rig.env.yaml`), permission rules as Claude Code rule strings,
  `permission_mode` values, the `/mnt/<drive>` warning, R32 (loadable by the pinned release).
- **Spec 0005**: the settings file adds `Bash(tmux:*)` to every seat's deny list; bodies arrive as
  a paste after the lead `[aiakos from <sender> #<d>]`; only `/compact` can be sent as a slash
  command; permission prompts show as `needs-input`; the seat runs with the user's `HOME` and
  Claude login (`auth: subscription`).
- **Spec 0006**: the three axes shown by `ps`; `send` only when `idle`; `up --fresh` is a recorded
  decision.
- **Spec 0007** (draft, in review with this one): `aiakos instance start|stop|status`, `up`
  (including `--dry-run`), `down`, `send --wait turn`, `capture`, `ps`, `attach`; versions
  `0.<milestone>.<patch>` with `0.1.0` as the M1 release; the CLI refuses mutating commands across
  minor versions. If 0007 changes in review, this spec follows.

## Goals

- `rigs/aiakos-dev/` holds a complete, loadable rig: two Claude Code seats (`impl`, `review`) and
  the human `lead`, with guidance, skills, culture and permissions that fit this repository's
  process.
- The M1 working loop is written down and works with only the M1 CLI: the lead assigns an issue,
  `impl` opens a PR, `review` reviews the exact diff, the lead merges.
- The team is pinned to a release in the repository, a CI job proves the rig files load with that
  release, and upgrading is a PR.
- The machine prerequisites are checkable by a script before the first `up`.
- The M1 acceptance is a procedure with recorded evidence, not a judgement call.

## Non-goals / out of scope

- Seat-to-seat messaging, handoff and queue items (M2 MCP, M3 queue). In M1 the lead relays.
- An OpenCode seat (M2), sandboxed seats and separate credentials per seat (M6).
- A GitHub machine account for the seats (Q2).
- Automating the lead: nothing sends to seats on a schedule or reacts to their output.
- Changing the CLI or the file format. Anything the rig needs from them goes through specs
  0003/0007.

## Requirements

### Files

- **R1** `rigs/aiakos-dev/` contains exactly the files in [Layout](#layout). `rig.env.yaml` is
  listed in `rigs/aiakos-dev/.gitignore` and never committed; `rig.env.example.yaml` is committed
  with the maintainer-neutral values of [Local binding](#local-binding).
- **R2** The rig is spec 0003's worked example with the changes in [Rig](#rig): same rig name,
  seat IDs, repos, harness and checkout policies.
- **R3** Loading the rig with the pinned release (`aiakos up --dry-run rigs/aiakos-dev --env
  rigs/aiakos-dev/rig.env.example.yaml`) reports zero errors and zero warnings.
- **R4** `rigs/aiakos-dev/README.md` is rewritten for stage B: seat table (the reviewer is
  `seat-worktree` with deny rules, spec 0003 D2), prerequisites, the runbook of
  [Operating the rig](#operating-the-rig), and the upgrade procedure.

### Seats and their instructions

- **R5** `CULTURE.md` is projected into both seats and states the team process: roles, the
  working loop, the turn report, what no seat ever does, when to stop and ask, and the bootstrap
  rule ([Culture](#culture)).
- **R6** The implementer (`agents/implementer`) has guidance and two skills: `implement-issue`
  (issue → spec → branch → code and tests → PR) and `address-review` (read the review, change,
  push, answer). Its permissions are those in [Implementer](#implementer).
- **R7** The reviewer (`agents/reviewer`) has guidance and the skill `review-pr`: check out the PR
  at its head commit, check it against the issue, the spec's acceptance criteria and the definition
  of done, build and test, and post one review comment that names the head commit it reviewed and
  a verdict. It never changes files, commits or pushes. Its permissions are those in
  [Reviewer](#reviewer).
- **R8** Every turn that ends a unit of work ends with a **turn report** in the format of
  [Turn report](#turn-report), so the lead can read the outcome with `aiakos capture`.
- **R9** Work done by a seat is attributable: commits carry a trailer `Aiakos-Seat:
  <seat>@aiakos-dev`; PR bodies say `Implemented by impl@aiakos-dev`; review comments start with
  `Review by review@aiakos-dev at <head sha>`. The Git author and the GitHub account stay the
  maintainer's in M1 (Q2).
- **R10** The deny rules common to both seats ([Common deny rules](#common-deny-rules)) block, as a
  speed bump, the ways a seat could reach the team's own instance or the machine outside its task:
  the `aiakos` CLI in any form, tmux, Windows interop executables, killing processes, the Windows
  filesystem, Aiakos homes and other seats' token files, merging, force-pushing and pushing to
  `main`, and GitHub administration. Branch protection on `main` stays the real enforcement for
  the repository.

### Pinning and compatibility

- **R11** The pinned release is recorded in the repository's local tool manifest
  `.config/dotnet-tools.json` (tool `aiakos`, command `aiakos`, exact version). The lead runs the
  team with `dotnet aiakos …` from the Windows checkout of `main`; `dotnet run --project
  src/Aiakos.Cli` is the development build and is never used for the team (ADR 0008).
- **R12** A CI workflow `rig-compat.yml` runs on pull requests that change `rigs/aiakos-dev/**` or
  `.config/dotnet-tools.json`, and on `main`: `dotnet tool restore`, then `dotnet aiakos up --dry-run
  rigs/aiakos-dev --env rigs/aiakos-dev/rig.env.example.yaml`. A PR that makes the rig
  unloadable by the pinned release fails (spec 0003 R32, D5). It becomes a required check once it
  has passed on `main`.
- **R13** Upgrading the team is a PR that changes only the manifest (and, if needed, the rig files
  for a new format feature). After merge the lead follows spec 0007's upgrade procedure. A rig file
  change that needs a newer release is merged in the PR after the pin moves (spec 0003 R32).
- **R14** The `aiakos-dev` rig is only ever brought up on the `release` instance (or another
  instance started from a released version). Testing a development build uses a copy under another
  rig name and `seat_root` (for example `aiakos-dev-test` under `~/aiakos/test-seats`), so its
  worktrees and branches can never be the team's (spec 0003 RK1).

### Machine

- **R15** `rigs/aiakos-dev/check-prereqs.sh` (POSIX `sh`, read-only, run by the lead inside WSL)
  checks every item of [Prerequisites](#prerequisites) and prints one `ok` / `FAIL <what to do>`
  line per item; it exits non-zero if any item fails. It never installs or changes anything.

### Acceptance

- **R16** M1 is accepted when the procedure in [M1 acceptance](#m1-acceptance) has run to the end
  on a released `0.1.x` and its evidence is recorded in this spec's
  [Acceptance record](#acceptance-record) in the PR that marks the spec `implemented`.
- **R17** The same PR updates `CLAUDE.md` ("Current stage" becomes stage B: the rig, the pinned
  release, the runbook link) and closes the M1 milestone's last risk review (docs/risks.md, "When
  to review it").

## Design

### Layout

```text
rigs/aiakos-dev/
  .gitignore                         rig.env.yaml
  README.md                          stage B: seats, prerequisites, runbook, upgrading
  rig.yaml
  rig.env.example.yaml               committed example binding (R1)
  CULTURE.md
  check-prereqs.sh                   R15
  agents/
    implementer/
      agent.yaml
      GUIDANCE.md
      skills/
        implement-issue/SKILL.md
        address-review/SKILL.md
    reviewer/
      agent.yaml
      GUIDANCE.md
      skills/
        review-pr/SKILL.md
.config/dotnet-tools.json            R11 (repository root)
.github/workflows/rig-compat.yml     R12
```

### Rig

`rigs/aiakos-dev/rig.yaml` — spec 0003's example with longer descriptions (they appear in every
seat's roster, so they say what to send to whom):

```yaml
apiVersion: aiakos.dev/v1
kind: Rig
name: aiakos-dev
description: The team that builds Aiakos. Runs on the last released Aiakos (ADR 0008).
culture_file: CULTURE.md

workspace:
  repos:
    - name: aiakos
      url: https://github.com/aiakos-hq/aiakos.git
      default_branch: main

seats:
  - id: lead
    kind: human
    description: Maintainer. Sets priorities, assigns issues, relays reviews, approves and merges.

  - id: impl
    agent_ref: local:agents/implementer
    description: Implements one issue at a time from its spec and opens a PR.
    harness: claude-code
    model: opus
    checkout: seat-worktree
    requires: { auth: subscription }

  - id: review
    agent_ref: local:agents/reviewer
    description: Reviews a PR's exact diff against its spec, acceptance criteria and the definition of done.
    harness: claude-code
    model: opus
    checkout: seat-worktree          # read-only intent enforced by deny rules (spec 0003 D2)
    requires: { auth: subscription }
```

### Local binding

`rig.env.example.yaml` (committed; the maintainer copies it to `rig.env.yaml`):

```yaml
apiVersion: aiakos.dev/v1
kind: RigEnv
rig: aiakos-dev
seat_root: ~/aiakos/seats           # on the WSL filesystem, never /mnt/c
placement:
  default_node: wsl-local
repos:
  aiakos: { path: ~/src/aiakos }    # a clone inside WSL, separate from the Windows checkout
```

The bound clone `~/src/aiakos` is only the source of the two seat worktrees
(`~/aiakos/seats/aiakos-dev/{impl,review}/repos/aiakos`); nobody works in it directly. The lead's
Windows checkout is where `dotnet aiakos up rigs/aiakos-dev` runs from, always on an up-to-date
`main`.

### Culture

`CULTURE.md`, as projected into both seats (final wording in the implementation PR; every rule
below is normative):

```markdown
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
```

### Turn report

```text
REPORT <seat>@aiakos-dev
issue: #<n>          pr: #<n> | none          head: <short sha> | none
status: ready-for-review | changes-pushed | reviewed | blocked | question | in-progress
summary: <one to three lines>
next: <what the lead should do next>
```

`reviewed` is the reviewer's status; the verdict is in its summary (`verdict: ready` or
`verdict: changes needed (<count> findings)`). The lead reads it with
`dotnet aiakos capture <seat> --lines 40`. The format is plain text on purpose: in M3 the same
fields become a queue item's closure (reason, target), so the habit carries over.

### Implementer

`agents/implementer/agent.yaml`:

```yaml
apiVersion: aiakos.dev/v1
kind: Agent
name: implementer
description: Implements a GitHub issue against its spec, with tests, and opens a PR.
defaults:
  harness: claude-code
  model: opus
guidance:
  - GUIDANCE.md
skills:
  - skills/implement-issue
  - skills/address-review
harnesses:
  claude-code:
    permission_mode: acceptEdits
    permissions:
      allow:
        - Bash(dotnet build:*)
        - Bash(dotnet test:*)
        - Bash(dotnet format:*)
        - Bash(dotnet restore:*)
        - Bash(dotnet pack:*)
        - Bash(git status:*)
        - Bash(git diff:*)
        - Bash(git log:*)
        - Bash(git show:*)
        - Bash(git fetch:*)
        - Bash(git switch:*)
        - Bash(git add:*)
        - Bash(git commit:*)
        - Bash(git merge origin/main:*)
        - Bash(git push:*)
        - Bash(gh issue view:*)
        - Bash(gh issue list:*)
        - Bash(gh issue comment:*)
        - Bash(gh pr create:*)
        - Bash(gh pr view:*)
        - Bash(gh pr diff:*)
        - Bash(gh pr checks:*)
        - Bash(gh pr comment:*)
        - Bash(gh run view:*)
        - Bash(gh run list:*)
      ask:
        - Bash(git reset:*)
        - Bash(git clean:*)
        - Bash(git checkout:*)
        - Bash(git stash:*)
      deny:
        # the common deny rules below, plus:
        - Bash(gh pr review:*)
```

`GUIDANCE.md` (outline, normative):

- **Inputs.** The issue (`gh issue view`), its merged spec, the ADRs and plan sections the spec
  cites, and the risk register rows whose owner is the issue (docs/risks.md "Before each
  implementation PR"). If the spec is not merged and the change is not trivial: stop and ask.
- **Worktree.** Your worktree persists between issues. Start each issue from a clean tree on a
  new branch `feat/<n>-<slug>` (or `fix/…`, `docs/…`) from `origin/main`. A dirty tree at the
  start is a `blocked` report, not something to clean up by yourself.
- **Definition of done.** Acceptance criteria met with evidence; tests added and passing; `dotnet
  build -c Release` with no new warnings; docs and the spec updated in the same PR; the PR body
  follows `.github/pull_request_template.md` and says which risk rows it checks or closes.
- **What you cannot do here.** The dev AppHost, the WSL end-to-end tests and the manual demos need
  Windows; list them in the PR under "Not verified here" for the lead instead of claiming them.
- **Bootstrap.** Never run the code you build against the team's instance or the aiakos-dev rig
  (R14); tests use their own names, homes and ports.
- **Attribution.** `Aiakos-Seat: impl@aiakos-dev` trailer on every commit; `Implemented by
  impl@aiakos-dev` in the PR body.

`skills/implement-issue/SKILL.md` (steps; extends spec 0003's example):

1. `gh issue view <n>`; find and read the linked spec; note its acceptance criteria and risk rows.
2. `git status` must be clean; `git fetch origin`; `git switch -c feat/<n>-<slug> origin/main`.
3. Implement against the acceptance criteria, with tests; run `dotnet build -c Release` and
   `dotnet test` until both pass.
4. Update the spec (if deviating), docs and the risk register in the same branch.
5. Commit with a conventional subject and the trailer; `git push -u origin <branch>`.
6. `gh pr create` with the template filled, `Closes #<n>`; wait for `gh pr checks` and fix CI
   failures.
7. End with a turn report, status `ready-for-review`.

`skills/address-review/SKILL.md`:

1. `gh pr view <n> --comments` and `gh pr diff <n>`; list each finding.
2. For each finding: change the code, or explain in a PR comment why not.
3. `git merge origin/main` if the branch is behind; build and test; commit; push.
4. `gh pr comment <n>` with "Addressed at <sha>:" and one line per finding.
5. Turn report, status `changes-pushed`.

### Reviewer

`agents/reviewer/agent.yaml`:

```yaml
apiVersion: aiakos.dev/v1
kind: Agent
name: reviewer
description: Reviews a PR's exact diff against its spec, acceptance criteria and the definition of done.
defaults:
  harness: claude-code
  model: opus
guidance:
  - GUIDANCE.md
skills:
  - skills/review-pr
harnesses:
  claude-code:
    permission_mode: default
    permissions:
      allow:
        - Bash(gh pr view:*)
        - Bash(gh pr diff:*)
        - Bash(gh pr checkout:*)
        - Bash(gh pr checks:*)
        - Bash(gh pr review:*)
        - Bash(gh issue view:*)
        - Bash(gh run view:*)
        - Bash(git status:*)
        - Bash(git diff:*)
        - Bash(git log:*)
        - Bash(git show:*)
        - Bash(git fetch:*)
        - Bash(git rev-parse:*)
        - Bash(dotnet build:*)
        - Bash(dotnet test:*)
        - Bash(dotnet format --verify-no-changes:*)
      deny:
        # the common deny rules below, plus:
        - Edit
        - Write
        - NotebookEdit
        - Bash(git commit:*)
        - Bash(git push:*)
        - Bash(gh pr create:*)
        - Bash(gh pr comment:*)
        - Bash(gh pr review --approve:*)
        - Bash(gh pr review --request-changes:*)
        - Bash(gh pr review -a:*)
        - Bash(gh pr review -r:*)
```

`gh pr review --comment` is the only way the reviewer speaks on GitHub: GitHub does not let an
account approve or request changes on its own PR, and in M1 every seat uses the maintainer's
account (Q2). One review per round keeps the record in one place.

`GUIDANCE.md` (outline, normative):

- **The exact diff.** Review the PR at its current head commit: `gh pr checkout <n>`, then
  `git rev-parse HEAD`. The review names that commit; a later push needs a new review.
- **Checklist.** Linked issue and merged spec; each acceptance criterion with its evidence (test,
  command output, or "manual, not verifiable here"); tests added for new behaviour; `dotnet build
  -c Release` without new warnings and `dotnet test` passing locally; CI checks green; docs and the
  spec updated, deviations under "Changes after acceptance"; risk rows the PR claims actually
  checked; CLAUDE.md rules (tenant_id on new tables, identity never from bodies, no silent
  fallbacks, host concerns behind interfaces, LF endings); no secrets; ADRs respected.
- **Findings.** Each finding has a severity (`blocking`, `should-fix`, `nit`), a `path:line` and a
  concrete reason; no finding without a reason. Verdict `ready` only with zero blocking findings.
- **Never.** Change files, fix code, commit, push, approve or request changes. If the build or
  tests cannot run, say so in the review; do not guess the result.

`skills/review-pr/SKILL.md`:

1. `gh pr view <n>`; read the linked issue and spec.
2. `gh pr checkout <n>`; `git rev-parse --short HEAD` → `<sha>`; `gh pr diff <n>`.
3. Walk the checklist; run `dotnet build -c Release` and `dotnet test`; `gh pr checks <n>`.
4. `gh pr review <n> --comment --body …` starting with `Review by review@aiakos-dev at <sha>`,
   then the verdict, the acceptance-criteria table and the findings.
5. Turn report, status `reviewed`, with the verdict.

### Common deny rules

Both agents carry these (v1 has no shared libraries, so the list is repeated; M2 moves it into a
shared library). Claude Code checks deny before allow; spec 0005 adds `Bash(tmux:*)` itself.

```yaml
- Bash(aiakos:*)
- Bash(dotnet aiakos:*)
- Bash(dotnet tool:*)
- Bash(cmd.exe:*)
- Bash(powershell.exe:*)
- Bash(pwsh.exe:*)
- Bash(wsl.exe:*)
- Bash(kill:*)
- Bash(pkill:*)
- Bash(killall:*)
- Bash(git push --force:*)
- Bash(git push -f:*)
- Bash(git push --force-with-lease:*)
- Bash(git push origin main:*)
- Bash(git push origin HEAD:main:*)
- Bash(gh pr merge:*)
- Bash(gh api:*)
- Bash(gh auth:*)
- Bash(gh secret:*)
- Bash(gh release:*)
- Bash(gh workflow:*)
- Bash(gh repo:*)
- Read(//mnt/**)
- Edit(//mnt/**)
- Read(~/.aiakos*/**)
- Edit(~/.aiakos*/**)
- Read(~/aiakos/seats/*/*/aiakos/**)
- Edit(~/aiakos/seats/*/*/aiakos/**)
```

The last two keep a seat out of every seat's Aiakos directory (settings, hook relay, token file,
spec 0005 [Seat home layout](0005-claude-code-adapter.md#seat-home-layout)) while leaving the
worktrees under `repos/` usable. All of these are prefix and path rules: a determined seat can go
around them (`bash -c`, another path spelling). They make an accident unlikely; they are not
isolation (RK3).

### Prerequisites

Checked by `check-prereqs.sh` inside WSL (`Ubuntu`), in this order:

| Item | Check | Why |
|---|---|---|
| Mirrored networking | `wslinfo --networking-mode` = `mirrored` | spec 0001 R26 |
| tmux ≥ 3.4, `curl`, `flock` | `tmux -V`, `command -v` | ADR 0026, spec 0005 R3 |
| Claude Code ≥ 2.1.284 at `~/.local/bin/claude`, logged in | `claude --version`; `~/.claude.json` has an account | spec 0005 R3, `auth: subscription` |
| .NET 10 SDK | `dotnet --version` ≥ `global.json` | seats build and test Aiakos |
| Docker from WSL | `docker info` | the database tests use Testcontainers (closes spike 0005's open item S0005-4) |
| GitHub CLI, logged in | `gh auth status` for `github.com` | issues, PRs, reviews |
| Git identity | `git config user.name`, `user.email` set | commits |
| The bound clone | `~/src/aiakos` is a git work tree with `origin` → `aiakos-hq/aiakos` | spec 0003 (M1 does not clone) |
| Not on /mnt | `seat_root` and the clone are not under `/mnt/` | spec 0003 AIK5009, performance |

On Windows, the lead also needs the .NET 10 SDK, Docker Desktop, `dotnet tool restore` in the
checkout, and an initialized instance (`dotnet aiakos instance init`).

### Operating the rig

The runbook in `rigs/aiakos-dev/README.md` (commands run in the Windows checkout of `main`):

| When | Commands |
|---|---|
| Start of day | `git pull`; `dotnet tool restore`; `dotnet aiakos instance start`; `dotnet aiakos up rigs/aiakos-dev`; `dotnet aiakos ps` |
| Assign an issue | `dotnet aiakos send impl "Implement #42 (spec docs/specs/NNNN-….md). Use the implement-issue skill." --wait turn` |
| Read a result | `dotnet aiakos capture impl --lines 40` (turn report); the PR on GitHub |
| Review | `dotnet aiakos send review "Review PR #57 for issue #42. Use the review-pr skill." --wait turn` |
| Changes needed | `dotnet aiakos send impl "Address the review on PR #57. Use the address-review skill." --wait turn` |
| Permission prompt (`needs-input`) | `dotnet aiakos attach impl --write`, answer, detach (`C-b d`) |
| Between issues | `dotnet aiakos send impl /compact`; a clean start instead: `dotnet aiakos up --fresh --seat impl --note "new area: …"` |
| After changing rig files | merge the PR, `git pull`, `dotnet aiakos up rigs/aiakos-dev`; drifted seats: `down` then `up` (spec 0007 R36); fresh or `/compact` if the guidance must apply (spec 0005 D11) |
| End of day | nothing (seats stay up) or `dotnet aiakos down --all` and `dotnet aiakos instance stop` |
| After a reboot | `dotnet aiakos instance start`; for each `unknown` seat: `capture`, `down`, `up` (spec 0007 R39) |
| Upgrade | merge the pin PR (R13), then spec 0007's upgrade procedure |

A seat that is `working` for a long time is looked at with `capture` or a read-only `attach`,
never interrupted by `send` (it is refused anyway).

### M1 acceptance

Run by the lead after `0.1.0` is released (spec 0007 Q12), with this spec's files merged and the
pin at `0.1.0`:

| Step | Action | Evidence |
|---|---|---|
| 1 | `check-prereqs.sh` in WSL | all `ok` |
| 2 | `dotnet tool restore`; `dotnet aiakos --version`; `instance.json` with `telemetry.dashboard: true`; `dotnet aiakos instance start` | version `0.1.x` = the pin; `instance status` healthy, node connected |
| 3 | `dotnet aiakos up rigs/aiakos-dev`; `dotnet aiakos ps` | `impl` and `review` `present / idle`, `lead` human |
| 4 | Send `impl` the chosen M2 issue (Q1) | a PR with `Closes #N`, the seat trailer, green CI; turn report `ready-for-review` |
| 5 | After step 4's turn report (seat `idle`, so no turn is cut off): `dotnet aiakos down impl`, `dotnet aiakos up`, then ask `impl` which issue and branch it is on | launch decision `resume`, outcome `ready`; the answer names the issue and branch (M1 "`down`/`up` resumes") |
| 6 | Send `review` the PR | one review comment naming the head commit, with the checklist and a verdict |
| 7 | If changes are needed: relay to `impl`, then review again | a new review at the new head commit |
| 8 | The lead checks the exact diff, approves and merges | merged PR; issue closed |
| 9 | Open the dashboard | one `send` trace from `cli.send` through the SeatActor and the node to `PROMPT_SUBMITTED` (M1 "full flow visible as traces") |
| 10 | Check what ran | `rig_revision.tool_version` and every `seat_launch` of the run come from the pinned version; `ps` shows no drift |

A failure in the released tool during the run is fixed like any bug: an issue, a fix PR (by the
rig if it can still work, otherwise in stage A), a `0.1.x` release, the pin PR, the upgrade, and
the procedure restarts at the step that failed. The acceptance never switches the team to a
development build to get past a problem (rule 1).

### Acceptance record

Filled in by the PR that marks this spec `implemented`: the date, the pinned version, the M2
issue and PR links, the review comment link, the `ps` output of steps 3 and 5, a dashboard
screenshot or trace ID for step 9, the query result of step 10, every problem found on the way
with its issue, and the counts that feed the risks below (`needs-input` prompts per seat, context
percentage at the end of the issue, number of review rounds).

## Acceptance criteria

- [ ] **AC1** `rigs/aiakos-dev/` matches [Layout](#layout); `git check-ignore
  rigs/aiakos-dev/rig.env.yaml` succeeds.
- [ ] **AC2** `dotnet run --project src/Aiakos.Cli -- up --dry-run rigs/aiakos-dev --env
  rigs/aiakos-dev/rig.env.example.yaml` (before the first release) and the `rig-compat` job
  (after it) report zero errors and zero warnings and list `impl` and `review` with node
  `wsl-local`, model `opus`, checkout `seat-worktree`.
- [ ] **AC3** The projected `CLAUDE.md` of each seat contains the roster, `CULTURE.md` and the
  seat's guidance in that order; each seat's `claude-settings.json` contains every common deny rule
  plus its own, and `Bash(tmux:*)` (spec 0005).
- [ ] **AC4** In a running `impl` seat: asking it to run `aiakos ps`, `cat /mnt/c/Windows/win.ini`
  and `gh pr merge 1` is denied by Claude Code for each (the seat reports the denial instead of
  working around it).
- [ ] **AC5** In a running `review` seat: asking it to edit a file, `git commit` and `gh pr review
  --approve` is denied for each; `gh pr review --comment` works.
- [ ] **AC6** `check-prereqs.sh` prints one line per prerequisite and exits 0 on the maintainer's
  machine; with `docker` removed from `PATH` it prints `FAIL` for Docker and exits non-zero.
- [ ] **AC7** A PR that adds an unknown field to `rigs/aiakos-dev/rig.yaml` fails `rig-compat`
  with `AIK2002`.
- [ ] **AC8** The [M1 acceptance](#m1-acceptance) has run to the end and the
  [Acceptance record](#acceptance-record) is filled in; `CLAUDE.md` describes stage B (R17).

## Test plan

- **Loading (CI).** AC2 and AC7 through `rig-compat`; before the first release, the same command
  with the development CLI in the implementation PR.
- **Projection (unit, spec 0003's golden tests).** The implementation PR adds `rigs/aiakos-dev` as
  a second fixture next to spec 0003's copy, so projection of the real files is golden-tested
  (AC3) and a change to guidance shows as a diff to the golden `CLAUDE.md`.
- **Permissions (manual, the maintainer's machine).** AC4 and AC5 in running seats; each denial is
  recorded in the implementation PR.
- **Prerequisites script.** AC6 by hand; `shellcheck` in CI.
- **Acceptance.** AC8, the procedure above.

## Risks and open questions

### Open questions (decide in review)

Each has a recommendation; the requirements above assume it.

- **Q1 — Which M2 issue is the acceptance task?** No M2 issues exist yet. *Recommendation:* create
  the M2 issues when M1's implementation is done, and use **`aiakos spec validate`** (M2 in plan
  §10): it is small, self-contained (loader + CLI, building on `up --dry-run`), easy to review
  against a short spec, and useful to the team right away. Its spec is written and merged in
  stage A before step 4. Something larger (the OpenCode adapter) would test the model more than
  the rig.
- **Q2 — GitHub identity of the seats.** (a) the maintainer's `gh` login in WSL; (b) a machine
  account `aiakos-bot` with a fine-grained token per seat. *Recommendation:* (a) for M1, with
  attribution by trailer and comment prefix (R9). (b) would allow real approvals, but a token per
  seat is a secret to deliver, and seat secrets arrive properly with M6; revisit then.
- **Q3 — How is the release pinned?** (a) the repository's local tool manifest; (b) a global tool
  plus a version file. *Recommendation:* (a) (R11). It is .NET's standard pin, `dotnet tool
  restore` enforces it on every machine and in CI, the upgrade is a reviewable one-line PR, and
  the name `dotnet aiakos` keeps it visibly apart from `dotnet run --project src/Aiakos.Cli`. It
  satisfies ADR 0008 ("installed as a pinned `dotnet tool`"); the ADR text says `-g` only as an
  example.
- **Q4 — Models.** *Recommendation:* `opus` for both seats, as in spec 0003's example. Review
  quality matters as much as implementation quality; revisit with the context and cost numbers
  from the acceptance record.
- **Q5 — Reviewer permission mode.** (a) `default` with an allowlist and deny rules; (b) `plan`.
  *Recommendation:* (a). Plan mode also blocks the builds and tests the reviewer must run, and it
  ends turns with a plan instead of a review.
- **Q6 — Implementer permission mode.** (a) `acceptEdits`; (b) `auto`. *Recommendation:* (a) in
  M1: unknown commands become visible `needs-input` prompts that the lead answers, which is how
  the allowlist gets tuned. Revisit `auto` with the prompt counts from the acceptance record (RK1).
- **Q7 — A fixed turn report format?** *Recommendation:* yes (R8). Without MCP the lead reads
  results from the pane; a fixed format makes that fast and prepares M3's closure contract.
- **Q8 — Disable WSL interop for the distro** (`[interop] enabled=false`), so seats cannot start
  Windows programs at all? *Recommendation:* not in M1. It also removes Windows `PATH` entries and
  tools the maintainer uses in WSL, and it does not stop reading `/mnt/c`. The deny rules cover the
  accident case; M6 sandboxing is the real fix (RK3).
- **Q9 — May seats change `rigs/aiakos-dev/`?** *Recommendation:* yes, through PRs like any other
  file, but only when an issue asks for it (culture). A merged change reaches the running team
  only when the lead runs `up` from the updated `main`, and `rig-compat` keeps it loadable.
- **Q10 — Where does the acceptance evidence go?** *Recommendation:* this spec's
  [Acceptance record](#acceptance-record), in the PR that marks it `implemented`, so M1's proof
  sits next to its definition.
- **Q11 — Fresh conversation per issue?** *Recommendation:* no; `/compact` between issues, and
  `up --fresh --seat` (a recorded decision) when switching areas or when context stays high. A
  fresh start per issue throws away what the seat learned about the repository.

### Risks

Stable IDs; [the register](../risks.md) indexes them.

| ID | Risk | How and when it is checked | Owner |
|---|---|---|---|
| **RK1** | **Permission prompts stall the seats**: every command outside the allowlist becomes `needs-input` and waits for the lead. | Prompt counts per seat in the acceptance record; the allowlist is tuned by PR. | #16 |
| **RK2** | **One GitHub identity for everyone**: commits, PRs and reviews all show the maintainer; attribution relies on trailers and prefixes. | Accepted for M1 (Q2); revisited with M6 seat secrets. | #16, M6 |
| **RK3** | **Deny rules are speed bumps**: prefix and path rules can be bypassed (`bash -c`, another path, interop by full path), so a seat could reach the team's instance or another seat's token. Same class as spec 0004 RK7, spec 0005 RK14 and spec 0007 RK4. | AC4, AC5; the real fix is sandboxing (M6). | #16 (M1 mitigation), M6 |
| **RK4** | **The long-lived implementer worktree accumulates state** (untracked files, `bin/`, `obj/`, a stale branch) and a new issue starts from it. | The skill requires a clean tree and reports `blocked` otherwise; observed during the acceptance. | #16 |
| **RK5** | **Docker is not reachable from WSL**, so the database tests fail in the seats (spike 0005 S0005-4). | `check-prereqs.sh` (AC6); CLAUDE.md gains the Docker Desktop WSL integration next to mirrored networking. | #16 |
| **RK6** | **Context growth across issues** lowers quality or triggers compactions mid-task. | Context percentage in `ps` and in the acceptance record; `/compact` or fresh per the runbook (Q11). | #16 |
| **RK7** | **Both worktrees share one clone's `.git`**, so concurrent `git fetch` calls can collide on lock files. | Observed during stage B; a failed fetch is retried by the seat. | #16 |
| **RK8** | **A guidance change may not reach a resumed seat** (spec 0005 RK5), so the team keeps working with old rules after a rig PR. | The runbook's "after changing rig files" row; spec 0005's AC9 step 14 answers it. | #16, #12 |
| **RK9** | **Acceptance is circular**: a bug in `0.1.0` found during the run must be fixed and released before the run can finish. | The procedure's failure path (fix, `0.1.x`, pin PR, upgrade, resume); never a development build. | #16 |

## Changes after acceptance

*(none yet)*
