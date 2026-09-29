# aiakos-dev — the rig that builds Aiakos

From milestone M1 (the self-hosting threshold), Aiakos is developed by this rig. Its files will
live here once the rig file format exists (M1): `rig.yaml`, agent definitions, `CULTURE.md`.
Machine-specific settings go in a local, uncommitted `rig.env.yaml`.

Planned shape for M1:

| Seat | Harness | Checkout | Role |
|---|---|---|---|
| `lead` | human (maintainer) | — | Owns priorities, approves specs and merges |
| `impl` | Claude Code | `seat-worktree` | Implements issues from specs, opens PRs |
| `review` | Claude Code | `shared-readonly` | Reviews the exact diff against the spec and acceptance criteria |

**Bootstrap rule ([ADR 0008](../../docs/adr/0008-bootstrap-rule.md)):** this rig always runs on
the last *released* Aiakos (pinned `dotnet tool`), never on the working tree it is changing.
