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

R3. NodeLinkEndpointPolicy.IsAllowed accepts absolute http addresses whose host is localhost (ordinal ignore-case), an IPv4 address in 127.0.0.0/8, or IPv6 ::1. Reject other schemes, wildcard hosts, hostnames requiring DNS and malformed addresses. Validate checks each address with IsAllowed and also rejects an empty address list, throwing the fixed error below. Before Kestrel binds any listener, read endpoint URLs from Kestrel:Endpoints:*:Url plus the host's configured server URLs (the urls configuration key / ASPNETCORE_URLS). Validate all addresses that can serve NodeLink: when GrpcPort is set, those on that port; otherwise all addresses. Also check each resolved endpoint eligible to serve NodeLink (same GrpcPort filter) in ConfigureEndpointDefaults before it binds, using IPAddress.IsLoopback; reject non-IP/indeterminate endpoints. No eligible configured address is an indeterminate configuration and is rejected. Do not wait until after the port is listening. A TestServer host (IServer implementation Microsoft.AspNetCore.TestHost.TestServer) skips listener policy because it has no network listener. A rejected or indeterminate address throws InvalidOperationException("NodeLink requires an explicit loopback HTTP endpoint."). The gRPC mapping is restricted to GrpcPort when set; HTTP health endpoints retain their earlier behavior.

R4. NodeLinkService authenticates before reading Hello or creating a proxy. Exactly one authorization metadata entry is required. Failure ends Connect with Unauthenticated and detail "Node authentication failed." First request must be Hello within HelloTimeout; empty stream, wrong first body or timeout ends FailedPrecondition with detail "Hello must be the first node message." Cancellation of the call cancels the timeout and leaves no link registered.

R5. After authentication, check Hello.NodeName equals identity.NodeName ordinal; mismatch is PermissionDenied, "Node name does not match registration." Then NodeProtocol.Negotiate against Current; null is FailedPrecondition, "Unsupported node protocol." Then require NodeInstanceId to parse as Guid and not Guid.Empty; failure is FailedPrecondition, "Invalid node instance ID." Welcome is first response: negotiated protocol, registered NodeId, new UUID ConnectionId, configured heartbeat/liveness durations, ServerTime from TimeProvider, Limits MaxMessageBytes=4194304, MaxInflightCommands=64, EventBufferCapacity=10000, and replay from INodeLinkApplication.GetReplayAsync. Do not change negotiated minor, capabilities or inventory contents.

R6. One registry entry and one NodeProxyActor per authenticated, validated node link. Install replacement only after R5 succeeds. A new validated connection atomically replaces the old entry; old writer receives Goodbye with reason Superseded and message "Node link superseded.", then the old RPC ends Aborted with detail "Node link superseded." Old cleanup must not remove its replacement. All response writes are serialized; no concurrent gRPC WriteAsync calls. On host shutdown send Goodbye Shutdown with message "Orchestrator shutting down.", then end Unavailable with that detail. Never stop node sessions.

R7. The NodeProxyActor starts Connected after Welcome is written. Any subsequent node message resets the liveness deadline, initially LivenessTimeout after Welcome. At elapsed >= LivenessTimeout with no message, notify INodeLinkApplication.StateChangedAsync(identity, Unknown) once; keep the stream open. First later message notifies Connected once. Ended stream notifies Disconnected once unless it was superseded (the replacement owns state). A second Hello or a request with no body ends FailedPrecondition, "Unexpected node message." Heartbeat is accepted without forwarding; node Goodbye ends gracefully with OK. CommandAck and SeatEvent are forwarded unchanged to INodeLinkApplication.ReceiveAsync with authenticated identity and Hello.NodeInstanceId; no ack or persistence here. Await application callbacks before reading the next request. While ReceiveAsync is blocked, stop reading requests but continue processing liveness deadlines, replacement and shutdown; StateChangedAsync may run concurrently with that ReceiveAsync. Deliver state notifications in their state-transition order, serialized with other state notifications. Supersession cancels the old callback token and closes the old stream without awaiting that callback. Application callbacks must honor cancellation. This notification is the seam for future seat liveness; do not mutate seat state here.

R8. NodeReconnectDelay uses full jitter: delay = floor(random * current ceiling in ticks), random in [0,1); initial ceiling 0.5 s, double each failed attempt. Cap is 300 s for Unauthenticated, PermissionDenied and FailedPrecondition; otherwise 30 s. Store a ceiling independently of error category, initialized to 0.5 s and bounded at 300 s: draw using min(stored ceiling, current status cap), then replace stored ceiling with min(stored ceiling * 2, 300 s). Multiply only values below 150 s; values at or above 150 s advance directly to 300 s to avoid overflow. Changing status does not reset this stored ceiling. Reset restores 0.5 s only after accepted Welcome. Stop cancellation does not consume a delay.

R9. A node creates one UUID NodeInstanceId per OrchestratorConnection lifetime, reused across reconnects. On every attempt use a fresh channel and Connect call with metadata authorization Bearer plus NodeToken. Send source.CreateHello(instanceId) first and exactly once, preserving its protocol/name/capabilities/inventory. Await first response: anything except Welcome, a major other than 1, minor above the sent Hello minor, empty NodeId, or nonpositive heartbeat/liveness duration is treated as FailedPrecondition. Do not become Connected before a valid Welcome and completed source.WelcomeAsync; invoke Reset then. Subsequent Welcome or empty response similarly fails. Later Command/EventAck/Goodbye are passed to source.ReceiveAsync; Goodbye terminates the attempt after callback. Do not execute commands here.

R10. While connected send source.CreateHeartbeat() every Welcome.HeartbeatInterval, and overwrite SentAt on the returned heartbeat with TimeProvider.GetUtcNow(), serialized through the same single writer as Hello and shutdown. Cancel/await that timer on every stream end so it cannot write into a later connection. Shutdown sends one Goodbye Shutdown with message "Node shutting down." if the stream is connected, completes request stream, and ends without retry; broken-stream writes may fail without delaying shutdown. Each other failure enters Unavailable and retries using NodeReconnectDelay until host cancellation. Preserve StateChanged, Connected metric and reconnect counter behavior (counter after each successful reconnect). Auth/precondition failures log Error on every failure; other failures log Warning, using only status code and chosen retry delay.

R11. Default INodeLinkSource builds Hello with NodeProtocol.Current, NodeName=NodeOptions.NodeId, supplied instance ID, assembly version as AgentVersion, platform from runtime, empty capabilities and seats, and Limits 4194304/64/10000. Heartbeat has BufferedEvents=0 and InflightCommands=0. Its callbacks complete without action. Default INodeLinkApplication returns empty replay and completes callbacks without action. Register both as replaceable singleton interfaces. No tmux availability or harness capability is claimed until those consumers are integrated. Require nonblank NodeToken via source-generated NodeOptions validation with exact error "AIAKOS_NODE_TOKEN is required." Retain existing NodeId/Home/URL checks and lock/shutdown exit behavior.

R12. Register service/registry/options/proxy support in orchestrator Program and the source/reconnect/time dependencies in NodeProgram; replace the health-watch hosted connection. NodeLinkService remains unavailable until migrations succeeded as with other mapped endpoints. Set server and client gRPC receive/send max message sizes to 4194304; server Http2 KeepAlivePingDelay=30 s, KeepAlivePingTimeout=10 s; preserve matching existing client ping settings.

R13. Per-message tracing uses public static ActivitySource NodeLinkTelemetry.ActivitySource in each side's Link namespace: source names "Aiakos.Orchestrator.Link" and "Aiakos.Node.Link". Start one ActivityKind.Consumer activity named "node-link.receive" for each received envelope (including Hello and Welcome); parse the envelope's TraceParent and TraceState with ActivityContext.TryParse and use that remote context as the explicit parent. For invalid/missing parents use an explicit default ActivityContext (a new root even if the Connect call has Activity.Current). While that receive activity is current, await its application callback; dispose afterwards. Sent envelopes set Trace.Traceparent and Trace.Tracestate from Activity.Current.Id and TraceStateString only when Current has W3C format; otherwise both strings are empty. Start no independent send activity. All writes (including Hello/Welcome/heartbeats/Goodbye) apply this sender rule. Invalid trace text never fails a stream. Test listeners subscribe to the exact source and activity names above.

C1. The earlier spec-0001 connection tests now host NodeLinkService-compatible loopback fake Connect streams instead of Health.Check/Watch. A serving health endpoint alone never makes the node Connected. The unavailable/reconnect assertions remain; update constructor injection to NodeReconnectDelay/INodeLinkSource. Backoff.cs and its tests are unchanged and are no longer used by the production connection. The token-test changes are C2; other existing results do not change.

C2. Replace NodeOptionsTests.NodeTokenIsNotRequired with NodeTokenIsRequired, expecting validation failure with exactly "AIAKOS_NODE_TOKEN is required." Add missing/blank NodeToken to MissingRequiredVariableExits2AndNamesIt. Give AbsoluteHttpUrlsAreValid a nonblank token so it still isolates URL validation. No other URL/home/identity validation result changes.

R14. Tighten IsAllowed to check the literal address before any URI normalization: the entire string must be an ASCII `http://<host>` optionally followed by `:<port>`, with scheme and localhost matched ordinal-ignore-case. Host is only `localhost`, exactly `[::1]`, or four decimal IPv4 octets with first octet exactly `127`, each other octet 0–255 and no leading zero unless the whole octet is `0`. Port, if present, is decimal 1–65535 with no leading zero. Missing port means 80. Reject all user info (`@`), paths (including a trailing `/`), query, fragment, whitespace anywhere, percent escapes, IPv6 scope IDs and alternate IPv4 numeric forms; do not strip or normalize these into accepted input. Port 0 is rejected for production link configuration. Invalid or null input returns false, without an exception. Validate continues to throw the fixed R3 error. No DNS lookup or listener is opened by the pure policy.

C3. The already merged S1 policy becomes stricter under R14. The address fixtures of E17 that were previously accepted must now be rejected; the canonical E3 allowed addresses remain accepted. This amendment changes no credential behavior and keeps the policy error text unchanged.

C4. After S7 startup wiring and S9 pure-policy correction exist, route every eligible configured URL string unchanged to NodeLinkEndpointPolicy.Validate before Kestrel binds. Do not trim, rewrite, normalize or duplicate the policy grammar at startup. Eligibility only selects listeners: when GrpcPort is absent all configured strings are eligible; when GrpcPort is set, use Uri.TryCreate with absolute HTTP scheme to read the configured port (including implicit port 80), and select strings whose parsed port equals GrpcPort. A string whose scheme/port cannot be determined is indeterminate and fails with the fixed policy error, rather than silently skipping it. A determinable different-port HTTP health listener is excluded. Never substitute the parsed URI string for the original selected string. A selected address failing Validate stops startup before any listener binds with "NodeLink requires an explicit loopback HTTP endpoint." Preserve R3's additional resolved-endpoint check, TestServer exemption and gRPC-port mapping. This correction is a final integration story depending on S7 and S9.

## Expected outputs: exact text

| ID | Input | Expected |
|---|---|---|
| `E1` | one valid registration Id=n, Name=machine, default tenant, bootstrap Token; snapshot then authenticate | identity n / 00000000-0000-0000-0000-000000000001 / machine; input Token empty; malformed registry cases in R1 throw "Invalid node registration." |
| `E2` | null; empty; Basic x; Bearer with empty value; bearer x; Bearer x with whitespace; unknown digest; registered token | null for first seven; exact registered identity for last; capture logs/errors contains no supplied token |
| `E3` | http://127.0.0.1:5180; http://127.2.3.4:5180; http://localhost:5180; http://[::1]:5180; http://0.0.0.0:5180; http://example.test:5180; https://127.0.0.1:5180 | true,true,true,true,false,false,false; policy Validate with a non-loopback or empty list throws "NodeLink requires an explicit loopback HTTP endpoint." |
| `E4` | Connect missing/duplicate/malformed/unknown authorization; valid authorization with silent client at 10 seconds, wrong first body or EOF | Unauthenticated / "Node authentication failed." for auth cases; FailedPrecondition / "Hello must be the first node message." for first-message cases; no proxy and no secret in captured output |
| `E5` | valid Hello protocol 1.3/name machine/UUID; wrong name; protocol 2.0; malformed/empty UUID | Welcome protocol 1.0 with registered identity, UUID connection ID, 5 s/15 s and exact limits; respective failures and exact details from R5; callback replay preserved |
| `E6` | two valid simultaneous streams of node n; invalid second Hello; old stream finally cleans up; host stop | old receives Superseded then Aborted; replacement remains live; invalid second does not replace first; shutdown receives Shutdown then Unavailable, exact messages in R6 |
| `E7` | fake clock: Welcome at t0, Heartbeat t4, no messages through t19, Heartbeat t20, EOF; second Hello; unset body; application blocks one event | states Connected,Unknown at t19,Connected at t20,Disconnected on EOF; invalid bodies FailedPrecondition / "Unexpected node message."; no next request consumed until application completes; blocked ReceiveAsync does not delay Unknown notification at the deadline; a superseding stream receives Welcome while old callback token is canceled and old stream receives Superseded/Aborted without awaiting the callback; state notifications serialized in transition order; forwarded request and identity unchanged; zero EventAck |
| `E8` | random always 0.5: five Unavailable failures; enough failures to cap; auth failures to cap; reset; random 0 | 250,500,1000,2000,4000 milliseconds; normal capped draw 15 seconds; auth capped draw 150 seconds; ten Unavailable failures then one Unauthenticated draw 150 seconds; 1000 consecutive failures stay bounded without overflow; reset draw 250 ms; random 0 draw zero |
| `E9` | fake server observes two reconnects; first sends Command instead of Welcome; valid Welcome; duplicate Welcome; invalid durations/major/minor/node ID | first outbound Hello with bearer metadata each time, same UUID instance; invalid cases Unavailable and retry; valid Welcome calls source before Connected; no supplied token/body in logs |
| `E10` | Welcome 5 s; advance test clock to 5/10 s, disconnect/reconnect, stop; three auth errors then success | one heartbeat per interval with SentAt exactly the current fake-clock UTC time, no old timer writes; one shutdown Goodbye and no retry on stop; auth errors each logged Error; if Connected preceded the three errors, successful reconnect counter increases once; if this is the first successful connection, counter remains zero; source receives later envelopes unchanged |
| `E11` | default source/app; NodeProgram with missing/blank NodeToken | Hello 1.0 with matching NodeId name, no capabilities/seats and exact limits; empty replay and zero heartbeat counts; missing token validation "AIAKOS_NODE_TOKEN is required." and RunAsync exit 2 |
| `E12` | real loopback Connect via registered production service/client; channel/server options | link handshake works; all max sizes 4194304 and both ping settings 30 s/10 s |
| `E13` | only healthy Health service; fake NodeLink restart; unchanged BackoffTests and prior NodeOptions cases | health alone leaves Unavailable; NodeLink success Connected, shutdown Unavailable, restart Connected; earlier tests green except explicitly replaced health connection expectations |

| `E14` | absent/blank token in NodeOptions validation and NodeProgram; valid absolute URL with token | validation fails with "AIAKOS_NODE_TOKEN is required."; RunAsync exit 2 before lock/home creation; URL cases still succeed |

| `E15` | production Kestrel configured urls=http://0.0.0.0:5180 with GrpcPort=5180; equivalent TestServer host; Kestrel loopback link port with separate non-loopback HTTP health port | Kestrel fails before binding with "NodeLink requires an explicit loopback HTTP endpoint."; TestServer starts; loopback link plus health listener starts and NodeLink is restricted to link port |
| `E16` | two received envelopes with distinct valid W3C parents; invalid/missing parent under an unrelated ambient Connect activity; sends with/without an ambient W3C activity | source names Aiakos.Orchestrator.Link / Aiakos.Node.Link, Consumer activity node-link.receive: matching trace and parent span for each valid parent, root for invalid/missing; sent traceparent/tracestate exactly ambient Id/TraceStateString or both empty |

| `E17` | IsAllowed on `http://user:pw@127.0.0.1:5180`; `http://example.test@127.0.0.1:5180`; `http://127.0.0.1#@example.test`; `http://127.0.0.1:5180/`; `http://127.0.0.1:5180/path`; `http://127.0.0.1:5180?x=1`; `http://127.0.0.1:5180#x`; ` http://127.0.0.1:5180`; `http://127.0.0.1:5180\n`; `http://[::1%1]:5180`; `http://127.1:5180`; `http://2130706433:5180`; `http://0x7f.0.0.1:5180`; `http://127.0.0.1:0`; `http://127.00.0.1:5180`; `http://127.0.0.1:05180`; null; canonical `http://127.0.0.1:5180`, `http://localhost:5180`, `http://[::1]:5180`, `http://127.2.3.4`, `HTTP://LOCALHOST:5180` | false for every rejected/null input; true for every canonical input; Validate on each rejected input throws "NodeLink requires an explicit loopback HTTP endpoint." |
| `E18` | production Kestrel configured with each rejected address `http://user:pw@127.0.0.1:5180`; `http://example.test@127.0.0.1:5180`; `http://127.0.0.1#@example.test`; `http://127.0.0.1:5180/`; `http://127.0.0.1:5180/path`; `http://127.0.0.1:5180?x=1`; `http://127.0.0.1:5180#x`; ` http://127.0.0.1:5180`; `http://127.0.0.1:5180\n`; `http://[::1%1]:5180`; `http://127.1:5180`; `http://2130706433:5180`; `http://0x7f.0.0.1:5180`; `http://127.0.0.1:0`; `http://127.00.0.1:5180`; `http://127.0.0.1:05180` (literal trailing LF in the escaped `\n` case), GrpcPort unset so every configured string is eligible; canonical loopback endpoints; TestServer; additionally GrpcPort=5180 with each explicit-5180 rejected address beside canonical non-loopback health URL http://0.0.0.0:5181 | every rejected configuration fails before binding with "NodeLink requires an explicit loopback HTTP endpoint."; canonical loopback starts; TestServer remains exempt; explicit-5180 malformed link URLs fail with the same error, while a canonical loopback 5180 link plus non-loopback 5181 health URL starts; no-port or port-0 URLs are tested with GrpcPort unset, not asserted to be selected under GrpcPort=5180 |

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
