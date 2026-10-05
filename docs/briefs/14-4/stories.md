## S1: Deterministic canonical JSON writer
goal: Write compact JSON with ordinal keys, NFC strings and exact escaping independent of resolved-record assembly.
depends: -
owns: R1
outputs: JSON-exact
tests: -
notes: No 14-3-5 prerequisite. Commit the output test here; R7 keeps it rather than deferring it.

## S2: Canonical rig trees and separate hashes
goal: Build shared and binding trees, separate hashes and a copied content-addressed map from supplied snapshots.
depends: S1
owns: R2, R3, R4, R5, R6
outputs: CAN-shared, CAN-order, CAN-binding, HASH-minimal, HASH-content
tests: -
notes: Start only after external predecessor 14-3-5 is merged; never implement its assembly. R3 preserves resolved workdir_repo and ordered guidance/permissions. R5 excludes derived parameters, version and hash fields. R6 copies bytes and retains declarations. Commit owned output tests here; R7 adds load-level checks.

## S3: Finalized loader hashes and stability tests
goal: Finalize successful loads, propagate hashes to parameters, and commit load-level golden and generated stability tests.
depends: S2
owns: R7, R8, C1
outputs: LOAD-stable, LOAD-change, LOAD-result
tests: T1, T2
notes: R8 calls S2's canonicalizer once after 14-3-5 assembly without changing diagnostics. C1 only adds declared serialization fields. R7 commits minimal canonical/hash goldens using S2's exact bytes and generated tests without a new package. 0003-RK2 stays with 14-5.
