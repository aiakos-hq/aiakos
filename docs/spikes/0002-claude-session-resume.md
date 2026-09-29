# 0002 — Claude Code session-ID capture and resume

Issue: [#2](https://github.com/aiakos-hq/aiakos/issues/2) · Milestone: M0 · Related:
[ADR 0005](../adr/0005-tmux-first-session-host.md), [ADR 0006](../adr/0006-claude-code-first-harness.md),
spike 0001 (paste delivery + hooks).

**Result:** works, with caveats. We can choose the session ID up front, see it in every hook and
statusLine payload, and resume the same conversation after killing the tmux server. A resume that
fails exits non-zero and never starts a fresh session on its own, **as long as the adapter only
passes a UUID it believes exists**. Anything that is not a UUID opens an interactive picker. Resume
after `wsl --shutdown` also **passed** (see [Resume after `wsl --shutdown`](#resume-after-wsl---shutdown)).

## Question

Can we launch Claude Code with a known session ID, capture it reliably, and resume the same
conversation after killing tmux and after a WSL shutdown? Can we also detect honestly when a resume
did *not* happen (CLAUDE.md rule 3: no silent fresh start)?

## Environment

- Windows 11 host, WSL2 distro `Ubuntu`, kernel 6.18.33.2-microsoft-standard-WSL2.
- tmux 3.6 on a private socket (`tmux -L aiakos-spike2`).
- Claude Code 2.1.284 (`~/.local/bin/claude`), logged in with a Claude Pro subscription (OAuth).
  `--model haiku` for every probe.
- Scratch area: `~/aiakos-spikes/0002/` (WSL filesystem):
  - `settings.json`: per-spike settings passed with `--settings`. It registers `SessionStart`,
    `SessionEnd`, `UserPromptSubmit` and `Stop` hooks and a `statusLine` command. The user-global
    `~/.claude/settings.json` was not changed.
  - `bin/hook.sh`: appends `<ISO timestamp>\t<stdin JSON>` to `logs/hooks.jsonl`.
  - `bin/statusline.sh`: appends the statusLine input to `logs/statusline.jsonl` and prints
    `seat sid=<session_id>`.
  - `ws/…` is a trusted workspace root. `other/…` is an untrusted one.

```json
{
  "hooks": {
    "SessionStart":     [ { "hooks": [ { "type": "command", "command": "/home/<user>/aiakos-spikes/0002/bin/hook.sh" } ] } ],
    "SessionEnd":       [ { "hooks": [ { "type": "command", "command": "/home/<user>/aiakos-spikes/0002/bin/hook.sh" } ] } ],
    "UserPromptSubmit": [ { "hooks": [ { "type": "command", "command": "/home/<user>/aiakos-spikes/0002/bin/hook.sh" } ] } ],
    "Stop":             [ { "hooks": [ { "type": "command", "command": "/home/<user>/aiakos-spikes/0002/bin/hook.sh" } ] } ]
  },
  "statusLine": { "type": "command", "command": "/home/<user>/aiakos-spikes/0002/bin/statusline.sh" }
}
```

## Steps

Interactive sessions always run inside tmux and are observed with `capture-pane -p`. One-shot
probes use `timeout 25 claude -p … </dev/null`. Never run bare `claude` in a foreground shell
from automation: an earlier attempt at this spike hung on the interactive picker (see F3).

```bash
S=~/aiakos-spikes/0002/settings.json
T="tmux -L aiakos-spike2"

# 1. Launch with a known ID and a name
U=$(python3 -c 'import uuid;print(uuid.uuid4())')
$T new-session -d -s seatL -x 160 -y 40 -c ~/aiakos-spikes/0002/ws/seatL
$T send-keys -t seatL "claude --session-id $U -n seatL --model haiku --settings $S" Enter
# turn 1 plants a nonce
$T send-keys -t seatL -l "Remember the code word KESTREL-9356. Reply only: OK"; $T send-keys -t seatL Enter

# 2. Kill the whole tmux server (claude receives SIGHUP), then resume in a new server
$T kill-server
$T new-session -d -s seatL2 -x 160 -y 40 -c ~/aiakos-spikes/0002/ws/seatL
$T send-keys -t seatL2 "claude --resume $U --model haiku --settings $S; echo CLAUDE_EXIT=\$?" Enter
$T send-keys -t seatL2 -l "What was the code word? Reply with only the code word."; $T send-keys -t seatL2 Enter

# 3. Fork: new ID (random or chosen), same history
claude --resume $U --fork-session -n fork1 …
claude --resume $U --fork-session --session-id <new-uuid> -n fork2 …

# 4. Error cases (one-shot, bounded)
timeout 25 claude --session-id not-a-uuid -p hi … </dev/null
timeout 25 claude --resume 22222222-2222-4222-8222-222222222222 -p hi … </dev/null
timeout 25 claude --resume not-a-uuid -p hi … </dev/null
timeout 25 claude --session-id <existing-uuid> -p hi … </dev/null
timeout 25 claude --resume <uuid> --session-id <other-uuid> -p hi … </dev/null
# the same three --resume cases were also run interactively in tmux, plus --continue in an empty dir

# 5. Trust prompt: launch and resume in ws/seatA (parent trusted) and other/seatB (untrusted);
#    observe, press Esc, never accept. Then `claude -p` in other/seatB.
```

## Findings

### F1: Launch, resume and fork commands

| Purpose | Command | Notes |
|---|---|---|
| Fresh, known ID | `claude --session-id <uuid> -n <seat>` | The UUID must be valid. `-n/--name` sets the display name (prompt box, picker, terminal title, `session_title` in hooks). |
| Resume | `claude --resume <uuid>` | Same ID. `SessionStart.source = "resume"`. Works from another cwd too (F6). |
| Fork | `claude --resume <uuid> --fork-session [--session-id <new-uuid>] [-n <name>]` | `source = "fork"`. Full history. With `--session-id` the new ID is ours; without it, Claude picks a random one. |
| Continue latest in cwd | `claude --continue` | Not suitable for seats, because it picks an implicit session. |

`--session-id` together with `--resume`/`--continue` is refused unless `--fork-session` is also
given (exit 1). `--bg` (background sessions under the same ID) exists in 2.1.284 but was not
tested.

### F2: Error behaviour (from the `-p` probes and the interactive runs in tmux)

| Case | Interactive (tmux) | `-p` print mode | Silent fresh start? |
|---|---|---|---|
| `--session-id not-a-uuid` | n/a | `Error: Invalid session ID. Must be a valid UUID.`, exit 1 | no |
| `--session-id <uuid already used>` | n/a | `Error: Session ID <uuid> is already in use.`, exit 1 | no |
| `--resume <valid but unknown uuid>` | `No conversation found with session ID: <uuid>`, exit 1 | same, exit 1 | no |
| `--resume <uuid of a session that never had a prompt>` | n/a | `No conversation found…`, exit 1 (F5) | no |
| `--resume not-a-uuid` | **Opens the "Resume session" picker** with the value as search text and **blocks** (F3) | `Error: --resume requires a valid session ID or session title when used with --print…`, exit 1 | no, but it hangs |
| `--resume <session title>` (e.g. `seat1`) | Resumes the session with that title | Documented as accepted | no, but ambiguous |
| `--continue` in a dir with no sessions | `No conversation found to continue`, exit 1 | n/a | no |

A failed resume still fires a **`SessionEnd` hook for an ID that never started**: the requested
UUID, or a random new UUID for the malformed `-p` case. No `SessionStart` comes before it.

### F3: The picker trap (why the earlier run hung)

In interactive mode, `--resume <value>` treats any value that is not a known UUID as a *search
term* or *title*. With no match it shows `No sessions match "<value>"` and waits for input.
Pressing **Esc clears the search and lists all sessions**. A stray Enter after that would
**resume an arbitrary other session**, which is worse than a fresh start. The adapter must:

- only ever pass a canonical lowercase UUID to `--resume` (validate it before building the command);
- never pass a title, even though titles work;
- treat "no `SessionStart` within N seconds" as `unknown`/blocked and take a screen capture as
  evidence. It must never send keys to "get past" a screen it does not recognise.

### F4: Where the session ID is observable

| Source | What it gives | Reliability |
|---|---|---|
| Our own launch command | The UUID we generated (`--session-id`) | Authoritative for fresh and fork launches. For resume, the ID is unchanged. |
| Hook stdin (`SessionStart`, `UserPromptSubmit`, `Stop`, `SessionEnd`) | `session_id`, `transcript_path`, `cwd`, `hook_event_name`; `SessionStart.source` is `startup`/`resume`/`fork`; `session_title` = `--name` | Primary live signal. `SessionStart` fires at launch **before any prompt**. |
| statusLine stdin | `session_id`, `session_name`, `transcript_path`, `cwd`, `context_window{…used_percentage}`, `cost`, `model`, `rate_limits` | Good heartbeat plus context usage. Only while the TUI renders. |
| `~/.claude/projects/<cwd-slug>/<uuid>.jsonl` | The transcript. Each entry carries `sessionId`. The slug is the cwd with `/` and `.` replaced by `-`. | The durable record. **Created only on the first prompt** (F5). |
| `~/.claude/sessions/<pid>.json` | `pid`, `sessionId`, `cwd`, `name`, `status` (`idle`…), `tmux` (`<session>:@<win>.%<pane>`), `procStart`, `version` | A live-process registry. Removed on a clean exit or SIGHUP. **Can go stale**: a file for a dead pid remained from an earlier run. Check the pid and `procStart` before trusting it. There is also a `<pid>.<hash>.key` file next to it (a secret; never read or copy it). |
| `~/.claude.json` → `projects[<cwd>].lastSessionId` | The last session per cwd | Global user config. Only for diagnostics; do not depend on it. |

Hook payload samples (paths shortened):

```json
{"session_id":"088452fe-…","transcript_path":"~/.claude/projects/-home-<user>-aiakos-spikes-0002-ws-seatL/088452fe-….jsonl","cwd":"…/ws/seatL","hook_event_name":"SessionStart","source":"startup","model":"claude-haiku-4-5-20251001","session_title":"seatL"}
{"session_id":"498424e1-…","hook_event_name":"SessionStart","source":"resume","session_title":"seat1","seconds_since_last_response":52,"context_tokens":32240,"prompt_cache_likely_expired":false,"estimated_cache_write_usd":0.0645}
{"session_id":"088452fe-…","hook_event_name":"Stop","stop_hook_active":false,"last_assistant_message":"KESTREL-9356","background_tasks":[],"session_crons":[]}
{"session_id":"088452fe-…","hook_event_name":"SessionEnd","reason":"other"}
```

A resume `SessionStart` also reports `seconds_since_last_response`, `context_tokens` and
`prompt_cache_likely_expired`. This is useful for cost and state: seeing these fields is further
evidence that prior context was loaded.

### F5: Sessions with no prompt are not resumable

`claude --session-id X` fires `SessionStart(startup)` immediately, but the transcript file is
written only when the first prompt is submitted. If the seat is killed before that,
`--resume X` fails with `No conversation found` (exit 1). The adapter should record a seat as
*resumable* only after it sees the first `UserPromptSubmit`/`Stop` for that ID, or the transcript
file exists. Before that, relaunch with `--session-id X`, which is allowed because the ID was never
persisted. Otherwise the orchestrator would report a failed resume where it should have started
fresh on purpose.

### F6: Resume from a different cwd

`claude --resume <uuid>` from another directory (`ws/sub` instead of `ws`) found the session and
continued it. Two catches:

- The `SessionStart` payload reported `transcript_path` under the *new* cwd's slug, but later
  events (and the actual writes) used the original file under the old slug. **Do not trust
  `SessionStart.transcript_path` after a resume**; use the path from `UserPromptSubmit`/`Stop`, or
  keep our own.
- The trust check is per cwd (F8), so resuming in a new directory can trigger the trust prompt.

Seats should always resume in the workspace they were launched in.

### F7: Kill tmux and resume (verified)

| Step | Observed |
|---|---|
| Launch `--session-id 088452fe-… -n seatL` in `ws/seatL` | `SessionStart source=startup`. `sessions/<pid>.json` has the ID and the tmux pane. No transcript yet. |
| Turn 1: plant `KESTREL-9356` | `UserPromptSubmit`, `Stop last_assistant_message=OK`. Transcript has 25 lines. |
| `tmux -L aiakos-spike2 kill-server` | claude exits (SIGHUP). `SessionEnd reason=other`. The registry file is removed. |
| New server, `claude --resume 088452fe-…` | `SessionStart source=resume`, same `session_id`. The TUI re-renders the earlier turns. |
| Turn 2: "What was the code word?" | `Stop last_assistant_message=KESTREL-9356`. Transcript has 45 lines, and every entry has `sessionId` = the same UUID. |

The earlier runs in this spike got the same result: session `498424e1-…` was resumed three times
and forked twice (`fork1` recalled the nonce under a new ID). One recall answered `ZEBRA` instead of
`ZEBRA-4417` because haiku shortened it. Compare the nonce leniently, or ask for it in a fixed
format.

### F8: Prompts that block unattended launch or resume

| Prompt | When | Effect | How to pre-accept |
|---|---|---|---|
| **Workspace trust** ("Accessing workspace … Is this a project you created or one you trust?") | Interactive launch **or resume** in a directory whose ancestors are all untrusted | Blocks **before `SessionStart` fires**, so no hook arrives. Esc or "No, exit" exits with **code 0**, so the exit code is not an error signal. | Trust is **inherited from a trusted ancestor**: `ws/seatA` did not prompt because `ws` is trusted (`~/.claude.json` → `projects["…/ws"].hasTrustDialogAccepted = true`). Trust one *seat root* (e.g. `~/aiakos/seats`) once, by hand or at node setup, and create per-seat workspaces under it. For sandboxed seats with their own config dir, the node can project `hasTrustDialogAccepted` for the workspace root into that seat's `.claude.json`. |
| Resume picker | `--resume <non-UUID>` interactively | Blocks (F3) | Avoid it: pass only UUIDs. |
| Print mode | `claude -p` | **No trust prompt**, even in an untrusted dir | Not relevant for interactive seats. |

No other onboarding, login or theme prompt appeared, because the account had finished onboarding
(`hasCompletedOnboarding`). A brand-new `CLAUDE_CONFIG_DIR` (for example a per-seat home in a
container) would add the first-run onboarding and login flow. That belongs to spike #5 (Docker
seat). `--dangerously-skip-permissions` has its own confirmation screen and was not used here.

### F9: Detecting honestly whether a resume happened

A resume counts as **verified** only if all of these hold:

1. `claude` did not exit. An exit code of 1 with `No conversation found…` means resume failed. It
   never falls back to a fresh start.
2. A `SessionStart` hook arrives with `session_id == <expected>` **and** `source == "resume"`
   (`fork` for forks). The adapter does not pass `--session-id` on resume, so a `startup` source or
   a different ID cannot happen by design. If it is ever seen, the result is `failed`, not "close
   enough".
3. Optional, stronger: the transcript `~/.claude/projects/<slug>/<uuid>.jsonl` existed before the
   launch and grows after the next turn. The `SessionStart(resume)` fields `context_tokens` and
   `seconds_since_last_response` are non-null.
4. Nonce recall (plant in turn 1, ask after resume) is a **test technique**, not something to run
   in production.

If no `SessionStart` arrives within a timeout (about 15 s), the state is `unknown`/`blocked`. Take a
`capture-pane` for evidence; it is usually the trust prompt or the picker. A `SessionEnd` without a
preceding `SessionStart` is noise from a failed launch and must not change seat state.

## Resume after `wsl --shutdown`

**Result: PASS.** The maintainer ran the verifier below after `wsl --shutdown`, once spikes #1 and
#3 (which share the distro) had finished. The resumed session reported `SessionStart source=resume`
with the same ID and recalled the nonce. The session was prepared as follows:

| | |
|---|---|
| Session ID | `acd10516-aaf5-4725-b6e9-e2b1c70d0007` (name `seatW`) |
| Workspace | `~/aiakos-spikes/0002/ws/seatW` (trusted through `ws`) |
| Nonce | `OSPREY-3562` (planted in turn 1, which answered `OK`) |
| Transcript | `~/.claude/projects/-home-<user>-aiakos-spikes-0002-ws-seatW/acd10516-aaf5-4725-b6e9-e2b1c70d0007.jsonl`, 37 lines when prepared |
| WSL boot id when prepared | `98c2c3cb-6414-4276-8571-5efa4e6c1e12` |
| Env file | `~/aiakos-spikes/0002/pending-wsl-shutdown.env` |
| Verifier | `~/aiakos-spikes/0002/bin/verify-pending.sh` |

Commands:

```powershell
wsl --shutdown
wsl -d Ubuntu -- timeout 120 bash -lc '~/aiakos-spikes/0002/bin/verify-pending.sh'
```

The verifier:

1. Prints the boot id, which must differ from the one above, to prove WSL really restarted.
2. Starts `tmux -L aiakos-spike2` session `seatW` in the workspace and runs
   `claude --resume acd10516-aaf5-4725-b6e9-e2b1c70d0007 --model haiku --settings ~/aiakos-spikes/0002/settings.json`.
3. Waits for `SessionStart` and sends "What was the code word? Reply with only the code word.".
4. Prints the new hook events and `RESULT: PASS` only if `SessionStart.source == "resume"`, the
   `session_id` matches, and `Stop.last_assistant_message` contains `OSPREY-3562`.
5. Shows the transcript line count (it should grow from 37) and kills the tmux session.

Manual equivalent:

```bash
source ~/aiakos-spikes/0002/pending-wsl-shutdown.env
cat /proc/sys/kernel/random/boot_id
tmux -L aiakos-spike2 new-session -d -s seatW -x 160 -y 40 -c "$WORKSPACE"
tmux -L aiakos-spike2 send-keys -t seatW "claude --resume $SESSION_ID --model haiku --settings ~/aiakos-spikes/0002/settings.json" Enter
# wait ~10 s
tmux -L aiakos-spike2 send-keys -t seatW -l "What was the code word? Reply with only the code word."
tmux -L aiakos-spike2 send-keys -t seatW Enter
# wait ~10 s
tail -n 4 ~/aiakos-spikes/0002/logs/hooks.jsonl     # expect SessionStart source=resume, Stop …"OSPREY-3562"
tmux -L aiakos-spike2 capture-pane -p -t seatW | tail -15
tmux -L aiakos-spike2 kill-session -t seatW
```

This matches the expectation. The transcript lives on the WSL ext4 disk, and kill-server resume
uses the same code path. A restart differs only in the lost `/run/user/1000/cc-socks/*.sock` files
and in stale `~/.claude/sessions/<pid>.json` files; none of these are needed for `--resume`.

## Decision / follow-ups

Recommendation for the Claude Code adapter (`IHarnessAdapter.BuildLaunch`, M1):

- **The orchestrator owns the ID.** Generate a UUIDv4 per seat conversation, persist it *before*
  launch, and launch with `claude --session-id <uuid> -n <seat> --settings <seat-settings.json>`.
  Never read the ID from a request body (rule 2). Hooks confirm it; they do not supply it.
- **Resume** = `claude --resume <uuid> -n <seat>` in the **same workspace dir**, without
  `--session-id`. Validate the UUID syntactically first, and never pass a title or free text (F3).
- **Fork** = `claude --resume <uuid> --fork-session --session-id <new-uuid> -n <name>`. The
  orchestrator owns the new ID as well.
- **Do not use `--continue`** for seats; it is implicit.
- **Resumable flag**: mark a conversation resumable only after its first `UserPromptSubmit`/`Stop`,
  or once the transcript file exists (F5). Before that, "resume" means relaunching with the same
  `--session-id`, and the adapter should report it as a fresh launch.
- **Resume outcome** is `verified` / `failed` / `unknown`, following F9. Never fall back to a fresh
  session automatically. A fresh start after a failed resume is an explicit, recorded decision
  (rule 3).
- **Ignore orphan `SessionEnd`** events, i.e. those without a `SessionStart` for that
  ID in this launch.
- **Trust**: the node agent ensures one trusted seat root per node (documented setup step, or
  projected into per-seat config for sandboxes). Seat workspaces live under it. A launch that shows
  no `SessionStart` within the timeout is `blocked`, with a pane capture as evidence.
- **Do not rely on `~/.claude/sessions/<pid>.json`** as a source of truth. At most it is a
  diagnostic, and only after checking that the pid and `procStart` are live.
- Pass hooks and statusLine per seat with `--settings <file>` (as done here) so that user-global
  settings stay untouched.

Follow-ups:

- Spike #5 (Docker seat): first-run onboarding and login in a fresh config dir, plus trust
  projection.
- Consider `--bg` / `claude attach` later as an alternative session host (not needed for M1).
