## S1: Session axis and vocabulary
goal: The session axis applies its transition table and handles session-related dispatch and unrecognized enum values. Stored vocabulary conversions are defined for the proto enums.
depends: -
owns: B2a, B18, B21
outputs: -
tests: T1, T10
notes: none

## S2: Activity axis and turn behavior
goal: The activity axis applies its transition table, pending-input rules, and turn-finding resolutions. Activity behavior is covered by the permission, exit, parallel-tool, and OpenCode scripts.
depends: S1
owns: B2b, B5, B10, B20
outputs: GS4, GS5, GS8, GS9
tests: T2
notes: B5 combines the session entry rules from B2a with the activity table B2b; B10 and B20 govern the activity scripts GS8, GS9, and GS4 respectively.

## S3: Resumability axis and conversation evidence
goal: The resumability axis applies its transition table and recognizes conversation evidence only for the matching native session. Its rows are covered by table tests.
depends: S1
owns: B2c, B9
outputs: -
tests: T3
notes: none

## S4: Delivery outcome state machine
goal: Delivery inputs follow the delivery table, including absorbing final states and the unconfirmed-delivery finding.
depends: -
owns: B22
outputs: -
tests: T6
notes: none

## S5: Event pipeline, overlays, and node attachment
goal: Events pass through epoch, sequence, launch, stale-event, and axis processing, while gaps, overlays, and node attachment update the known and reported state. Pipeline properties and the permission-turn script are verified.
depends: S1, S2, S3
owns: B6, B7, B8, B11, B12, B13
outputs: GS1
tests: T7, T8, T11, T12
notes: B6 orders the axis changes defined by B2a, B2b, and B2c and invokes B11 on gaps; B8 uses all three axis tables and B9's conversation-evidence profile check. B12's attachment branches apply B13's overlay handling. GS1 also exercises B10 from S2.

## S6: Seat commands and launch decisions
goal: Up, down, and send requests make the specified decisions, and launch outcomes and validated timers update the seat state. The decision scripts verify resume, fresh-launch, rotation, and send behavior.
depends: S1, S2, S3, S5
owns: B14, B15, B16, B17, B19
outputs: GS2, GS3, GS6, GS7
tests: T4, T5, T9
notes: B14 and B17 build on the session and resumability tables from S1 and S3; B16 and B19 also use the activity rules from S2. GS2 depends on the event pipeline in S5, and GS6 combines B8 from S5 with B14 and B15 here.
