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
owns: R3, R4, R5, R9, R10, C2, C3
outputs: L1, L2, L3, L4, L5, L6, D1, D2, Q1
tests: T2, T3, T4, T5
notes: SeatSeed is written here; it inserts into the tables of S1 (R1). R4 returns the same seat row as R3. T2 covers R6. T3 covers safe percentage parsing (R9); T4 covers string inputs and null address rejection by returning null (R10); T5 covers false SpecDrift without a current launch (R3). The retry includes these items in the same read-query concern.
