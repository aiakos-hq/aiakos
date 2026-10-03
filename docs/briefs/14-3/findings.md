# Story review: slice 14-3

Reviewed at commit 643bb63. Stories: 4. Check: ok.

## Findings

- [x] S4 (context-gap): S4 holds two concerns in one run: wiring references into `Load` (R8, R9: skip rules, the per-Load cache, diagnostic order, duplicate skills) and building about twenty resolved records with their defaults (R10 to R13, C2); it is under the cap but not a Sonnet-level run. Fix: split into a Load-integration story (R8, R9, RES-errors, the error half of RES-duplicate, `Rig` still null) and an assembly story (R10 to R13, C2, the other RES outputs) that depends on it.
- [x] S1 (context-gap): for `agent_ref: local:agents/x/../impl`, which `IsUnsafeAgentPath` skips today, C1 says "unsafe paths formerly silently skipped become AIK3002" and R1 says a `..` that stays within the root is normalized and accepted. Fix: word C1 as "paths that R1 rejects become AIK3002; a contained `..` now loads the agent", and add that input to PATH-agent.
- [x] S2 (context-gap): TEXT-secret expects "lookalikes no diagnostic" without naming one, and the existing `URL with user info` pattern matches `ssh://git@github.com/org/repo.git`, a line that guidance and skill files commonly contain, so the brief does not say whether that line is an error. Fix: list the lookalike inputs in TEXT-secret and state the expected result for that line.
- [x] S3 (context-gap): R5 says "each link error is AIK3002 at the original skill scalar" and G3 puts every credential-like filename at the same scalar, so a skill with two nested links or two unsafe filenames gives one diagnostic or two identical ones; and for one failing skill directory declared twice, R8 ("file diagnostics once" against "reference errors per distinct source scalar") does not say whether the second scalar gets the error. Fix: state the count per skill scalar and code in R5 and add both inputs to SKILL-links.
- [x] S2 (context-gap): R3 and R7 do not say that the caps are checked from the file length before the content is read, so a 3 GiB `GUIDANCE.md` or skill file gives AIK3001 or runs out of memory, not AIK3005, and a skill directory of 100000 files is read in full. Fix: say in R3 and R7 that the length is checked before reading and that enumeration stops once a cap is exceeded.

## Not checked

- Conformance of the brief to spec 0003; only its lines on AIK4011 and AIK4020 were read.
- The link cases on Windows, and whether the test environment allows creating symbolic links.
- That the existing YAML tree parser can be reused for skill front matter as R6 assumes.
- The meaning of "canonical sorting" in R11; it belongs to 14-4.
- `artifacts/trials/` (acceptance tests), by role.

## Author resolutions, round 1

1. Split S4 into Load reference integration (R8/R9, T5, error outputs) and S5 resolved assembly
   (R10-R13/C2, T4, successful outputs). RES-distinct owns the separate-agent success variant.
2. C1 now distinguishes paths rejected by R1 from contained parent traversal; PATH-agent
   includes local:agents/x/../impl and requires normal agent loading.
3. TEXT-secret names four accepted lookalikes and explicitly rejects ssh://git@github.com/org/repo.git
   as URL with user info, preserving the existing detector.
4. R5 defines one scalar-level diagnostic per code per skill declaration (AIK4020 still suppresses
   other scalar diagnostics); R8 caches/replays failed-directory scalar diagnostics at every
   declaration while file-level failures appear once. SKILL-links and T5 cover repeated links,
   unsafe filenames, repeated failing skill declarations and no spurious duplicate-name error.
5. R3 checks length before reading, bounds reads to cap+1 and tests a sparse 3 GiB file. R7 checks
   aggregate count/length before content reads, stops enumeration at first cap exceedance,
   bounds subsequent reads against growth and tests sparse/count limits.

All five findings are addressed in the brief/items/split.
Author ran story.sh split, split-done and check: ok (5 stories, 43 items).

## Re-review, round 2

Reviewed at commit 7d6842d. Stories: 5. Check: ok.

The five findings of round 1 are resolved: the resolutions were read against C1, R3, R5, R7, R8,
the changed outputs, T5 and the five story blocks, and the four named lookalikes and the `ssh://`
line were checked against the existing detector patterns.

### Findings

- [x] S4 (context-gap): T5 has the S4 tests assert that a valid minimal/full `Load` "still" returns a null `Rig`, and S5 turns exactly that result non-null, while C2 allows the change only for the two `ValidTests`; S5 would turn an S4 test red. Fix: drop the null-`Rig` assertion for valid loads from T5 (the two `ValidTests` already hold it until C2), or name the S4 tests in C2.
- [x] S3 (context-gap): R7 stops enumeration at the first cap overage and keeps "already collected" diagnostics, but the enumeration order is not defined, so a skill with 150 files and one nested link (or one unsafe filename, or no `SKILL.md`) gives AIK3005 alone or AIK3005 with AIK3002/AIK4020/AIK3004, depending on the order the file system returns entries. Fix: state in R7 which scalar-level diagnostics a declaration reports when a cap is exceeded (for example AIK3005 only) and add that input to SKILL-limits.

## Not checked, round 2

- The sparse 3 GiB cases: whether the test file system supports sparse files was not checked.


## Author resolutions, round 2

1. T5 now asserts null Rig only for invalid calls and asserts only diagnostics for valid
   minimal/full calls; the two existing ValidTests retain the temporary null assertions until C2.
2. R7 now gives cap failure precedence over every diagnostic collected inside that skill read,
   emitting only AIK3005 at its declaration scalar while retaining unrelated diagnostics. R5
   defers returning on metadata-stage errors until the cap pass completes. SKILL-limits includes
   150-file mixed cases with links, unsafe filenames and missing SKILL.md, created in opposite
   orders, all requiring exactly AIK3005 and no other skill diagnostic.

Both round-2 findings addressed. No unresolved disagreement.

## Re-review, round 3

Reviewed at commit 28b784a. Stories: 5. Check: ok.

Both findings of round 2 are resolved: T5 no longer asserts a null `Rig` for valid loads, and R7
makes AIK3005 the only diagnostic of an over-limit skill read, with the mixed 150-file cases in
SKILL-limits. The change was read against R5, R7, R8 and G3.

New findings: None.

