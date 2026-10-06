## S1: Exact generated Claude guidance
goal: Render deterministic seat guidance and roster bytes with source comments and an explicit authority disclaimer.
depends: -
owns: R1, R2
outputs: GUIDE-base, GUIDE-body, GUIDE-unicode
tests: T1
notes: Uses existing resolved snapshots and CanonicalJson only. G1 defines shared NFC semantics; no new helper from S2/S3 or 14-4-3 is needed. Commit the full byte golden and Unicode checks here. Checks 0003-RK2 text.

## S2: Byte-exact skill projection mapping
goal: Map embedded skills to normalized Claude destination paths without reading source files.
depends: -
owns: R3
outputs: SKILLS-map
tests: T2
notes: Uses existing ResolvedSkill/EmbeddedFile and CanonicalJson only. G1 provides NFC semantics independently of S1. Retain duplicate destinations so S3 can reject them later; do not implement or stub S3. No 14-4-3 dependency.

## S3: Validated projection descriptors and hash
goal: Build copied content-addressed plans with safe paths, lexical checkout separation and the inclusive 2 MiB limit.
depends: -
owns: R4, R5, R6
outputs: PLAN-files, PLAN-invalid, PLAN-overlap, PLAN-size, PLAN-hash
tests: T3
notes: Accepts supplied EmbeddedFile snapshots and checkout paths directly; neither S1 nor S2 is needed. R4 then R5 then R6 determines diagnostic precedence. Uses merged CanonicalJson. New records belong here; preserve physical node checks as deferred.

## S4: Finalized loader projection integration
goal: Populate every agent seat's plan after hash finalization and preserve earlier hashes, content and diagnostics.
depends: S1, S2, S3
owns: R7, C1
outputs: LOAD-plan, LOAD-errors
tests: T4
notes: External prerequisite 14-4-3 must be merged before start; it is absent from the analysis base loader. Do not supply its finalization here. Calls the three helpers once per seat and commits load regressions; C1 updates only additive Projection JSON goldens. No actual node writes.
