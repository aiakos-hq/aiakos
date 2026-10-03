# Story review: slice 11-1

Reviewed on the branch `chore/story-workflow`, with the conversion of the brief to items.
Original stories: 4. Amendment: 5 stories; S1 unchanged.

## Findings

- [x] Split: the splitter made 10 stories, five of them with one rule, and a first story (the
  library project alone) that no test could accept. Fix: merged into 4 stories along the same
  seams (validators; fake lifecycle; delivery; the rest of the fake).
- [x] Brief, R18: `AdoptAsync` and `GetAttachCommand` have no expected output with exact text;
  `K2` only asserts that `ListAsync` is empty. Fix: add contract tests for a non-empty listing,
  for adopt and for the attach command, or name the unit tests that cover them. Resolved (context-gap): added K23/K24/K25 with
  exact results and named contract Facts, owned by new S5.
- [x] Brief: `status` is `draft`. Set to `approved` in this amendment at lead's explicit instruction; approval takes effect when the maintainer merges the PR.

## Not checked

- The public surface against spec 0004.
## S2 size assessment

- Resolved (context-gap): move the independent R18 listing/adopt/attach seam to S5.
  S2 now has 6 rules and 6 output items, builds only the lifecycle part of the shared
  contract suite, and fits one implementer run. Start, dead-pane replacement, status,
  stop and exit events share the same session/exit state; further separation would couple
  their implementation. The new project follows the prescribed R1 setup without design
  choices. S3/S4/S5 extend the suite later. S1's items and scope are unchanged.
