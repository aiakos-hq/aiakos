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
notes: Uses S1 path checking and S2 EmbeddedFile/credential detection, but raw skill bytes bypass Markdown normalization. Tests call SharedSkillReader; duplicate skill declarations and Load wiring are S4. Do not create aggregate hashes or projection plans.

## S4: Resolved rig and seat parameters
goal: Load assembles shared snapshots, defaults, bindings and node-independent seat parameters, returning a complete rig only when there are no errors.
depends: S3
owns: R8, R9, R10, R11, R12, R13, C2
outputs: RES-content, RES-duplicate, RES-defaults, RES-binding, RES-parameters, RES-errors
tests: T4
notes: Integrates the S1-S3 helpers and introduces only the assembly records. C2 changes the two earlier valid-result assertions here. One assembly concern with six rules and six outputs; all file parsing/safety/content logic is already merged. Aggregate hashes and generated projection remain later slices.
