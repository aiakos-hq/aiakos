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
| 0001 | [Claude Code in WSL tmux — paste delivery and hooks](0001-claude-wsl-tmux-hooks.md) | #1 | Works: paste + separate submit reliable; all hooks and statusLine captured. Needs readiness gate, typed lead line before pastes, and an in-WSL relay (NAT mode blocks WSL → host). |
