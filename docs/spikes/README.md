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
| [0005](0005-docker-seat.md) | Docker seat: exec, API key file, hooks to host, resume after restart | [#5](https://github.com/aiakos-hq/aiakos/issues/5) | Works without a model: `docker exec -it` from tmux, hooks via `host.docker.internal`, and `SessionStart source=resume` after `docker restart`. Nonce recall with a real key is PENDING. Use `apiKeyHelper` plus a tmpfs key file (the env var triggers a blocking dialog), SIGTERM the harness before a stop (exec orphans, no `SessionEnd` on restart), and enable Docker Desktop WSL integration. |
