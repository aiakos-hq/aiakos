## S1: Diagnose and fix orchestrator disposal race
goal: The actor startup/shutdown test retains its assertions and its host disposes deterministically under a deterministic regression of the diagnosed lifetime boundary.
depends: -
owns: R1, R2, C1, R3, R4
outputs: E1, E2, E3, E4, E5, E6
tests: T1
route: impl/senior
escalation: Concurrency race with unknown cause across host and tracer-provider lifetime. Diagnosis, minimal ordering fix and the same deterministic before/after lifetime concern cannot be split into independently accepted changes; senior judgment is required.
notes: R1 source diagnosis determines the lifetime boundary R2/R4 fix and test; C1 preserves real actor behavior. R4 has direct and library-boundary evidence routes; stress R3/E6 supports evidence only. Senior gives diagnosis to lead/author before behavior changes; author writes the regression and QA confirms baseline, then lead authorizes the run. Keep single impl/senior story. No broader production file change is authorized; diagnosed ServiceDefaults changes block on maintainer scope decision. E5 distinguishes infrastructure without waiving attempt accounting. BrokenMigrationTests contention failure belongs to a separate issue.
