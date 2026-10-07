## S1: Configuration and isolated instance layout
goal: Configuration and isolated instance layout implements the owned rules with permanent tests.
depends: -
owns: R1, R2, R3
outputs: E1
tests: T1
notes: Pure config after merged15-1 S1 test infrastructure; no API/host/node prerequisite.

## S2: Transactional initialization and secret ACLs
goal: Transactional initialization and secret ACLs implements the owned rules with permanent tests.
depends: S1
owns: R4
outputs: E2
tests: T2
notes: Consumes S1 config/layout. Temporary Windows ACL test only.

## S3: Versioned runtime copies
goal: Versioned runtime copies implements the owned rules with permanent tests.
depends: -
owns: R5
outputs: E3
tests: T3
notes: Independent after merged15-1 S1 test infrastructure; temporary runtime byte/hash tests.

## S4: Shared orchestrator composition
goal: Shared orchestrator composition implements the owned rules with permanent tests.
depends: -
owns: C1
outputs: E4
tests: T4
notes: External prerequisite: merged15-2 API hosting story. Public Program regression tests remain.

## S5: Aspire-free WSL helpers and node installation
goal: Aspire-free WSL helpers and node installation implements the owned rules with permanent tests.
depends: -
owns: C2, R6, R7
outputs: E5
tests: T5
notes: Creates Wsl/Wsl.Tests projects; Hosting.Wsl facades delegate. No CLI/API dependency.

## S6: Owned node supervision
goal: Owned node supervision implements the owned rules with permanent tests.
depends: -
owns: R8, R9
outputs: E6
tests: T6
notes: Pure process ownership/time policy after merged15-1 S1. No real WSL/config dependency.

## S7: Released host dependency startup
goal: Released host dependency startup implements the owned rules with permanent tests.
depends: S2, S4, S5, S6
owns: R10, R11, R12
outputs: E7
tests: T7
notes: Consumes S2 secrets/S4 composition/S5 WSL/S6 supervisor; owns real node process adapter, not actor or API replacement.

## S8: Host logging and telemetry
goal: Host logging and telemetry implements the owned rules with permanent tests.
depends: S2
owns: R16
outputs: E9
tests: T9
notes: Consumes config and secrets for redaction; owns package pins and recording dashboard port tests.

## S9: Instance lifecycle API and command wiring
goal: Instance lifecycle API and command wiring implements the owned rules with permanent tests.
depends: S3, S7, S8
owns: C3, R13, R14, R15
outputs: E8
tests: T8
notes: External prerequisites merged15-1 S5 and15-2 auth/read hosting; consumes runtime/startup/logging; real packaged start demo waits15-5 payload, no development node fallback.
