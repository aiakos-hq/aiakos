# Story review: slice 16-2

Reviewed at commit b60d71d. Stories: 3. Check: ok.

## Findings

- [ ] S2 (context-gap): R2 filters the `pull_request` trigger by path and R4 says the check is to be made required after its first run on `main`; a required check that a path filter skips never reports, so every pull request that does not touch the rig or the manifest could no longer be merged. Fix: remove the path filter from the `pull_request` trigger in R2/E2 (with the matching edit of spec 0008 R12 on this branch), or state in R4 that the check must not be made required while the filter exists.
- [ ] S2 (context-gap): E3 expects the released dry-run to show `impl` and `review` with node `wsl-local`, model `opus` and checkout `seat-worktree`, but the brief does not give the lines of the dry-run output to match, and that format belongs to slice 15-1, which the tests seat does not read. Fix: quote in E3 the exact expected output lines for the two seats and for `lead`, or state the fields to match and the form (text or `--json`) the gate uses.
- [ ] S3 (context-gap): E4 and E5 are called exact text, but apart from the heading, the commands and one sentence, R4 and R5 describe the content in prose (status wording, the required-check statement, "format-requiring rig edits follow", the minor-version rule, the development-test isolation), so the tests seat and the implementer cannot arrive at the same strings. Fix: give in E4/E5 the exact sentences or marker phrases the tests assert, one per statement.

## Not checked

- `bash tools/story.sh show 16-2 <n>` for each story: I read brief.md and stories.md directly.
- That release 0.1.0 exists and restores; the brief names it as the router's check before S1 is ready.
- Whether `up --dry-run` exits non-zero when it reports only warnings. If it exits 0, the `rig-compat` job does not enforce spec 0008 R3's "zero warnings"; the brief checks that only once, in the gate.
- That the gate machine (WSL) can run `dotnet tool restore` and the released tool for E1/E3; the package layout in spec 0007 (`tools/net10.0/any`) suggests it can.
- ADR 0037, spec 0003 R32, and the current text of rigs/aiakos-dev/README.md beyond its Upgrade lines; AiakosDevRunbookTests.
- R5's step order against spec 0007 "Upgrading the released instance": I compared the four steps and found them consistent; the minor-version rule is the brief's own, stricter than the spec.
