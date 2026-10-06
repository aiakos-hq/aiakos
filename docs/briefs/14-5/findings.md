# Story review: slice 14-5

Reviewed at commit 71f7d61. Stories: 4. Check: ok.

## Findings

- [ ] S1 (context-gap): S1, S2 and S3 have no dependency on each other and each needs G1's NFC step, but `CanonicalJson.NormalizeAndValidate` is private and G1 does not say where the write-then-parse wrapper lives, so three stories built in parallel can each add the same shared helper file or each edit `CanonicalJson.cs`, and the second and third pull request then conflict. Fix: state in G1 that each helper class keeps that step as a private member of its own and that `CanonicalJson.cs` is not touched, or name one helper owned by one story that the other two depend on.
- [ ] S4 (context-gap): R7, C1 and `LOAD-errors` say `Resolved=null` and "Resolved null", but the result record is `LoadResult(ResolvedRig? Rig, IReadOnlyList<Diagnostic> Diagnostics)` and has no member of that name. Fix: write `LoadResult.Rig` is null in R7, C1 and `LOAD-errors`.

## Not checked

- 14-4-3 is not merged at this commit, so the finalized `SpecHash` and the hash properties on the records were taken from `docs/briefs/14-4/brief.md`; the tie is named in S4's notes and nothing I ran enforces it.
- The two literal hashes were checked: SHA-256 of the empty input and of `[]` match the brief. The `H(x)` values and the GUIDE goldens were compared with R1 and R2 by reading, not by running a renderer.
- Earlier validation (14-2 AIK5002) already limits node paths to `/` and `~/`, so R5's two anchors cover every path that reaches Load; this was read in the 14-2 brief, not run.
- Left unraised: R5 reports a binding overlap as AIK3002 at the seat id in `rig.yaml` without naming a path, while spec 0003 lists AIK3002 for shared rig paths and earlier binding errors are AIK5xxx in `rig.env.yaml` with the path. The message is determinate and the author recorded it as a closed choice.
- Whether an earlier whole-result JSON golden exists for C1 to extend.
