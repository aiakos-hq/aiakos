# Story review: slice 16-1

Reviewed at commit 35dc1d5. Stories: 9. Check: ok.

## Findings

- [x] S9 (context-gap): R10 and `PROJECT-real` compare each rendered `CLAUDE.md` with a committed literal golden that must not come from the renderer, but the first line of that file carries the rig's spec hash, which only the loader can produce and which changes with every edit of a rig file, so the implementer has to choose between pasting the loader's value and inventing a placeholder. Fix: state in R10 and T9 how the golden holds that header value (for example a named placeholder replaced by the returned `SpecHash`, everything else literal).
- [x] S9 (context-gap): T9 asserts "the exact committed rig file list" and rejects a "local binding", while R9's README tells the lead to create the ignored `rigs/aiakos-dev/rig.env.yaml` in the checkout; a test that lists the directory then fails on the maintainer's machine, and one that lists tracked files does not. Fix: state in T9 and `LOAD-real` that the list is the git-tracked files (or that an ignored `rig.env.yaml` on disk is not a failure).
- [x] S7 (context-gap): R8 says the clone's origin is accepted "with no trailing slash, credentials or other remotes", which leaves open whether a clone that has a second remote beside a valid `origin` fails the clone group. Fix: state the result for an additional remote in R8 and add it to `PREREQ-fail` or `PREREQ-ok`.

## Not checked

- The permission strings in R3 and R4 are the same set as in spec 0008 (compared mechanically); their order inside each list and the prose of R2, R5, R6 and R9 were not compared with the spec word by word.
- The loader accepts what the rig uses, by reading: the permission rule pattern admits every string in R3 and R4, `lead`, `impl` and `review` are not reserved seat ids, the skill names match their directory names, and YamlDotNet reaches the test project through `Aiakos.Spec`. The loader was not run on these files.
- Left unraised, because the lists are the accepted spec's: `Bash(gh pr review --approve:*)` and its three siblings match only when the flag comes directly after `review`, so `gh pr review 57 --approve` is not denied by a rule; spec 0008 relies on GitHub refusing a review of one's own pull request, and its AC5 is a live check for 16-3.
- The spec 0008 R15 sentence in this branch is said to be lead-approved; I did not look for the approval.
- `check-prereqs.sh` exists only as rules; no shell was run. S7 (two rules, three outputs, a nine-group script with fakes) was judged to fit one run.
- A plain diff against `origin/main` shows unrelated files because main moved after the branch's merge; against the merge base the branch touches the three 16-1 files, the briefs index and spec 0008.

Author resolution, round 1: R10/T9/PROJECT-real use the sole first-line {{SPEC_HASH}} placeholder replaced by Rig.SpecHash; all other golden bytes are literal. T9/LOAD-real enumerate git-tracked rig files and permit an ignored local binding on disk. R8/PREREQ-ok accept extra remotes when origin is valid.
