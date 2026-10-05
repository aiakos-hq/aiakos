## S1: Bounded process runner
goal: Child clients run with separate arguments and explicit environments, bounded IO, concurrency, timeout and cancellation.
depends: -
owns: R1, R2
outputs: E1, E2
tests: T1
notes: FakeProcessRunner records requests without launching a process. G1/G2 and T0 apply; ProcessRunner timeout/cancellation kills only its own client.

## S2: Private tmux client initialization
goal: A resolved supported tmux binary uses the private socket/config and sanitized environment, with honest availability and classified failures.
depends: S1
owns: R3, R4, R5, R6, R7, R8
outputs: E3, E4, E5, E6, E7, E8
tests: T2
notes: Uses R1/R2 for process execution. InitializeAsync needs a private invocation path before Available; public RunAsync requires Available. Includes naming helpers and config because they form the same startup boundary. No NodeProgram or Hello registration. Existing servers are scrubbed and converged, never recreated.

## S3: Atomic launch registry
goal: Launch records round-trip in private files and updates preserve the old committed record until atomic replacement.
depends: -
owns: R9, R10
outputs: E9, E10
tests: T3
notes: Reuses merged 11-1 RegistryEntry records. Direct-write tests cover starting/running records; start ordering R11/E11 belongs to S6. R9 recovery array retains full failed-launch evidence across replacement attempts; it is not a watcher or another managed listing. This store owns no watcher or process.

## S4: Process snapshots and orphan checks
goal: Same-user process snapshots provide exact identities and helpers exclude live managed trees before checking orphan predicates.
depends: -
owns: R12, R13, R14
outputs: E12, E13, E14
tests: T4
notes: CheckOrphans accepts roots supplied by a caller; RequireStartTime consumes a snapshot. Both can be tested without tmux, registry or future starter. S6 extracts roots from R18 and integrates E13; S5 consumes R14. No signals or process termination. A vanished immediate-exit PID has no invented start time.

## S5: Direct tmux launch commands
goal: Creation and verified labelling run a supplied executable directly and return its actual pane identity without readiness or input.
depends: S2, S4
owns: R15, R16, R17
outputs: E15, E16, E17
tests: T5
notes: TmuxLaunchCommands is a low-level creation component, not the StartAsync facade. Its caller later performs R18-R20 and commits starting; do not create placeholder lifecycle methods here. Script tests call CreateAsync with valid inputs. R7 escapes data once; R14 supplies start time. R17 initial-label verification permits an empty launch label until it is set. Opt-in tests use only unique sockets.

## S6: Start lifecycle
goal: StartAsync validates, refuses live/foreign/orphaned seats, records launches and replaces verified dead panes.
depends: S2, S3, S4, S5
owns: R11, R18, R19, R20
outputs: E11, E18, E19, E20
tests: T6
notes: Composes merged validators with registry, snapshots and CreateAsync. Add integrated E11/E13/E14 ordering assertions. R19 publishes through the supplied callback and persists exit.reported; no watcher/outbox is assumed. The starter remains a start component, not a partial ISessionHost implementation; final node registration/reconciliation belongs to 11-5. R19 applies maintainer decision 2026-10-06 to dead starting launches without a returned handle; no event or invented start time, and recovery evidence lasts until durable replacement. Publication atomicity stays explicit for architect review.

## S7: Read-only attach commands
goal: A started pane has an exact read-only attach command, with an explicit writable variant and no mutation.
depends: S2, S6
owns: R21
outputs: E21
tests: T7
notes: Adds AttachCommand to TmuxNames and GetAttachCommand to the starter; reuse S2 SocketName validation and existing address parsing, without changing earlier helper behavior.

## S8: Safe start and tmux diagnostics
goal: Existing start and client operations emit the specified spans and metrics without payload or credentials.
depends: S2, S6
owns: R22
outputs: E22
tests: T8
notes: R22 instruments the established client and starter, using their supplied loggers/IMeterFactory and the existing ActivitySource. No placeholder metrics for later operations. Permanent listener tests guard diagnostic values. This story does not depend on attach behavior.
