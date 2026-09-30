---
id: NNNN
title: Short title
status: draft            # draft | accepted | implemented | superseded
issue: https://github.com/aiakos-hq/aiakos/issues/N
milestone: M1
owner: "@handle or seat@rig"
---

# NNNN — Short title

## Context
Why this is needed now. Link the issue, relevant ADRs and plan sections.

## Goals
- Observable outcomes this spec delivers.

## Non-goals / out of scope
- What this deliberately does not do.

## Requirements
Numbered, testable statements (R1, R2, …).

## Design
Components, interfaces, data (tables/migrations), protocols, sequence of operations.
Call out which interfaces (`ISessionHost`, `ISandbox`, …) are touched.

## Acceptance criteria
- [ ] AC1 — stated as something a reviewer can verify (command + expected result).

## Test plan
Unit, integration (real tmux/Docker where relevant), and the manual demo.

## Risks and open questions
Open questions with a recommendation each (Q1, …), resolved into decisions (D1, …) in review.
Risks get stable IDs (RK1, …) with how and when each is checked, and are indexed in
[`docs/risks.md`](../risks.md) (register and "Open risks by issue") in the same PR.
- …

## Changes after acceptance
Dated notes when the implementation deviated from the design above.
