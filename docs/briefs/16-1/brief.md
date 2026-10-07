---
id: 16-1
title: "#16 slice 1 — aiakos-dev rig files and read-only prerequisites"
issue: 16
status: approved
route: impl
paths: [rigs/aiakos-dev/, tests/Aiakos.Spec.Tests/]
date: 2026-10-06
---

# Brief: #16 slice 1 — aiakos-dev rig files and read-only prerequisites

Part of #16. Self-contained. Do not read specs or ADRs to fill in requirements.
Where this brief is silent, choose the simplest behavior and state that choice in the commit
body. Do not add commands, permissions, files or dependencies to the prescribed rig layout.

## Goal and current prerequisites

Create the two-seat Claude Code rig (implementer and reviewer, human maintainer lead), its
instructions and operating runbook. Validate the actual files with the existing pure loader,
without starting seats or using a development build against the team's instance.

Base: origin/main d847290 (2026-10-06). Only a placeholder README exists under rigs/aiakos-dev.
The #14 loader is merged: RigLoader.Load, resolved parameters, canonicalization, embedded
culture/guidance/skills and the 14-5 renderer/skill mapper are available. 14-5 S4 attaching
projection plans to LoadResult is not merged; this slice does not require that property.
The #15 CLI remains a placeholder: no up --dry-run or released 0.1.x instance exists yet.
16-2 owns the tool manifest/pin and rig-compat workflow; 16-3 owns live permission checks,
release acceptance evidence, stage B announcement and final risk review. None is claimed here.
16-2 and the written procedure of 16-3 can be briefed independently; the live 16-3 run waits for
all M1 runtime work, 16-1/16-2, a released 0.1.x and the accepted M2 spec-validate task.

## Files to create or touch (nothing else)

The exact committed rig layout after all stories is:

```text
rigs/aiakos-dev/.gitignore
rigs/aiakos-dev/README.md
rigs/aiakos-dev/rig.yaml
rigs/aiakos-dev/rig.env.example.yaml
rigs/aiakos-dev/CULTURE.md
rigs/aiakos-dev/check-prereqs.sh
rigs/aiakos-dev/agents/implementer/agent.yaml
rigs/aiakos-dev/agents/implementer/GUIDANCE.md
rigs/aiakos-dev/agents/implementer/skills/implement-issue/SKILL.md
rigs/aiakos-dev/agents/implementer/skills/address-review/SKILL.md
rigs/aiakos-dev/agents/reviewer/agent.yaml
rigs/aiakos-dev/agents/reviewer/GUIDANCE.md
rigs/aiakos-dev/agents/reviewer/skills/review-pr/SKILL.md
```

No committed rig.env.yaml. Tests go in tests/Aiakos.Spec.Tests/AiakosDev*Tests.cs;
independent static goldens only in tests/Aiakos.Spec.Tests/Fixtures/aiakos-dev-golden/.
Do not duplicate the source rig into a fixture; locate repository root by walking parents
from AppContext.BaseDirectory to Aiakos.slnx, and use the actual rigs/aiakos-dev files.
Goldens are test files, outside the exact rig layout. No source-library changes, package
versions, NoWarn, suppression attributes or pragma warning suppression.

## Public surface

No new compiled public API. Use existing RigLoader.Load(rigRoot, envPath), ResolvedRig,
ResolvedSeatParameters, internal ClaudeGuidanceRenderer.Render(rig,seatId,specHash) and
ClaudeSkillProjection.Map(agent.Skills), accessible to the existing friend test assembly.
check-prereqs.sh is executable POSIX sh, called with no arguments from any working directory.
The script checks committed example defaults, not a local YAML binding; no YAML parser added.

## General rules

G1. UTF-8 without BOM, LF, one final newline. Bash permission strings are opaque Claude rule
    strings, never shell commands to execute in tests. Quote every permission string as a YAML
    scalar. No credential values in fixtures/reports. Nobody starts, stops, sends to, attaches to
    or changes a running rig in this slice; tests use pure loader or isolated command fakes.
    A denied command is reported, never circumvented. This preserves owner environments.

## Changes to earlier behavior

C1. Replace the placeholder README's reviewer `shared-readonly` with `seat-worktree` plus deny
    rules. State that this slice supplies validated files, not that M1 acceptance ran or stage B
    began. Existing loader and projection behavior stays unchanged.

C2. Spec0008 R15 is clarified in this analysis PR: the no-argument script checks example defaults;
    ~/.claude.json's oauthAccount object is an undocumented heuristic; absent/unreadable is
    unknown/check by hand and nonzero, custom RigEnv paths require manual README checks.
    Do not change this clarification again in an implementation story.

## Rules

R1. Create rig.yaml and rig.env.example.yaml with the exact semantic values below (YAML comments
    and equivalent quoting may differ). .gitignore is exactly `rig.env.yaml\n`. Preserve seat
    order lead, impl, review; repo aiakos; subscriptions; opus; both seat-worktree. No secret
    declarations, node overrides or additional fields. Full-load checks wait for other stories.

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

R2. CULTURE.md is the exact following Markdown plus the Turn report block immediately below,
    using LF. This is projected into both seats. The report is the last thing in a completed,
    blocked or paused turn, with no following text. Prefixes, statuses and attribution are exact.

````markdown
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
````

R3. Implementer agent.yaml has exactly the semantic values below; allow/ask/deny arrays are exact
    and ordered as shown. No library/includes or permission wildcard broadening. Claude checks
    deny before allow; these are accidental-action barriers, not isolation. Write all scalars
    containing permission strings quoted; the display below is the exact string value list.

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
        - Bash(tmux:*)
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
        - Bash(gh pr review:*)
```

R4. Reviewer agent.yaml has exactly the semantic values below, with quoted permission-string scalars and
    exact ordered lists below. It uses default mode, not plan. Comment-only reviews are permitted;
    approving/requesting changes and edits/commits/pushes remain denied under shared GitHub identity.

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
        - Bash(tmux:*)
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

R5. Implementer GUIDANCE.md has headings Inputs, Worktree, Definition of done, Not verified here,
    Bootstrap, Attribution, each containing these requirements in plain prose:
    Inputs: gh issue view, merged linked spec/acceptance criteria, cited ADRs/plan, issue-owned
    docs/risks.md rows; nontrivial unmerged spec stops with question. Worktree: persists across
    issues; dirty git status blocks, no automatic reset/clean/stash; fetch origin and branch from
    origin/main named feat/<n>-<slug>, fix/<n>-<slug> or docs/<n>-<slug>. Definition of done:
    criteria met with evidence, committed tests, build Release without new warnings, docs/spec
    deviations under Changes after acceptance, PR template and checked/closed risk IDs. Not
    verified here: Windows dev AppHost, WSL E2E and manual demos listed honestly in PR. Bootstrap:
    never run a development build against aiakos-dev; tests use other rig names/homes/ports.
    Attribution: every commit Aiakos-Seat: impl@aiakos-dev; PR Implemented by impl@aiakos-dev.
    Report shape (last output): REPORT impl@aiakos-dev; issue: #<n> pr: #<n>|none
    head: <short sha>|none on one line; status: <status>; summary: <one to three lines>;
    next: <lead action>. Status is ready-for-review/changes-pushed or blocked/question/in-progress.
    A failed fetch caused by a shared-clone lock is retried once, then blocked if still failing;
    do not remove locks. No other automatic retries of unrelated failures.
    Skill frontmatter is exactly name/description below, delimited by ---; bodies are numbered
    steps with the stated sequence and commands. End each skill with the prescribed turn report.
    implement-issue: description `Implement one GitHub issue from its merged spec and open a PR.`
    Steps: gh issue view <n>, read spec/criteria/risk rows; clean git status, git fetch origin,
    git switch -c feat/<n>-<slug> origin/main (fix/docs as applicable); code/tests, dotnet build
    -c Release and dotnet test; update docs/spec/risk register; conventional commit + trailer,
    git push -u origin <branch>; gh pr create with .github/pull_request_template.md, Closes #<n>
    and attribution, gh pr checks and fix this change's CI failures; report ready-for-review.
    address-review: description `Address each finding on the existing PR without rewriting history.`
    Steps: gh pr view <n> --comments and gh pr diff <n>, list findings; each addressed by code
    change or reason in PR comment; fetch origin then git merge origin/main when behind, build
    and test, commit + trailer and push; gh pr comment <n> starting `Addressed at <sha>:` with one
    line per finding; report changes-pushed. Use body-file for multiline gh content, never shell
    interpolate report/review text. Both skills refer to CULTURE.md's report format and stop rules.

R6. Reviewer GUIDANCE.md has headings Exact diff, Checklist, Findings, Never, Attribution.
    Exact diff: gh pr checkout <n>, git rev-parse HEAD; head named in review, later push requires
    new review. Dirty checkout blocks, no automatic reset/clean. Checklist: issue/merged spec;
    each criterion with test/command evidence or manual/not-verifiable-here; committed tests;
    Release build without new warnings and tests locally, green gh pr checks; docs/spec deviations;
    claimed risk rows actually checked; CLAUDE.md tenant_id, environment identity, no silent
    fallbacks, host interfaces, LF; no secrets, accepted ADRs. Findings: severity blocking,
    should-fix or nit, path:line, concrete reason; ready only if zero blocking. Never: edit source,
    fix, commit, push, approve/request changes; build/test generated outputs are allowed; failure
    to run checks is reported, never guessed. Attribution: review starts exactly
    `Review by review@aiakos-dev at <head sha>`; Git identity stays maintainer.
    review-pr SKILL.md frontmatter name review-pr, description exactly `Review the PR at its exact head and post one comment review.`
    Report shape: REPORT review@aiakos-dev; issue: #<n> pr: #<n>|none head: <short sha>|none
    on one line; status: reviewed (or blocked/question); summary: verdict: ready or
    verdict: changes needed (<count> findings); next: <lead action>. Last output, no text after.
    Numbered steps: gh pr view <n>, linked issue/spec; clean status, gh pr checkout <n>,
    git rev-parse HEAD, gh pr diff <n>; checklist, dotnet build -c Release, dotnet test,
    gh pr checks <n>; gh pr review <n> --comment --body-file <file> with prefix above, verdict,
    criterion/evidence table and findings; last report reviewed and verdict. One review per round.
    Temporary body-file lives outside checkout and is removed; writing that report is permitted
    by shell, never bypassing a denied tool. Shared-clone fetch lock retries once then blocks.

R7. check-prereqs.sh starts #!/bin/sh, mode 100755, no arguments; wrong arguments print exactly
    `usage: check-prereqs.sh` on stderr and exit2 before checking anything. With no arguments,
    evaluate all nine groups in R8 order, one stdout line per group, no other stdout/stderr,
    exit0 only if every group is ok, otherwise exit1. Capture/suppress invoked program output,
    including failures; never echo paths, account/config/token data, command error or version.
    Use only POSIX shell/awk/sed/grep/sort/cut/tr/pwd utilities plus the commands being checked;
    no jq, Python, YAML parser, network login, installs, file writes, docker start or rig actions.
    Each command failure/missing prerequisite affects its own group, not the later groups.
    Version checks compare numeric components, never lexical strings. tmux accepts 3.4 or later,
    including trailing release letter (3.4a); reject malformed. Claude executable must be
    ~/.local/bin/claude, version first numeric major.minor.patch >=2.1.284; unrelated text does
    not count. dotnet --version must be stable numeric major.minor.patch >= SDK version in the
    repository global.json (currently10.0.100); reject malformed/prerelease versions. Locate
    global.json relative to script, not caller CWD; absent/malformed fails the dotnet group.
    For Claude auth only, read ~/.claude.json privately: POSIX awk detects the literal key
    oauthAccount followed by colon and opening object brace, allowing whitespace/newlines.
    This is an undocumented account-marker heuristic, not proof of current subscription login.
    When file is absent/unreadable or marker absent use the exact unknown/manual failure line,
    never ok or silent success. Claude version/executable failure takes precedence over marker.

R8. The nine checks, labels and exact stdout lines are the table below. Paths are the committed
    example defaults only: ~/src/aiakos clone and ~/aiakos/seats seat root. Custom RigEnv paths
    require equivalent manual checks in README; configurable paths are out of this slice.
    Git origin accepts only https://github.com/aiakos-hq/aiakos[.git],
    git@github.com:aiakos-hq/aiakos[.git] or ssh://git@github.com/aiakos-hq/aiakos[.git],
    with no trailing slash or embedded credentials. Only origin is checked; additional remotes
    do not fail the clone group when origin is valid. Git worktree check must return true.
    For paths use physical existing ancestors (cd -P/pwd -P) plus planned descendants, following
    symlinks without creating directories; either resolved default under /mnt or resolution
    failure fails filesystem group. Root may be absent; resolve nearest existing ancestor.
    A present oauthAccount object only passes the marker subcheck, subject to R7 caveat.

| Group in order | Read-only checks | Success line | Failure line |
|---|---|---|---|
| networking | wslinfo --networking-mode exactly mirrored (trim trailing LF only) | ok networking | FAIL networking: enable mirrored WSL networking and restart WSL |
| tools | tmux -V numeric >=3.4; command -v curl and flock | ok tools | FAIL tools: install tmux >= 3.4, curl and flock |
| claude | ~/.local/bin/claude --version >=2.1.284 and account marker | ok claude: account marker present; login not verified | FAIL claude: install Claude Code >= 2.1.284 at ~/.local/bin/claude |
| claude alternate | version passes but account marker unknown | (never ok) | FAIL claude: unknown, check by hand; run ~/.local/bin/claude and /login |
| dotnet | dotnet --version numeric stable floor from global.json | ok dotnet | FAIL dotnet: install the SDK required by global.json |
| docker | command present and docker info exit0 | ok docker | FAIL docker: enable Docker Desktop WSL integration for Ubuntu and start Docker |
| github | gh auth status --hostname github.com exit0 | ok github | FAIL github: install gh and run gh auth login --hostname github.com |
| git | git config user.name and user.email both nonempty | ok git | FAIL git: configure user.name and user.email |
| clone | git -C ~/src/aiakos rev-parse --is-inside-work-tree true and remote get-url origin allowed | ok clone | FAIL clone: clone aiakos-hq/aiakos into ~/src/aiakos with origin set |
| filesystem | physical default clone and seat-root paths not /mnt or descendants | ok filesystem | FAIL filesystem: keep clone and seat_root on the WSL filesystem outside /mnt |

R9. README replaces the placeholder with headings Status, Seats, Prerequisites, Local binding,
    Operating the rig, Upgrade, Verification limits. Status says files validated with #14 loader;
    CLI/release pin/compat/live M1 acceptance are pending 16-2/16-3 and #15, until actually merged;
    do not announce Stage B. Seat table: lead human/no checkout, impl Claude Code opus/seat-worktree,
    review Claude Code opus/seat-worktree with deny rules. Prerequisites lists R8's nine groups
    and Windows .NET10, Docker Desktop, dotnet tool restore, initialized released instance.
    Local binding: cp rigs/aiakos-dev/rig.env.example.yaml rigs/aiakos-dev/rig.env.yaml, edit ignored
    copy; WSL clone only source for seat worktrees; Windows main checkout runs commands. Run
    sh rigs/aiakos-dev/check-prereqs.sh inside WSL for example defaults. Script does not parse
    RigEnv: custom paths require manual git worktree/origin and physical-not-/mnt checks;
    marker is heuristic, unreadable/absent needs manual Claude /login and a successful subscription
    session before up. The heuristic can be stale even when present; lead verifies login manually.
    Operating table has the exact command/action entries below, marked runbook for the released
    CLI once available. Only the maintainer lead runs them, not the seats.
    Upgrade: PR exact version in .config/dotnet-tools.json, with rig changes if necessary; after
    merge pull main/tool restore, stop old released instance/seats using old version, start new
    released instance, up, verify version/status/no drift. Follow spec0007 linked upgrade runbook,
    no development-build substitution, format-needing rig change after pin PR. Verification limits:
    deny rules are speed bumps, not sandboxing; shared account uses trailer/PR/review attribution;
    missing manual/Windows/live evidence explicitly pending; failed release run fixed by issue/fix
    PR/new0.1.x/pin/upgrade/restart failed step, never development tool. Link spec0008 acceptance
    procedure and record, CLAUDE.md, ADR0008,0037,0038 and docs/risks.md; do not edit risk statuses.

| When | Exact command or action |
|---|---|
| Start of day | git pull; dotnet tool restore; dotnet aiakos instance start; dotnet aiakos up rigs/aiakos-dev; dotnet aiakos ps |
| Assign | dotnet aiakos send impl "Implement #42 (spec docs/specs/NNNN-….md). Use the implement-issue skill." --wait turn |
| Result | dotnet aiakos capture impl --lines 40; read PR |
| Review | dotnet aiakos send review "Review PR #57 for issue #42. Use the review-pr skill." --wait turn |
| Changes | dotnet aiakos send impl "Address the review on PR #57. Use the address-review skill." --wait turn |
| needs-input | dotnet aiakos attach impl --write; answer; detach C-b d |
| Between issues | dotnet aiakos send impl /compact; or dotnet aiakos up --fresh --seat impl --note "new area: …" |
| Rig changes | merge PR, git pull, dotnet aiakos up rigs/aiakos-dev; drifted: down then up; fresh or /compact to apply guidance |
| End of day | leave up; or dotnet aiakos down --all; dotnet aiakos instance stop |
| Reboot | dotnet aiakos instance start; each unknown seat: capture, down, up |
| Upgrade | pin PR merge, then released-instance upgrade procedure |

R10. Integrate actual rig with existing loader after its referenced files land. Call Load with
    explicit rig.env.example.yaml, require nonnull Rig and zero diagnostics (warnings count).
    Independently assert name aiakos-dev; repository aiakos/https://github.com/aiakos-hq/aiakos.git/main;
    seat order lead/impl/review and kinds human/agent/agent; harness claude-code, model opus,
    auth subscription, checkout seat-worktree; agent modes implementer acceptEdits/reviewer default;
    selected skills implement-issue/address-review and review-pr respectively; no secrets,
    no human seat parameters; exact two seat parameter
    worktrees/branches/projection roots below, raw embedded culture and selected guidance/skill
    bytes. Render each seat's guidance with ClaudeGuidanceRenderer.Render using returned SpecHash;
    compare committed independent literal goldens (not expected text obtained by the renderer).
    The golden first line contains exactly one {{SPEC_HASH}} placeholder instead of the spec
    hash; replace that placeholder with returned Rig.SpecHash before comparison. Every other
    golden byte is literal, including independently computed source-byte SHA256 comments.
    Goldens contain roster then CULTURE.md then only that seat's guidance, each source comment
    with source-byte SHA256. Map skill directories and compare exact destination names/bytes.
    No loader/projection change, no requirement for the unmerged ResolvedSeatParameters.Projection.
    Copy real rig into isolated temp folder for negative mutation tests; never modify real rig.
    Add top-level unrecognized_field: true: exactly one Error AIK2002, Rig null; assert code/count,
    not parser line numbers. These pure validations replace unavailable CLI dry-run in this slice.

## Expected outputs: exact text and values

| ID | Input | Expected |
|---|---|---|
| `RIG-files` | top-level files | exact R1 semantic YAML; .gitignore bytes rig.env.yaml\n; git check-ignore rigs/aiakos-dev/rig.env.yaml exit0; no committed local binding |
| `CULTURE-text` | CULTURE.md | exact R2 text/report; roles/relay/stop/bootstrap/attribution, last report only |
| `IMPL-agent` | implementer agent | exact R3 ordered arrays; acceptEdits, claude-code, opus; two skill references |
| `REVIEW-agent` | reviewer agent | exact R4 ordered arrays; default, claude-code, opus; one skill; deny Edit/Write/NotebookEdit/approval/commit/push |
| `IMPL-guidance` | guidance and two skills | all R5 headings/steps/commands/trailer; frontmatter names implement-issue/address-review and exact descriptions; ready-for-review/changes-pushed reports |
| `REVIEW-guidance` | guidance and review-pr | all R6 checklist/severity/head/comment-only rules; exact skill frontmatter; reviewed report/prefix, no approvals |
| `PREREQ-ok` | fakes all checks valid at exact version floors | nine success lines in R8 order, final LF, no stderr, exit0; Claude line states marker only; valid origin plus an additional remote remains ok clone |
| `PREREQ-fail` | each prerequisite missing/invalid individually | corresponding exact R8 failure line; other eight success lines, continue all checks, exit1; marker absent/unreadable exact unknown/manual line |
| `PREREQ-bounds` | tmux3.3/3.4a; Claude2.1.283/2.1.284; SDK10.0.99/10.0.100/10.1.0; args | numeric low versions fail own group, boundary/newer pass; wrong args stderr usage: check-prereqs.sh\n, stdout empty, exit2 |
| `README-stage` | operating README | review seat-worktree (never shared-readonly); all R9 headings/runbook actions/links; no claim acceptance completed; custom paths checked manually |
| `LOAD-real` | full real rig | Rig.Name aiakos-dev, zero diagnostics; seats lead/impl/review, human has null Agent/no parameters; two agents with exact settings and embedded evidence; exact git-tracked rig layout, ignored local rig.env.yaml on disk is permitted |
| `LOAD-paths` | impl and review | node wsl-local, model opus, auth subscription, checkout seat-worktree; workdir ~/aiakos/seats/aiakos-dev/<seat>/repos/aiakos, branch aiakos/aiakos-dev/<seat>, BaseRef origin/main, projection root ~/aiakos/seats/aiakos-dev/<seat>/projection |
| `PROJECT-real` | existing renderer and skill mapper | each CLAUDE.md exact static golden after replacing its sole first-line {{SPEC_HASH}} with Rig.SpecHash, roster/culture/guidance in order; impl skills .claude/skills/implement-issue/SKILL.md and .claude/skills/address-review/SKILL.md; review .claude/skills/review-pr/SKILL.md, byte-exact sources |
| `LOAD-unknown` | temp copy append unrecognized_field: true | one Error AIK2002, null Rig; original rig unchanged |

## Tests

Tests are committed in the existing Aiakos.Spec.Tests project; source-rig files discovered from
repository root, no copied second source-of-truth. Shell script tests invoke sh using isolated
HOME/PATH command fakes in temp directories; never real authenticated config or owner clone.
Fake commands record invocation only in the test sandbox. Author acceptance checks stay separate.

T1. Commit top-level YAML/ignore tests pinning R1 exact values/order; no full loader requirement
    until integration. Can use existing YamlDotNet parse via test project transitive dependency.
T2. Commit exact CULTURE.md byte golden including final report block, ensuring its last-report,
    attribution and stop rules survive edits.
T3. Commit implementer YAML checks for exact allow/ask/deny string arrays/defaults/skills. No need
    for guidance files or running Claude, no real permission probes.
T4. Commit reviewer YAML checks for exact arrays/defaults/skills. Verify explicit broad edit
    and narrow approval denials alongside review allow. No running Claude.
T5. Commit implementer guidance/skill tests checking every required R5 step, frontmatter description,
    commit/PR attribution, dirty-tree block and shared-lock retry/report; no gh/git actions.
T6. Commit reviewer guidance/skill tests checking R6 required checklist/head/verdict/attribution,
    comment-only review and honest failures. No gh/git actions.
T7. Commit POSIX script fake-command tests pinning all R7/R8 exact lines/status/order, single and
    multiple failures, version boundaries, malformed versions, absent marker/file, symlink paths
    under /mnt, safe planned nonexistent seat root, and valid origin with an additional remote
    (still ok clone). Run only on Linux (explicitly skip on
    other hosts); Linux executes actual script with no real prerequisites. Shell syntax sh -n
    required. Shellcheck -s sh when installed; report unavailable rather than add dependency.
    On Windows test project still builds and non-shell tests run.
T8. Commit README assertions for R9/C1 pending status, seat table, custom-binding limits, runbook,
    upgrade and risk/bootstrap links. Pure file checks; no live commands.
T9. Commit actual-rig integration tests R10 with exact parameter goldens and mutated copy. Pin
    expected culture/guidance contents in static renderer goldens with only first-line
    {{SPEC_HASH}} replaced by returned Rig.SpecHash; all other expected bytes remain literal; skill byte equality to source
    and literal destination names. Do not derive an expected render from production renderer.
    Include exact git-tracked rig file list from layout using git ls-files -- rigs/aiakos-dev;
    reject extra tracked source files or tracked rig.env.yaml. An ignored rig.env.yaml present
    on disk is permitted and excluded from the list; test that case without editing owner files
    by using an isolated git repository fixture for the tracked-layout assertion.

## Definition of done

Each story's committed tests pass; dotnet build -c Release has zero warnings/errors, and
`dotnet test --project tests/Aiakos.Spec.Tests -c Release` passes. All text LF/UTF-8/no BOM/final LF;
check-prereqs.sh executable and sh -n passes in its story. No authenticated machine prerequisite
run or live rig is required here. Another seat runs the acceptance gate; author does not.
Use tools/story.sh for story workflow. One retry at most under docs/workflow.md. Conventional
commit subject references #16, body states silent choices and risk checks. No manual push/PR;
lead's workflow handles it. Never claim unchecked Windows/manual/live results.

## Risk scope and out of scope

Mitigates 0003-RK1/0007-RK4/0008-RK3 by separate released/development rig roots and explicit
permissions/bootstrap; 0008-RK2 by attribution; RK4 by clean tree; RK5/S0005-4 by Docker check;
RK7 by bounded shared fetch retry; RK8 by guidance refresh runbook. RK1 prompts, RK6 context
and RK9 acceptance circularity are documented but their observed counts stay for 16-3.
No risk is closed by writing instructions. No CLI, settings emitter, new loader behavior,
manifest pin, rig-compat CI, release, live M2 issue run, stage B update, sandbox or queue.
