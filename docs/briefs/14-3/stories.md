## S1: Safe shared reference paths
goal: Shared references are normalized within the rig root and agent discovery rejects missing, unsafe or linked targets before reading them.
depends: -
owns: R1, R2, C1
outputs: PATH-unsafe, PATH-normal, PATH-missing, PATH-agent
tests: T1
notes: Load still returns null. Test SharedReferencePaths directly and existing agent-discovery integration. Do not create content or resolved records yet. G3 applies before path diagnostics and G1 preserves earlier schema/semantic suppression.

## S2: Markdown snapshots and credential scanning
goal: Checked Markdown files become normalized embedded byte snapshots with per-file hashes and safe credential diagnostics.
depends: S1
owns: R3, R4
outputs: TEXT-normal, TEXT-size, TEXT-secret
tests: T2
notes: Test SharedContentReader directly; Load integration belongs to S4. R4 also provides the shared text detector for S3. Introduce only EmbeddedFile and the Markdown helper; do not add skill or final assembly stubs.

## S3: Skill directory snapshots
goal: Safe skill directories produce complete byte-exact snapshots after metadata, links, size and credential checks.
depends: S2
owns: R5, R6, R7
outputs: SKILL-valid, SKILL-missing, SKILL-front, SKILL-links, SKILL-limits, SKILL-secret
tests: T3
notes: Uses S1 path checking and S2 EmbeddedFile/credential detection, but raw skill bytes bypass Markdown normalization. Tests call SharedSkillReader; duplicate skill declarations and Load wiring are S4. Do not create aggregate hashes or projection plans. R5 special-entry handling is added by S6 using R14 classification; S3 builds the normal directory reader that S6 hardens.

## S4: Load reference integration
goal: Load reads and caches referenced content, reports independent file failures in defined order, and rejects duplicate skill declarations.
depends: S3
owns: R8, R9
outputs: RES-duplicate, RES-errors
tests: T5
notes: Load still returns null, including valid calls. Consume S1-S3 helpers into a per-call internal catalog; S5 consumes that catalog without reading files again. Scalar skill failures replay at each declaration, file diagnostics are emitted once. No assembly records or placeholder successful Rig are introduced here.

## S5: Resolved rig and seat parameters
goal: Load returns complete shared snapshots, defaults, bindings and node-independent seat parameters when there are no errors.
depends: S4
owns: R10, R11, R12, R13, C2
outputs: RES-content, RES-distinct, RES-defaults, RES-binding, RES-parameters
tests: T4
notes: Assemble records from S4's per-call catalog and existing validated YAML; do not implement reference traversal or caching again. C2 changes the two earlier valid-result assertions here. R9's duplicate-name checks are already merged; RES-distinct verifies different agents may share a skill name. Aggregate hashes and generated projection remain later slices.

## S6: Reject special files and preserve agent reference diagnostics
goal: Shared file references reject nonregular files before opening them and unreadable agents report at each reference scalar.
depends: S3
owns: R14, C3
outputs: PATH-special, PATH-unreadable
tests: T6
notes: C3 corrects merged S1 behavior without renumbering approved stories. Uses R1 link precedence and R2 diagnostic text; retain syntax and rig/env diagnostics. S6 shares its classifier with the existing S3 skill enumerator under R5, including cap precedence from R7. It depends on S3 because that reader must exist; lead may prioritize S2/S3 then S6 before S4/S5. No new Markdown or skill snapshot format belongs here.
