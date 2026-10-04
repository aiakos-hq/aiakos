## S1: Diagnose and fix orchestrator disposal race
goal: The actor startup/shutdown test retains its assertions and its host disposes deterministically under a deterministic regression of the diagnosed lifetime boundary.
depends: -
owns: R1, R2, C1, R3, R4
outputs: E1, E2, E3, E4, E5, E6, E7
tests: T1
route: impl/senior
escalation: The read-only source diagnosis is preparatory analysis with no behavior change; the diagnosed boundary's ordering fix and deterministic proof form one lifetime concern that cannot be split into independently accepted implementation changes. Concurrency across host and tracer-provider ownership requires senior judgment.
notes: R1 source diagnosis determines the lifetime boundary R2/R4 fix and test; C1 preserves real actor behavior. R4 has direct and library-boundary evidence routes; stress R3/E6 supports evidence only. Senior performs read-only diagnosis in analysis-9-1 and records artifacts/diagnoses/9-1-1.md for lead/author/QA before behavior changes; author writes the regression and QA confirms both baseline failure and its exact diagnosed boundary before ready, then lead authorizes the run. Keep single impl/senior story. No broader production file change is authorized; diagnosed ServiceDefaults changes block on maintainer scope decision. E5 is supporting-command infrastructure; E7 defines deterministic-gate infrastructure and preserves done attempt accounting. BrokenMigrationTests contention failure belongs to a separate issue.
