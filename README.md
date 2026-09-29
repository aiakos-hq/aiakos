# Aiakos

**A file-defined control plane for teams of AI coding agents.**

Define your agent team in files you can share — roles, skills, culture, who hands work to whom —
and Aiakos brings it to life: it launches the agents (Claude Code first, then OpenCode and
Codex), keeps them addressable and observable, routes messages and work between them, and keeps
you in the loop from the terminal, a dashboard, or chat.

> Named after **Aiakos (Αιακός)**, the king of Aegina for whom Zeus turned the island's ants into
> the **Myrmidons** — a disciplined, loyal crew.

## Status

🚧 **Pre-alpha — nothing to install yet.** The project is in *stage A*: the design is written
down and the first spikes are being planned. From milestone M1 on, Aiakos is built by an
Aiakos-managed team of agents (it builds itself).

- Plan and architecture: [`docs/plan.md`](docs/plan.md)
- Decisions: [`docs/adr/`](docs/adr/)
- Specs: [`docs/specs/`](docs/specs/)
- Roadmap: [milestones](https://github.com/aiakos-hq/aiakos/milestones)
- Website: [aiakos.dev](https://aiakos.dev)

## Planned shape

- **Orchestrator** (.NET 10, ASP.NET Core, Akka.NET, Postgres) — rigs, seats, identity, work
  queue, routing, humans, history.
- **Node agent** — runs on each machine; owns terminal sessions (tmux), sandboxes (Docker, later
  brig) and local probes.
- **CLI** (`aiakos`), **dashboard** (Blazor), **MCP server** for agents, **Telegram/Slack**.
- **Local mode** (your subscriptions, WSL) and **sandboxed mode** (API keys, Linux box).

## Contributing

See [CONTRIBUTING](https://github.com/aiakos-hq/.github/blob/main/CONTRIBUTING.md). Work is
tracked in [issues](https://github.com/aiakos-hq/aiakos/issues); every non-trivial change has
a spec in `docs/specs/`.

## License

[Apache License 2.0](LICENSE)
