## S1: xUnit v3 actor test infrastructure
goal: Run real actor probes through TestKitBase with xUnit v3 assertions and document the compatibility decision.
depends: -
owns: R1
outputs: KIT-v3
tests: T1
notes: Only test infrastructure and the one central TestKit reference; no shell dependency. Check 0001-RK4/0006-RK1 before other actor tests.

## S2: Seat commit protocol and wire input mapping
goal: Define the exact node-to-seat commit and reload contracts and map normalized protobuf bodies into existing pure inputs.
depends: -
owns: R2, R3
outputs: PORT-shape, MAP-bodies
tests: T2
notes: No actor/store implementation, no 10-4 dependency. Existing state machine/proto types suffice. This story owns the named internal requests/notices used by later independent actor tests.

## S3: Consistent seat snapshot reader
goal: Load complete tenant-scoped actor snapshots, committed event keys and eager startup keys without replaying evidence.
depends: S2
owns: R4
outputs: READ-snapshot, READ-startup
tests: T3
notes: Separate reader class/private SQL helpers; no writer or actor is needed. Uses existing 13-2 schema, including current launch/session joins. Metadata for humans permits the later region to exclude them before child creation.

## S4: Atomic seat evidence and conclusion writer
goal: Commit evidence, transitions, state, findings, session rotation and received result metadata in one version-checked transaction.
depends: S2
owns: R5, R6, R7, R8
outputs: WRITE-atomic, WRITE-conflict, WRITE-evidence, WRITE-findings, WRITE-session, WRITE-results
tests: T4
notes: One transaction concern with a separate writer class; uses supplied before/steps, not S3 reader or S5 actor. R5 order enables rollback evidence; R7 consumes persistence-only rotation. R8 records existing command outcomes only, never creates or sends commands. No schema edits. If breadth exceeds one run, report actual size evidence to architect; no escalation assumed.

## S5: Serialized seat apply and commit actor
goal: Apply normalized events/link inputs serially and return success only after committing; fail safely and reload on restart.
depends: S1, S2
owns: R9, R10
outputs: ACTOR-commit, ACTOR-reject, ACTOR-fail
tests: T5
notes: Fake reader/writer/profile and named S2 requests/SeatChildLoaded allow real actor tests without S3/S4 or the S6 gateway. No production harness profile exists; reject its absence. Actual public observer and gateway cancellation assertions belong to S6. External RequestCapture effects only return after commit.

## S6: Seat region routing, gateway and reload notification
goal: Route eligible tenant seats to supervised children, expose the three commit ports, and notify node consumers only after successful restart reload.
depends: S5
owns: R11, R12, R13
outputs: REGION-route, REGION-limit, REGION-reload, ACTOR-cancel
tests: T6
notes: Uses S5 actor with fake stores; no S3/S4 dependency. R11 clones/admission/cancellation handles unread callers. R12 writes only actor-stopped seat findings through writer port, never node-scoped findings. R13 observer is the exact 10-4 re-forward seam; no node facade dependency.

## S7: Production shell registration and host integration
goal: Host the region after migrations with concrete Postgres stores and expose the same gateway singleton through all ports.
depends: S3, S4, S6
owns: R14, C1
outputs: HOST-shell
tests: T7
notes: Preserve health/shutdown/node application tests. Production #12 profile is absent; empty registration fails closed as R9 specifies, while integration injects a test profile. 10-4 later replaces node application and consumes the ports; do not implement it here.
