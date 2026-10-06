## S1: Claude profile and adapter contract
goal: Strict native-ID and normalized-event profile exists with neutral orchestrator interface/data records.
depends: -
owns: R1, R2
outputs: E1, E2
tests: T1
notes: Independent of settings/delivery/node/actor. Caller owns native-ID matching/persistence. Add stated Spec ProjectReference for contract input types, without implementing other builders.

## S2: Deterministic Claude settings
goal: Pure settings generation provides exact permissions, hooks/statusLine and safe API-key source references.
depends: -
owns: R3, R4, R5
outputs: E3, E4, E5
tests: T2
notes: Independent of profile/node relay. Add stated Spec ProjectReference if absent; no filesystem/relay execution. Preserve resolved permission order and bytes.

## S3: Pure delivery hints and slash allowlist
goal: ClaudeCodeDelivery builds the marker lead/normalized body and accepts only compact as slash command.
depends: S1
owns: R8, R9
outputs: E8, E9
tests: T4
notes: Public ClaudeCodeDelivery needs S1 profile/DeliverySpec only; no settings/files dependency. Full adapter composition arrives S4; caller authenticates sender.

## S4: Launch builder and adapter composition
goal: Concrete ClaudeCodeAdapter builds exact Fresh/Resume launch plans from opaque caller-supplied file snapshots.
depends: S2, S3
owns: R6, R7
outputs: E6, E7
tests: T3
notes: S2 supplies settings; S3 supplies delivery and S1 transitively profile/contract. SuppliedFiles avoids14-5/12-2 implementation dependency. No fallback/missing relay bytes created; persistence/dispatch is later13-4/10-5.

## S5: Orchestrator harness registration
goal: Production DI resolves singleton adapter/profile/settings and preserves explicit overrides/other profiles.
depends: S4
owns: R10
outputs: E10
tests: T5
notes: Uses complete adapter afterS4; profile enumerable resolves the same concrete singleton. No actor/node capability or missingfile provider introduced.
