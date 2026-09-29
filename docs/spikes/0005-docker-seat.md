# Spike 0005: Docker seat with an API key file, hooks to the host and resume after restart

Issue: [#5](https://github.com/aiakos-hq/aiakos/issues/5) · Milestone: M0 · Related:
[plan §6/§7](../plan.md), [ADR 0004](../adr/0004-orchestrator-node-split.md),
[ADR 0006](../adr/0006-claude-code-first-harness.md), spikes [0001](0001-claude-wsl-tmux-hooks.md)
and [0002](0002-claude-session-resume.md) · Feeds: M6 (sandboxed node, Docker `ISandbox`) · Date: 2026-09-29

**Result:** works, with conditions. Claude Code 2.1.284 runs as a non-root user in a plain Docker
container. A host tmux pane drives it through `docker exec -it`. Hooks and the statusLine reach a
listener on the host through `host.docker.internal`. The session survives `docker restart`:
`claude --resume <uuid>` in a new exec fires `SessionStart source=resume` with the same ID and
re-renders the earlier turn. There was **no API key** during this spike, so a model reply and
nonce recall after a restart are **PENDING** (see [Pending](#pending-real-api-key)).

Everything that needs no model was verified: the image, run/exec, the fresh-home onboarding
screens and how to skip them, key delivery as a file, hooks before auth, networking, what the
host can observe, and restart. An invalid **placeholder** string stood in for a key, to see the
key-related screens and the auth-failure signal. It is not a real key.

Conditions:

1. **`docker exec` does not end with its client.** Killing the pane, or the `docker exec` client,
   leaves `claude` running in the container as an orphan (F4). The node must stop the harness
   inside the container itself.
2. **Container stop is a SIGKILL for the harness.** A `docker restart` sends no `SessionEnd`
   hook. Docker events (`exec_die`, `die`) are the exit signal (F7).
3. **Avoid the environment variable for the key; use `apiKeyHelper` with a key file.** An
   `ANTHROPIC_API_KEY` in the environment brings up a blocking "Detected a custom API key"
   dialog, and its default is "No". The dialog also shows the key's last 20 characters in the
   pane (F3).
4. **On this Windows dev box, enable Docker Desktop's WSL integration for `Ubuntu`.** Without it,
   bind mounts from the WSL filesystem fail. The Windows `docker.exe` in a tmux pane also dies
   when the `wsl.exe` session it was spawned through exits (F1, F2).

## Question

Does the sandboxed-seat design (plan §6 `sandboxed` node, §7 sandbox specifics) work with plain
Docker? The design has these parts:

- Claude Code runs in a container, driven from a host tmux pane through `docker exec -it`.
- The API key is delivered as a file.
- Each seat has its own home volume.
- Hooks reach the host.
- `--resume` works after the container restarts.

## Environment

| Item | Value |
|---|---|
| Host | Windows 11 Home 10.0.26200 |
| WSL | WSL2 distro `Ubuntu`, kernel 6.18.33.2-microsoft-standard-WSL2, **mirrored** networking (`wslinfo --networking-mode`) |
| tmux | 3.6, private socket `tmux -L aiakos-spike5` |
| Docker | Docker Desktop, engine and client 29.8.0 (`linux`, 8 CPUs). **WSL integration for `Ubuntu` is not enabled**, so the Windows CLI `/mnt/c/Program Files/Docker/Docker/resources/bin/docker.exe` was called from WSL |
| Claude Code | 2.1.284 in the image (`npm i -g @anthropic-ai/claude-code@2.1.284`, which ships a native binary, `…/claude-code/bin/claude.exe`). `--model haiku` for every launch |
| Auth | **No API key available.** An invalid placeholder string was used for the key-screen and auth-failure tests. User-global Claude config and credentials were not read or changed |
| Listeners | WSL `python3` HTTP listeners on `127.0.0.1:5150` and `0.0.0.0:5152`, and a Windows .NET file-based listener on `127.0.0.1:5151` (the 0001 listener with a URL argument) |

Scratch area: `~/aiakos-spikes/0005/` in WSL (`image/`, `conf/`, `bin/`, `logs/`). Docker objects
all start with `aiakos-spike5`: the image `aiakos-spike5-claude:2.1.284`, the container
`aiakos-spike5-seat1`, and the volumes `aiakos-spike5-home-seat1` and `aiakos-spike5-ws-seat1`.

## Steps

### 1. Image

`Dockerfile`:

```dockerfile
# aiakos-spike5-claude: minimal Claude Code seat image (spike 0005)
FROM node:22-bookworm-slim

ARG CLAUDE_VERSION=2.1.284
ENV DISABLE_AUTOUPDATER=1 \
    CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC=1

RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates curl git procps tini \
 && rm -rf /var/lib/apt/lists/*

# Claude Code lives in the image (not in the home volume), pinned, no autoupdate.
RUN npm install -g "@anthropic-ai/claude-code@${CLAUDE_VERSION}" && npm cache clean --force

# Non-root seat user: reuse the image's uid 1000 ("node") under the name "seat".
RUN usermod -l seat -d /home/seat -m node && groupmod -n seat node

COPY --chmod=755 entrypoint.sh seat-claude hook-relay /usr/local/bin/

USER seat
WORKDIR /workspace
ENTRYPOINT ["/usr/bin/tini", "--", "/usr/local/bin/entrypoint.sh"]
CMD ["sleep", "infinity"]
```

`entrypoint.sh` (PID 1 under tini; it idles, and the harness is started with `docker exec`):

```sh
#!/bin/sh
# Nothing exported here reaches `docker exec` processes.
set -eu
mkdir -p "$HOME/.claude"
echo "entrypoint: home=$HOME uid=$(id -u) secrets=$(ls /run/secrets 2>/dev/null | tr '\n' ' ')"
exec "$@"
```

`hook-relay` (used by every hook and by the statusLine):

```sh
#!/bin/sh
# POST stdin to the node agent; never block or fail Claude.
kind="${1:-hook}"; name="${2:-unknown}"
curl -s -m 3 -X POST -H 'Content-Type: application/json' \
  -H "X-Aiakos-Seat: ${AIAKOS_SEAT:-none}" \
  --data-binary @- "${AIAKOS_HOOK_URL:-http://host.docker.internal:5150}/$kind/$name" >/dev/null 2>&1
exit 0
```

`seat-claude` (the environment-variable variant of key delivery; F3 explains why we prefer
`apiKeyHelper`):

```sh
#!/bin/sh
KEYFILE="${AIAKOS_API_KEY_FILE:-/run/secrets/anthropic_api_key}"
if [ -s "$KEYFILE" ]; then
  ANTHROPIC_API_KEY="$(cat "$KEYFILE")"
  export ANTHROPIC_API_KEY
fi
exec claude "$@"
```

Build from WSL (the build context is resolved as `\\wsl.localhost\Ubuntu\…`, which works for
`build`). It took about 90 s cold and produced an image of **822 MB**: 105 MB of apt packages,
246 MB for the npm Claude Code package, and the rest is the node base.

```bash
D="/mnt/c/Program Files/Docker/Docker/resources/bin/docker.exe"
cd ~/aiakos-spikes/0005/image && "$D" build -t aiakos-spike5-claude:2.1.284 .
```

### 2. Seat settings (projected into the home volume)

`settings-helper.json`: hooks and the statusLine as in 0001/0002, plus `apiKeyHelper`.

```json
{
  "apiKeyHelper": "cat /run/secrets/anthropic_api_key",
  "hooks": {
    "SessionStart":     [{ "hooks": [{ "type": "command", "command": "hook-relay hook SessionStart", "timeout": 5 }] }],
    "UserPromptSubmit": [{ "hooks": [{ "type": "command", "command": "hook-relay hook UserPromptSubmit", "timeout": 5 }] }],
    "Notification":     [{ "hooks": [{ "type": "command", "command": "hook-relay hook Notification", "timeout": 5 }] }],
    "Stop":             [{ "hooks": [{ "type": "command", "command": "hook-relay hook Stop", "timeout": 5 }] }],
    "StopFailure":      [{ "hooks": [{ "type": "command", "command": "hook-relay hook StopFailure", "timeout": 5 }] }],
    "SessionEnd":       [{ "hooks": [{ "type": "command", "command": "hook-relay hook SessionEnd", "timeout": 5 }] }]
  },
  "statusLine": { "type": "command", "command": "hook-relay statusline tick; echo aiakos-seat" }
}
```

`seat-settings.json` is the same file without `apiKeyHelper` and `StopFailure`.

First-run state (`seed-claude-json.js`, run in the container with `node`): it merges
onboarding and trust into `~/.claude.json`.

```js
const fs = require("fs"), path = require("path");
const file = path.join(process.env.HOME, ".claude.json");
const ws = process.argv[2] || "/workspace";
let cfg = {};
try { cfg = JSON.parse(fs.readFileSync(file, "utf8")); } catch { /* fresh home */ }
cfg.hasCompletedOnboarding = true;
cfg.theme = cfg.theme || "dark";
cfg.projects = cfg.projects || {};
cfg.projects[ws] = Object.assign({}, cfg.projects[ws], { hasTrustDialogAccepted: true });
fs.writeFileSync(file, JSON.stringify(cfg, null, 2));
```

### 3. Run, project, deliver the key, exec

```bash
D="/mnt/c/Program Files/Docker/Docker/resources/bin/docker.exe"   # or `docker` with WSL integration
C=aiakos-spike5-seat1

# Container: idle PID 1, per-seat home volume, worktree at /workspace, secrets on a tmpfs.
"$D" run -d --name $C --hostname seat1 \
  -v aiakos-spike5-home-seat1:/home/seat \
  -v aiakos-spike5-ws-seat1:/workspace \
  --tmpfs /run/secrets:uid=1000,gid=1000,mode=0700,size=64k \
  -e AIAKOS_SEAT=seat1 -e AIAKOS_HOOK_URL=http://host.docker.internal:5150 \
  aiakos-spike5-claude:2.1.284

# Project config into the home volume (docker cp creates root-owned files; they only need to be readable).
"$D" exec $C mkdir -p /home/seat/.aiakos
"$D" cp "$(wslpath -w ~/aiakos-spikes/0005/conf/settings-helper.json)" $C:/home/seat/.aiakos/settings-helper.json
"$D" cp "$(wslpath -w ~/aiakos-spikes/0005/conf/seed-claude-json.js)" $C:/home/seat/.aiakos/
"$D" exec $C node /home/seat/.aiakos/seed-claude-json.js /workspace

# Key file into the tmpfs over stdin: the value never appears in argv, env, inspect or events.
"$D" exec -i $C sh -c 'umask 077; cat > /run/secrets/anthropic_api_key' < "$KEYFILE"

# Seat pane: the tmux server must belong to a long-lived WSL process (F2).
tmux -L aiakos-spike5 new-session -d -s seat1 -x 160 -y 45 \
  "\"$D\" exec -it $C claude --session-id $U -n seat1 --model haiku --settings /home/seat/.aiakos/settings-helper.json; echo EXEC_EXIT=\$?; sleep 3600"
```

Stop, restart and resume:

```bash
"$D" exec $C pkill -TERM -x claude        # graceful: SessionEnd reason=other fires
"$D" restart -t 10 $C                      # the tmpfs is emptied: deliver the key again
"$D" exec -i $C sh -c 'umask 077; cat > /run/secrets/anthropic_api_key' < "$KEYFILE"
tmux -L aiakos-spike5 new-session -d -s seat1 -x 160 -y 45 \
  "\"$D\" exec -it $C claude --resume $U -n seat1 --model haiku --settings /home/seat/.aiakos/settings-helper.json; …"
```

### 4. Probes run

- Networking: `curl` from a throwaway container to `host.docker.internal`,
  `gateway.docker.internal`, `172.17.0.1`, `127.0.0.1` and the host's LAN IP, on ports
  5150/5151/5152.
- Fresh home (volume just created): launch the TUI and walk the first-run screens without
  logging in. Then seed `~/.claude.json` and relaunch.
- Key screens: a placeholder key through `ANTHROPIC_API_KEY` (dialog, Esc, pre-approval), and
  through `apiKeyHelper`. One prompt with the placeholder, to get a transcript and the
  auth-failure signal.
- Lifecycle: kill the tmux session, kill the exec client, SIGTERM inside, `docker rm -f` plus
  recreate, `docker restart` with claude running, and `--resume` afterwards.
- Host view: hooks, `docker top`, `docker inspect`, `docker events`, `docker cp` out, and the
  volume through the `docker-desktop` distro share.

## Findings

### F1: Mounts on Docker Desktop without WSL integration

| Mount | Result |
|---|---|
| Named volume `aiakos-spike5-home-seat1:/home/seat` | Works. An empty volume is initialised from the image's `/home/seat` (uid 1000, skeleton dotfiles). It persists across `restart` and across `rm -f` plus a new `run`. |
| Named volume for `/workspace` | Works. `WORKDIR` after `USER seat` makes `/workspace` uid 1000, so the volume is writable by the seat. |
| Bind mount of a WSL path (`-v "$(wslpath -w ~/…/ws/seat1)":/workspace`) | **Fails**: `Error response from daemon: accessing specified distro mount service: stat /run/guest-services/distro-services/ubuntu.sock: no such file or directory`. Docker Desktop can bind-mount a distro's files only when WSL integration is on for that distro. |
| `docker build` with a WSL build context | Works (the CLI streams the context). |
| `docker cp` from WSL into the container | Works. Files arrive as uid 0 with the source mode. |
| `--tmpfs /run/secrets:uid=1000,gid=1000,mode=0700,size=64k` | Works. Owned by the seat, `noexec,nosuid,nodev`, and **emptied on every restart**. |

**Recommendation for this dev box: enable Docker Desktop → Settings → Resources → WSL
integration → `Ubuntu`.** It gives a Linux `docker` CLI in WSL (no interop, see F2) and lets the
worktree and seat homes be bind mounts from ext4, which is what plan §7 wants ("seat homes kept
on the host"). This spike did not change the setting. On the M6 Linux box this issue does not
exist: bind mounts are native.

### F2: `docker.exe` in a tmux pane lives only as long as the `wsl.exe` that spawned it

`docker exec -it` through the Windows CLI does work in a WSL tmux pane: WSL interop gives it a
console, and the TUI rendered and took keys. But the exec client exited with rc 1 about 4 s after
the `wsl.exe` call that had **started the tmux server** returned. Claude kept running in the
container (F4). Pinning the pane's `WSL_INTEROP` to the socket of an already-dead session did
not help. Once the tmux server was started by a long-lived `wsl.exe` (a "keeper" process
standing in for the node agent), panes created by later, short `wsl.exe` calls survived.

Two related details:

- `tmux kill-session` of the last session also stops the tmux server. The next `new-session`
  starts a new server in the caller's (short-lived) WSL session.
- Redirecting `docker.exe`'s stderr to a file (`2>>log`) makes interop use pipes, not a console:
  `cannot attach stdin to a TTY-enabled container because stdin is not a terminal`.

The real node agent runs as a long-lived process in WSL (or on Linux), so this matches the
design. With WSL integration the Linux CLI avoids interop entirely. One early run survived its
launching call. We did not find out why, so do not rely on it.

### F3: First run in a fresh home, and the API-key screens

Launch in a brand-new home volume (`claude --session-id … --settings …`, no key):

| Screen | Blocks? | Hook fired? | How to avoid it unattended |
|---|---|---|---|
| **Theme picker** ("Choose the text style…") | Yes | No `SessionStart` | `hasCompletedOnboarding: true` (plus `theme`) in `~/.claude.json` |
| **Login method** ("Select login method: 1. Claude account … 2. Anthropic Console … 3. 3rd-party platform") | Yes. Not answered in this spike | No | Same seed. With `hasCompletedOnboarding` and no key, the TUI starts with the footer `Not logged in · Run /login`, and **`SessionStart` fires** |
| Workspace trust | Did not appear once `projects["/workspace"].hasTrustDialogAccepted = true` was seeded (0002 F8: trust projection) | – | Seed it per workspace path |
| **"Detected a custom API key in your environment"** (only with `ANTHROPIC_API_KEY` set) | Yes, and **the default is "No (recommended)"**. It prints `ANTHROPIC_API_KEY: sk-ant-...<last 20 chars>` in the pane. **Esc exits claude** (exec rc 1). No hook fires | No | Pre-seed `customApiKeyResponses.approved = [<last 20 chars of key>]` in `~/.claude.json` (verified with the placeholder: no dialog, `SessionStart` fired). Better: do not use the environment variable at all |
| `apiKeyHelper` in `--settings` (`cat /run/secrets/anthropic_api_key`) | **No dialog.** The header shows "API Usage Billing", `SessionStart` fires | – | Recommended |

Other things observed:

- With the env-var path and a bad key, the TUI showed `⚠ Remote managed settings failed to load
  (authentication rejected (401))` at startup. With `apiKeyHelper` and a bad or missing key file,
  launch looked normal. The failure appears only on the first request.
- An auth failure is slow to report. The prompt fired `UserPromptSubmit`. Then the pane showed
  `401 invalid x-api-key · Retrying in Ns · attempt k/10` for **about 3 minutes, with no hook**.
  Finally it fired **`StopFailure`** with `last_assistant_message: "Invalid API key · Fix external
  API key"` (no `Stop`). The adapter should register `StopFailure`, map it to a health finding,
  and not read a long quiet period as "working fine".
- `/proc/<pid>/environ` of `claude` was unreadable even from `docker exec -u 0`. The process is
  not dumpable, but the host root can still read it. The `apiKeyHelper` path keeps the key out of
  the environment entirely.

Where the key never showed up (checked by grepping for the variable name; the value was never
printed): the container's `Config.Env` in `docker inspect`, and the `docker events` exec
command lines (`exec_create: sh -c umask 077; cat > /run/secrets/anthropic_api_key`). Both would
show it if it were passed with `-e` or as an argument. **`docker events` logs every exec
command line**, so never put secrets in exec argv.

**Pitfall: `docker cp` into a tmpfs mount writes underneath it.** The copy reported success, the
tmpfs stayed empty, and `docker diff` showed `A /run/secrets/anthropic_api_key` in the
container's **writable layer**, which `docker commit`/`export` would capture. Deliver secrets
with `docker exec -i … 'cat > file' < keyfile` (as above), or on Linux with a read-only bind
mount of a host tmpfs file (`-v /run/aiakos/secrets/seat1/anthropic_api_key:/run/secrets/anthropic_api_key:ro`).

### F4: The exec'd harness outlives its client (orphans)

| Action | Claude in the container | Hook |
|---|---|---|
| `tmux kill-session` (the `docker exec -it` client dies) | **Keeps running**, re-parented to PID 0 (`ps` shows PPID 0) | none |
| exec client dies (F2) | **Keeps running**; `docker top` still lists it | none |
| `docker exec $C pkill -TERM -x claude` | Exits | `SessionEnd reason=other` |
| `docker restart` / `stop` | Killed (SIGKILL, F7) | none |

Orphans pile up. They hold the session ID in use, and they make later launches rename
themselves: *"Another live session on this machine goes by "seat1", so this session is now
"seat1-vivid-acorn"*. That changes `session_title` in the hooks. The node's `Stop(seat)` must
therefore be: SIGTERM inside the container → wait for `SessionEnd` (or `exec_die`) → then close
the pane. Before any launch, check for leftovers: `pgrep -x claude` in the container or
`docker top`. Alternatives for later: run the harness as the container's main process and use
`docker attach`, or run tmux *inside* the container.

### F5: Hooks reach the host (networking under Docker Desktop and WSL mirrored mode)

| From the container to | WSL listener `127.0.0.1:5150` | Windows listener `127.0.0.1:5151` | WSL listener `0.0.0.0:5152` |
|---|---|---|---|
| `host.docker.internal` (192.168.65.254) | **200** | **200** | **200** |
| `gateway.docker.internal` (192.168.65.1) | timeout | timeout | timeout |
| `172.17.0.1` (bridge gateway, inside the Docker VM) | refused | refused | refused |
| `127.0.0.1` (the container's own loopback) | refused | refused | refused |
| host LAN IP (redacted) | timeout | refused | timeout |

- `host.docker.internal` reaches **both** a Windows loopback listener and a WSL loopback listener.
  In mirrored mode WSL and Windows share `127.0.0.1`, and Docker Desktop forwards
  `host.docker.internal` to host loopback. No firewall rules and no `0.0.0.0` binding needed.
- The listener sees **`remote=127.0.0.1`** for every container, with `Host: host.docker.internal:<port>`.
  The source address therefore cannot identify a seat. Identity has to come from a per-seat token
  (rule 2). In this spike the `X-Aiakos-Seat` header came from `-e AIAKOS_SEAT`, which is visible
  in `docker inspect`. A real token belongs in a file in the secrets tmpfs, like the key.
- "Bridge IP" from plan §7 applies to native Docker on Linux (a node agent listening on the
  `docker0` address, or `--add-host=host.docker.internal:host-gateway`). It does not apply to
  Docker Desktop.
- Every hook event arrived: `SessionStart` (`startup`/`resume`), `UserPromptSubmit`,
  `StopFailure`, `SessionEnd`, and statusLine ticks. The payloads match 0001/0002, with container
  paths:

```json
{"hook_event_name":"SessionStart","session_id":"5e5a0001-0000-4000-8000-000000000010","source":"startup","cwd":"/workspace","transcript_path":"/home/seat/.claude/projects/-workspace/5e5a0001-0000-4000-8000-000000000010.jsonl","model":"claude-haiku-4-5-20251001","session_title":"seat1"}
{"hook_event_name":"StopFailure","session_id":"5e5a0001-…-000000000010","cwd":"/workspace","last_assistant_message":"Invalid API key · Fix external API key"}
{"hook_event_name":"SessionStart","session_id":"5e5a0001-…-000000000010","source":"resume","cwd":"/workspace","transcript_path":"/home/seat/.claude/projects/-workspace/5e5a0001-…-000000000010.jsonl","session_title":"seat1"}
{"hook_event_name":"SessionEnd","session_id":"5e5a0001-…-000000000011","reason":"other","cwd":"/workspace"}
```

`transcript_path` and `cwd` are **container paths**. The node agent must map them
(`ISandbox.Paths`) to where the home volume lives on the host before reading files.

### F6: What the host can observe

| Source | What it gives | Notes |
|---|---|---|
| Hooks and statusLine through `host.docker.internal` | Session ID, state, context, errors (`StopFailure`) | Primary signal, as in 0001 |
| `docker top $C` | Host PIDs, user and argv of `tini`, `sleep` and every `claude` (including orphans) | Liveness of the harness from outside |
| `docker inspect $C` | `State.Status/Pid/StartedAt`, `RestartCount`, `ExecIDs` count, `Config.Env` | No secrets as long as none are passed with `-e` |
| `docker events --filter container=$C` | `exec_create/exec_start: <argv>`, `exec_die`, `kill`, `stop`, `die`, `start`, `restart` | Maps to plan §7 source 4. `exec_die` is a harness-exit signal that also covers SIGKILL |
| `docker exec $C …` / `docker cp $C:/home/seat/.claude/projects/-workspace/<uuid>.jsonl <dest>` | Transcript and registry | Works everywhere; costs an exec per read |
| The volume on disk | `docker volume inspect` → `/var/lib/docker/volumes/aiakos-spike5-home-seat1/_data`. On this box Windows can read it at `\\wsl.localhost\docker-desktop\mnt\docker-desktop-disk\data\docker\volumes\aiakos-spike5-home-seat1\_data\` (the transcript and `sessions/*.json` were listed and read) | On Linux it is root-only under `/var/lib/docker`. A **bind-mounted host directory** per seat home is simpler for the node agent (plan §7) |
| `~/.claude/sessions/<pid>.json` in the home | `pid`, `sessionId`, `status`, `procStart`, `pidDomain`, `name`, `messagingSocketPath` | **Survives restarts as stale files**: after the second restart there were three entries, one still saying `status:"busy"` for a SIGKILLed process. PIDs inside a new container start low again (20, 79, 289…), so a stale file can name a live, unrelated pid, and `pidDomain` stayed the same across restarts. Diagnostic only (as in 0002 F4) |

### F7: Restart, recreate and resume

| Step | Observed |
|---|---|
| `docker rm -f` plus a new `docker run` with the same volumes | `~/.claude.json` (onboarding and trust seeds), settings and transcripts all kept. The tmpfs key is gone |
| `docker restart -t 10` with claude running | Took 0.8 s. Docker events: `kill`, `exec_die`, `stop`, `die`, `start`, `restart`. `docker stop` signals only PID 1 (`tini`/`sleep` exit at once). The exec'd claude is then **SIGKILLed** when the PID namespace is torn down: **no `SessionEnd`**. The pane's exec client exits with **137** and a Docker CLI error |
| After restart | `/run/secrets` empty (tmpfs). No claude processes. Stale `sessions/*.json` remain. The transcript is intact (23 lines) |
| `claude --resume <unknown uuid> -p hi` in the container | `No conversation found with session ID: …`, exit 1 (same as 0002 F2) |
| `claude --resume 5e5a0001-…-000000000010` in a new `docker exec -it` | **`SessionStart source=resume`, same `session_id`**. The TUI re-renders the earlier `❯ Reply only: OK`. No trust prompt (trust was seeded for `/workspace`) |
| A new prompt in the resumed session | `UserPromptSubmit`, the same session ID, and the transcript grew (23 → 27 lines). With the placeholder key it ended in `StopFailure` as in F3 |

Resume works after restart and after recreation, because everything it needs is in the home
volume (`~/.claude/projects/-workspace/<uuid>.jsonl`), the `cwd` is the same fixed `/workspace`,
and trust is projected. As 0002 F5 predicts, the transcript is created on the first prompt (the
failed one here). Model recall after the restart is still **PENDING**.

## Pending (real API key)

These steps need a key. They are the only open part of the question.

1. **Place the key file** (in WSL; paste the key, then press Ctrl-D; it is not echoed):

   ```bash
   mkdir -p ~/aiakos-spikes/0005/secrets && install -m 600 /dev/stdin ~/aiakos-spikes/0005/secrets/anthropic_api_key
   ```

2. **Run the verifier.** It is a single long `wsl.exe` call, which keeps the `docker.exe` panes
   alive (F2):

   ```powershell
   wsl -d Ubuntu -- timeout 600 bash -lc '~/aiakos-spikes/0005/bin/verify-pending.sh'
   ```

   It does the following:
   1. Starts the hook listener on `127.0.0.1:5150` if needed and starts `aiakos-spike5-seat1`.
   2. Delivers the key into the tmpfs over stdin, then prints how many times the key value
      appears in `docker inspect` and `docker events` (expected `0 0`; the value itself is never
      printed).
   3. Launches `claude --session-id <new uuid> -n seatK --model haiku --settings /home/seat/.aiakos/settings-helper.json`
      and waits for `SessionStart startup`.
   4. Plants `HERON-<nnnn>` and waits for `Stop`.
   5. Runs `docker restart -t 10`, delivers the key again, and runs `claude --resume <uuid>`.
      It waits for **`SessionStart source=resume`** and asks for the code word.
   6. Prints `RESULT: PASS` only if the `Stop.last_assistant_message` contains the nonce.
   7. Stops claude with SIGTERM, removes the key from the tmpfs, and stops the container.

3. Optional (with WSL integration enabled): repeat with bind mounts in place of the named volumes.
   Use `-v ~/aiakos-spikes/0005/homes/seat2:/home/seat`, and pass the key as a read-only bind
   mount of the file. This confirms that the node agent can read the seat home directly on the
   host.

Record the result here as "Resume after restart: PASS/FAIL" with the session ID and nonce, as in
0002.

## Decision / follow-ups

Recommendations for the Docker `ISandbox` (M6) and for the Claude adapter's sandbox path:

- **Launch shape.** A long-lived container per seat (`sleep infinity` under `tini`), with the
  harness started through `docker exec -it` in a tmux pane owned by the long-lived node agent.
  `ISandbox.Wrap(cmd)` = `docker exec -it <c> <cmd>`. There is no environment on `docker exec`:
  all per-seat values come from files in the home volume or the secrets tmpfs.
- **Key delivery.** Use a file in a per-container tmpfs (`/run/secrets`, uid of the seat, 0700),
  written by the node agent over `docker exec -i` stdin, or a read-only bind mount of a host
  tmpfs file on Linux. Use **`apiKeyHelper: "cat /run/secrets/anthropic_api_key"`** in the
  projected settings, not `ANTHROPIC_API_KEY`. After every container (re)start, write the key
  again before launching or resuming. Never use `docker cp` into a tmpfs, `-e`, or secrets in
  exec argv (they are logged in events).
- **Home projection.** Per-seat home volume, or better a bind-mounted host directory. The node
  agent projects the following before the first launch:
  - `~/.claude.json`: `hasCompletedOnboarding`, `theme`, and
    `projects[<workspace>].hasTrustDialogAccepted`;
  - the seat settings file with hooks, `StopFailure`, the statusLine and `apiKeyHelper`.

  Without these, the first launch blocks on the theme and login screens with no hook (F3).
- **Workspace at a fixed path** (`/workspace`). It keeps the transcript slug (`-workspace`) and
  the trust entry stable across recreations, so resume works (F7). Map container paths in
  payloads to host paths through `ISandbox.Paths`.
- **Stop protocol.** Send SIGTERM to the harness inside the container and wait for `SessionEnd`
  or `exec_die`. Only then close the pane or stop the container. Before launch, detect orphan
  harnesses (`docker top`) and refuse or clean up (F4). Treat `exec_die`/`die` from Docker
  events as `exited` when no `SessionEnd` arrives (plan §7, sandbox death ⇒ `exited`).
- **Hook transport.** Hooks POST to `host.docker.internal:<node port>` on Docker Desktop, and to
  the bridge gateway or `host-gateway` on native Linux Docker. The source IP is always the
  proxy, so the seat token must travel in a header read from a secrets file (rule 2). Register
  `StopFailure`: auth and API errors surface only there, after about 3 minutes of silent retries.
- **Resume** is unchanged from 0002: `--resume <uuid>` in the same `/workspace`, verified by
  `SessionStart source=resume` plus the same ID. Do not trust `~/.claude/sessions/*.json` across
  container restarts.
- **Dev prerequisite.** Enable Docker Desktop WSL integration for `Ubuntu` before implementing
  the Docker sandbox on the PC. Add it next to mirrored networking in the environment notes once
  this is confirmed.
- **Image.** The npm package ships a native binary, so Node is not needed at runtime. For
  `seat-images`, try `debian:bookworm-slim` plus the native binary (smaller than 822 MB), pin the
  version, and keep `DISABLE_AUTOUPDATER=1`. Add the `aiakos` CLI and the hook relay later
  (plan §3).

Open questions:

- Egress control (allowlisting proxy) was not tested (M6).
- Whether `CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC` also suppresses the remote-managed-settings
  fetch seen with the env-var key.
- `docker attach` to a harness running as PID 1, or tmux inside the container, as ways to avoid
  exec orphans.
