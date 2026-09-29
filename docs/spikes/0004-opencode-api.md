# Spike 0004 — OpenCode as an API-driven seat: server, events, permissions and TUI attach

Issue: [#4](https://github.com/aiakos-hq/aiakos/issues/4) · Milestone: M0 · Related: [plan §3 and §6](../plan.md),
[ADR 0005](../adr/0005-tmux-first-session-host.md), [ADR 0006](../adr/0006-claude-code-first-harness.md),
spikes [0001](0001-claude-wsl-tmux-hooks.md) and [0002](0002-claude-session-resume.md) · Date: 2026-09-29

## Question

How do we drive OpenCode as an API-driven seat? That means `opencode serve`, creating and
prompting sessions, subscribing to events, answering permission requests, and attaching a TUI in
tmux so a human can watch. Which subscription logins does OpenCode support, and what do the
provider terms allow?

**Short answer: it works, and it is a better fit for a seat than the tmux-paste route.** One
`opencode serve` per node (or per seat) exposes a documented OpenAPI 3.1 HTTP API. Delivery is
`POST /session/{id}/prompt_async` (204, no paste, no readiness race). State comes from one SSE
stream (`GET /event`): `session.status` busy/idle, `session.idle`, `permission.asked`/`replied`,
`session.error`. Permissions are answered over HTTP, and a human-attached TUI
(`opencode attach <url> --session <id>`) shows and can answer the same dialog. Sessions are rows
in a SQLite database, so "resume" is simply prompting the same session ID after a server restart.
The live tests used OpenCode Zen's free `opencode/big-pickle` model, which needs no credential.

Caveats: (1) the server exposes **two API generations**. The legacy one (`/session`, `/event`)
works; the new `/api/*` "v2" one is partly unfinished in 1.18.33 (`wait` returns 503, `/api/event`
closed itself, and v2 prompts failed on the free tier). Use the legacy API for M2 and keep v2 behind
the adapter. (2) Waiting for a permission shows up as `busy`, not a separate status, so
`needs-input` has to be derived from `permission.asked`. (3) OpenCode reads Claude Code's
`~/.claude/skills` by default; seats must disable that. (4) **Anthropic prohibits** using Claude
Pro/Max subscription credentials in OpenCode, and OpenCode removed that login. GitHub Copilot
officially supports OpenCode; ChatGPT plans are publicly tolerated by OpenAI but not contractually
promised (see [Auth options and provider terms](#auth-options-and-provider-terms)).

## Environment

| Item | Value |
|---|---|
| Host | Windows 11 Home 10.0.26200 |
| WSL | WSL2, distro `Ubuntu`, **mirrored** networking (as required since spike 0003) |
| tmux | 3.6, dedicated socket `-L aiakos-spike4` |
| OpenCode | **1.18.33**, official install script (`https://opencode.ai/install`, which downloads the release binary from `github.com/anomalyco/opencode/releases`), user-local at `~/.opencode/bin/opencode` (a single ~185 MB Bun-compiled binary). Installed with `--no-modify-path`, so shell rc files were not changed. No sudo, no node/npm. |
| Model | `opencode/big-pickle` (OpenCode Zen free model, cost 0, **no credential needed**; the Zen docs say that during the free period collected data may be used to improve the model, so prompts were kept tiny and harmless). `opencode models` without credentials also lists `opencode/*-free` models. |
| Credentials | None. `opencode providers list` → `0 credentials`. No login flow was started. |
| Isolation | All OpenCode state went to `~/aiakos-spikes/0004/xdg/` via `XDG_CONFIG_HOME`, `XDG_DATA_HOME`, `XDG_STATE_HOME`, `XDG_CACHE_HOME`. Workspaces: `~/aiakos-spikes/0004/ws/seat1`, `ws/seat2` (fresh `git init`, no commits). Port 4096. |

Side effects outside the spike dir: the install script's own `opencode --version` check created
empty `~/.config/opencode/` and `~/.local/share/opencode/{log,repos}` (it ran before the XDG
variables were set). Nothing else in the home directory was changed.

## Steps

Spike scripts lived in the session scratch directory and were run from PowerShell with
`wsl -d Ubuntu --exec bash /mnt/c/.../script.sh` (see spike 0001, pitfall 9). Every interactive
thing ran inside `tmux -L aiakos-spike4` and was observed with `capture-pane -p`. Common prelude:

```bash
B=~/aiakos-spikes/0004
export XDG_CONFIG_HOME=$B/xdg/config XDG_DATA_HOME=$B/xdg/data XDG_STATE_HOME=$B/xdg/state XDG_CACHE_HOME=$B/xdg/cache
OC=~/.opencode/bin/opencode; T="tmux -L aiakos-spike4"; U=http://127.0.0.1:4096; J='content-type: application/json'
```

### 1. Install

```bash
curl -fsSL https://opencode.ai/install -o install.sh     # inspected first: 460 lines, INSTALL_DIR=$HOME/.opencode/bin
bash install.sh --no-modify-path                          # -> "Installing opencode version: 1.18.33"
~/.opencode/bin/opencode --version                        # 1.18.33
```

### 2. Project config for the seat workspace

`ws/seat1/opencode.json`:

```json
{
  "$schema": "https://opencode.ai/config.json",
  "model": "opencode/big-pickle",
  "autoupdate": false,
  "share": "disabled",
  "permission": { "bash": "ask", "edit": "ask", "webfetch": "deny" }
}
```

### 3. Server in tmux, event captures in extra windows

```bash
$T new-session -d -s srv -x 200 -y 50 -c $B/ws/seat1 \
  -e XDG_CONFIG_HOME=$XDG_CONFIG_HOME -e XDG_DATA_HOME=$XDG_DATA_HOME -e XDG_STATE_HOME=$XDG_STATE_HOME -e XDG_CACHE_HOME=$XDG_CACHE_HOME \
  "$OC serve --port 4096 --hostname 127.0.0.1 --print-logs --log-level INFO 2>>$B/logs/serve.log"
$T set-option -t srv remain-on-exit on
until curl -s -m 2 $U/global/health; do sleep 1; done     # {"healthy":true,"version":"1.18.33"} after ~3 s
curl -s $U/doc -o openapi.json                            # OpenAPI 3.1, 479 KB, 472 schemas
$T new-window -d -t srv -n ev   "curl -sN $U/event        > $B/logs/event.sse"
$T new-window -d -t srv -n gev  "curl -sN $U/global/event > $B/logs/global.sse"
$T new-window -d -t srv -n v2ev "curl -sN $U/api/event    > $B/logs/v2event.sse"
```

### 4. Session, prompt, permission, TUI (legacy API)

```bash
curl -s -X POST $U/session -H "$J" -d '{"title":"spike4-seat1"}'                      # -> Session JSON, id ses_…
curl -s -X POST $U/session/$SID/prompt_async -H "$J" \
  -d '{"parts":[{"type":"text","text":"Reply with exactly the word PONG and nothing else."}]}'   # 204
curl -s $U/session/status                                    # {"ses_…":{"type":"busy"}} … then {}
curl -s $U/session/$SID/message                              # [{info, parts}] per message
curl -s $U/permission                                        # pending permission requests
curl -s -X POST $U/permission/$PERM_ID/reply -H "$J" -d '{"reply":"once"}'                       # true
curl -s -X POST $U/permission/$PERM_ID/reply -H "$J" -d '{"reply":"reject","message":"Not now (aiakos spike)."}'
$T new-session -d -s tui -x 140 -y 40 -c $B/ws/seat1 -e XDG_…=… "$OC attach $U --session $SID; echo TUI_EXITED rc=\$?; sleep 600"
$T send-keys -t tui -l "Reply with exactly the word HUMAN."; $T send-keys -t tui Enter          # a human typing
```

### 5. Other probes

- Second workspace on the same server: `POST /session?directory=$B/ws/seat2` and the
  `x-opencode-directory` header.
- Session-level permission ruleset: `POST /session` with `"permission":[{"permission":"bash","pattern":"*","action":"deny"}]`.
- v2: `POST /api/session` (with and without a chosen `id`), `POST /api/session/{id}/prompt`,
  `POST /api/session/{id}/wait`, `GET /api/session/{id}/event?after=N`, `GET /api/session/{id}/history`.
- Abort while busy (`POST /session/{id}/abort`), a second `prompt_async` while busy, fork
  (`POST /session/{id}/fork`), sync prompt (`POST /session/{id}/message`).
- Server restart (resume), server stop while a TUI is attached, basic auth
  (`OPENCODE_SERVER_PASSWORD`), unknown session IDs (API and `attach`).
- `opencode session list --format json`, `opencode export <id>`, `opencode db path`, `opencode debug skill`.

## Findings

### F1: Commands

| Purpose | Command | Notes |
|---|---|---|
| Headless server | `opencode serve --port <p> --hostname 127.0.0.1 [--print-logs --log-level INFO]` | Default port is `0` (random), so always pass one. Serves the instance for its cwd, plus any other directory named per request (F4). Warns `OPENCODE_SERVER_PASSWORD is not set; server is unsecured.` |
| Attach a TUI | `opencode attach http://127.0.0.1:<p> --session <id> [--dir <path>]` | Full TUI, rendered from the server. `-p/--password` or `OPENCODE_SERVER_PASSWORD`, `-u` or `OPENCODE_SERVER_USERNAME` (default `opencode`). `--continue`, `--fork` also exist. |
| One-shot via a server | `opencode run --attach <url> -s <id> [--format json] "msg"` | Not needed by the adapter (listed in help, not tested). |
| Standalone TUI | `opencode [dir]` | Starts its own in-process server; not suitable for seats. |
| Sessions (CLI) | `opencode session list --format json`, `opencode session delete <id>`, `opencode export <id> [--sanitize]`, `opencode import <file>` | Read the same SQLite DB as the server. |
| Storage | `opencode db path` → `$XDG_DATA_HOME/opencode/opencode.db` | SQLite (WAL). Tables include `session`, `message`, `part`, `permission`, `event`, `event_sequence`, `session_input`, `credential`. |
| Providers | `opencode providers list \| login \| logout` (alias `auth`), `opencode models [provider] [--verbose]` | Credentials live in `$XDG_DATA_HOME/opencode/auth.json`. |
| Other entry points | `opencode acp` (Agent Client Protocol server), `opencode web` (server + web UI), `opencode mcp …` | Not tested. ACP may matter later for editor integration. |

Env vars worth knowing (from the binary; the ones marked * were used or verified here):
`OPENCODE_SERVER_PASSWORD`*, `OPENCODE_SERVER_USERNAME`, `OPENCODE_CONFIG` (extra config file),
`OPENCODE_CONFIG_CONTENT` (inline JSON config), `OPENCODE_CONFIG_DIR`, `OPENCODE_PERMISSION`,
`OPENCODE_DISABLE_PROJECT_CONFIG`, `OPENCODE_DISABLE_AUTOUPDATE`, `OPENCODE_DISABLE_SHARE`,
`OPENCODE_DISABLE_CLAUDE_CODE`* (and `…_PROMPT`, `…_SKILLS`), `OPENCODE_DISABLE_EXTERNAL_SKILLS`,
`OPENCODE_DISABLE_MODELS_FETCH`, `OPENCODE_AUTH_CONTENT`, `OPENCODE_DB`,
`OPENCODE_ENABLE_QUESTION_TOOL`, `OPENCODE_EXPERIMENTAL_*`. Only the starred ones were verified.

### F2: The HTTP API (two generations)

`GET /doc` returns the OpenAPI 3.1 document (`info.version` `1.0.0`, 170+ operations). Two API
generations coexist:

| | Legacy (`/session`, `/event`, `/permission`) | v2 (`/api/*`, operationIds `v2.*`) |
|---|---|---|
| Status in 1.18.33 | Works end to end; used by the TUI | Partly unfinished (below) |
| Create session | `POST /session` body `{title?, parentID?, agent?, model?{id,providerID}, permission?: PermissionRule[], metadata?, workspaceID?}` → `Session` | `POST /api/session` body `{id?, agent?, model?, location?{directory}}` → `{data: SessionV2Info}` |
| Prompt | `POST /session/{id}/prompt_async` (204) or `POST /session/{id}/message` (blocks until done; returns `{info, parts}`) body `{parts:[{type:"text",text}], model?{providerID,modelID}, agent?, system?, noReply?, tools?, messageID?, variant?, format?}` | `POST /api/session/{id}/prompt` body `{prompt:{text, files?, agents?}, delivery?: "steer"\|"queue", resume?}` → `{data:{admittedSeq, id, delivery, …}}` |
| Events | `GET /event` (per instance), `GET /global/event` (all directories) | `GET /api/event`, `GET /api/session/{id}/event?after=<seq>` (durable, replayable) |
| Status | `GET /session/status` → `{ "<sessionID>": {type: busy\|idle\|retry…} }` (idle sessions are omitted) | `GET /api/session/active` |
| Permissions | `GET /permission`, `POST /permission/{requestID}/reply` `{reply: once\|always\|reject, message?}` | `GET /api/permission/request`, `POST /api/session/{id}/permission/{requestID}/reply` |
| Control | `POST /session/{id}/abort`, `/fork`, `/summarize` (compact), `/revert`, `/shell`, `/command`, `DELETE /session/{id}` | `/interrupt`, `/compact`, `/wait`, `/revert/*` |
| Health | `GET /global/health` → `{"healthy":true,"version":"1.18.33"}` | `GET /api/health` → `{"healthy":true}` |

v2 problems observed: `POST /api/session/{id}/wait` → **503** `"Session wait is not available
yet"`. `GET /api/event` **closed by itself** after the first few events (twice, curl rc 0).
Prompts through the v2 engine (`session.next.*` events) failed with the free-tier 403 described
in F9. `GET /api/permission/request` stayed empty while legacy `GET /permission` showed the
pending request: the legacy permission system is what is live. Also, `/tui/*` endpoints can drive
an attached TUI (append/submit prompt, toast, select session), which could be useful for
"show the human what the seat is doing".

### F3: Session IDs

- Format: `ses_` + 26 characters, e.g. `ses_f127862b8ffeCIdndcEUiug5y3`. Session IDs **decrease**
  over time (newest sort first); `msg_`, `prt_`, `evt_`, `per_` IDs increase.
- The ID is in the create response and in every event (`properties.sessionID`), so the adapter
  knows it before the first prompt. Unlike Claude Code (spike 0002, F5) a session **exists and is
  persisted from creation**, before any prompt.
- **The orchestrator can choose the ID** with v2 `POST /api/session {"id": "ses_…"}`. The only
  validation is the `ses` prefix (`"aiakos-seat2"` → 400 `Expected a string starting with "ses"`).
  A v2-created session is fully usable through the legacy API.
- **Creating an existing ID is not an error**: a second `POST /api/session` with the same `id`
  returned **200 with the existing session**. The adapter cannot use "create" to detect a
  collision (rule 3); check with `GET /session/{id}` first, or treat the ID as owned.
- One hand-made ID (`ses_0aiakos…seat2x`) was rejected by the Zen free tier on every prompt, while
  an ID in OpenCode's own shape (`ses_` + 12 hex + 14 alphanumerics) worked (F9). If we choose IDs,
  generate them in OpenCode's shape.
- Unknown ID: `GET/POST /session/<id>/…` → **404** `{"name":"NotFoundError","data":{"message":"Session not found: ses_…"}}`.
  A malformed ID (`not-an-id`) → **500** `UnknownError` (validate before calling).

### F4: Directories, projects and one server for many seats

- The server serves an **instance per directory**. Requests without a directory use the server's
  cwd. `?directory=<abs path>` or the header `x-opencode-directory: <abs path>` selects another
  one: both created sessions in `ws/seat2` on the same server (`Session.directory` confirmed it).
- `GET /session/status`, `GET /permission` and `GET /event` are **per instance**. With no
  directory given they did **not** show the seat2 session. `GET /global/event` carries every
  instance, wrapping each event in `{directory, project, payload}`.
- The project for a git repo without commits is `"global"` (the project ID comes from the repo's
  root commit). Project config (`opencode.json`) is resolved per instance directory.
- Config is **read when an instance starts**. See F10 for how a stale server kept an old config.

Recommendation: **one `opencode serve` per seat** (own port, own cwd = seat workspace, own
`XDG_*`/config, own password). It keeps the per-instance endpoints simple, isolates credentials
and config per seat, and matches the seat = harness × session host × sandbox model. One server
for many seats is possible (`directory` on every call plus `/global/event`) but couples their
lifecycles.

### F5: Event stream and samples

`GET /event` is plain SSE with `data:` lines only (no `id:`/`event:` fields, so no
`Last-Event-ID` replay). It starts with `server.connected` and sends `server.heartbeat` every
**~10 s** (`/api/event` uses SSE comments `: heartbeat` instead). On (re)connect a client gets no
history; it must re-read state (`GET /session/status`, `GET /permission`,
`GET /session/{id}/message`). Each event has a unique, increasing `id` (`evt_…`).

A single turn ("Reply with exactly the word PONG") produced, in order (`plugin.added`,
`catalog.updated` and similar noise omitted):

```
session.updated        (agent/model set)
message.updated        role=user
message.part.updated   type=text  "Reply with exactly the word PONG…"
session.status         busy
message.updated        role=assistant (empty)
session.diff           []
message.part.updated   type=step-start
message.part.updated   type=text ""  → message.part.delta field=text delta="PONG" → message.part.updated text="PONG"
message.part.updated   type=step-finish reason=stop tokens{…}
message.updated        role=assistant finish=stop time.completed
session.status         idle
session.idle
session.updated        (tokens, summary)
```

Samples (trimmed; `/home/<user>` shortened to `~`):

```json
{"id":"evt_0ed8795690012LEJS5Fbtd8rsx","type":"server.connected","properties":{}}
{"id":"evt_0ed87bcb9001WKynDaBVvgOQJh","type":"server.heartbeat","properties":{}}

{"id":"evt_0ed879d4d001h0hMvtn93uRiqP","type":"session.created","properties":{"sessionID":"ses_f127862b8ffeCIdndcEUiug5y3",
  "info":{"id":"ses_f127862b8ffeCIdndcEUiug5y3","slug":"shiny-knight","version":"1.18.33","projectID":"global",
          "directory":"~/aiakos-spikes/0004/ws/seat1","title":"spike4-seat1","cost":0,
          "tokens":{"input":0,"output":0,"reasoning":0,"cache":{"read":0,"write":0}},"time":{"created":1790691482951,"updated":1790691482951}}}}

{"id":"evt_0ed87a00f001Lh4pSYLetggjqx","type":"session.status","properties":{"sessionID":"ses_f127862b8ffeCIdndcEUiug5y3","status":{"type":"busy"}}}

{"id":"evt_0ed87b830001rxbYCOjRLcYNjQ","type":"message.part.delta","properties":{"sessionID":"ses_f127…","messageID":"msg_0ed87a01f001Sl2xkFfpgiQTjO",
  "partID":"prt_0ed87b823001w1dMM8mjjBwbuO","field":"text","delta":"PONG"}}

{"id":"evt_0ed87b8e6001MhKnHU5tVkjagS","type":"message.part.updated","properties":{"sessionID":"ses_f127…","part":{"id":"prt_0ed87b8e5001V96ZVVBCFc38pF",
  "reason":"stop","snapshot":"11b0061a45faebbf9f8f7b27d5d0e669919ebaac","messageID":"msg_0ed87a01f001Sl2xkFfpgiQTjO","type":"step-finish",
  "tokens":{"total":10099,"input":8155,"output":3,"reasoning":0,"cache":{"write":0,"read":1941}},"cost":0},"time":1790691490022}}

{"id":"evt_0ed87b998001uGsJQHG4pMjb7O","type":"session.status","properties":{"sessionID":"ses_f127…","status":{"type":"idle"}}}
{"id":"evt_0ed87b998002Cfd2cP5yQOQhAf","type":"session.idle","properties":{"sessionID":"ses_f127862b8ffeCIdndcEUiug5y3"}}
```

Tool call (bash), as `message.part.updated` with `type:"tool"` and a `state.status` that goes
`pending` → `running` → `completed` or `error`:

```json
{"type":"message.part.updated","properties":{"sessionID":"ses_f127…","part":{"type":"tool","tool":"bash","callID":"call_function_z7rrb9jdt3lo_1",
  "state":{"status":"completed","input":{"command":"echo spike > created.txt"},"output":"(no output)",
           "metadata":{"output":"(no output)","exit":0,"truncated":false},"title":"echo spike > created.txt",
           "time":{"start":1790691954842,"end":1790691961434}},
  "id":"prt_0ed8ed07c0010OvJEUgHnnVZLp","messageID":"msg_0ed8eb8f60015db3KMr0tm7wjR"}}}
```

`session.error` (schema): `{sessionID?, error?: ProviderAuthError | UnknownError |
MessageOutputLengthError | MessageAbortedError | StructuredOutputError | ContextOverflowError |
ContentFilterError | APIError}`. Provider errors also land on the assistant message as
`info.error` (`{"name":"APIError","data":{"message":…,"statusCode":403,"isRetryable":false}}`).

`SessionStatus` is `{type:"idle"} | {type:"busy"} | {type:"retry", attempt, message, next, action?}`.
`busy` is re-emitted several times per turn (once per step), so treat it as a level, not an edge.

**Global and durable streams.** `/global/event` wraps each event as
`{"directory":"~/…/seat1","project":"global","payload":{…}}` and additionally carries
`{"payload":{"type":"sync","syncEvent":{"id":"evt_…","type":"session.updated.1","seq":4,"aggregateID":"ses_…","data":{…}}}}`.
The v2 streams carry the same events with `"durable":{"aggregateID":"ses_…","seq":N,"version":1}`
for events that change the stored session (created/updated, message and part updates, prompt
admitted, step started/failed). Ephemeral ones (`session.status`, `session.idle`, deltas) have no
`seq`. `GET /api/session/{id}/event?after=3` replayed exactly `seq` 4 onward: **`after` is an
exclusive per-session sequence number**, so a per-session cursor can be persisted and replayed
after a disconnect. Whether that endpoint then keeps tailing live events was not verified.

### F6: Permissions (needs-input)

With `"bash": "ask"` the flow was:

```
message.part.updated  tool=bash state=pending
message.part.updated  tool=bash state=running input.command="echo spike > created.txt"
permission.asked      (≈0.1 s later)
   … session.status stays "busy" while waiting …
permission.replied    reply=once
message.part.updated  tool=bash state=completed
… session.idle
```

```json
{"id":"evt_0ed8ed10f002Tmdd3krcmukZhh","type":"permission.asked","properties":{
  "id":"per_0ed8ed10f001D3UOEUui37UGrb","sessionID":"ses_f127862b8ffeCIdndcEUiug5y3",
  "permission":"bash","patterns":["echo spike > created.txt"],"metadata":{"command":"echo spike > created.txt"},
  "always":["echo *"],"tool":{"messageID":"msg_0ed8eb8f60015db3KMr0tm7wjR","callID":"call_function_z7rrb9jdt3lo_1"}}}

{"id":"evt_0ed8eea08001MumLE7Wf2GEgXY","type":"permission.replied","properties":{
  "sessionID":"ses_f127862b8ffeCIdndcEUiug5y3","requestID":"per_0ed8ed10f001D3UOEUui37UGrb","reply":"once"}}
```

- `GET /permission` returned the same object while pending (it is the recovery path after an SSE
  reconnect).
- `POST /permission/{id}/reply {"reply":"once"}` → `true`; the tool ran.
- `{"reply":"reject","message":"Not now (aiakos spike)."}` → the tool part became
  `status:"error"` with `error: "The user rejected permission to use this specific tool call with
  the following feedback: Not now (aiakos spike)."`. The model saw the feedback and replied
  `DENIED`; the turn ended normally (`session.idle`). A denial is visible, unlike Claude Code
  (spike 0001 gap).
- `"always"` would store the `always` patterns (`echo *`) as a saved rule. Aiakos should normally
  answer `once` and keep policy in its own records.
- Replying to an already answered request → **404**.
- Unlike Claude Code's ~6 s `Notification` delay, `permission.asked` arrives immediately.
- Permission rules can come from config (`permission` in `opencode.json`), from the agent
  (`GET /agent` shows the effective ruleset, e.g. `{"permission":"*","pattern":"*","action":"allow"}`,
  `doom_loop: ask`, `external_directory: ask`) and **per session** (`POST /session` with
  `permission: [{permission, pattern, action}]`; the ruleset is stored on the session).
  A session with `bash: ask` behaved normally. The live test of a `deny` session rule was blocked
  by the free-tier error (F9).
- **Defaults are permissive**: without config the `build` agent allows everything except
  `doom_loop` and `external_directory` (ask). The first bash test ran without asking because the
  server had been started before `opencode.json` existed (F10). Seats must pin their permission
  policy explicitly.
- A related "needs-input" source is the question tool (`question.asked`/`replied`/`rejected`,
  `GET /question`, `POST /question/{id}/reply|reject`), gated by `OPENCODE_ENABLE_QUESTION_TOOL`.
  Not exercised here.

### F7: State mapping

| Signal | Seat state | Notes |
|---|---|---|
| `server.connected` on `/event` plus `GET /global/health` 200 | readiness | No trust dialog, no TUI readiness race: a prompt can be sent as soon as the server answers. |
| `POST …/prompt_async` → 204 and then `message.updated role=user` | delivery confirmed | Carry our own `messageID` in the body if we need to correlate. |
| `session.status busy` | `working` | Re-emitted per step. |
| `permission.asked` (or a non-empty `GET /permission` for the session) | `needs-input` | Status still says `busy`; needs-input has to be derived. Clear on `permission.replied`. |
| `question.asked` | `needs-input` | Not observed. |
| `session.status idle` / `session.idle` | `idle` | The last assistant `text` part is the turn result (`GET /session/{id}/message`). |
| `session.status retry` | `working` (sub-state *retrying*) | Has `attempt`, `message`, `next`. |
| `session.error`, or assistant `info.error` | `idle` + finding | e.g. provider 403 or auth errors (`ProviderAuthError`). |
| `server.heartbeat` missing > ~30 s, SSE closed, or health fails | `unknown` | Then reconnect and re-read status. |
| Server process gone | `exited` (from session host) | The server has no "exit" event. |

### F8: TUI attach in tmux

- `opencode attach http://127.0.0.1:4096 --session <id>` in a tmux pane renders the full TUI:
  conversation, a side panel (title, context tokens, % used, cost, LSP), the agent/model line and
  the workspace path with git branch. It picked the model and agent up from the session.
- Right after attaching, the first capture (8 s) showed an empty conversation and "0 tokens"; the
  history appeared once the session had new activity. The attached TUI may not load old messages
  until something happens (not investigated further).
- **API prompts appear live in the TUI**, and so do the tool calls and results.
- **The permission dialog appears in the TUI too** (`△ Permission required` / `$ echo third > third.txt` /
  `Allow once  Allow always  Reject`). Answering over the API closed the dialog in the TUI.
  So a human watching the pane can answer as well; both paths end in the same `permission.replied`.
- **A human typing in the TUI** (`send-keys -l …; send-keys Enter`) produces the same events
  as an API prompt (`message.updated role=user`, `session.status busy`, …). The adapter sees
  human input as ordinary events. It cannot tell them apart by source (both show up as a user
  message), unless Aiakos tags its own messages with `messageID`s it generated.
- **Server stop while attached**: the TUI did not exit or show an error; it froze silently. Text
  typed during the outage was lost. When the server came back on the same port, the TUI
  **reconnected by itself** and the next typed prompt worked. The node must not rely on the TUI to
  surface server death.
- `attach --session <unknown>` → prints `Error: Session not found: ses_…` and **exits 1**.
- The TUI shows a "Getting started / Connect provider" panel on a fresh config; harmless.

### F9: Resume semantics

- Sessions, messages and parts are stored in `$XDG_DATA_HOME/opencode/opencode.db` (SQLite).
  Nothing lives in the server process only.
- **Resume = keep the session ID, start a server with the same data dir, prompt the ID again.**
  Verified: the code word `HERON-4471` was planted on one server process; after that server was
  killed (SIGTERM) and a new one started, `prompt_async` on the same ID asked *"What was the code
  word…"* and the model answered `HERON-4471`. The TUI attached to the new server showed the whole
  history. There is no separate resume command and nothing like the trust prompt or resume picker
  of Claude Code.
- A failed resume is explicit: an unknown ID returns **404** from every session endpoint, and
  `attach --session` exits 1. There is **no silent fresh start** (rule 3) as long as the adapter
  never falls back to `POST /session` on a 404.
- `POST /session/{id}/fork` → a new session (`title "spike4-seat1 (fork #1)"`, `parentID` null)
  with the history. `parentID` is only used for sub-agent child sessions.
- Abort: `POST /session/{id}/abort` → `true`. The aborted assistant message carries an error
  (`MessageAbortedError` in the schema). A second `prompt_async` while busy returned 204 as well
  (legacy API queues; v2 has an explicit `delivery: steer|queue`). The abort/queue run in this spike
  hit the free-tier error below, so the exact ordering after an abort is **not verified**.
- `opencode export <id>` → `{info, messages[]}` (20 messages for the test session) is a usable
  archival format.

**Free-tier caveat (not a design issue).** Some sessions got `HTTP 403 FreeTierError: "OpenCode's
free tier can only be used from within OpenCode"` on every prompt: the v2 engine, a session created
with the hand-made ID `ses_0aiakos…`, and a session created with a `bash: deny` rule. Legacy-created
sessions, a v2-created session without an ID, one with an OpenCode-shaped ID, and one with a
`bash: ask` rule all worked. It is deterministic per session, and our guess is that Zen checks
that requests look like the stock client (ID shape, tool list). This is unclear and does not
apply to paid providers, but it means **the free models are not a reliable test bed for API
variations**.

### F10: Process lifecycle pitfalls

- A server started as `bash -c "opencode serve … 2>&1 | tee log; echo; sleep 600"` **survived
  `tmux kill-server`** (reparented, still holding port 4096). The next `opencode serve` on that
  port then failed with only `Error: Unexpected error` / `ServeError`, and all later requests went
  to the stale server with its **old config** (no `opencode.json`, so permissions were `allow`).
  A server started directly as the pane command (with or without a pipe) exited on
  `kill-session`/`kill-server`. The node must own the server PID, stop it with SIGTERM (it exited
  within 1 s), and check that the port is free before starting.
- `pgrep -f 'opencode serve --port 4096'` also matched the **tmux server** (whose argv is the
  first `new-session` command), so a pgrep-based kill took down the whole tmux server. Use
  `#{pane_pid}` or a PID file.
- `--port 0` is the default (random port). Always pass the port the orchestrator allocated.

### F11: Auth for the server itself

With `OPENCODE_SERVER_PASSWORD` set (a random local test value):

| Request | Result |
|---|---|
| any endpoint without credentials, including `/global/health`, `/doc`, `/event` | 401 |
| `-u opencode:<wrong>` | 401 |
| `-u opencode:<pw>` | 200 |
| `-u aiakos:<pw>` (username not set on server) | 401 |
| `OPENCODE_SERVER_PASSWORD=<pw> opencode attach …` | attached and worked |

HTTP basic auth over loopback is enough for a per-seat server. The node generates the password
per seat and keeps it out of the seat's own environment if the agent should not call its own
server (it would be visible to the agent's bash tool otherwise). CORS is closed by default
(`--cors` adds origins).

### F12: Claude Code compatibility leaks

OpenCode loads Claude Code's skills from `~/.claude/skills` by default. `opencode debug skill`
listed the user's synced Claude skills (`~/.claude/skills/synced/<id>/docs/SKILL.md`, `pdf`,
`pptx`, …), and the `build` agent got `external_directory: allow` rules for those paths.
With `OPENCODE_DISABLE_CLAUDE_CODE=1` only the built-in skill remained. For seats: set
`OPENCODE_DISABLE_CLAUDE_CODE=1` (also covers `CLAUDE.md` prompts, per the flag names) and project
the seat's own skills explicitly.

## Auth options and provider terms

`GET /provider/auth` on 1.18.33 (no credentials) lists these interactive login methods:

| Provider | Methods offered by OpenCode 1.18.33 |
|---|---|
| OpenAI | OAuth "ChatGPT Pro/Plus (browser)", "ChatGPT Pro/Plus (headless)", API key |
| GitHub Copilot | OAuth device flow (GitHub.com or GitHub Enterprise URL) |
| GitLab | OAuth, Personal Access Token |
| xAI | OAuth "SuperGrok Subscription", API key |
| Poe, DigitalOcean, Snowflake Cortex | OAuth or key/PAT |
| Cloudflare Workers AI / AI Gateway, Azure | API key (+ account/resource IDs) |
| OpenCode Zen / Go | API key from `opencode.ai/auth`; the free Zen models need none |
| Anthropic | **No subscription login** (not in the list). API key via config/`providers login` remains possible. |

Credentials are stored in `$XDG_DATA_HOME/opencode/auth.json` (per seat if `XDG_DATA_HOME` is
per seat). The server also has `PUT /auth/{providerID}` and OAuth endpoints, so the orchestrator
could push an API key over the (authenticated) API instead of writing a file.
`OPENCODE_AUTH_CONTENT` also exists (not tested).

What the providers allow (checked 2026-09-29):

- **Anthropic (Claude Free/Pro/Max): not allowed.** Claude Code's
  [Legal and compliance](https://code.claude.com/docs/en/legal-and-compliance) page says OAuth
  authentication *"is intended exclusively for purchasers of Claude Free, Pro, Max, Team, and
  Enterprise subscription plans and is designed to support ordinary use of Claude Code and other
  native Anthropic applications"*, that Anthropic does not permit third parties *"to route requests
  through Free, Pro, or Max plan credentials on behalf of their users"*, and that developers building
  products (including with the Agent SDK) should use API keys. Anthropic clarified this on
  2026-02-19 ([The Register, 2026-02-20](https://www.theregister.com/2026/02/20/anthropic_clarifies_ban_third_party_claude_access/)).
  OpenCode's [provider docs](https://opencode.ai/docs/providers/) (updated 2026-09-28) say plugins
  exist for Claude Pro/Max but *"Anthropic explicitly prohibits this"*, and that OpenCode stopped
  shipping them as of 1.3.0. **For Aiakos: OpenCode seats use Anthropic models only with an API key
  (Console or a cloud provider).** The same page also says an end user may sign in to the
  *unmodified Claude Code binary* with their own subscription, even when a platform hosts it. That
  is the M1 path (spikes 0001/0002), so M1 is not affected.
- **GitHub Copilot: officially supported.** [GitHub changelog, 2026-01-16](https://github.blog/changelog/2026-01-16-github-copilot-now-supports-opencode/):
  *"All developers with paid GitHub Copilot subscriptions (Pro, Pro+, Business, or Enterprise)"*
  can authenticate OpenCode with the device flow. OpenCode itself stays under its own terms.
  Some models need Pro+. Business/Enterprise use is subject to the org's Copilot policies.
- **OpenAI (ChatGPT Plus/Pro): tolerated and publicly encouraged, not contractually promised.**
  OpenCode ships a "ChatGPT Pro/Plus" OAuth login. OpenAI staff have said publicly that ChatGPT
  accounts can be used in other tools, naming OpenCode ([Tibo Sottiaux on X](https://x.com/thsottiaux/status/2058071172361998482), 2026),
  and OpenAI's [Codex for Open Source](https://developers.openai.com/community/codex-for-oss) page
  names OpenCode among the tools developers should be free to use. We found **no clause in OpenAI's
  terms** that explicitly permits or forbids it ([analysis, 2026-07-01](https://manifest.build/blog/chatgpt-plus-tokens-third-party-harnesses/)).
  **Unclear**; acceptable for a personal local node, but re-check before recommending it to teams.
- **GitLab Duo**: OpenCode docs list it (Premium/Ultimate, marked experimental). We found no
  GitLab statement on terms; **unclear**.
- **xAI SuperGrok**: offered by OpenCode (device-code OAuth); we did not find xAI's position;
  **unclear**.
- **OpenCode Zen / Go**: OpenCode's own offering, so it is allowed by construction. Free models:
  data may be used for training during the free period (Big Pickle, MiMo), per the
  [Zen docs](https://opencode.ai/docs/zen/) (updated 2026-09-28); some free providers are
  zero-retention. Not for confidential code.

## PENDING: steps that need the maintainer's own login

Everything above ran on the free Zen model. These checks need a subscription or API key and were
**not** run. The maintainer should do the login (never an agent), in the spike's isolated config:

```bash
# in WSL
B=~/aiakos-spikes/0004
export XDG_CONFIG_HOME=$B/xdg/config XDG_DATA_HOME=$B/xdg/data XDG_STATE_HOME=$B/xdg/state XDG_CACHE_HOME=$B/xdg/cache
OC=~/.opencode/bin/opencode

# 1. Log in (choose e.g. GitHub Copilot device flow, or OpenAI "ChatGPT Pro/Plus (headless)")
$OC providers login
$OC providers list                    # expect 1 credential in $B/xdg/data/opencode/auth.json
$OC models github-copilot             # or: $OC models openai   -> pick a model id, e.g. github-copilot/<model>

# 2. Start a server and a legacy session, prompt with the paid model
tmux -L aiakos-spike4 new-session -d -s srv -c $B/ws/seat1 \
  -e XDG_CONFIG_HOME=$XDG_CONFIG_HOME -e XDG_DATA_HOME=$XDG_DATA_HOME -e XDG_STATE_HOME=$XDG_STATE_HOME -e XDG_CACHE_HOME=$XDG_CACHE_HOME \
  "$OC serve --port 4096 --hostname 127.0.0.1"
U=http://127.0.0.1:4096; J='content-type: application/json'; MODEL_PROVIDER=github-copilot; MODEL_ID=<model>
SID=$(curl -s -X POST $U/session -H "$J" -d '{"title":"pending"}' | python3 -c 'import json,sys;print(json.load(sys.stdin)["id"])')
curl -s -X POST $U/session/$SID/message -H "$J" \
  -d "{\"model\":{\"providerID\":\"$MODEL_PROVIDER\",\"modelID\":\"$MODEL_ID\"},\"parts\":[{\"type\":\"text\",\"text\":\"Reply with exactly the word PAID.\"}]}"

# 3. Re-run the free-tier-blocked checks with the paid model:
#    a) session-level deny rule: create with "permission":[{"permission":"bash","pattern":"*","action":"deny"}],
#       ask for a bash command, expect no permission.asked and a tool error
#    b) abort: prompt_async a long answer, POST /session/$SID/abort after 3 s, then a second prompt;
#       record the events (MessageAbortedError?) and the order of the queued prompt
#    c) v2 engine: POST /api/session/$SID/prompt '{"prompt":{"text":"Reply KITE"}}' and GET /api/session/$SID/event?after=0
#       (does it stream live after the replay?)
#    d) orchestrator-chosen ID: POST /api/session '{"id":"ses_<12 hex><14 alnum>"}' then legacy prompt
# 4. Stop: tmux -L aiakos-spike4 kill-server ; then `$OC providers logout` if the login should not stay
```

Also still open: whether a subscription-backed provider exposes rate-limit information (Claude
Code's statusLine has `rate_limits`; OpenCode only reports tokens and cost per step) and what
`session.status retry` looks like when a subscription limit is hit.

## Decision / follow-ups

- **ADR 0006 holds.** OpenCode is a clean API-driven harness. The M2 adapter implements
  `ISeatChannel` over HTTP + SSE; the tmux pane is only for the human view (`opencode attach`).
- **Use the legacy API** (`/session`, `/event`, `/permission`) for M2. Keep v2 (`/api/*`) behind
  the adapter interface and re-check it on upgrades; its durable per-session `seq` + `after`
  cursor is exactly what the SeatActor wants once it is finished. Pin the OpenCode version per rig
  (`autoupdate: false` / `OPENCODE_DISABLE_AUTOUPDATE`): the API is moving.
- **Seat launch** (`IHarnessAdapter.BuildLaunch` for OpenCode):
  - per seat: workspace dir, `XDG_CONFIG_HOME`/`XDG_DATA_HOME`/`XDG_STATE_HOME`/`XDG_CACHE_HOME`
    under the seat home, a projected `opencode.json` (model, `permission`, `autoupdate:false`,
    `share:"disabled"`), `OPENCODE_DISABLE_CLAUDE_CODE=1`, `OPENCODE_SERVER_PASSWORD=<per-seat secret>`;
  - `opencode serve --port <allocated> --hostname 127.0.0.1` as the pane command of a hidden tmux
    window (or a plain child process of the node agent); readiness = `/global/health` 200 +
    `server.connected`;
  - human view: a second pane `opencode attach http://127.0.0.1:<port> --session <id>`.
- **Session ID**: the orchestrator generates an OpenCode-shaped ID (`ses_` + 12 hex + 14
  alphanumerics) and persists it before creation. Create with v2 `POST /api/session {"id",…}`,
  or create with legacy `POST /session` and persist the returned ID. Either way, check
  `GET /session/{id}` first, because create is idempotent on the ID.
- **Resume** = start the seat's server with the same data dir, `GET /session/{id}` (404 → `failed`,
  never create a new one silently), then prompt. `verified` once the server returns the session
  with its messages.
- **Events**: one SSE connection per seat server to `/event`; on connect and reconnect re-read
  `GET /session/status`, `GET /permission`, and the last messages. Heartbeat gap > ~30 s →
  `unknown`. Stamp our own receive sequence, but use `evt_` IDs (increasing) and the v2 `seq` where
  available for ordering.
- **Permissions**: map `permission.asked` → `needs-input` with the request (tool, patterns,
  metadata) sent to the orchestrator; answer with `POST /permission/{id}/reply` (`once`/`reject` +
  message). Humans can also answer in the attached TUI; both converge on `permission.replied`.
- **Node lifecycle**: own the server PID, SIGTERM on stop, verify the port is free before start
  (a stale server silently serves old config), never `pgrep -f` the command line.
- **Provider policy** (plan §6): OpenCode seats may use API keys (any provider), GitHub Copilot
  subscriptions, OpenCode Zen/Go, and (with the caveat above) ChatGPT plans. **Never Claude
  Pro/Max in OpenCode.** The rig schema should make the credential kind explicit per seat so
  this can be validated.
- Follow-ups:
  - Run the [PENDING](#pending-steps-that-need-the-maintainers-own-login) checks after a login.
  - Question tool (`OPENCODE_ENABLE_QUESTION_TOOL`) payloads and whether seats should enable it.
  - Why the attached TUI showed an empty history until the next event.
  - Compaction events (`session.compacted`, `POST /session/{id}/summarize`) and context usage for
    the dashboard (tokens per step are available; the context limit comes from `GET /config/providers`).
  - `opencode acp` as an alternative protocol, if editor integration comes up.
  - Update plan §12 ("OpenCode: exact serve/attach commands, event schema, supported subscription
    logins and terms") to point here.
