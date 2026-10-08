## S1: Bounded capture and handle verification
goal: Capture preserves newest screen bytes and returns sanitized snapshots without input or the delivery gate.
depends: -
owns: C1, R1, R2, R3, R4, R5
outputs: E1, E2, E3, E4, E5
tests: T1, T2
notes: Introduces the input component with CaptureAsync and shared identity helpers; do not stub later methods. Uses merged 11-1 records and 11-2 client/process contracts. The capture-only tail option preserves existing default runner results. R2 mutation verification is an available helper tested directly; callers land in S2/S3. Visible-screen priority may exceed a requested cap as in the existing fake.

## S2: Serialized delivery stages and cancellation
goal: Validated input is loaded, typed, pasted and submitted once, with accurate failure/cancellation reports and buffer cleanup.
depends: S1
owns: R6, R7, R8, R9, R10
outputs: E6, E7, E8, E9, E10
tests: T3
notes: Uses S1 identity verification/capture and existing InputValidator; do not change validator messages. R6 provides the shared gate for later keys and a cancellation-only hook for 11-4 stop. At this stage test no-confirmer requests; callback/resubmit and keys enter in S3. Gate ownership extends through them when added. Requires the 11-2 client/runner contracts but not the starter for scripted tests.

## S3: Confirmation and named keys
goal: The supplied driver can confirm and resubmit once; explicit named keys share the delivery gate.
depends: S1, S2
owns: C2, R11, R12
outputs: E11, E12
tests: T4
notes: Uses R6 gate and R8/R9/R10 stages/cancellation/reporting; context capture uses S1 and bypasses the gate. Optional report reason keeps existing constructor calls compatible. No full host facade, readiness or driver implementation.

## S4: Safe input and capture diagnostics
goal: Existing input/capture operations emit required spans and measurements without exposing contents.
depends: S1, S2, S3
owns: R13
outputs: E13
tests: T5
notes: Reuses 11-2 child tmux diagnostics rather than emitting duplicate measurements. Listener tests assert positive emission plus sentinel absence; this story closes no live-environment risk.

## S5: Isolated real tmux delivery and capture evidence
goal: Opt-in component tests prove paste boundaries, maximum-size delivery, capture, concurrency and resubmit behavior on a private tmux socket.
depends: S1, S2, S3
owns: R14, R15
outputs: E14, E15
tests: T6
notes: MERGE PREREQUISITES: 11-2 starter stories S5/S6 must merge before this story is baselined/readied; router enforces cross-slice prerequisites. No substitute starter/facade if absent. These are permanent implementation integration tests, not author acceptance trials. CI and the complete ISessionHost contract fixture belong to 11-5; no owner environment or real Claude.
