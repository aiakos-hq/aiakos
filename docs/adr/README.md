# Architecture Decision Records

Decisions that are already made. Do not reopen them in code review or implementation; write a new
ADR that supersedes the old one instead. Template: [`TEMPLATE.md`](TEMPLATE.md).

| # | Decision | Status |
|---|---|---|
| [0001](0001-record-architecture-decisions.md) | Record architecture decisions | accepted |
| [0002](0002-dotnet-and-akka-for-live-entities.md) | .NET 10, with Akka.NET for live entities only | accepted |
| [0003](0003-postgres-dbup-dapper.md) | Postgres with DbUp and Dapper; `tenant_id` from day one | accepted |
| [0004](0004-orchestrator-node-split.md) | Orchestrator and node agent split; nodes dial out over gRPC | accepted |
| [0005](0005-tmux-first-session-host.md) | tmux as the first session host; terminals are transport | accepted |
| [0006](0006-claude-code-first-harness.md) | Claude Code as the first harness; OpenCode second | accepted |
| [0007](0007-shareable-rig-files.md) | Shareable rig files separate from local bindings | accepted |
| [0008](0008-bootstrap-rule.md) | The bootstrap rule: the team runs on the last release | accepted |
| [0009](0009-work-management.md) | Tickets in GitHub Issues; specs and ADRs in the repository | accepted |
| [0010](0010-apache-2-license.md) | Apache License 2.0 | accepted |
| [0011](0011-aspire-for-local-development.md) | Aspire for local development and telemetry | accepted |
