## S1: Names, launch validation and input validation
goal: Seat addresses and session names are validated and formed, LaunchValidator rejects a bad launch spec with the first failure, and InputValidator checks a lead and a body. No session host exists yet.
depends: -
owns: R3, R4, R5, R6
outputs: -
tests: T2, T3, T4
notes: This story adds the interface and the types of the public surface that the validators need, and removes the placeholder ISessionHost from Seams.cs. R4 validates SeatAddress with R3. R7 applies to every reason and message here.

## S2: The testing library and the fake's lifecycle
goal: The Aiakos.Node.Testing library exists with FakeSessionHost and the contract suite. The fake starts, reports status, stops, publishes exit events and lists sessions.
depends: S1
owns: R1, R8, R9, R15, R16, R17, R18
outputs: K1, K2, K3, K4, K19, K20
tests: T1, T5
notes: R9 uses LaunchValidator (R4) and the session name of R3, both in S1. SessionHostContractTests is created here with these six facts; S3 and S4 add theirs to the same class. K4 and K19 need the PaneExited event of R17. R16 says stop cancels a delivery in flight: the delivery itself is R11 in S3, and its test K21 is in S4.

## S3: Fake delivery
goal: The fake delivers input: lead, one bracketed paste, submit, the confirmer, and the input gate that refuses a second delivery.
depends: S1, S2
owns: R11
outputs: K5, K6, K7, K8, K9, K10, K11, K13
tests: -
notes: R11 calls CheckLead and CheckBody of S1 (R5, R6) and needs a live session from R9 in S2. The DeliveryContext is created here; its ResubmitAsync and CaptureAsync are R12 and R14 in S4.

## S4: Resubmit, keys, capture and stale handles
goal: The rest of the fake: one resubmit per delivery, named keys, pane capture with truncation, and NotFound for a stale handle on every operation.
depends: S2, S3
owns: R10, R12, R13, R14
outputs: K12, K14, K15, K16, K17, K18, K21, K22
tests: -
notes: R12 extends the delivery of R11 (S3). R13 takes the input gate of R11. K14 and K21 combine this story with a delivery in flight (R11) and with stop (R16 in S2). R10 changes every operation, also those of S2 and S3: K22 checks deliver, keys, capture and stop.
