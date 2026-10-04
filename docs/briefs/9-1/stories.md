## S1: Behavior-neutral shutdown test-control seam
goal: The existing shared shutdown service exposes controllable scheduling and time without changing production shutdown behavior.
depends: -
owns: C3
outputs: E9, E10
tests: T2
route: impl
notes: C3 fixes both constructor signatures and production defaults. Add no ownership repair here. E10 characterizes the current detached behavior only in S1; C2 deliberately corrects it in S2. Tests run unchanged orchestrator and node suites. Main must contain this seam before S2 acceptance baseline; no counter reset or premature ownership-fix attempt is authorized.

## S2: Fix shutdown admission and provider ownership
goal: At the diagnosed host boundary, late queued work cannot enter a provider and already-entered shutdown finishes before DI disposal.
depends: S1
owns: R1, R2, C1, R3, R4, C2
outputs: E1, E2, E3, E4, E5, E6, E7, E8
tests: T1
route: impl/senior
escalation: Following the separate neutral seam, admission closure, joining entered work and timeout/cancellation/fault handling are one shared lifetime boundary; splitting those changes would permit unsafe disposal in intermediate states. Concurrent host/provider ownership requires senior judgment.
notes: Read the existing diagnosis at artifacts/diagnoses/9-1-1.md. S1 provides the exact controls used by author/QA for the R4(b) baseline on main-with-seam. C2/E8 adopt the 2026-10-04 maintainer decision: close admission at timeoutMs+500; await entered work even beyond that deadline; retain provider timeoutMs and DI ownership. C1 preserves actor assertions. Focused shared-service tests plus both full Release suites cover the node effect. R3 stress is separate supporting evidence, not gate. QA confirms the baseline observer matches the diagnosed boundary before ready. Other file changes remain out of scope.
