## S1: Seat model migration
goal: The nine seat tables exist in schema aiakos after migration, exactly as the spec's SQL block defines them, and the migration tests show their constraints hold.
depends: -
owns: C1, R1
outputs: -
tests: T1
notes: The existing test that counts or names applied migration scripts changes here. S2 reads the tables this story creates.

## S2: SeatQueries and its read models
goal: SeatQueries lists seats for ps, returns one seat's detail, and returns a launch or a command by ID, always within one tenant and without ever returning payloads or token hashes.
depends: S1
owns: R3, R4, R5
outputs: L1, L2, L3, L4, L5, L6, D1, D2, Q1
tests: T2
notes: SeatSeed is written here; it inserts into the tables of S1 (R1). R4 returns the same seat row as R3. T2 is the test for R6: nothing returned may contain the delivery body or the token hash.
