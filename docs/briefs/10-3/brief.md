---
id: 10-3
title: "#10 slice 3 — authenticated node link"
issue: 10
status: draft
route: impl
paths: [src/Aiakos.Orchestrator/Link/, src/Aiakos.Orchestrator/AiakosOptions.cs, src/Aiakos.Orchestrator/Program.cs, src/Aiakos.Node/Link/, src/Aiakos.Node/OrchestratorConnection.cs, src/Aiakos.Node/NodeProgram.cs, src/Aiakos.Node/NodeOptions.cs, tests/Aiakos.Orchestrator.Tests/Link/, tests/Aiakos.Node.Tests/Link/, tests/Aiakos.Node.Tests/OrchestratorConnectionTests.cs, tests/Aiakos.Node.Tests/NodeOptionsTests.cs]
date: 2026-10-04
---

# Brief: #10 slice 3 — authenticated node link

Part of #10. Self-contained. Do not read docs/specs/ or docs/adr/. Read the generated-contract
source proto, Aiakos.Contracts/Node helpers, the current OrchestratorConnection, NodeProgram,
NodeOptions, AiakosOptions, both Program files and the existing connection tests first.
Where this brief is silent, choose the simplest behavior and record the choice in the commit body.

## Goal

Replace the health-watch connection with the v1 authenticated bidirectional node link. The
orchestrator owns a link-only NodeProxyActor per connected node; the node keeps reconnecting.
No event persistence, replay algorithm, command execution, SeatActor integration or session
host detection is implemented here. Extension interfaces carry those responsibilities to 10-4
and 10-5; their empty implementations are explicitly part of this slice.

## Files to create or touch (nothing else)

| Path | What |
|---|---|
| src/Aiakos.Orchestrator/Link/*.cs | Authentication, endpoint policy, NodeLinkService, link registry, NodeProxyActor and extension interfaces |
| src/Aiakos.Orchestrator/AiakosOptions.cs | Node name, tenant and optional prehashed-token configuration |
| src/Aiakos.Orchestrator/Program.cs | Registration, endpoint validation, service mapping and server limits/keepalive |
| src/Aiakos.Node/Link/*.cs | Link options, reconnect delay and node message-source interface |
| src/Aiakos.Node/OrchestratorConnection.cs | Connect client and existing connection-state surface |
| src/Aiakos.Node/NodeProgram.cs, NodeOptions.cs | Link registrations and required token validation |
| tests/Aiakos.Orchestrator.Tests/Link/*.cs | Pure and loopback stream conformance tests |
| tests/Aiakos.Node.Tests/Link/*.cs | Deterministic delay and client conformance tests |
| tests/Aiakos.Node.Tests/OrchestratorConnectionTests.cs, NodeOptionsTests.cs | Replace health-only expectations and add missing-token expectation |

No new packages, proto changes, migration changes, warning suppressions, or Version= package
references. Node production code remains AOT-compatible and does not reference Akka, Data or
Orchestrator. Preserve existing suppressions only where they already exist. Use TimeProvider
and injected randomness for deterministic tests; production defaults are System and Random.Shared.

## Public surface (exact names)

The following types are public in the namespaces shown. Later slices and acceptance tests use
these interfaces. Actor internals and constructor wiring not listed here are implementation details.

```csharp
namespace Aiakos.Orchestrator.Link;
public sealed record NodeIdentity(string NodeId, Guid TenantId, string NodeName);
public sealed class NodeTokenRegistry
{
    public NodeTokenRegistry(IEnumerable<Aiakos.Orchestrator.NodeRegistration> nodes);
    public NodeIdentity? Authenticate(string? authorization);
}
public static class NodeLinkEndpointPolicy
{
    public static bool IsAllowed(string address);
    public static void Validate(IEnumerable<string> addresses); // throws the fixed policy error
}
public sealed class NodeLinkOptions
{
    public TimeSpan HelloTimeout { get; set; } // default 10 seconds
    public TimeSpan HeartbeatInterval { get; set; } // default 5 seconds
    public TimeSpan LivenessTimeout { get; set; } // default 15 seconds
}
public enum NodeLinkState { Connected, Unknown, Disconnected }
public interface INodeLinkApplication
{
    Task<IReadOnlyList<Aiakos.Contracts.Node.V1.ReplayFrom>> GetReplayAsync(
        NodeIdentity identity, Aiakos.Contracts.Node.V1.Hello hello, CancellationToken ct);
    Task ReceiveAsync(NodeIdentity identity, string nodeInstanceId,
        Aiakos.Contracts.Node.V1.ConnectRequest request, CancellationToken ct);
    Task StateChangedAsync(NodeIdentity identity, NodeLinkState state, CancellationToken ct);
}
public sealed class NodeLinkService : Aiakos.Contracts.Node.V1.NodeLinkService.NodeLinkServiceBase { }
public sealed class NodeProxyActor : Akka.Actor.ReceiveActor { }

namespace Aiakos.Node.Link;
public sealed class NodeReconnectDelay
{
    public NodeReconnectDelay(Func<double>? random = null);
    public TimeSpan NextDelay(Grpc.Core.StatusCode status);
    public void Reset();
}
public interface INodeLinkSource
{
    Aiakos.Contracts.Node.V1.Hello CreateHello(string nodeInstanceId);
    Aiakos.Contracts.Node.V1.Heartbeat CreateHeartbeat();
    Task WelcomeAsync(Aiakos.Contracts.Node.V1.Welcome welcome, CancellationToken ct);
    Task ReceiveAsync(Aiakos.Contracts.Node.V1.ConnectResponse response, CancellationToken ct);
}
```

NodeRegistration retains Id and Token for AppHost bootstrap compatibility; add string Name
(default empty), Guid TenantId (default 00000000-0000-0000-0000-000000000001), and string
TokenHash (default empty). Empty Name means Id. Node ID is a nonempty opaque string, not a UUID.
OrchestratorConnection retains State, StateChanged, ExecuteTask and CreateChannel; remove the
obsolete public CheckTimeout. Its constructor may change; update only allowed callers/tests.

## Rules and changes

G1. Every error, log and activity omits authorization values, tokens, message bodies, argv, environment values and projected content. Do not log RpcException detail or arbitrary exception messages from a peer. Fixed protocol status details below are exact. Never serialize NodeRegistration into logs.

R1. NodeTokenRegistry snapshots registrations into identity plus SHA-256 digest of UTF-8 Token. If TokenHash is set, decode exactly 64 hexadecimal characters instead; supplying both Token and TokenHash, neither, duplicate Id, duplicate digest, empty Id, or Guid.Empty tenant throws InvalidOperationException with exact message "Invalid node registration." Clear each input registration's Token after a successful snapshot. Do not retain plaintext in registry fields or write tokens to disk. Configuration providers/AppHost remain bootstrap inputs; this slice does not introduce a database credential table.

R2. Authenticate accepts exactly one case-sensitive "Bearer " prefix followed by a nonempty token with no whitespace. Hash it and compare digest bytes using CryptographicOperations.FixedTimeEquals. Missing, malformed or unknown authorization returns null. Name and tenant come only from the registered identity.

R3. NodeLinkEndpointPolicy.IsAllowed accepts absolute http addresses whose host is localhost (ordinal ignore-case), an IPv4 address in 127.0.0.0/8, or IPv6 ::1. Reject other schemes, wildcard hosts, hostnames requiring DNS and malformed addresses. Validate checks each address with IsAllowed and also rejects an empty address list, throwing the fixed error below. At startup, pass every configured Kestrel endpoint or ASPNETCORE_URLS address that can serve NodeLink: when GrpcPort is set, those on that port; otherwise all addresses. A rejected or indeterminate address throws InvalidOperationException("NodeLink requires an explicit loopback HTTP endpoint."). TestServer with no network listener is exempt. The gRPC mapping is restricted to GrpcPort when set; HTTP health endpoints retain their earlier behavior.

R4. NodeLinkService authenticates before reading Hello or creating a proxy. Exactly one authorization metadata entry is required. Failure ends Connect with Unauthenticated and detail "Node authentication failed." First request must be Hello within HelloTimeout; empty stream, wrong first body or timeout ends FailedPrecondition with detail "Hello must be the first node message." Cancellation of the call cancels the timeout and leaves no link registered.

R5. After authentication, check Hello.NodeName equals identity.NodeName ordinal; mismatch is PermissionDenied, "Node name does not match registration." Then NodeProtocol.Negotiate against Current; null is FailedPrecondition, "Unsupported node protocol." Then require NodeInstanceId to parse as Guid and not Guid.Empty; failure is FailedPrecondition, "Invalid node instance ID." Welcome is first response: negotiated protocol, registered NodeId, new UUID ConnectionId, configured heartbeat/liveness durations, ServerTime from TimeProvider, Limits MaxMessageBytes=4194304, MaxInflightCommands=64, EventBufferCapacity=10000, and replay from INodeLinkApplication.GetReplayAsync. Do not change negotiated minor, capabilities or inventory contents.

R6. One registry entry and one NodeProxyActor per authenticated, validated node link. Install replacement only after R5 succeeds. A new validated connection atomically replaces the old entry; old writer receives Goodbye with reason Superseded and message "Node link superseded.", then the old RPC ends Aborted with detail "Node link superseded." Old cleanup must not remove its replacement. All response writes are serialized; no concurrent gRPC WriteAsync calls. On host shutdown send Goodbye Shutdown with message "Orchestrator shutting down.", then end Unavailable with that detail. Never stop node sessions.

R7. The NodeProxyActor starts Connected after Welcome is written. Any subsequent node message resets the liveness deadline, initially LivenessTimeout after Welcome. At elapsed >= LivenessTimeout with no message, notify INodeLinkApplication.StateChangedAsync(identity, Unknown) once; keep the stream open. First later message notifies Connected once. Ended stream notifies Disconnected once unless it was superseded (the replacement owns state). A second Hello or a request with no body ends FailedPrecondition, "Unexpected node message." Heartbeat is accepted without forwarding; node Goodbye ends gracefully with OK. CommandAck and SeatEvent are forwarded unchanged to INodeLinkApplication.ReceiveAsync with authenticated identity and Hello.NodeInstanceId; no ack or persistence here. Await application callbacks before reading the next request. This notification is the seam for future seat liveness; do not mutate seat state here.

R8. NodeReconnectDelay uses full jitter: delay = floor(random * current ceiling in ticks), random in [0,1); initial ceiling 0.5 s, double each failed attempt. Cap is 300 s for Unauthenticated, PermissionDenied and FailedPrecondition; otherwise 30 s. The ceiling advances independently of error category; cap it for the current error before drawing and doubling. Reset restores 0.5 s only after accepted Welcome. Stop cancellation does not consume a delay.

R9. A node creates one UUID NodeInstanceId per OrchestratorConnection lifetime, reused across reconnects. On every attempt use a fresh channel and Connect call with metadata authorization Bearer plus NodeToken. Send source.CreateHello(instanceId) first and exactly once, preserving its protocol/name/capabilities/inventory. Await first response: anything except Welcome, a major other than 1, minor above the sent Hello minor, empty NodeId, or nonpositive heartbeat/liveness duration is treated as FailedPrecondition. Do not become Connected before a valid Welcome and completed source.WelcomeAsync; invoke Reset then. Subsequent Welcome or empty response similarly fails. Later Command/EventAck/Goodbye are passed to source.ReceiveAsync; Goodbye terminates the attempt after callback. Do not execute commands here.

R10. While connected send source.CreateHeartbeat() every Welcome.HeartbeatInterval, with SentAt from TimeProvider, serialized through the same single writer as Hello and shutdown. Cancel/await that timer on every stream end so it cannot write into a later connection. Shutdown sends one Goodbye Shutdown with message "Node shutting down." if the stream is connected, completes request stream, and ends without retry; broken-stream writes may fail without delaying shutdown. Each other failure enters Unavailable and retries using NodeReconnectDelay until host cancellation. Preserve StateChanged, Connected metric and reconnect counter behavior (counter after each successful reconnect). Auth/precondition failures log Error on every failure; other failures log Warning, using only status code and chosen retry delay.

R11. Default INodeLinkSource builds Hello with NodeProtocol.Current, NodeName=NodeOptions.NodeId, supplied instance ID, assembly version as AgentVersion, platform from runtime, empty capabilities and seats, and Limits 4194304/64/10000. Heartbeat has BufferedEvents=0 and InflightCommands=0. Its callbacks complete without action. Default INodeLinkApplication returns empty replay and completes callbacks without action. Register both as replaceable singleton interfaces. No tmux availability or harness capability is claimed until those consumers are integrated. Require nonblank NodeToken via source-generated NodeOptions validation with exact error "AIAKOS_NODE_TOKEN is required." Retain existing NodeId/Home/URL checks and lock/shutdown exit behavior.

R12. Register service/registry/options/proxy support in orchestrator Program and the source/reconnect/time dependencies in NodeProgram; replace the health-watch hosted connection. NodeLinkService remains unavailable until migrations succeeded as with other mapped endpoints. Set server and client gRPC receive/send max message sizes to 4194304; server Http2 KeepAlivePingDelay=30 s, KeepAlivePingTimeout=10 s; preserve matching existing client ping settings. Trace each sent envelope from Activity.Current (empty fields when absent); start a per-received-message activity with a valid incoming W3C parent, otherwise a new root. Do not fail a stream on invalid trace text. Do not reuse the Connect call parent as a substitute for an envelope parent.

C1. The earlier spec-0001 connection tests now host NodeLinkService-compatible loopback fake Connect streams instead of Health.Check/Watch. A serving health endpoint alone never makes the node Connected. The unavailable/reconnect assertions remain; update constructor injection to NodeReconnectDelay/INodeLinkSource. Backoff.cs and its tests are unchanged and are no longer used by the production connection. The token-test changes are C2; other existing results do not change.

C2. Replace NodeOptionsTests.NodeTokenIsNotRequired with NodeTokenIsRequired, expecting validation failure with exactly "AIAKOS_NODE_TOKEN is required." Add missing/blank NodeToken to MissingRequiredVariableExits2AndNamesIt. Give AbsoluteHttpUrlsAreValid a nonblank token so it still isolates URL validation. No other URL/home/identity validation result changes.

## Expected outputs: exact text

| ID | Input | Expected |
|---|---|---|
| `E1` | one valid registration Id=n, Name=machine, default tenant, bootstrap Token; snapshot then authenticate | identity n / 00000000-0000-0000-0000-000000000001 / machine; input Token empty; malformed registry cases in R1 throw "Invalid node registration." |
| `E2` | null; empty; Basic x; Bearer with empty value; bearer x; Bearer x with whitespace; unknown digest; registered token | null for first seven; exact registered identity for last; capture logs/errors contains no supplied token |
| `E3` | http://127.0.0.1:5180; http://127.2.3.4:5180; http://localhost:5180; http://[::1]:5180; http://0.0.0.0:5180; http://example.test:5180; https://127.0.0.1:5180 | true,true,true,true,false,false,false; policy Validate with a non-loopback or empty list throws "NodeLink requires an explicit loopback HTTP endpoint." |
| `E4` | Connect missing/duplicate/malformed/unknown authorization; valid authorization with silent client at 10 seconds, wrong first body or EOF | Unauthenticated / "Node authentication failed." for auth cases; FailedPrecondition / "Hello must be the first node message." for first-message cases; no proxy and no secret in captured output |
| `E5` | valid Hello protocol 1.3/name machine/UUID; wrong name; protocol 2.0; malformed/empty UUID | Welcome protocol 1.0 with registered identity, UUID connection ID, 5 s/15 s and exact limits; respective failures and exact details from R5; callback replay preserved |
| `E6` | two valid simultaneous streams of node n; invalid second Hello; old stream finally cleans up; host stop | old receives Superseded then Aborted; replacement remains live; invalid second does not replace first; shutdown receives Shutdown then Unavailable, exact messages in R6 |
| `E7` | fake clock: Welcome at t0, Heartbeat t4, no messages through t19, Heartbeat t20, EOF; second Hello; unset body; application blocks one event | states Connected,Unknown at t19,Connected at t20,Disconnected on EOF; invalid bodies FailedPrecondition / "Unexpected node message."; no next request consumed until application completes; forwarded request and identity unchanged; zero EventAck |
| `E8` | random always 0.5: five Unavailable failures; enough failures to cap; auth failures to cap; reset; random 0 | 250,500,1000,2000,4000 milliseconds; normal capped draw 15 seconds; auth capped draw 150 seconds; reset draw 250 ms; random 0 draw zero |
| `E9` | fake server observes two reconnects; first sends Command instead of Welcome; valid Welcome; duplicate Welcome; invalid durations/major/minor/node ID | first outbound Hello with bearer metadata each time, same UUID instance; invalid cases Unavailable and retry; valid Welcome calls source before Connected; no supplied token/body in logs |
| `E10` | Welcome 5 s; advance test clock to 5/10 s, disconnect/reconnect, stop; three auth errors then success | one heartbeat per interval, no old timer writes; one shutdown Goodbye and no retry on stop; auth errors each logged Error; successful reconnect counter increases once; source receives later envelopes unchanged |
| `E11` | default source/app; NodeProgram with missing/blank NodeToken | Hello 1.0 with matching NodeId name, no capabilities/seats and exact limits; empty replay and zero heartbeat counts; missing token validation "AIAKOS_NODE_TOKEN is required." and RunAsync exit 2 |
| `E12` | real loopback Connect via registered production service/client; incoming envelopes with two distinct valid parents and invalid trace; channel/server options | link handshake works; per-message activities use respective parents, invalid trace is root; all max sizes 4194304 and both ping settings 30 s/10 s |
| `E13` | only healthy Health service; fake NodeLink restart; unchanged BackoffTests and prior NodeOptions cases | health alone leaves Unavailable; NodeLink success Connected, shutdown Unavailable, restart Connected; earlier tests green except explicitly replaced health connection expectations |

| `E14` | absent/blank token in NodeOptions validation and NodeProgram; valid absolute URL with token | validation fails with "AIAKOS_NODE_TOKEN is required."; RunAsync exit 2 before lock/home creation; URL cases still succeed |

## Tests

Use the existing sentence-style xUnit v3 tests and TestContext.Current.CancellationToken.
All timing tests use injected TimeProvider; no multi-minute sleeps. Stream conformance tests
use real loopback HTTP/2 Kestrel and generated clients. Authentication/policy/actor tests may
use a slim test host without migrations; test production registration once using the existing
OrchestratorFactory/database fixture. Register test doubles for the two extension interfaces.

## Definition of done

- Story acceptance tests pass, prior tests stay green, dotnet build -c Release has zero warnings/errors.
- dotnet test --project tests/Aiakos.Node.Tests -c Release and dotnet test --project tests/Aiakos.Orchestrator.Tests -c Release pass (database tests require Docker).
- LF, UTF-8 without BOM, final newline. One local commit with subject feat(link): <story title> (#10).
- Commit body records silent choices and says: "Risks: checks 0002-RK2; 0002-RK1, 0006-RK6 and 0006-RK9 remain for event and SeatActor integration."
- Do not push or open a PR. Use tools/story.sh for the gate and retry limit.

## Out of scope (do not implement, do not stub)

Event buffer/overflow, EventAck generation, replay persistence, database event schema, assigned-seat
security findings, command dispatch/execution/dedupe, token minting/rotation, tmux probing,
harness drivers, launch registries, seat liveness mutations, migrations, AppHost changes, remote
TLS, proto changes, docs changes by implementers. Empty extension defaults specified in R11
are the only placeholders requested. 10-4 extends the application/source interfaces for events;
10-5 adds commands. An unimplemented command is not executed by this link slice.
