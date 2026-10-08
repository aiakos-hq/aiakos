## S1: Generated-contract HTTP client
goal: The CLI sends typed v1 requests and maps failures without retries or raw response disclosure.
depends: -
owns: C2, R1
outputs: E1
tests: T1
notes: External prerequisite 15-2-2 contract project/JSON context merged; no instance/discovery prerequisite. Owns project references and generic client/reply/exception types only. JsonDocument success union does not amend API DTOs.

## S2: Version checks, address resolution and rejection text
goal: Version mismatches and ambiguous addresses fail explicitly and seat rejections explain the next step.
depends: S1
owns: R2, R9
outputs: E2, E9
tests: T2, T9
notes: Owns CliVersionPolicy, CliSeatResolver, CliCommandContext and CliCommandRendering including Quote. R2 CheckAsync owns the single version GET and diagnostics; resolver errors carry candidate snapshots through the S1 exception. Tests invoke these components directly; command orchestration comes later.

## S3: Bounded delivery body reading
goal: All three input sources normalize and validate locally without disclosing their contents.
depends: S1
owns: R3
outputs: E3
tests: T3
notes: R3 throws S1 CliApiException with fixed reasons and Retryable=false. Uses existing Core.InputValidator and 15-1-1 parsed CliRequest; does not discover instances or call the API. External prerequisite 15-1-2 invocation validation merged before production integration; pure reader can be tested independently.

## S4: Honest seat status rendering and watch
goal: ps lists and details preserve the three axes and support cancellable two-second watch.
depends: S2
owns: R4
outputs: E4
tests: T4
notes: Owns CliReadCommands ps branch only; do not stub capture. Shared read DTOs are from S1 external prerequisite. Detail output exposes only the specified read fields.

## S5: Capture evidence command
goal: capture prints the pane and flags without treating screen text as actionable input.
depends: S4
owns: R5
outputs: E5
tests: T5
notes: Extends CliReadCommands with capture; uses S2 address/rejection helpers, S4 read tests remain unchanged. No terminal/actor changes.

## S6: Selected seat shutdown client
goal: down resolves all targets first and reports each no-op, stop or failed outcome.
depends: S2
owns: R6
outputs: E6
tests: T6
notes: Owns CliDownCommand and its JSON/text output; polling is private to the handler and uses supplied TimeProvider. No shared polling abstraction required from send/up.

## S7: Delivery confirmation and turn wait
goal: send submits once and honestly reports delivery and optional final activity within one deadline.
depends: S2, S3
owns: R7
outputs: E7
tests: T7
notes: Owns CliSendCommand; S3 returns only normalized accepted body. Shares no implementation prerequisite with down; does not change server force policy.

## S8: Bounded git source metadata
goal: Source metadata is collected without leaking process output or blocking indefinitely.
depends: -
owns: R13
outputs: E13
tests: T13
notes: Owns CliSourceReader and CliSource only; private repository tests never write an owner checkout. R8 handles the unavailable-metadata notice.

## S9: Rig registration and launch client
goal: up validates locally, registers exact canonical content and reports ordered launches without automatic restarts.
depends: S2, S8
owns: R8
outputs: E8
tests: T8
notes: External prerequisites 15-1-4 GitEnvChecker, 15-1-5 application/local dry-run and 14-4-3 finalized loader canonical output merged. Owns CliUpCommand; R13 source metadata comes from S8; no host/actor implementation. Conservative guidance note on every resume is explicit in the brief.

## S10: Shared exact attach argv and terminal client
goal: attach uses the configured private tmux server read-only by default and returns the child exit code.
depends: S2
owns: R10
outputs: E10
tests: T10
notes: Owns additive Core.TmuxNames.AttachArguments plus CliAttachCommand/runner and new Core.Tests project/solution entry. Preserve existing AttachCommand; Linux home slash validation is independent of Windows Path rules. Config/home are supplied directly in handler tests; production discovery belongs S11.

## S11: Operational application and executable wiring
goal: All six commands use their production components with authenticated instance discovery while local commands remain local.
depends: S5, S6, S7, S9, S10
owns: C1, R11
outputs: E11
tests: T11
notes: External prerequisites 15-1 all stories, 15-4 configuration/InstanceLayout/ConnectionStore/InstancePlatform/InstanceCommands/CliApplication wiring and Aiakos.Wsl runner merged. Adds ICliOperationalCommands and production composition, preserves original constructors and instance dispatch; no fake production surfaces. C1 updates only earlier operational unsupported expectations and README scope.

## S12: Command traces, verbose trace IDs and bounded export
goal: One CLI root traces every request and optional export completes within one second without sensitive values.
depends: S11
owns: R12
outputs: E12
tests: T12
notes: Adds tracing at operational invocation boundary and traceparent to S1 client; no new root per request/poll. Exporter package already centrally managed; no package version change. Connection/disposal and executable composition are S11 prerequisites.
