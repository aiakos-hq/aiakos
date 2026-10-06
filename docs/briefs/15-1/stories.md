## S1: CLI grammar and project foundation
goal: A real production System.CommandLine parser exposes the complete command tree with immutable parsed requests.
depends: -
owns: R1, R2
outputs: E1
tests: T1
notes: Creates CLI Spec/package references, new Cli.Tests project and solution entry; later stories add source/test files only. Does not replace Program yet; C1 belongs S5. No API or host prerequisite.

## S2: Pure invocation validation
goal: Invalid command combinations fail locally without body, instance or API access.
depends: S1
owns: R3
outputs: E2
tests: T2
notes: Depends only on S1 CliRequest record; independent of renderer/git/application implementation.

## S3: Local dry-run output
goal: Pure renderers produce deterministic text and JSON summaries without file payloads or credentials.
depends: S1
owns: R4, R5
outputs: E3, E4
tests: T3
notes: S1 adds Spec reference/test project. No validation/git/application prerequisite; directly constructs existing Spec records.

## S4: Read-only env tracking warning
goal: A bounded git checker reports tracked local bindings without exposing subprocess content.
depends: S1
owns: R6
outputs: E5
tests: T4
notes: S1 creates project/test infrastructure only. No renderer/validator prerequisite; private temporary repository only.

## S5: Executable local dry run
goal: The CLI replaces its preview greeting with help and local loader-driven dry run and honest unsupported operational commands.
depends: S2, S3, S4
owns: C1, R7, R8, R9
outputs: E6, E7, E8
tests: T5
notes: Combines parser/validator/renderers/git check; owns Program/README/package-description changes. Later15-3/15-4 remove only the explicit not-implemented execution limit, not local validation. No local API, instance host, database, node or tmux dependency.
