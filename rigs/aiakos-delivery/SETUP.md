# Setting up the aiakos-delivery rig on a machine

The rig runs in WSL (distro `Ubuntu`) on the WSL checkout of this repository, not on a checkout
under `/mnt/c`. What is in the repository: the rig file, the roles and the culture. What is not:
logins, per-seat permission choices and OpenRig's own state (`~/.openrig`).

## Once per machine

1. Tools in WSL: `tmux`, `git`, `gh`, `rg` (ripgrep), Node, the .NET SDK from `global.json`, Docker (Docker
   Desktop with WSL integration), and optionally `herdr`.
2. Agents and OpenRig:

   ```bash
   npm install -g @openrig/cli@0.6.6 @openai/codex
   ```

   Claude Code is installed with its own installer.
3. Logins: `gh auth login`, `claude` (log in once), `codex login`.
4. `rig start` once, so that OpenRig creates `~/.openrig` and its kernel rig.
5. Clone the repository in the WSL home folder and check the machine:

   ```bash
   git clone https://github.com/aiakos-hq/aiakos.git ~/aiakos && cd ~/aiakos
   bash rigs/aiakos-delivery/setup.sh
   ```

   The script installs nothing. It lists what is missing.
6. Start the rig for the first time ([Running unattended](#running-unattended) says how the
   seats are allowed to work without asking):

   ```bash
   rig up rigs/aiakos-delivery/rig.yaml
   ```

## Every day

```bash
cd ~/aiakos
rig up aiakos-delivery --existing        # start the rig that was stopped; new conversations
rig tui                                  # the board; "terminal rig:aiakos-delivery" opens herdr
rig down aiakos-delivery                 # stop; queue items are kept for the next start
```

Every seat starts with an empty conversation (`restore_policy: relaunch_fresh` in `rig.yaml`)
and reads its queue.

The rig has two pods. `desk` holds `desk-lead`, the seat you talk to: it receives the team's
reports, makes the next move and tells you what needs you. `team` holds the seven seats that do
the work; look at them now and then. Give the team work by typing in the terminal of
`desk-lead`. A report of a seat can arrive there while you type; it is not often.

After you merge a pull request or label a chore or a bug `ready`, the lead notices at its next
move (`tools/story.sh events`). When the whole rig is quiet, tell it "continue".

**Start by name, not from the file.** `rig up rigs/aiakos-delivery/rig.yaml` on a stopped rig
does not resume it: it creates a new rig with new seats and archives the old one. The new seats
have no per-seat settings. Use the file only for the first start
and after `rig.yaml` changed (see [After a change to rig.yaml](#after-a-change-to-rigyaml)).

## Running unattended

By default a seat stops and asks before it runs a command its harness does not already allow.
One unanswered prompt stalls the whole queue. `permission_policy: builtin:open` in `rig.yaml`
does not prevent this: OpenRig records that policy but does not change how a seat is launched
(`rig policy permissions current` shows `launch_posture=floor`). A policy on the member does
change it, and every member has one. The maintainer sets them; no seat may change its own or
another seat's permissions.

**What you give up.** A Codex seat with full access has no sandbox: it can read and write
anything your WSL user can, including `/mnt/c`, the `gh` token and key files. A Claude seat in
auto mode runs commands without asking, behind Claude Code's own safety check. Merging stays
blocked by branch protection in both cases.

### 1. Codex seats: full access (in the repository, already there)

Each Codex member in `rig.yaml` (`low1`, `low2`, `high1`, `high2`) has this line:

```yaml
        permission_policy: builtin:yolo
```

Nothing to do on a new machine. To check:

```bash
rig policy permissions current --spec rigs/aiakos-delivery/rig.yaml
ps -eo args | grep "[c]odex --no-daemon" | grep aiakos
```

The first command lists the four members with `launch_posture=full_bypass`. The second, with
the rig running, shows `-s danger-full-access -a never` on every Codex seat.

### 2. Claude seats: auto mode (in the repository, already there)

Each Claude member in `rig.yaml` (`lead`, `architect`, `reviewer`, `gate`) has this line:

```yaml
        permission_policy: builtin:auto
```

Nothing to do on a new machine. The first command above lists the four members with
`launch_posture=auto`. With the rig running:

```bash
ps -eo args | grep "[c]laude --permission-mode" | grep aiakos-delivery
```

Every line must say `--permission-mode auto`, and each seat's terminal shows "auto mode on".

### Going back

Remove the `permission_policy` lines of the members from `rig.yaml`, then start from the file.

## Clean conversations

A seat sends its whole conversation again with every request. On 5–6 October the Claude seats
ran at a median of 316k to 477k tokens per request and the Codex seats at 100k to 130k, almost
all of it finished work (issue #278). So a seat does not keep its conversation between items:

- **Every start of the rig** begins with empty conversations.
- **Every delivery** goes through `tools/story.sh hand`. When the destination is idle and has
  nothing in progress, it gets an empty conversation first: a fresh launch
  (`rig seat launch --fresh --stop`) of a pool seat, `team-architect`, `team-reviewer` or
  `team-gate`. It takes 5 to 17 seconds. A busy seat is never touched; its item is queued.
- **`desk-lead`** keeps its conversation, because you talk to it. It runs in a 200k window
  in place of Claude Code's 1M, so Claude Code compacts it before it grows large:
  `CLAUDE_CODE_DISABLE_1M_CONTEXT=1` in `agents/shared/runtime/claude-settings.fragment.json`,
  which applies to every Claude seat. On 8 October a seat that was never cleared ran at a
  median of 265k tokens per request in the 1M window and acted on stale state.

`artifacts/hand.log` in the main checkout has one line per delivery: when, from, to, the role
and whether the conversation was cleared (`fresh`, or `no` with the reason). Read it to
see how often an item went to a busy seat.

```bash
tail -n 20 artifacts/hand.log
AIAKOS_NO_WRITE=1 bash tools/story.sh hand low --role impl --summary "try" --body "try"
```

The second command shows which seat would be picked and changes nothing.

To check the window of a running Claude seat (`context_window_size` must be 200000):

```bash
cat ~/.openrig/state/context-usage/desk-lead@aiakos-delivery.json
```

Codex compacts by itself when its window (258k) is nearly full.

## After a change to rig.yaml

A new seat, another model or a changed policy only takes effect on a start from the file, which
makes a new rig:

```bash
rig down aiakos-delivery
rig up rigs/aiakos-delivery/rig.yaml
```

Wait until the queue is quiet first. Queue items and the files in `docs/briefs/` and
`artifacts/trials/` are kept.

## What the rig writes into the checkout

OpenRig projects instructions, skills and plugins into the working directory. These paths are
git-ignored: `CLAUDE.local.md`, `AGENTS.md`, `.agents/`, `.codex/`, `.openrig/`,
`.claude/plugins/` and everything under `.claude/skills/` except `story`.

## Pi seats (not used at the moment)

No seat runs on Pi. It worked in [spike 0006](../../docs/spikes/0006-openrig-pi-seat.md), but
OpenRig prints only a thin summary of what a Pi seat does, and `runtime: pi` is not in OpenRig's
rig spec reference. To use Pi again for a seat:

1. `npm install -g @earendil-works/pi-coding-agent@1.0.1`.
2. Put the OpenCode Go key in `~/.config/opencode-go/key` (mode 600).
3. Set the member to `runtime: pi` and `model: opencode-go/<model>`
   (`pi --list-models opencode-go` lists them).
4. Add the seat to `PI_SEATS` in `setup.sh` (for example `team-low1`) and run the script: it
   writes the seat's `auth.json`, which points at the key file.

## Versions

The rig runs on OpenRig 0.6.6 and Codex 0.160. It was first set up with OpenRig 0.6.4.
