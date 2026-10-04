## S1: Diagnose and fix orchestrator disposal race
goal: The actor startup/shutdown test retains its assertions and its host disposes deterministically under the same stress that reproduced the failure.
depends: -
owns: R1, R2, C1, R3
outputs: E1, E2, E3, E4, E5
tests: T1
route: impl/senior
escalation: Concurrency race with unknown cause across host and tracer-provider lifetime. Diagnosis, minimal ordering fix and the same before/after reproduction concern cannot be split into independently accepted changes; senior judgment is required.
notes: R1 determines the lifetime boundary R2 fixes; C1 preserves the actor behavior; R3/T1 prove the same race before and after. If the 200-run baseline does not reproduce, QA routes that evidence to lead before ready, rather than manufacturing a failure or treating historical CI as the gate. No broader production file change is authorized; a diagnosed TelemetryShutdownService change blocks on maintainer scope decision with evidence to lead. E5 distinguishes infrastructure result without waiving tools/story.sh attempt accounting.
