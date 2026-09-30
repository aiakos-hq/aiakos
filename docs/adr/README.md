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
| [0012](0012-tenant-keys.md) | Tenant keys are UUIDs with a unique slug; fixed default tenant | accepted |
| [0013](0013-rig-file-header-and-compatibility.md) | Rig file header, strict fields and additive-only v1 | accepted |
| [0014](0014-flat-seat-addresses.md) | Flat, rig-unique seat addresses; pods are grouping only | accepted |
| [0015](0015-projection-outside-checkouts.md) | Guidance and skills are projected outside repository checkouts | accepted |
| [0016](0016-secrets-as-node-file-references.md) | Secrets are node-local file references, delivered as files | accepted |
| [0017](0017-resolved-rig-and-hashes.md) | Self-contained resolved rig with separate spec and binding hashes | accepted |
| [0018](0018-harness-adapter-split.md) | Harness adapter split into orchestrator adapter and node driver | accepted |
| [0019](0019-node-link-delivery-model.md) | Node link delivery model | accepted |
| [0020](0020-node-contract-versioning.md) | Node contract versioning: package major, negotiated minor, capabilities | accepted |
| [0021](0021-buf-for-proto-tooling.md) | buf for proto lint and breaking checks; Grpc.Tools for C# codegen | accepted |
