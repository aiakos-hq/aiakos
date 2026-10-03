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
6. Start the rig once (`rig up rigs/aiakos-delivery/rig.yaml`), then do
   [Running unattended](#running-unattended), which needs the seats to exist.

## Every day

```bash
cd ~/aiakos
rig up rigs/aiakos-delivery/rig.yaml     # start, or resume after a stop
rig tui                                  # the board; "terminal rig:aiakos-delivery" opens herdr
rig down aiakos-delivery                 # stop; sessions are kept for the next start
```

Give the team work by telling the `lead` seat which issue or slice to take
(`rig send lead-lead@aiakos-delivery "..."`, or type in its terminal).

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

### 1. Codex seats: full access (in the repository)

In `rig.yaml`, add this line to each Codex member (`author`, `author2`, `impl`, `senior`, `qa`),
at the same indent as its `model:` line:

```yaml
        permission_policy: builtin:yolo
```

Check it before starting:

```bash
rig spec validate rigs/aiakos-delivery/rig.yaml
rig policy permissions current --spec rigs/aiakos-delivery/rig.yaml
```

The second command must list those five members with `builtin:yolo` and
`launch_posture=full_bypass`. Commit the change, so
that the next machine has it. After the next start, each Codex seat runs with
`-s danger-full-access -a never`:

```bash
ps -eo args | grep "[c]odex --no-daemon" | grep aiakos
```

### 2. Claude seats: auto mode (on this machine)

This choice is stored by OpenRig per seat, not in the repository, so it is done once on every
machine, after the first `rig up`:

```bash
for seat in lead-lead analysis-architect verify-reviewer; do
  rig seat set-permissions "$seat@aiakos-delivery" --mode auto --reason "Unattended delivery rig"
done
```

It applies at the next launch:

```bash
rig down aiakos-delivery && rig up rigs/aiakos-delivery/rig.yaml
```

Check that the seats were launched in auto mode:

```bash
ps -eo args | grep "[c]laude --permission-mode" | grep aiakos-delivery
```

Every line must say `--permission-mode auto`. If `set-permissions` refuses `auto` (it depends on
the Claude Code version OpenRig finds), either switch each Claude seat by hand after a start
(Shift+Tab in its terminal until it shows "auto mode on"), or use `--mode full_bypass`, which
skips all prompts and has no safety check.

### Going back

Remove the `permission_policy: builtin:yolo` lines, and run `set-permissions` with
`--mode inherit` for the three Claude seats. Then `rig down` and `rig up`.

## What the rig writes into the checkout

OpenRig projects instructions, skills and plugins into the working directory. These paths are
git-ignored: `CLAUDE.local.md`, `AGENTS.md`, `.agents/`, `.codex/`, `.openrig/`,
`.claude/plugins/` and everything under `.claude/skills/` except `story`.

## Changing a model

Edit the seat's `model:` line in `rig.yaml`, then `rig down` and `rig up`.

## Adding a seat to a running rig

After a new member is merged into `rig.yaml`, `rig down` and `rig up` start it. A seat added
this way needs its permission choice too (section "Running unattended").

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
