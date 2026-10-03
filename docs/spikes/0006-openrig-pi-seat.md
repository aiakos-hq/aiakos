# Spike 0006: a Pi seat on OpenCode Go inside an OpenRig rig

Issue: none yet · Related: [workflow](../workflow.md) · Feeds: the delivery rig
(`rigs/aiakos-delivery/`, seat `qa`) · Date: 2026-10-03

**Result:** works. OpenRig 0.6.4 launches the Pi coding agent as a managed seat. With an
OpenCode Go key the seat took a QA task from the OpenRig queue, ran the code under test, found
the planted defect, wrote a report and closed the queue item, in under one minute. The seat's
credentials survive `rig down` and `rig up`.

Conditions:

1. **The key must be set per seat.** OpenRig gives every Pi seat its own config folder and
   passes almost nothing of the environment through (F2). A login made elsewhere is not used.
2. **`runtime: pi` is not in OpenRig's rig spec reference.** It is in the code and validates,
   so it may change between versions. Pin the OpenRig version.
3. **A queue write from outside a seat needs an identity in the environment** (F4).

## Question

Can a Pi seat that uses the OpenCode Go subscription work as the QA role of an OpenRig rig, so
that the gate is run by a third vendor and the seat can serve as a spare implementer?

## Environment

Windows 11, WSL2 `Ubuntu`. `@openrig/cli` 0.6.4, `@earendil-works/pi-coding-agent` 1.0.1 (the
package OpenRig's preflight names), tmux, Node. Spike files: `~/aiakos-spikes/0006-pi-seat/` in
WSL (not in this repository).

## Steps

```yaml
# rig.yaml
version: "0.2"
name: pi-spike
pods:
  - id: qa
    label: QA
    members:
      - id: check
        agent_ref: "path:<openrig install>/daemon/specs/agents/development/qa"
        profile: default
        runtime: pi
        model: opencode-go/gpt-6-luna
        cwd: "work"
    edges: []
edges: []
```

```bash
rig spec validate rig.yaml
rig up ./rig.yaml --yes

# Key: one file in the home folder, referenced from the seat's own auth.json.
#   ~/.config/opencode-go/key                               (mode 600, written by the maintainer)
#   ~/.openrig/state/pi/qa-check@pi-spike/agent/auth.json:
#   { "opencode-go": { "type": "api_key", "key": "!cat ~/.config/opencode-go/key" } }

OPENRIG_SESSION_NAME=operator-human@kernel rig queue create \
  --destination qa-check@pi-spike --summary "QA leap.sh against claim.md" --body "…"
```

The task: `work/leap.sh` treats every year divisible by 4 as a leap year; `work/claim.md` says
it follows the Gregorian rules. The seat was told to test without editing.

## Findings

### F1: Pi is a real runtime in OpenRig 0.6.4

`rig spec validate` accepts `runtime: pi`. The daemon has a Pi adapter that runs
`pi --mode rpc` behind a runner in a tmux pane and reads the runner's own markers
(`[pi-runner] READY`, `ERROR`, `EXITED`), not the Pi screen. The seat received the QA role
guidance at start. `model:` takes Pi's `provider/id` form.

### F2: Each Pi seat is isolated, and the key does not arrive by itself

The seat's Pi config is `~/.openrig/state/pi/<seat>/agent/` (`PI_CODING_AGENT_DIR`), not
`~/.pi/agent`. The child environment is an allowlist: baseline variables, the OpenRig identity
variables, and the API key of the model's provider for three providers only (OpenRouter, ZAI,
Kimi). `OPENCODE_API_KEY` is not forwarded. Without a key the seat reports
`[pi-runner] ERROR rpc: No API key found for opencode-go` and stays up.

The seat's `auth.json` accepts a `!command` as the key. Pointing it at one key file keeps the
secret in a single place outside OpenRig and outside the repository. Pi read the new
`auth.json` on the next request; no relaunch was needed.

### F3: The seat does QA work through the queue

After the queue item was created, the seat went `working`, read the claim, ran the script with
14 inputs, wrote `report.md` (FAIL; five century years listed with expected and actual output;
what it did not check) and closed the item (`state: done`). Elapsed: 54 seconds.

### F4: Queue identity comes from the environment

`rig queue create` outside a managed session fails with
`--source is required when OPENRIG_SESSION_NAME is not set`; the `--source` option itself is
ignored. Setting `OPENRIG_SESSION_NAME` to a seat (here the human seat of the kernel rig) works.
This is the same principle as rule 2 in `CLAUDE.md`.

### F5: Credentials survive a restart

The seat folder is named after the session (`qa-check@pi-spike`). `rig down` followed by
`rig up` created a new rig ID, archived the old one and reused the folder; `auth.json` was
unchanged and the seat started without an error.

### F6: The whole Go catalogue is available

`pi --list-models opencode-go` with the seat's config lists 29 models, among them
`gpt-6-luna`, `kimi-k2.7-code`, `kimi-k3`, `deepseek-v4-pro`, `glm-5.3`, `minimax-m3` and
`qwen3.8-max`. Changing the seat's model is one line in `rig.yaml`.

## Not checked

- Cost and Go quota use of the run (`rig usage` was not read).
- Resume of a Pi session with its conversation after a restart.
- A symlink from the seat's `auth.json` to `~/.pi/agent/auth.json`, which would allow one
  `/login` in Pi for all seats.
- A permission policy on the seat (`launch_posture=floor` was reported as a warning).
- Pi as an implementer on a real story with `dotnet build` and `dotnet test`.

## Decision / follow-ups

- Use Pi on OpenCode Go for the `qa` seat of the delivery rig.
- `SETUP.md` of the rig describes the key file, and a setup script writes the `auth.json`
  pointer for every Pi seat.
- Pin `@openrig/cli` and `@earendil-works/pi-coding-agent` versions in `SETUP.md`.
