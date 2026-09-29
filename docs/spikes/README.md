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
| [0003](0003-aspire-wsl-node.md) | Aspire AppHost launching a WSL node (OTLP + gRPC) | [#3](https://github.com/aiakos-hq/aiakos/issues/3) | Works; mirrored networking verified (loopback, no firewall rules); NAT needs allow rules; no orphans |
