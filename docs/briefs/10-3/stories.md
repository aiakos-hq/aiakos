## S1: Node credentials and loopback endpoint policy
goal: Node credentials resolve to registered identity without retaining plaintext, and a pure policy rejects unsafe link listeners.
depends: -
owns: R1, R2, R3
outputs: E1, E2, E3
tests: -
notes: R3 defines the startup check but its invocation in Program is wired by R12 in S6. Test policy Validate directly here; production registration belongs to S6. G1 applies to credentials and diagnostics.

## S2: Link extension interfaces and empty defaults
goal: Both link consumers have replaceable sources and application callbacks, and the node requires its bootstrap token.
depends: -
owns: R11, C2
outputs: E11, E14
tests: -
notes: Create the public extension interfaces and NodeLinkOptions here. R11 registrations in NodeProgram and Program are completed with R12 in S6; test defaults and token validation directly here. No callback persists or executes anything.

## S3: Authenticated server handshake and link ownership
goal: The server authenticates Connect, sends Welcome, replaces old streams and tracks liveness in a link-only node proxy.
depends: S1, S2
owns: R4, R5, R6, R7
outputs: E4, E5, E6, E7
tests: -
notes: One server-link concern sharing its stream lifetime, cancellation and registry. R4 uses R2 identity; R5 uses R11 replay callback; R6 replacement and R7 old cleanup share generation ownership. Use a slim loopback host with explicit test registrations; production registration is S6. Inbound application callbacks are awaited, but timer/replacement messages must remain processable so a blocked callback cannot stall supersession or liveness.

## S4: Full-jitter reconnect delay
goal: Failed link attempts use deterministic-testable exponential full jitter and the status-specific caps.
depends: -
owns: R8
outputs: E8
tests: -
notes: This is a new helper; keep Backoff and its earlier tests unchanged. R9 and R10 consume the helper in S5.

## S5: Node Connect stream and heartbeat loop
goal: The node authenticates and handshakes on Connect, heartbeats, shuts down and reconnects without exiting on link failure.
depends: S2, S4
owns: R9, R10
outputs: E9, E10
tests: -
notes: R9 uses R11 message-source defaults and R8 delay. R9 and R10 share one call lifetime and writer; preserve connection metrics/state. Adjust the existing connection tests for the changed constructor and fake Connect server now so this story builds and earlier tests stay green; S6 checks the final regression behavior under C1. NodeProgram can retain prior Backoff registration until S6; add the minimum new dependencies required to construct the hosted connection in this story, with final wiring in S6.

## S6: Production registration, transport tracing and regressions
goal: The production hosts serve and use the node link with loopback enforcement, limits, keepalive and per-message trace parents.
depends: S1, S2, S3, S4, S5
owns: R12, C1
outputs: E12, E13
tests: -
notes: R12 activates R3 startup policy and service mapping, R11 replaceable defaults, and R9/R10 hosted client. C1 finalizes the existing test replacements begun for compilation in S5. Existing database fixture tests verify production registration; other conformance tests use slim hosts. All earlier results except explicitly listed C1 replacements stay unchanged.
