# Setting up the aiakos-delivery rig on a machine

The rig runs in WSL (distro `Ubuntu`) on the WSL checkout of this repository, not on a checkout
under `/mnt/c`. What is in the repository: the rig file, the roles and the culture. What is not:
logins, per-seat permission choices and OpenRig's own state (`~/.openrig`).

## Once per machine

1. Tools in WSL: `tmux`, `git`, `gh`, Node, the .NET SDK from `global.json`, Docker (Docker
   Desktop with WSL integration), and optionally `herdr`.
2. Agents and OpenRig:

   ```bash
   npm install -g @openrig/cli@0.6.4 @openai/codex
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
6. Start the rig for the first time, then do [Running unattended](#running-unattended):

   ```bash
   rig up rigs/aiakos-delivery/rig.yaml
   ```

## Every day

```bash
cd ~/aiakos
rig up aiakos-delivery --existing        # start the rig that was stopped; sessions resume
rig tui                                  # the board; "terminal rig:aiakos-delivery" opens herdr
rig down aiakos-delivery                 # stop; sessions are kept for the next start
```

Give the team work by telling the `lead` seat which issue or slice to take
(`rig send lead-lead@aiakos-delivery "..."`, or type in its terminal).

**Start by name, not from the file.** `rig up rigs/aiakos-delivery/rig.yaml` on a stopped rig
does not resume it: it creates a new rig with new seats and archives the old one. The new seats
have no conversation history and no per-seat settings. Use the file only for the first start
and after `rig.yaml` changed (see [After a change to rig.yaml](#after-a-change-to-rigyaml)).

## Running unattended

By default a seat stops and asks before it runs a command its harness does not already allow.
One unanswered prompt stalls the whole queue. `permission_policy: builtin:open` in `rig.yaml`
does not prevent this: OpenRig records that policy but does not change how a seat is launched
(`rig policy permissions current` shows `launch_posture=floor`). Two settings do change it. The
maintainer makes both; no seat may change its own or another seat's permissions.

**What you give up.** A Codex seat with full access has no sandbox: it can read and write
anything your WSL user can, including `/mnt/c`, the `gh` token and key files. A Claude seat in
auto mode runs commands without asking, behind Claude Code's own safety check. Merging stays
blocked by branch protection in both cases.

### 1. Codex seats: full access (in the repository, already there)

Each Codex member in `rig.yaml` (`author`, `author2`, `impl`, `senior`, `qa`) has this line:

```yaml
        permission_policy: builtin:yolo
```

Nothing to do on a new machine. To check:

```bash
rig policy permissions current --spec rigs/aiakos-delivery/rig.yaml
ps -eo args | grep "[c]odex --no-daemon" | grep aiakos
```

The first command lists the five members with `launch_posture=full_bypass`. The second, with
the rig running, shows `-s danger-full-access -a never` on every Codex seat.

### 2. Claude seats: auto mode (per rig start from the file)

`rig.yaml` has no field for Claude's auto mode. OpenRig stores it on the seat, so it has to be
set again whenever the seats are new: on a new machine, and after every start from the file.

With the rig running, record the choice. OpenRig takes the caller from the environment, so a
plain shell must say who it is:

```bash
for seat in lead-lead analysis-architect verify-reviewer; do
  OPENRIG_SESSION_NAME=operator-human@kernel rig seat set-permissions "$seat@aiakos-delivery" \
    --mode auto --reason "Unattended delivery rig"
done
```

Without `OPENRIG_SESSION_NAME` the command answers
`Sender identity, mode and reason are required`.

Check that it was recorded (`selectionState` must not be `inherit`):

```bash
rig seat status lead-lead@aiakos-delivery --json | grep -A3 '"permissions"'
```

The choice applies when a seat is launched, so restart the same rig by name:

```bash
rig down aiakos-delivery && rig up aiakos-delivery --existing
```

Then check how the Claude seats were launched:

```bash
ps -eo args | grep "[c]laude --permission-mode" | grep aiakos-delivery
```

Every line must say `--permission-mode auto`.

**If a line still says `acceptEdits`**, the stored choice was not used. Switch that seat by
hand: open its terminal and press Shift+Tab until it shows "auto mode on". This lasts until the
seat is launched again. The restart by name and the check above have not been confirmed on
OpenRig 0.6.4 yet; the hand switch has.

### Going back

Remove the `permission_policy: builtin:yolo` lines from `rig.yaml`, and run the
`set-permissions` loop with `--mode inherit`. Then start from the file.

## After a change to rig.yaml

A new seat, another model or a changed policy only takes effect on a start from the file, which
makes a new rig:

```bash
rig down aiakos-delivery
rig up rigs/aiakos-delivery/rig.yaml
```

Wait until the queue is quiet first: the new seats start with no conversation history. Queue
items and the files in `docs/briefs/` and `artifacts/trials/` are kept. Then repeat step 2 of
[Running unattended](#running-unattended) for the Claude seats.

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
4. Add the seat to `PI_SEATS` in `setup.sh` (for example `verify-qa`) and run the script: it
   writes the seat's `auth.json`, which points at the key file.

## Versions

The rig was set up with OpenRig 0.6.4 and Codex 0.160.
