## S1: Rig topology and example local binding
goal: Add the aiakos-dev topology, maintainer-neutral example binding and ignored local binding.
depends: -
owns: R1
outputs: RIG-files
tests: T1
notes: Parse and assert only the new YAML files; full loader validation waits for S9, so no stub agent/guidance files are created.

## S2: Shared team culture and turn report
goal: Supply the exact process and final turn report text projected into both seats.
depends: -
owns: R2
outputs: CULTURE-text
tests: T2
notes: One culture document concern, independent of topology and agents.

## S3: Implementer agent and explicit permission arrays
goal: Define the implementer defaults and exact Claude allow/ask/deny arrays.
depends: -
owns: R3
outputs: IMPL-agent
tests: T3
notes: Pure YAML assertions; referenced guidance/skills land independently in S5; no full loader yet.

## S4: Reviewer agent and comment-only permissions
goal: Define the reviewer default mode and explicit edit/approval/commit/push denials.
depends: -
owns: R4
outputs: REVIEW-agent
tests: T4
notes: Pure YAML assertions; referenced guidance/skill land independently in S6; no running permission probe.

## S5: Implementer guidance and issue/review skills
goal: Describe implementation and addressing reviews with clean branches, attribution and reports.
depends: -
owns: R5
outputs: IMPL-guidance
tests: T5
notes: One implementer workflow concern across three short Markdown files. Reports refer to CULTURE.md, whose exact format is global and lands independently in S2; no projection test until S9.

## S6: Reviewer guidance and exact-head review skill
goal: Describe evidence-based comment reviews with exact head attribution and a reviewed report.
depends: -
owns: R6
outputs: REVIEW-guidance
tests: T6
notes: One reviewer workflow concern across two short Markdown files. No checkout, permission probe or live GitHub review is run here; full projection waits for S9.

## S7: Read-only prerequisite checker for example defaults
goal: Check nine groups with fixed truthful output and no machine mutation.
depends: -
owns: C2, R7, R8
outputs: PREREQ-ok, PREREQ-fail, PREREQ-bounds
tests: T7
notes: One POSIX script concern plus isolated command-fake tests. Lead approved account-marker heuristic and manual custom-path checks; C2 spec clarification is already in analysis PR. Uses repository global.json, not rig.yaml or local binding, so no preceding story dependency.

## S8: Released-team runbook and verification limits
goal: Replace the placeholder README with correct seat policies and the prospective operating runbook.
depends: -
owns: C1, R9
outputs: README-stage
tests: T8
notes: One README concern, independently specifies script limits and pending releases; no script execution needed. C1 removes the incorrect shared-readonly reviewer claim.

## S9: Actual-rig loader and projection validation
goal: Validate the real completed rig with current loader and existing renderer, using independent goldens.
depends: S1, S2, S3, S4, S5, S6, S7, S8
owns: R10
outputs: LOAD-real, LOAD-paths, PROJECT-real, LOAD-unknown
tests: T9
notes: References must exist before loader/guidance projection runs. Exact full layout assertion also needs S7/S8. No #15 CLI, pinned release or unmerged 14-5 S4 property needed.
