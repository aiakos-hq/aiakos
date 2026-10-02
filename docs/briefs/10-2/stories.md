## S1: Contract helpers and contract tests
goal: The pure helpers next to the generated contract exist (negotiation, capability names, error catalogue, limits, path rules, placeholder expansion, command validator, harness event limits), with their contract tests.
depends: -
owns: R1, R2, R3, R4, R5, R7
outputs: N, E1, E2, E3, P1, P2, X1, X2, X3, X4, V1, V2, V3, V4, V5, V6, V7, V8, V9, V10, V11, V12, V13, H1, H2, H3
tests: T1, T2, T3
notes: The whole slice is one story: six small pure helpers in one project, each with a handful of one-line cases. R5 uses R3 (path rules) and the error catalogue of R2.
