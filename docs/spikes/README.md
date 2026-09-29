# Spikes

Throwaway experiments that answer a question before we commit to a design. Spike code is never
merged into `src/`; the *findings* are.

For each spike, write `NNNN-short-title.md` with:

- **Question** — what we needed to know.
- **Environment** — OS/WSL distro, tool versions (tmux, claude, opencode, docker, .NET).
- **Steps** — exact commands, so it can be reproduced.
- **Findings** — what worked, what did not, surprises; include payload samples (e.g. hook JSON).
- **Decision / follow-ups** — what this means for the design; links to ADRs, specs and issues.

| # | Spike | Issue | Result |
|---|---|---|---|
| [0001](0001-claude-wsl-tmux-hooks.md) | Claude Code in WSL tmux: paste delivery and hooks | [#1](https://github.com/aiakos-hq/aiakos/issues/1) | Works. Paste plus a separate submit is reliable, and all hooks and the statusLine were captured. Needs a readiness gate and a typed lead line before pastes. With mirrored networking, hooks reach Windows on `127.0.0.1`. |
| [0002](0002-claude-session-resume.md) | Claude Code session ID capture and resume | [#2](https://github.com/aiakos-hq/aiakos/issues/2) | Works. Chosen `--session-id`, then `--resume` after a tmux kill and after `wsl --shutdown`. A failed resume exits 1 and never silently starts fresh. Pass validated UUIDs only (anything else opens a picker). A trusted seat root is required. |
| [0003](0003-aspire-wsl-node.md) | Aspire AppHost launching a WSL node (OTLP + gRPC) | [#3](https://github.com/aiakos-hq/aiakos/issues/3) | Works. Needs `WSLENV` and an OTLP endpoint rewrite. Mirrored networking verified and required (loopback, no firewall rules). No orphans on stop. |
| [0004](0004-opencode-api.md) | OpenCode as an API-driven seat: server, events, permissions, TUI attach | [#4](https://github.com/aiakos-hq/aiakos/issues/4) | Works (1.18.33, free Zen model). Use the legacy HTTP API + `/event` SSE; `permission.asked` is needs-input and is answered over HTTP or in the attached TUI. Resume = prompt the same session ID after a server restart; unknown IDs 404. v2 `/api/*` is unfinished. Claude Pro/Max in OpenCode is prohibited by Anthropic; Copilot is officially supported. |
