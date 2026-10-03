# Setting up the aiakos-delivery rig on a machine

The rig runs in WSL (distro `Ubuntu`) on the WSL checkout of this repository, not on a checkout
under `/mnt/c`. What is in the repository: the rig file, the roles and the culture. What is not:
logins, the OpenCode Go key and OpenRig's own state (`~/.openrig`).

## Once per machine

1. Tools in WSL: `tmux`, `git`, `gh`, Node, the .NET SDK from `global.json`, Docker (Docker
   Desktop with WSL integration), and optionally `herdr`.
2. Agents and OpenRig:

   ```bash
   npm install -g @openrig/cli@0.6.4 @openai/codex @earendil-works/pi-coding-agent@1.0.1
   ```

   Claude Code is installed with its own installer.
3. Logins: `gh auth login`, `claude` (log in once), `codex login`.
4. The OpenCode Go key, in one file that only you can read:

   ```bash
   mkdir -p ~/.config/opencode-go && chmod 700 ~/.config/opencode-go
   read -rs -p "OpenCode Go key: " K && printf '%s' "$K" > ~/.config/opencode-go/key && chmod 600 ~/.config/opencode-go/key && unset K
   ```

5. `rig start` once, so that OpenRig creates `~/.openrig` and its kernel rig.
6. Clone the repository in the WSL home folder and check the machine:

   ```bash
   git clone https://github.com/aiakos-hq/aiakos.git ~/aiakos && cd ~/aiakos
   bash rigs/aiakos-delivery/setup.sh
   ```

   The script installs nothing. It lists what is missing and writes the `auth.json` of each Pi
   seat, which points at the key file (see [spike 0006](../../docs/spikes/0006-openrig-pi-seat.md)).

## Every day

```bash
cd ~/aiakos
rig up rigs/aiakos-delivery/rig.yaml     # start, or resume after a stop
rig tui                                  # the board; "terminal rig:aiakos-delivery" opens herdr
rig down aiakos-delivery                 # stop; sessions are kept for the next start
```

Give the team work by telling the `lead` seat which issue or slice to take
(`rig send lead-lead@aiakos-delivery "..."`, or type in its terminal).

## What the rig writes into the checkout

OpenRig projects instructions, skills and plugins into the working directory. These paths are
git-ignored: `CLAUDE.local.md`, `AGENTS.md`, `.agents/`, `.codex/`, `.openrig/`,
`.claude/plugins/` and everything under `.claude/skills/` except `story`.

## Changing a model

Edit the seat's `model:` line in `rig.yaml`, then `rig down` and `rig up`. For the Pi seat the
form is `opencode-go/<model>`; `pi --list-models opencode-go` lists them.

## Versions

The rig was set up with OpenRig 0.6.4 and Pi 1.0.1. `runtime: pi` is not in OpenRig's rig spec
reference, so check the Pi seat after an OpenRig upgrade.
