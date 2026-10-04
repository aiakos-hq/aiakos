## S1: Node credentials and loopback endpoint policy
goal: Node credentials resolve to registered identity without retaining plaintext, and pure endpoint policy rejects unsafe listeners.
depends: -
owns: R1, R2, R3
outputs: E1, E2, E3
tests: -
notes: S1 creates NodeIdentity. R3 defines policy methods and startup algorithm; R12 in S7 invokes it in production. Test IsAllowed and Validate directly here; E15 checks production activation later. G1 applies to credentials and diagnostics.

## S2: Link extension interfaces and empty defaults
goal: Both link consumers have replaceable sources and application callbacks, and the node requires its bootstrap token.
depends: S1
owns: R11, C2
outputs: E11, E14
tests: -
notes: Create INodeLinkApplication and INodeLinkSource plus NodeLinkOptions here; the application interface uses NodeIdentity from S1. R11 registrations are finalized by R12 in S7. C2 updates earlier token validation tests in this story.

## S3: Authenticated server handshake
goal: The gRPC service authenticates the first node message and returns a negotiated Welcome with registered identity.
depends: S1, S2
owns: R4, R5, C5
outputs: E4, E5, E19
tests: -
notes: R4 explicitly uses the injected TimeProvider for HelloTimeout; E4 advances only that clock. R4 uses R2 authentication and R5 uses the R11 replay callback. C5 specifies every slim-host registration, the public registry constructor and host-owned ActorSystem lifetime. S3 creates the minimum registry/proxy support used by handshake; S4 extends it. E19 checks construction and teardown. Use that exact slim loopback host here; production wiring belongs to S7. Historical S3 scope was handshake only, but PR #145 also merged partial ownership/post-Welcome and production wiring. S4/S7/S10 now finish those existing paths as specified below.

## S4: Server link ownership and liveness
goal: Valid streams are owned by one link-only proxy per node, supersede older streams and track liveness without blocking behind application callbacks.
depends: S3
owns: R6, R7, C6
outputs: E6, E7, E20
tests: -
notes: Main after PR #145 already has a receive loop, Connected/final callbacks and a registry that stops the previous actor. Complete R6/R7, not a second loop/registry. E6 must close the superseded RPC after second Welcome without sending another old message, including when ReceiveAsync is blocked; stopping its actor alone hangs the pending completion. C6/E20 correct canonical IDs before both replay and registry/application forwarding. C5 from S3 fixes the complete slim host and registry constructor for E6/E7 too, with no extra application registrations; host-provided IHostApplicationLifetime exposes shutdown and NodeLinkService supplies proxy time/options. R6 and R7 share the established stream lifetime and generation ownership. R7 uses the same injected TimeProvider for all initial/reset liveness scheduling and elapsed-time accounting; E7 advances only that clock, while C5 teardown safety bounds remain real time. R7 defines callback concurrency and cancellation in the brief; implement its blocked-callback cases exactly. Preserve R4/R5 handshake behavior.

## S5: Full-jitter reconnect delay
goal: Failed link attempts use exponential full jitter, bounded stored ceiling and status-specific caps.
depends: -
owns: R8
outputs: E8
tests: -
notes: New helper only; keep Backoff and its earlier tests unchanged. S6 consumes the helper.

## S6: Node Connect stream and heartbeat loop
goal: The node authenticates and handshakes on Connect, heartbeats, shuts down and reconnects without exiting on link failure.
depends: S2, S5
owns: R9, R10, C1
outputs: E9, E10, E13
tests: -
notes: R9 uses R11 source and R8 delay. R9/R10 share one call lifetime/writer; C1 owns the existing health-test replacement here. Add the minimum NodeProgram dependencies needed to construct the hosted connection now; S7 completes production wiring. E10 explicitly distinguishes first connection from reconnect and checks the connection-assigned heartbeat timestamp.

## S7: Production registration and transport settings
goal: The production hosts serve and use the node link with loopback enforcement, message limits and keepalive.
depends: S1, S2, S3, S4, S5, S6
owns: R12, C7
outputs: E12, E15, E21
tests: -
notes: Main already maps NodeLinkService and registers token/registry/time and startup endpoint callbacks. Retain them; finish node registrations and message/keepalive limits under R12. C7/E21 add bound options, exact HelloTimeout validation and eager credential validation before listening; baseline must fail on invalid timeout/registration startup, not an absent service. R12 activates R3 startup address enforcement, R11 replaceable defaults and R9/R10 client registrations. E15 verifies configured non-loopback Kestrel rejection before bind and TestServer exemption. Existing database fixture verifies production registration; slim hosts cover remaining protocol cases.

## S8: Per-message trace context
goal: Both link directions propagate ambient W3C context and start named per-envelope receive activities.
depends: S4, S6, S7
owns: R13
outputs: E16
tests: -
notes: R13 modifies both established stream loops and all outgoing envelope writes. Use exact source and activity names, explicit parent contexts and the invalid-parent root cases in E16. G1 forbids payload or credential trace content.

## S9: Reject ambiguous literal endpoint addresses
goal: The already merged pure loopback policy rejects URL forms that URI normalization accepts but Kestrel interprets differently.
depends: S1
owns: R14, C3
outputs: E17
tests: -
notes: R14/C3 amend R3 policy in S1; retain canonical E3 behavior and exact Validate error. S10 integrates this policy at startup after both S7 and S9 merge. No startup wiring is implemented in S9.

## S10: Integrate strict endpoint policy at startup
goal: Production startup passes original eligible URL strings to the strict policy and rejects ambiguous listener forms before binding.
depends: S7, S9
owns: C4
outputs: E18
tests: -
notes: Main already invokes ValidateConfigured/ValidateResolved and maps the service. Program splits urls with TrimEntries; ValidateConfigured can silently exclude indeterminate URLs with a selected port. C4 repairs those existing paths. E18 baseline includes leading/trailing whitespace (currently normalized) and indeterminate selected-port input; previously passing rejection cases are regression assertions. S7 provides R12 wiring and S9 provides R14 policy. C4 changes startup to use that policy once, with no grammar copy or merge-order branch. E18 distinguishes the unset-GrpcPort all-address matrix from selected 5180 link URLs and excluded 5181 health listeners. No pure-policy change is implemented here.
