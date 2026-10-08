# aiakos-dev — the rig that builds Aiakos

## Status

These rig files have been validated with the #14 loader. This is documentation and validated
configuration only; it does not mean that M1 acceptance has run or that Stage B has begun. The
CLI/release pin and compatibility workflow are pending #15 and 16-2. Live M1 acceptance and its
release evidence are pending 16-3. The rig must run on a released Aiakos version, never on the
working tree it is changing, as required by [ADR 0008](../../docs/adr/0008-bootstrap-rule.md).

## Seats

| Seat | Harness | Model | Checkout | Policy |
|---|---|---|---|---|
| `lead` | human maintainer | — | none | Sets priorities, assigns issues, relays reviews, and alone approves and merges. |
| `impl` | Claude Code | opus | `seat-worktree` | Implements one issue against its merged spec and opens a PR. |
| `review` | Claude Code | opus | `seat-worktree` | Reviews the exact PR head and posts one comment review. Deny rules express read-only intent; they are not a security sandbox. |

## Prerequisites

Complete these checks before the lead starts the released rig:

1. In WSL, networking mode is `mirrored`.
2. WSL has tmux 3.4 or newer, `curl`, and `flock`.
3. Claude Code is installed at `~/.local/bin/claude` at version 2.1.284 or newer. The account marker is only a heuristic: the lead must manually verify `/login` and a successful subscription session. An absent, unreadable, or stale marker is not proof of login.
4. The required .NET 10 SDK is installed in Windows and WSL.
5. Docker Desktop is running with WSL integration enabled.
6. GitHub CLI is installed and authenticated to `github.com`.
7. Git `user.name` and `user.email` are configured.
8. The physical WSL source clone is `~/src/aiakos` with the expected `origin` remote.
9. The clone and seat root are on the WSL filesystem, not under `/mnt` or a descendant. The lead also runs `dotnet tool restore` and initializes the released Aiakos instance before bringing up seats.

Run `sh rigs/aiakos-dev/check-prereqs.sh` inside WSL to check the committed example defaults. It
prints a separate success or failure line for networking, tools, Claude, .NET, Docker, GitHub,
Git, clone, and filesystem checks. It does not parse or validate your local YAML binding.

## Local binding

Copy `rigs/aiakos-dev/rig.env.example.yaml` to the ignored
`rigs/aiakos-dev/rig.env.yaml` and edit the copy for the machine. Never commit the local file.
The WSL clone is the only source for seat worktrees; use the Windows main checkout only to run
the Windows commands in the operating table. For custom paths, manually verify the clone's Git
worktree and `origin`, and confirm the physical clone and seat-root paths are outside `/mnt`.
The Claude account marker can be stale even when present, so the lead verifies login manually
before starting the rig.

## Operating the rig

This is the prospective runbook for the released CLI once it is available. Only the maintainer
lead runs these commands; seats do not operate the shared instance.

| When | Exact command or action |
|---|---|
| Start of day | `git pull`; `dotnet tool restore`; `dotnet aiakos instance start`; `dotnet aiakos up rigs/aiakos-dev`; `dotnet aiakos ps` |
| Assign | `dotnet aiakos send impl "Implement #42 (spec docs/specs/NNNN-….md). Use the implement-issue skill." --wait turn` |
| Result | `dotnet aiakos capture impl --lines 40`; read the PR |
| Review | `dotnet aiakos send review "Review PR #57 for issue #42. Use the review-pr skill." --wait turn` |
| Changes | `dotnet aiakos send impl "Address the review on PR #57. Use the address-review skill." --wait turn` |
| needs-input | `dotnet aiakos attach impl --write`; answer; detach with `C-b d` |
| Between issues | `dotnet aiakos send impl /compact`; or `dotnet aiakos up --fresh --seat impl --note "new area: …"` |
| Rig changes | Merge the PR, `git pull`, then `dotnet aiakos up rigs/aiakos-dev`; if drifted, run down then up; use fresh or `/compact` to apply guidance |
| End of day | Leave it up, or run `dotnet aiakos down --all` and `dotnet aiakos instance stop` |
| Reboot | `dotnet aiakos instance start`; for each unknown seat, capture, down, then up |
| Upgrade | Merge the exact-version pin PR, then follow the released-instance upgrade procedure below |

## Upgrade

Pin the exact released version in `.config/dotnet-tools.json`; include any required rig changes in
the pin PR. After merge, pull `main` and restore the tool. Stop the old released instance and its
seats using the old version, start the new released instance, bring up the rig, and verify the
version, status, and absence of drift. Follow the [spec 0007 upgrade procedure](../../docs/specs/0007-cli-and-released-instance.md)
linked there and the applicable acceptance record. If a rig change needs formatting, include that
change in the pin PR. Never substitute a development build for the released version.

## Verification limits

Deny rules are speed bumps, not a sandbox. The seats share an account; use the `Aiakos-Seat:`
trailer and PR/review attribution to identify work. This documentation does not provide manual,
Windows, or live-rig evidence; those checks remain pending until the relevant acceptance procedure
runs. A failed released-version run is fixed through an issue and fix PR, followed by a new release,
version pin, upgrade, and restart at the failed step. Do not try a development tool instead.

See the [spec 0008 acceptance procedure](../../docs/specs/0008-aiakos-dev-rig.md),
[CLAUDE.md](../../CLAUDE.md), [ADR 0008](../../docs/adr/0008-bootstrap-rule.md),
[ADR 0037](../../docs/adr/0037-team-pin-in-local-tool-manifest.md), [ADR 0038](../../docs/adr/0038-seat-attribution-under-shared-github-identity.md), and
[docs/risks.md](../../docs/risks.md). Writing these instructions does not close any risk; do not
change risk statuses here.
