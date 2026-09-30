---
id: 0021
title: buf for proto lint and breaking checks; Grpc.Tools for C# codegen
status: accepted
date: 2026-09-30
---

# 0021 — buf for proto lint and breaking checks; Grpc.Tools for C# codegen

## Context
The node contract must stay backwards compatible within a major version (ADR 0020), and that
rule has to be enforced by CI, not by reviewers noticing a renumbered field. The .NET SDK
generates C# from `.proto` files but has no lint or breaking-change check. From Q8 in
[spec 0002](../specs/0002-orchestrator-node-grpc-contract.md).

## Decision
- The **`buf` CLI** lints the proto files (`buf lint`) and checks compatibility against `main`
  (`buf breaking --against '.git#branch=main'`) in CI. A PR that breaks `v1` fails the build.
- **`Grpc.Tools`** (MSBuild integration) generates the C# code for the orchestrator and the node
  from the same proto files, as part of the normal `dotnet build`.
- buf is used only for checks, not for code generation or the Buf Schema Registry.

## Consequences
Contract breaks are caught mechanically before review. The build adds one non-.NET tool: CI
installs a pinned `buf` version, and contributors who edit proto files install it locally to run
the checks (the .NET build itself does not need it). buf's naming rules shape the proto (e.g.
`ConnectRequest`/`ConnectResponse`).

## Alternatives considered
- **Grpc.Tools only**: no lint and no breaking-change detection.
- **buf for code generation as well**: a second codegen path next to MSBuild, with plugins to
  manage, for no benefit in a single-language solution.
- **A hand-written compatibility test**: reimplements buf's rules, incompletely.
