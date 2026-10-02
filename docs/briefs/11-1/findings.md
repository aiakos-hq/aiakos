# Story review: slice 11-1

Reviewed on the branch `chore/story-workflow`, with the conversion of the brief to items.
Stories: 4. Check: ok.

## Findings

- [x] Split: the splitter made 10 stories, five of them with one rule, and a first story (the
  library project alone) that no test could accept. Fix: merged into 4 stories along the same
  seams (validators; fake lifecycle; delivery; the rest of the fake).
- [ ] Brief, R18: `AdoptAsync` and `GetAttachCommand` have no expected output with exact text;
  `K2` only asserts that `ListAsync` is empty. Fix: add contract tests for a non-empty listing,
  for adopt and for the attach command, or name the unit tests that cover them.
- [ ] Brief: `status` is `draft`. Fix: the maintainer approves it.

## Not checked

- The public surface against spec 0004.
- Whether S2 (7 rules, a new project and the contract suite) fits one implementer run. It is
  the largest story of the slice.
