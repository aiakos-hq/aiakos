#!/usr/bin/env bash
# Checks what the aiakos-delivery rig needs on this machine and prepares the Pi seats.
# Run it in WSL from anywhere:  bash rigs/aiakos-delivery/setup.sh
# It installs nothing and never prints a secret.
set -u

RIG=aiakos-delivery
OPENRIG_VERSION=0.6.6
PI_VERSION=1.0.1
KEY_FILE="$HOME/.config/opencode-go/key"
# Seats that run on Pi (pod-member, for example "verify-qa"). None at the moment: Pi and the
# OpenCode Go key are only checked when this list is not empty (see docs/spikes/0006).
PI_SEATS=""

problems=0
ok()      { printf 'ok       %s\n' "$1"; }
problem() { printf 'MISSING  %s\n' "$1"; problems=$((problems + 1)); }
note()    { printf 'note     %s\n' "$1"; }

need() { # command, hint
  if command -v "$1" >/dev/null 2>&1; then ok "$1"; else problem "$1: $2"; fi
}

need tmux   "sudo apt install tmux"
need git    "sudo apt install git"
need gh     "https://cli.github.com, then 'gh auth login'"
need dotnet ".NET SDK from global.json"
need docker "Docker Desktop with WSL integration for this distro"
need rig    "npm install -g @openrig/cli@$OPENRIG_VERSION"
need claude "Claude Code, then log in"
need codex  "npm install -g @openai/codex, then 'codex login'"
[ -z "$PI_SEATS" ] || need pi "npm install -g @earendil-works/pi-coding-agent@$PI_VERSION"
need herdr  "optional: only for 'terminal rig:$RIG' in the rig TUI"

version_is() { # command, expected, actual
  case "$3" in
    *"$2"*) ok "$1 $2" ;;
    *) note "$1 is '$3'; the rig was set up with $2" ;;
  esac
}
command -v rig >/dev/null 2>&1 && version_is rig "$OPENRIG_VERSION" "$(rig --version 2>/dev/null)"
[ -z "$PI_SEATS" ] || { command -v pi >/dev/null 2>&1 && version_is pi "$PI_VERSION" "$(pi --version 2>/dev/null)"; }

if command -v gh >/dev/null 2>&1; then
  if gh auth status >/dev/null 2>&1; then ok "gh is logged in"; else problem "gh is not logged in: gh auth login"; fi
fi

if [ -z "$PI_SEATS" ]; then
  :
elif [ -s "$KEY_FILE" ]; then
  ok "OpenCode Go key file"
  mode="$(stat -c %a "$KEY_FILE")"
  [ "$mode" = "600" ] || note "$KEY_FILE has mode $mode; run: chmod 600 $KEY_FILE"
else
  problem "OpenCode Go key: put the key in $KEY_FILE (mode 600)"
fi

# Every Pi seat has its own config folder and gets no key from the environment.
# Its auth.json points at the one key file. An existing non-empty auth.json is left alone.
for seat in $PI_SEATS; do
  dir="$HOME/.openrig/state/pi/$seat@$RIG/agent"
  auth="$dir/auth.json"
  if [ -f "$auth" ] && [ "$(tr -d ' \n' < "$auth")" != "{}" ]; then
    ok "auth.json of $seat"
  else
    mkdir -p "$dir"
    printf '{ "opencode-go": { "type": "api_key", "key": "!cat %s" } }\n' "$KEY_FILE" > "$auth"
    chmod 600 "$auth"
    ok "auth.json of $seat (written)"
  fi
done

echo
if [ "$problems" -eq 0 ]; then
  echo "Ready. From the repository root: rig up rigs/$RIG/rig.yaml"
else
  echo "$problems problem(s). Fix them and run this script again."
  exit 1
fi
