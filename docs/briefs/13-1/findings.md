# Story review: slice 13-1

Reviewed on the branch `chore/story-workflow`, with the conversion of the brief to items.
Stories: 6 (the splitter's, unchanged). Check: ok.

## Findings

- [ ] Brief is not closed: B2a, B2b and B2c point at the transition tables of spec 0006 (lines
  379–638). The 40 rows (S1–S16, A1–A16, U1–U8) are the real items, and their expected values
  are not in the brief. Fix: make each row an item of type output with the rule that drives it
  as its need, or copy the tables into the brief.
- [ ] S1: `T1` (one test per session row, every column) cannot pass in S1. Rows S1, S3, S10,
  S13, S15 and S16 are driven by B14, B15, B19, B6 and B12, which are in S5 and S6. Fix: with
  rows as items, each row's test goes to the story of the rule that drives it.
- [ ] S2, S3: the same for `T2` and `T3`: rows A15 and A16 need B19 and B11, rows U6, U7 and U8
  need B11 and B8.
- [ ] Brief: `status` is `draft`. Fix: the maintainer approves it.

## Not checked

- The spec lines themselves were not read for this review; the row-to-rule mapping above comes
  from the brief's own text.
- `needs` in `items.tsv` was written during the conversion and not checked against the spec.
