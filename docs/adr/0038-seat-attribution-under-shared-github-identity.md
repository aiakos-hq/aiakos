---
id: 0038
title: In M1, seats act under the maintainer's GitHub identity; attribution is by trailer and prefix
status: accepted
date: 2026-10-01
---

# 0038 — In M1, seats act under the maintainer's GitHub identity; attribution is by trailer and prefix

## Context
The `aiakos-dev` seats open PRs, push branches and post reviews. Giving each seat its own GitHub
account means delivering a token per seat as a secret, which arrives properly only with sandboxed
seats and seat secrets (M6). GitHub does not let an account approve or request changes on its own
PR. The definition of done still has to show that a reviewer checked the exact diff and that the
maintainer approved. From [spec 0008](../specs/0008-aiakos-dev-rig.md) (R7, R9, D2).

## Decision
- In M1 all seats use the **maintainer's `gh` login and Git identity** in WSL.
- Work is attributed by convention: every seat commit has the trailer
  **`Aiakos-Seat: <seat>@<rig>`**; PR bodies say **`Implemented by <seat>@<rig>`**; reviews start
  with **`Review by <seat>@<rig> at <head sha>`**.
- The reviewer seat posts **comment reviews only** (`gh pr review --comment`), naming the head
  commit it reviewed and a verdict. **Approving and merging stay with the maintainer.**
- Seats are denied merging, approving, force-pushing, pushing to `main` and GitHub administration;
  branch protection on `main` is the real enforcement.

## Consequences
No new credentials in M1, and the maintainer stays the only approver. GitHub's history shows the
maintainer for everything; which seat did what is recovered from trailers, prefixes and the Aiakos
database, not from GitHub accounts (spec 0008 RK2). Revisited when seat secrets exist (M6), where a
machine account per rig or seat becomes possible.

## Alternatives considered
- **A machine account (`aiakos-bot`) now**: real reviews and separate audit, but a token per seat
  to deliver and protect before the secret model for seats exists.
- **No attribution**: the record could not tell the maintainer's own work from a seat's.
