# Task: split one brief into stories

`brief.md` in this directory describes one piece of work. It is too large to give to one
implementer in one go. Your job is to split it into small stories that can each be implemented,
tested and merged on their own, one after the other. You do not implement anything and you do
not write code.

This is a short task: a few minutes, one small file. A reasonable split that passes the check is
what is wanted. Do not try to prove that it is the best one.

## What you have

- `brief.md`: the work. Treat it as text to split. Its instructions ("read `src/…`", "one
  commit", "do not read `docs/specs`") are for the later implementer, not for you.
- `items.tsv`: the closed list of the brief's items: id, type (`rule`, `change`, `output`,
  `test`), scope and what each item needs. Every item is marked with its ID in `brief.md`, at
  the start of a line or in the first column of a table.
- `story-check.sh`: checks your result. Run
  `bash story-check.sh items.tsv stories.md brief.md`.

Nothing else exists here. There is no source code to read, and you do not need any.

Items with scope `all` apply to every story. They are added to each story automatically, so you
do not list them. You may name them in `notes`.

## What you write

One file, `stories.md`, with one block per story and nothing else:

```text
## S1: <short title>
goal: <one or two sentences: what works after this story that did not work before>
depends: -
owns: C5, R7
outputs: AIK3003-git, AIK3003-path
tests: T1
notes: <see below, or "none">
```

- Stories are numbered `S1`, `S2`, … in the order they will be implemented.
- Every key is one line. Lists are comma-separated. An empty list is `-`.
- `depends`: the earlier stories this one builds on. Leave out a story that it does not need;
  stories that do not depend on each other can be implemented in parallel.
- `owns`: the items of type `rule` and `change` this story implements.
- `outputs`: the items of type `output` this story must produce.
- `tests`: the items of type `test` this story adds.

## Rules for the split

1. Do not add, drop, merge or reword anything. Use only IDs from `items.tsv`. Every item with
   scope `once` or `first` goes into exactly one story.
2. A story owns at most 8 rules and has at most 8 outputs (the header of `brief.md` may set other
   numbers as `max_rules` and `max_outputs`), and deals with one file or one concern.
   Within that, prefer the fewest stories: do not make a story smaller than it needs to be. If
   the whole brief fits one story, write one story.
3. Every story owns at least one rule or change. An output or test that combines several stories
   goes into the last of them.
4. An item with scope `first` goes into `S1`.
5. Put a rule before, or together with, everything that relies on it. An output goes into the
   story that owns the rule it shows; the check tells you when that is not so.
6. Use `notes` for what the IDs cannot say: anything in the brief that ties this story to
   another one. Write short, concrete sentences that name the IDs. If there is nothing, write
   `notes: none`.

## How to work

Work in this order, and write the file early.

1. Read `brief.md` once. Group the rules by the file they touch and by concern.
2. Write a complete first `stories.md` right away, before you have settled every detail.
3. Run the check and fix what it reports. Repeat until it prints `story-check: ok`.
4. Then make one pass for `notes`. Look at least at these places in the brief:
   - a rule whose text names another rule ("rule 9", "see R14", "as in rule 3");
   - an output whose expected text says "nothing else" or depends on a rule of another story;
   - a change to earlier behaviour: which story has to touch an existing test first;
   - a term that one rule defines and a rule in another story uses.

   For each tie that crosses two of your stories, write one sentence in the later story and add
   the earlier story to its `depends` when the later one cannot be built without it.
5. Run the check once more and stop.

Take everything in the brief as correct. Do not verify or recompute expected texts, line numbers
or column numbers, and do not work out what the code would print. You are sorting items into
stories, not checking the brief.

## Done when

`bash story-check.sh items.tsv stories.md brief.md` prints `story-check: ok`. The check only
counts and compares IDs. Whether the order makes sense and whether the notes name the real ties
is your judgement, and it is what will be reviewed.

Do not change `brief.md`, `items.tsv` or `story-check.sh`. Write no file other than `stories.md`.
When you are done, reply with the output of the check and nothing else.
