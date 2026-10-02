---
id: 10-1
title: "#10 slice 1 — node link proto and buf checks"
issue: 10
status: implemented
route: impl/opencode
date: 2026-10-01
---

# Brief: #10 slice 1 — node link proto and buf checks

This brief is self-contained. **Do not read `docs/specs/` or `docs/adr/`**, with one exception:
the fenced `proto` block in `docs/specs/0002-orchestrator-node-grpc-contract.md`, lines 324–761
(from `// aiakos/node/v1/node_link.proto` to the closing `}` of `enum ErrorCode`). Read only those
lines. Where the brief is silent, pick the simplest behaviour and say so in the commit body.

## Goal

The orchestrator ↔ node contract exists as one proto3 file, C# is generated from it by the normal
`dotnet build`, and CI lints it and rejects breaking changes with `buf`. No service or client is
implemented: this slice adds the contract and its guards only.

## Files to create or touch (nothing else)

| Path | What |
|---|---|
| `src/Aiakos.Contracts/Protos/aiakos/node/v1/node_link.proto` | The spec's proto block, **byte for byte** (comments, blank lines, field numbers, order). LF, final newline |
| `buf.yaml` (repo root) | Exactly the content under "buf configuration" |
| `.github/workflows/ci.yml` | New job `proto` (below). Do not change the `build-test` job |
| `tests/Aiakos.Contracts.Tests/Aiakos.Contracts.Tests.csproj` | Only a `ProjectReference` to `..\..\src\Aiakos.Contracts\Aiakos.Contracts.csproj`. xUnit v3 and `using Xunit` come from `tests/Directory.Build.props` |
| `tests/Aiakos.Contracts.Tests/NodeLinkProtoTests.cs` | The tests below |
| `Aiakos.slnx` | Add the test project to `/tests/`, alphabetical |
| `src/Aiakos.Contracts/Protos/README.md` | Replace the body: what the file is, the pinned buf version, the two local commands |
| `CLAUDE.md` → "Commands" | One line: `- Check the proto (needs buf <version>): `buf lint` and `buf breaking --against '.git#branch=main'`` |

`src/Aiakos.Contracts/Aiakos.Contracts.csproj` already has
`<Protobuf Include="Protos/**/*.proto" ProtoRoot="Protos" GrpcServices="Both" />`; do not change
it. `Aiakos.Orchestrator` and `Aiakos.Node` already reference the project; check that, change
nothing there. No `Version=` on `PackageReference`, no `NoWarn`, no `#pragma warning disable`,
no `[SuppressMessage]`.

## Generated surface (what the tests compile against)

Namespace `Aiakos.Contracts.Node.V1` (from `option csharp_namespace`):

```csharp
NodeLinkReflection.Descriptor                 // FileDescriptor
NodeLinkService.NodeLinkServiceBase           // server stub:  Connect(IAsyncStreamReader<ConnectRequest>, IServerStreamWriter<ConnectResponse>, ServerCallContext)
NodeLinkService.NodeLinkServiceClient         // client stub
ConnectRequest, ConnectResponse, Hello, Welcome, Command, SeatEvent, Error, ErrorCode, …
```

## buf configuration

```yaml
version: v2
modules:
  - path: src/Aiakos.Contracts/Protos
lint:
  use:
    - STANDARD
breaking:
  use:
    - FILE
```

## CI job

```yaml
  proto:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
      - uses: bufbuild/buf-action@v1
        with:
          version: <pinned>
          setup_only: true
      - name: Lint
        run: buf lint
      - name: Breaking changes against main
        if: github.event_name == 'pull_request'
        run: |
          git fetch --no-tags --depth=1 origin +refs/heads/main:refs/heads/main
          if git cat-file -e main:buf.yaml 2>/dev/null; then
            buf breaking --against '.git#branch=main'
          else
            echo "::notice::main has no buf.yaml yet; buf breaking skipped"
          fi
```

## Rules

1. **Verbatim proto.** Do not rename, renumber, reorder, reformat or "improve" anything in the
   proto, and do not run `buf format`. `diff` between the spec block and the file must be empty.
2. **One file, one RPC.** Package `aiakos.node.v1`; the only service is `NodeLinkService` with the
   only method `Connect` (bidirectional stream). No second `.proto` file.
3. **buf version.** Replace `<pinned>` with the exact latest stable buf release (`1.x.y`, never
   `latest` or a range). Use the same version in the README and in `CLAUDE.md`.
4. **Lint must pass as configured.** If `buf lint` reports anything, do not add `except`,
   `ignore` or comment directives and do not edit the proto: stop, and put the full output in the
   commit body.
5. **Breaking check.** It runs on pull requests only, against the local branch `main` created by
   the fetch. The guard skips it while `main` has no `buf.yaml` (this PR). If the shallow fetch
   does not work with `buf breaking`, use `fetch-depth: 0` on the checkout instead and say so.
6. **No generated code is committed.** C# comes from `Grpc.Tools` at build time (`obj/`).
7. **No build warnings from generated code.** If the Release build warns in a generated file,
   stop and report the warning text; do not suppress it.
8. **Nothing consumes the contract yet.** No service implementation, no client call, no change
   under `src/Aiakos.Orchestrator` or `src/Aiakos.Node`.

## Expected outputs

- `buf lint` at the repo root: no output, exit code 0.
- `buf build` at the repo root: exit code 0.
- Breaking-change demonstration (put the command and its output in the commit body, then revert):
  commit the proto on your branch, change `string node_name = 2;` in `Hello` to
  `string node_name = 9;` in the working tree, and run
  `buf breaking --against '.git#branch=<your branch>'`. Expected: non-zero exit, with a line that
  names `node_link.proto` and field `2` of message `Hello`.

## Tests (`tests/Aiakos.Contracts.Tests/NodeLinkProtoTests.cs`)

All tests carry `[Trait("Category", "Contract")]`. No fixtures, no network.

1. `NodeLinkReflection.Descriptor.Package` is `aiakos.node.v1` and `.Name` is
   `aiakos/node/v1/node_link.proto`.
2. The file has exactly one service, `NodeLinkService`, with exactly one method, `Connect`:
   client streaming and server streaming, input `ConnectRequest`, output `ConnectResponse`.
3. Every enum in the file (walk `Descriptor.EnumTypes`) has a value numbered 0 whose name ends
   with `_UNSPECIFIED`.
4. `ErrorCode` mirrors gRPC: for every value except `Unspecified`, `(int)value` equals the
   `Grpc.Core.StatusCode` member with the same name (`InvalidArgument` = 3 … `Unavailable` = 14).
5. Envelope `oneof` numbers: `ConnectRequest.body` is exactly
   `hello=10, heartbeat=11, command_ack=12, seat_event=13, goodbye=14`; `ConnectResponse.body` is
   exactly `welcome=10, command=11, event_ack=12, goodbye=13`; `Command.body` is exactly
   `start_seat=10, deliver_input=11, send_keys=12, capture_pane=13, stop_seat=14`.
6. Presence: a new `ProcessExited` has `HasExitCode == false` and `HasSignal == false`; after
   `ExitCode = 0`, `HasExitCode == true`. Same for `Usage.ContextUsedPercent`.
7. Round trip: a `ConnectRequest` with `Trace` and a `SeatEvent` holding a `HarnessEvent` (every
   scalar field set, one attribute, `Raw` of 3 bytes) equals itself after `ToByteArray()` and
   `Parser.ParseFrom`.
8. Both stubs exist: `typeof(NodeLinkService.NodeLinkServiceBase)` is abstract, and
   `typeof(NodeLinkService.NodeLinkServiceClient)` has a method `Connect`.

Style: file-scoped namespace `Aiakos.Contracts.Tests`, `public sealed class`, sentence-style
method names (`HasExactlyOneBidirectionalRpc`), as in
`tests/Aiakos.Hosting.Wsl.Tests/WslConfigParserTests.cs`.

## Definition of done

- `dotnet build -c Release` at the repo root: 0 warnings, 0 errors.
- `dotnet test --project tests/Aiakos.Contracts.Tests -c Release`: all green. (Other test projects
  need Docker and WSL; you do not need to run them, and must not change them.)
- `buf lint` and `buf build` pass locally with the pinned version.
- All files LF, UTF-8 without BOM, final newline.
- One commit on the current branch, subject
  `feat(contracts): node link proto v1 and buf checks (#10)`. The body has: the buf version, the
  breaking-change demonstration, and the sentence "Risks: this slice checks none of #10's open
  risks (0002-RK1, 0002-RK2, 0006-RK6, 0006-RK9)." Do not push, do not open a PR.

## Out of scope (do not implement, do not stub)

`NodeLinkService` implementation or client; authentication and tokens; handshake, heartbeat,
reconnect; event buffer, acks, replay; command execution; capability-name constants; validators
(paths, sizes, key allowlist, placeholders); forward-compatibility tests with unknown fields;
database changes; buf code generation or the Buf Schema Registry; changes to `docs/`.
