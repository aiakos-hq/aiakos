# Spike 0001 — Claude Code in WSL tmux: paste delivery and hooks to a host endpoint

Issue: #1 · Milestone: M0 · Related: [plan §7](../plan.md), [ADR 0005](../adr/0005-tmux-first-session-host.md),
[ADR 0006](../adr/0006-claude-code-first-harness.md) · Date: 2026-09-29

## Question

Can we drive Claude Code reliably inside a WSL tmux session (bracketed paste + separate submit)
and receive its hook events (and statusLine) on an HTTP endpoint on the Windows host? What do
the payloads look like, and how do they map to seat states `working` / `idle` / `needs-input` /
`exited`?

**Short answer: yes, with three caveats** — (1) in WSL NAT mode the host firewall blocks
WSL → Windows traffic, so the hook relay goes through WSL interop (`curl.exe` → Windows loopback)
or, in the real design, through a node agent inside WSL; (2) Claude Code wraps larger pastes in
`<pasted_content>` and the model then treats them as data, not instructions, so every delivered
message needs a short typed (non-pasted) lead line; (3) input sent before the TUI is ready loses
the submit key, so delivery must wait for a readiness signal.

## Environment

| Item | Value |
|---|---|
| Host | Windows 11 Home 10.0.26200, .NET SDK 10.0.401 |
| WSL | WSL2, distro `Ubuntu`, kernel 6.18.33.2-microsoft-standard-WSL2 |
| WSL networking mode | `nat` (`wslinfo --networking-mode`); default gateway `172.25.48.1`; no `.wslconfig` changes made |
| tmux | 3.6 (dedicated socket `-L aiakos-spike1`) |
| Claude Code | 2.1.284, native install at `~/.local/bin/claude`, subscription login (Pro) |
| Models used | Opus 5.5 (session 1, user default, `auto` permission mode), Haiku 4.5 (session 2, `--permission-mode default`) |
| Listener | .NET 10 file-based app (ASP.NET Core minimal API) on `0.0.0.0:5101` |

Nothing was installed. `jq` is not present in the distro; `python3` and `curl` are.

## Steps

All spike files lived in `~/aiakos-spikes/0001/` (WSL) and a scratch directory on Windows; none
are kept in the repo except as quoted below. Claude ran in `~/aiakos-spikes/0001/work` (a fresh
`git init` directory). User-global Claude config was not touched: hooks and statusLine came from
a per-spike file passed with `claude --settings`.

### 1. Host listener (Windows)

`listener.cs`, run with `dotnet run listener.cs`:

```csharp
#:sdk Microsoft.NET.Sdk.Web
#:property PublishAot=false   // file-based apps default to AOT-style trimming: reflection JSON throws without this
using System.Text;
using System.Text.Json;

var log = Path.Combine(Directory.GetCurrentDirectory(), "events.jsonl");
var app = WebApplication.CreateBuilder(args).Build();
long seq = 0; var gate = new object();
app.MapPost("/{kind}/{name?}", async (HttpContext ctx, string kind, string? name) =>
{
    using var r = new StreamReader(ctx.Request.Body, Encoding.UTF8);
    var body = await r.ReadToEndAsync();
    JsonElement parsed;
    try { parsed = JsonDocument.Parse(body).RootElement.Clone(); }
    catch { parsed = JsonSerializer.SerializeToElement(body); }
    var line = JsonSerializer.Serialize(new {
        seq = Interlocked.Increment(ref seq), at = DateTimeOffset.Now.ToString("O"), kind, name,
        remote = ctx.Connection.RemoteIpAddress?.ToString(),
        headers = ctx.Request.Headers.Where(h => h.Key.StartsWith("X-")).ToDictionary(h => h.Key, h => h.Value.ToString()),
        body = parsed });
    lock (gate) File.AppendAllText(log, line + "\n");
    return Results.Json(new { });
});
app.MapGet("/ping", () => "pong");
app.Run("http://0.0.0.0:5101");
```

### 2. Reaching the host from WSL

```bash
GW=$(ip route show default | awk '{print $3}')      # 172.25.48.1
curl -s -m 5 http://$GW:5101/ping                    # rc=28 (timeout)
curl -s -m 5 http://localhost:5101/ping              # rc=7  (refused; NAT mode has no WSL->host loopback)
curl -s -m 5 http://$(hostname).local:5101/ping      # rc=6  (no mDNS)
echo '{}' | /mnt/c/Windows/System32/curl.exe -s -X POST --data-binary @- http://127.0.0.1:5101/probe/interop
                                                     # rc=0, 690 ms cold, then ~90-150 ms
```

The gateway times out because the Windows Defender Firewall has no inbound allow rule for the
listener on the `vEthernet (WSL (Hyper-V firewall))` interface (all profiles enabled; Hyper-V
firewall for WSL: `DefaultInboundAction Block`). Adding a firewall rule needs admin rights and is
a security-setting change, so it was **not** done in this spike. The interop route (a Windows
executable launched from WSL talks to Windows loopback) works without any system change.

### 3. Hook relay, statusLine script, per-spike settings (WSL)

`hook.sh` (used for every hook; `$1` = kind, `$2` = event name):

```bash
#!/usr/bin/env bash
kind="${1:-hook}"; name="${2:-unknown}"
payload=$(cat)
printf '%s' "$payload" | /mnt/c/Windows/System32/curl.exe -s -m 3 -X POST \
  -H 'Content-Type: application/json' -H "X-Aiakos-Seat: ${AIAKOS_SEAT:-none}" \
  --data-binary @- "http://127.0.0.1:5101/$kind/$name" >/dev/null 2>&1
exit 0   # never block or fail Claude because the relay is down
```

`statusline.sh`:

```bash
#!/usr/bin/env bash
payload=$(cat)
( printf '%s' "$payload" | "$HOME/aiakos-spikes/0001/hook.sh" statusline tick ) &
python3 -c 'import json,sys; d=json.loads(sys.argv[1]); c=d.get("context_window") or {}; print("aiakos | %s | %s" % ((d.get("model") or {}).get("display_name"), c.get("used_percentage", "?")))' "$payload" 2>/dev/null || echo "aiakos"
```

`spike-settings.json` (passed with `--settings`):

```json
{
  "hooks": {
    "SessionStart":     [{ "hooks": [{ "type": "command", "command": "$HOME/aiakos-spikes/0001/hook.sh hook SessionStart", "timeout": 5 }] }],
    "UserPromptSubmit": [{ "hooks": [{ "type": "command", "command": "$HOME/aiakos-spikes/0001/hook.sh hook UserPromptSubmit", "timeout": 5 }] }],
    "PreToolUse":       [{ "matcher": "*", "hooks": [{ "type": "command", "command": "$HOME/aiakos-spikes/0001/hook.sh hook PreToolUse", "timeout": 5 }] }],
    "PostToolUse":      [{ "matcher": "*", "hooks": [{ "type": "command", "command": "$HOME/aiakos-spikes/0001/hook.sh hook PostToolUse", "timeout": 5 }] }],
    "Notification":     [{ "hooks": [{ "type": "command", "command": "$HOME/aiakos-spikes/0001/hook.sh hook Notification", "timeout": 5 }] }],
    "Stop":             [{ "hooks": [{ "type": "command", "command": "$HOME/aiakos-spikes/0001/hook.sh hook Stop", "timeout": 5 }] }],
    "PreCompact":       [{ "hooks": [{ "type": "command", "command": "$HOME/aiakos-spikes/0001/hook.sh hook PreCompact", "timeout": 5 }] }],
    "SessionEnd":       [{ "hooks": [{ "type": "command", "command": "$HOME/aiakos-spikes/0001/hook.sh hook SessionEnd", "timeout": 5 }] }]
  },
  "statusLine": { "type": "command", "command": "$HOME/aiakos-spikes/0001/statusline.sh" }
}
```

### 4. Start Claude in a dedicated tmux server

```bash
SID=$(cat /proc/sys/kernel/random/uuid)
tmux -L aiakos-spike1 new-session -d -s s1 -x 160 -y 45 -c ~/aiakos-spikes/0001/work \
  -e AIAKOS_SEAT=spike1 \
  "$HOME/.local/bin/claude --settings $HOME/aiakos-spikes/0001/spike-settings.json --session-id $SID; echo CLAUDE_EXITED rc=\$?; sleep 600"
# session 2 additionally: --permission-mode default --model haiku
```

`tmux new-session -e VAR=…` reaches the hook processes (the `X-Aiakos-Seat` header carried
`spike1` / `spike1b`), so a per-seat identity/token can be injected via the pane environment.

### 5. Delivery

`send.sh <target> <delay>` (message on stdin):

```bash
S=(tmux -L aiakos-spike1); buf="aiakos-$$"
"${S[@]}" load-buffer -b "$buf" -                     # stdin -> named buffer; no shell escaping of the message
"${S[@]}" paste-buffer -p -r -d -b "$buf" -t "$T"     # -p bracketed paste, -r keep LF, -d delete buffer
sleep "$D"
"${S[@]}" send-keys -t "$T" C-m                       # separate submit
```

`send2.sh` is the same but first types a one-line lead with `send-keys -l "$PREFIX"`.

Messages tried: single line; 5 lines with `$HOME`, backticks, both quote kinds, `\n`, `\\`,
shell metacharacters, a TAB, Greek + `✓`, and a blank line; 3 lines; 10 lines; 400 lines
(28 KB); slash commands `/compact` and `/exit`.

### 6. Triggers

- needs-input: in session 2 (`--permission-mode default`), prompt *"Use the Bash tool to run
  exactly: echo spike > created.txt"* → permission dialog → approved with `Enter`.
- PreCompact: pasted `/compact` + Enter.
- SessionEnd: pasted `/exit` + Enter (session 2); `tmux kill-session -t s1` (session 1).

## Findings

### Delivery (bracketed paste + separate submit)

| Case | Result |
|---|---|
| Single line, `sleep 0` before Enter | Submitted correctly. |
| 400 lines / 28 KB, `sleep 0` before Enter | Submitted correctly; all 400 lines present in the transcript. tmux writes the paste to the pty before the following `send-keys`, so no submit-before-paste race was observed even with zero delay. |
| Special characters | Byte-exact (checked in `transcript_path` and `UserPromptSubmit.prompt`) **except TAB, which became 4 spaces**. Backticks are rendered as inline code in the TUI but are intact in the record. |
| 3-line paste | Delivered inline as plain prompt text. |
| 5- and 10-line pastes | Delivered wrapped: `"\n\n<pasted_content id=\"0b6f\">\n…\n</pasted_content id=\"0b6f\">\n"`. **The model then treated it as pasted data and asked what to do with it** instead of following it (Opus 5.5: *"Your message contains only pasted text, with no instruction from you outside it…"*). The threshold is somewhere between 3 and 5 lines; the id was identical (`0b6f`) across pastes. |
| Typed lead line (`send-keys -l "[aiakos] instruction: "`) + multi-line paste | Model followed the instruction every time (3, 10 and 400 lines). The lead stays outside the `<pasted_content>` wrapper. |
| Paste sent immediately after `new-session` (TUI not ready) | Text ended up in the input box (typeahead), **but the Enter was lost**; the prompt sat unsubmitted until another Enter was sent. |
| Slash commands via paste + Enter | `/compact` and `/exit` executed. They **do not** emit `UserPromptSubmit`. The completion menu is shown while typing but did not swallow the Enter. |
| First start in a new directory | Blocking "Is this a project you trust?" dialog; **default selection is "No, exit"**, so a blind Enter kills the session. `SessionStart` fires only after the dialog is answered. |
| After a turn | A prompt-suggestion ghost text (e.g. `❯ yes, do it`) can appear in the input box. It is not real input: a paste replaces it. Never send a bare Enter to an idle prompt. |
| Ghost text / notices | tmux shows *"tmux focus-events off"* in the footer; harmless, but it may matter for focus-based notifications (see idle below). |

### Hooks and statusLine on the host

All eight hook events arrived on the Windows listener through the interop relay:
`SessionStart` (`source`: `startup`, and `compact` after compaction), `UserPromptSubmit`,
`PreToolUse`, `PostToolUse`, `Notification` (`permission_prompt`), `Stop`, `PreCompact`
(`trigger: manual`), `SessionEnd` (`reason: prompt_input_exit` for `/exit`, `other` for
`tmux kill-session`). Hook runtime as recorded by Claude (`stop_hook_summary.durationMs`) was
135–315 ms, dominated by the interop `curl.exe` start.

Every hook payload carries `session_id` (equals the `--session-id` we passed), `transcript_path`
(`~/.claude/projects/<cwd with / → ->/<session_id>.jsonl`), `cwd`, `scratchpad_dir` and
`hook_event_name`; from the first prompt on also `prompt_id` (correlates all events of one turn)
and `permission_mode`.

Observed timeline for the permission case (session 2):

```
15:02:48.909 UserPromptSubmit  prompt_id ac86…  permission_mode default
15:02:51.039 PreToolUse        Bash "echo spike > created.txt"
15:02:57.300 Notification      permission_prompt  "Claude needs your permission"   (~6 s after PreToolUse)
   … human approves at ~15:03:47 …
15:03:48.176 PostToolUse       duration_ms 331
15:03:49.381 Stop              last_assistant_message "DONE."
15:04:12.395 PreCompact        trigger manual
15:04:24.817 SessionStart      source compact
15:29:28.976 SessionEnd        reason prompt_input_exit
```

Payload samples (identical envelope fields trimmed after the first):

```json
{ "session_id": "92a77c31-9448-450a-bd5e-ff43ee8f23a7",
  "transcript_path": "/home/bsakel/.claude/projects/-home-bsakel-aiakos-spikes-0001-work/92a77c31-9448-450a-bd5e-ff43ee8f23a7.jsonl",
  "cwd": "/home/bsakel/aiakos-spikes/0001/work",
  "scratchpad_dir": "/tmp/claude-1000/-home-bsakel-aiakos-spikes-0001-work/92a77c31-9448-450a-bd5e-ff43ee8f23a7/scratchpad",
  "hook_event_name": "SessionStart", "source": "startup", "model": "claude-haiku-4-5-20251001" }

{ "…envelope…", "prompt_id": "86d66396-…", "permission_mode": "auto",
  "hook_event_name": "UserPromptSubmit", "prompt": "Reply with exactly the word OK and nothing else." }

{ "…envelope…", "prompt_id": "ac86fdb9-…", "permission_mode": "default", "hook_event_name": "PreToolUse",
  "tool_name": "Bash", "tool_input": { "command": "echo spike > created.txt", "description": "Create file with spike content" },
  "tool_use_id": "toolu_0153QrajJHEMRwBfq6zKBoVc" }

{ "…envelope…", "prompt_id": "ac86fdb9-…", "hook_event_name": "Notification",
  "message": "Claude needs your permission", "notification_type": "permission_prompt" }

{ "…envelope…", "hook_event_name": "PostToolUse", "tool_name": "Bash", "tool_input": { "…": "…" },
  "tool_response": { "stdout": "", "stderr": "", "interrupted": false, "isImage": false, "noOutputExpected": false },
  "tool_use_id": "toolu_0153QrajJHEMRwBfq6zKBoVc", "duration_ms": 331 }

{ "…envelope…", "hook_event_name": "Stop", "stop_hook_active": false, "last_assistant_message": "DONE.",
  "background_tasks": [], "session_crons": [], "effort": { "level": "medium" } }

{ "…envelope…", "hook_event_name": "PreCompact", "trigger": "manual", "custom_instructions": null }

{ "…envelope…", "hook_event_name": "SessionEnd", "reason": "prompt_input_exit" }
```

statusLine JSON (after a turn; `effort` appears for models that support it). It is emitted on
start, after events and after turns — it is **not** tagged with an event name and has no
sequence number:

```json
{
  "session_id": "92a77c31-9448-450a-bd5e-ff43ee8f23a7",
  "transcript_path": "/home/bsakel/.claude/projects/-home-bsakel-aiakos-spikes-0001-work/92a77c31-….jsonl",
  "cwd": "/home/bsakel/aiakos-spikes/0001/work",
  "prompt_id": "ac86fdb9-de20-44a3-beaa-54628ece22ea",
  "session_name": "Reply with exactly RACE",
  "model": { "id": "claude-haiku-4-5-20251001", "display_name": "Haiku 4.5" },
  "workspace": { "current_dir": "/home/bsakel/aiakos-spikes/0001/work", "project_dir": "/home/bsakel/aiakos-spikes/0001/work", "added_dirs": [] },
  "version": "2.1.284",
  "output_style": { "name": "default" },
  "cost": { "total_cost_usd": 0.034351, "total_duration_ms": 114563, "total_api_duration_ms": 9764,
            "total_lines_added": 0, "total_lines_removed": 0 },
  "context_window": { "total_input_tokens": 32576, "total_output_tokens": 32, "context_window_size": 200000,
                      "current_usage": { "input_tokens": 8, "output_tokens": 32,
                                         "cache_creation_input_tokens": 202, "cache_read_input_tokens": 32366 },
                      "used_percentage": 16, "remaining_percentage": 84 },
  "exceeds_200k_tokens": false,
  "prompt_cache": { "warm": true, "ttl": "1h", "hit_ratio": 0.907, "…": "…" },
  "fast_mode": false,
  "thinking": { "enabled": true },
  "rate_limits": { "five_hour": { "used_percentage": 43, "resets_at": 1790684400 },
                   "seven_day": { "used_percentage": 11, "resets_at": 1790823600 } }
}
```

Before the first turn `context_window.used_percentage`/`current_usage` are `null` (report
`unknown`, not 0). After `/compact` `used_percentage` dropped to 0. `rate_limits` exposes the
subscription's 5-hour / 7-day usage — useful for scheduling and for a "subscription nearly
exhausted" health finding. With Opus 5.5 `context_window_size` was 1 000 000.

### State mapping

| Signal | State | Notes |
|---|---|---|
| `SessionStart` (`source: startup` / `resume` / `clear`) | `idle` | Also the **readiness signal** for delivery. Seat is ready for input only after this. |
| `SessionStart` (`source: compact`) | unchanged (`working` if compaction was mid-turn, else `idle`) | Not a new session: same `session_id`. |
| `UserPromptSubmit` | `working` | Confirms delivery (compare `prompt` with what we sent). Not emitted for slash commands. |
| `PreToolUse` / `PostToolUse` | `working` | `PostToolUse` after a `permission_prompt` Notification means input was given → back to `working`. |
| `Notification` `permission_prompt` | `needs-input` | Arrives ~6 s after `PreToolUse`, not immediately. |
| `Notification` `idle_prompt` (documented) | `idle` | **Not observed**: session 1 sat idle 28 min with no Notification (possibly because no client is attached / focus events off). Do not depend on it; `Stop` is the idle signal. |
| `PreCompact` | `working` (sub-state *compacting*) | |
| `Stop` | `idle` | `last_assistant_message` is the turn result. |
| `SessionEnd` | `exited` | Fired both for `/exit` and for `tmux kill-session` (SIGHUP). Cannot fire on SIGKILL/crash → need pane-dead / process watch as backup. |
| Pane dead / process gone, no `SessionEnd` | `exited` (from session host) | tmux `remain-on-exit` or `#{pane_dead}`; the spike's wrapper printed `CLAUDE_EXITED rc=0`. |
| No hook/statusLine/heartbeat for N s while `working` | `unknown` | Per plan §7. Long tool calls produce no events between `PreToolUse` and `PostToolUse`. |

Gaps: nothing signals "permission dialog answered" until the tool finishes (`PostToolUse`), or
"permission denied" except the following `Stop`/`PostToolUse`. The newer `PermissionRequest`
hook event (not tested here) may close that gap and fire without the ~6 s delay.

### Ordering

The host saw events almost simultaneously in a few cases (statusLine and `SessionStart` within
the same millisecond; statusLine before the `Stop` it followed). Each hook is a separate process,
so **arrival order is not guaranteed**. The relay must stamp a monotonic sequence number at the
source (per seat), and the SeatActor must order by it, not by arrival.

## Pitfalls (checklist for the adapter)

1. **Wait for readiness** (`SessionStart` hook for this seat, optionally plus a prompt-box check
   in `capture-pane`) before the first delivery; otherwise the submit key is lost.
2. **Always type a one-line lead** (`send-keys -l`) before the bracketed paste, e.g.
   `[aiakos from <sender>]:`. Without it, pastes of ≥ ~5 lines arrive as `<pasted_content>` and
   the model may refuse to act on them.
3. **Verify delivery** via `UserPromptSubmit` (for non-slash messages); retry the submit key once
   if it does not arrive within a few seconds and the pane shows the text in the input box.
4. TAB characters are converted to spaces; send files by path, not by paste, when bytes matter.
5. Pre-trust the working directory (or answer the trust dialog deterministically). The dialog's
   default is "No, exit".
6. Never send a bare Enter to an idle prompt (prompt-suggestion ghost text).
7. Hook scripts must always `exit 0` quickly and time out their network call; a hung relay would
   stall the harness.
8. In WSL NAT mode, WSL cannot reach a Windows listener without a firewall rule; `localhost` from
   WSL does not reach Windows.
9. Driving `wsl.exe` from PowerShell 5.1 mangles embedded double quotes in `bash -lc '…'`; put
   commands in script files (or use a node agent inside WSL). A `wsl.exe` call whose script
   leaves a background child (the statusLine relay) can also linger until the child exits.
10. .NET file-based apps disable reflection-based `System.Text.Json` by default
    (`#:property PublishAot=false` or source-generated contexts).

## Decision / follow-ups

- **ADR 0005 / 0006 hold.** tmux bracketed paste + separate `C-m` is reliable for content of
  any size tested; the problems are readiness, the paste wrapper and trust dialog, all solvable
  in the adapter.
- **Hook transport**: hooks should POST to a **node agent inside WSL** (localhost, no firewall,
  no interop cost), which stamps sequence numbers, buffers while offline, and forwards to the
  orchestrator. This matches plan §7 ("hooks POST to the node agent"). The node agent → Windows
  orchestrator link then has the same NAT/firewall problem; options, to be decided in spike #3
  (Aspire launching a WSL node): the orchestrator connects *into* WSL (NAT mode forwards Windows
  `localhost` to WSL listeners), mirrored networking, or a one-time firewall rule by the user.
  The interop `curl.exe` relay is an acceptable fallback, not the design.
- **ISessionHost (tmux)** needs: `Start(seat, cmd, env)` via `new-session -e` (per-seat token
  in env → `X-Aiakos-Seat`-style header, rule 2), `Deliver(lead, body)` = `send-keys -l lead` +
  `load-buffer`/`paste-buffer -p -r -d` + `send-keys C-m`, `Capture()` (evidence only),
  `IsAlive()` (`#{pane_dead}`, `remain-on-exit`), `Kill()`. Use a per-node tmux socket
  (`-L aiakos`).
- **Claude adapter** needs: projected `--settings` file with all hooks (+ `PermissionRequest`),
  statusLine script, `--session-id <uuid>` chosen by Aiakos (so the ID is known before
  `SessionStart`), readiness gate on `SessionStart`, delivery confirmation via
  `UserPromptSubmit.prompt_id`, context usage and rate limits from statusLine.
- Open questions:
  - Exact `<pasted_content>` threshold (lines vs. characters) and whether it is configurable.
  - Whether `Notification idle_prompt` fires at all in a detached tmux pane (focus events).
  - `PermissionRequest` hook payload and timing; how a denial shows up.
  - Whether Claude Code's native `type: "http"` hooks are available in 2.1.284 (would remove
    the relay script where the endpoint is reachable).
  - Resume behaviour and `SessionStart source: resume` — spike #2.
