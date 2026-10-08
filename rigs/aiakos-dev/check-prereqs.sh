#!/bin/sh

failed=0

if [ "$#" -ne 0 ]; then
    printf '%s\n' 'usage: check-prereqs.sh' >&2
    exit 2
fi

ok() {
    printf 'ok %s\n' "$1"
}

fail() {
    printf 'FAIL %s\n' "$1"
    failed=1
}

numeric_at_least() {
    awk -v actual="$1" -v minimum="$2" '
        BEGIN {
            if (actual !~ /^[0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*$/ ||
                minimum !~ /^[0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*$/) exit 1
            split(actual, a, ".")
            split(minimum, m, ".")
            for (i = 1; i <= 3; i++) {
                if ((a[i] + 0) > (m[i] + 0)) exit 0
                if ((a[i] + 0) < (m[i] + 0)) exit 1
            }
            exit 0
        }
    ' >/dev/null 2>&1
}

if networking_mode=$(wslinfo --networking-mode 2>/dev/null) && [ "$networking_mode" = mirrored ]; then
    ok networking
else
    fail 'networking: enable mirrored WSL networking and restart WSL'
fi

tmux_output=
if tmux_output=$(tmux -V 2>/dev/null) && printf '%s\n' "$tmux_output" | awk '
    {
        for (i = 1; i <= NF; i++) {
            if ($i ~ /^[0-9][0-9]*\.[0-9][0-9]*[A-Za-z]?$/) {
                version = $i
                sub(/[A-Za-z]$/, "", version)
                split(version, part, ".")
                if ((part[1] + 0) > 3 || ((part[1] + 0) == 3 && (part[2] + 0) >= 4)) found = 1
                break
            }
        }
    }
    END { exit !found }
' >/dev/null 2>&1 && command -v curl >/dev/null 2>&1 && command -v flock >/dev/null 2>&1; then
    ok tools
else
    fail 'tools: install tmux >= 3.4, curl and flock'
fi

claude_path=$HOME/.local/bin/claude
claude_version_ok=0
if [ -x "$claude_path" ] && claude_output=$("$claude_path" --version 2>/dev/null) && printf '%s\n' "$claude_output" | awk '
    {
        line = $0
        for (i = 1; i <= length(line); i++) {
            if (substr(line, i, 1) ~ /[0-9]/) {
                candidate = substr(line, i)
                if (match(candidate, /^[0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*/)) {
                    version = substr(candidate, 1, RLENGTH)
                    following = substr(candidate, RLENGTH + 1, 1)
                    if (following !~ /[0-9.]/) {
                        split(version, part, ".")
                        if ((part[1] + 0) > 2 ||
                            ((part[1] + 0) == 2 && (part[2] + 0) > 1) ||
                            ((part[1] + 0) == 2 && (part[2] + 0) == 1 && (part[3] + 0) >= 284)) found = 1
                        break
                    }
                }
            }
        }
    }
    END { exit !found }
' >/dev/null 2>&1; then
    claude_version_ok=1
fi

if [ "$claude_version_ok" -eq 0 ]; then
    fail 'claude: install Claude Code >= 2.1.284 at ~/.local/bin/claude'
elif [ ! -r "$HOME/.claude.json" ] || ! awk '
    {
        if ($0 ~ /"oauthAccount"[[:space:]]*:/) {
            line = $0
            sub(/^.*"oauthAccount"[[:space:]]*:/, "", line)
            if (line ~ /^[[:space:]]*\{/) found = 1
            else waiting = 1
            if (found) exit
            next
        }
        if (waiting && $0 ~ /[^[:space:]]/) {
            if ($0 ~ /^[[:space:]]*\{/) found = 1
            exit
        }
    }
    END { exit !found }
' "$HOME/.claude.json" >/dev/null 2>&1; then
    fail 'claude: unknown, check by hand; run ~/.local/bin/claude and /login'
else
    ok 'claude: account marker present; login not verified'
fi

script_path=$0
case "$script_path" in
    */*) script_dir=${script_path%/*} ;;
    *) script_dir=. ;;
esac
[ -n "$script_dir" ] || script_dir=/
repo_root=$(CDPATH= cd -P "$script_dir/../.." 2>/dev/null && pwd -P)
global_version=
if [ -n "$repo_root" ] && [ -r "$repo_root/global.json" ]; then
    global_version=$(awk '
        /"version"[[:space:]]*:/ {
            line = $0
            sub(/^.*"version"[[:space:]]*:[[:space:]]*"/, "", line)
            sub(/".*$/, "", line)
            if (line ~ /^[0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*$/) {
                print line
                exit
            }
            exit 1
        }
    ' "$repo_root/global.json" 2>/dev/null)
fi
dotnet_version=
if [ -n "$global_version" ] && dotnet_version=$(dotnet --version 2>/dev/null) && numeric_at_least "$dotnet_version" "$global_version"; then
    ok dotnet
else
    fail 'dotnet: install the SDK required by global.json'
fi

if command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then
    ok docker
else
    fail 'docker: enable Docker Desktop WSL integration for Ubuntu and start Docker'
fi

if gh auth status --hostname github.com >/dev/null 2>&1; then
    ok github
else
    fail 'github: install gh and run gh auth login --hostname github.com'
fi

git_name=$(git config user.name 2>/dev/null)
git_email=$(git config user.email 2>/dev/null)
if [ -n "$git_name" ] && [ -n "$git_email" ]; then
    ok git
else
    fail 'git: configure user.name and user.email'
fi

clone_path=$HOME/src/aiakos
clone_origin=
if [ "$(git -C "$clone_path" rev-parse --is-inside-work-tree 2>/dev/null)" = true ] && clone_origin=$(git -C "$clone_path" remote get-url origin 2>/dev/null) && {
    [ "$clone_origin" = https://github.com/aiakos-hq/aiakos ] ||
    [ "$clone_origin" = https://github.com/aiakos-hq/aiakos.git ] ||
    [ "$clone_origin" = git@github.com:aiakos-hq/aiakos ] ||
    [ "$clone_origin" = git@github.com:aiakos-hq/aiakos.git ] ||
    [ "$clone_origin" = ssh://git@github.com/aiakos-hq/aiakos ] ||
    [ "$clone_origin" = ssh://git@github.com/aiakos-hq/aiakos.git ]
}; then
    ok clone
else
    fail 'clone: clone aiakos-hq/aiakos into ~/src/aiakos with origin set'
fi

resolve_path() {
    resolve_candidate=$1
    resolve_suffix=
    while [ ! -e "$resolve_candidate" ] && [ ! -L "$resolve_candidate" ]; do
        resolve_component=${resolve_candidate##*/}
        resolve_parent=${resolve_candidate%/*}
        [ "$resolve_parent" != "$resolve_candidate" ] || return 1
        [ -n "$resolve_parent" ] || resolve_parent=/
        resolve_suffix=/$resolve_component$resolve_suffix
        resolve_candidate=$resolve_parent
    done
    [ -d "$resolve_candidate" ] || return 1
    resolve_base=$(CDPATH= cd -P "$resolve_candidate" 2>/dev/null && pwd -P) || return 1
    printf '%s%s\n' "$resolve_base" "$resolve_suffix"
}

clone_physical=$(resolve_path "$clone_path") || clone_physical=
# The committed example seat_root default is ~/aiakos/seats.
seat_root_physical=$(resolve_path "$HOME/aiakos/seats") || seat_root_physical=
filesystem_bad=0
case "$clone_physical" in
    ''|/mnt|/mnt/*) filesystem_bad=1 ;;
esac
case "$seat_root_physical" in
    ''|/mnt|/mnt/*) filesystem_bad=1 ;;
esac
if [ "$filesystem_bad" -eq 1 ]; then
        fail 'filesystem: keep clone and seat_root on the WSL filesystem outside /mnt'
else
    ok filesystem
fi

exit "$failed"
