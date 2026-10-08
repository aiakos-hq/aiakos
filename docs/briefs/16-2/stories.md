## S1: Pin the first stable M1 release
goal: Repository-local dotnet aiakos resolves exactly the released M1 tool.
depends: -
owns: R1
outputs: E1
tests: T1
notes: EXTERNAL READINESS PREREQUISITE: published stable0.1.0 with dry-run must exist. Router verifies availability before ready; placeholder/development packages are forbidden. No release engineering in this story.

## S2: Released rig compatibility check
goal: CI restores the pinned tool and fails for an unloadable real rig, with released negative-test gate evidence.
depends: S1
owns: R2, R3
outputs: E2, E3
tests: T2
notes: R1 supplies manifest/package. Requires16-1 real rig files already merged. Uses Windows release runner and read-only dry-run; regression tests are offline, but E3 acceptance uses real pinned package and isolated unknown-field copy.

## S3: Pin and upgrade runbook
goal: The lead has exact local pin and safe ordered patch/minor upgrade instructions with honest acceptance limits.
depends: S2
owns: C1, R4, R5
outputs: E4, E5
tests: T3
notes: C1 corrects16-1 README ordering without changing seat policies. R4 only claims pin/check present after R1/R2; required GitHub check and live16-3 acceptance remain maintainer actions. No running instance is upgraded.
