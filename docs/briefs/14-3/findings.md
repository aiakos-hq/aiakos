# Story review: slice 14-3

Reviewed at commit 643bb63. Stories: 4. Check: ok.

## Findings

- [ ] S4 (context-gap): S4 holds two concerns in one run: wiring references into `Load` (R8, R9: skip rules, the per-Load cache, diagnostic order, duplicate skills) and building about twenty resolved records with their defaults (R10 to R13, C2); it is under the cap but not a Sonnet-level run. Fix: split into a Load-integration story (R8, R9, RES-errors, the error half of RES-duplicate, `Rig` still null) and an assembly story (R10 to R13, C2, the other RES outputs) that depends on it.
- [ ] S1 (context-gap): for `agent_ref: local:agents/x/../impl`, which `IsUnsafeAgentPath` skips today, C1 says "unsafe paths formerly silently skipped become AIK3002" and R1 says a `..` that stays within the root is normalized and accepted. Fix: word C1 as "paths that R1 rejects become AIK3002; a contained `..` now loads the agent", and add that input to PATH-agent.
- [ ] S2 (context-gap): TEXT-secret expects "lookalikes no diagnostic" without naming one, and the existing `URL with user info` pattern matches `ssh://git@github.com/org/repo.git`, a line that guidance and skill files commonly contain, so the brief does not say whether that line is an error. Fix: list the lookalike inputs in TEXT-secret and state the expected result for that line.
- [ ] S3 (context-gap): R5 says "each link error is AIK3002 at the original skill scalar" and G3 puts every credential-like filename at the same scalar, so a skill with two nested links or two unsafe filenames gives one diagnostic or two identical ones; and for one failing skill directory declared twice, R8 ("file diagnostics once" against "reference errors per distinct source scalar") does not say whether the second scalar gets the error. Fix: state the count per skill scalar and code in R5 and add both inputs to SKILL-links.
- [ ] S2 (context-gap): R3 and R7 do not say that the caps are checked from the file length before the content is read, so a 3 GiB `GUIDANCE.md` or skill file gives AIK3001 or runs out of memory, not AIK3005, and a skill directory of 100000 files is read in full. Fix: say in R3 and R7 that the length is checked before reading and that enumeration stops once a cap is exceeded.

## Not checked

- Conformance of the brief to spec 0003; only its lines on AIK4011 and AIK4020 were read.
- The link cases on Windows, and whether the test environment allows creating symbolic links.
- That the existing YAML tree parser can be reused for skill front matter as R6 assumes.
- The meaning of "canonical sorting" in R11; it belongs to 14-4.
- `artifacts/trials/` (acceptance tests), by role.
