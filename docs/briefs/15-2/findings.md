# Story review: slice 15-2

Reviewed at commit 683fc44. Stories: 6. Check: ok.

## Findings

- [x] S1 (context-gap): the public surface names no record, property or interface member: it says "immutable records for Version, problem details, seat status/detail, launch, command, node, rig registration and every endpoint in spec 0007's API table", and `ISeatAddressResolver` and `IRigRevisionRepository` have no signatures, so an implementer who may not read the spec has to invent every DTO name, JSON property and method. Fix: list each record with its properties, each interface with its members, and each route with its request and response type, in the brief.
- [x] S1 (context-gap): the brief says both that 13-4 owns `Aiakos.Core.CallerContext` and that S1 (no dependency) authenticates into it; the type is not on main, so S1 either waits for 13-4 without saying so or creates a type that 13-4 will also create. Fix: name the one story that creates `CallerContext` and make the other depend on it or say so in its notes.
- [x] S1 (context-gap): R2 and G1 do not name where the API token, the operator name, the Windows user name and the listen address come from (configuration keys, the token store interface T2 fakes, the port), nor the exact failure of a non-loopback bind. Fix: give the configuration keys, the interface with its members and the exact startup error in R2 and G1, with a row in E1.
- [x] S2 (judgment-gap): the read routes are listed only in R7, which S4 owns, while S2 implements them from R3 and R4 and does not depend on S4, and R4 and E3 give no JSON for any of the five responses. Fix: move the route list to R4 and put one exact response document per endpoint in E3.
- [x] S3 (context-gap): R5 and C2 leave open the request and response documents of `PUT /v1/rigs/{rig}`, how the hash pair is recomputed, the columns and types of the revision table, how it relates to the existing `aiakos.rig` table (which already holds `spec_hash`, `binding_hash`, `tool_version` and `resolved`), how seats are upserted and retired, and the migration's file name, which C2 makes conditional although `0003` is the next free number on main. Fix: give the exact SQL of the migration, the request and response JSON, the recomputation rule and the per-seat rules in R5 and C2, with one exact document per row of E4.
- [x] S4 (context-gap): R6 says the problem mapping "is exact" but lists four classes and no reasons or detail texts, and R7 does not say how the server learns the client's version, so E5's "exact status/reason/detail/retryable mapping" has nothing to be exact against. Fix: add the table of every reason with its status, detail text and retryable flag to R6, and the header or field that carries the client version to R7.
- [x] S6 (context-gap): R9 puts "one `cli.<command>` root continuation" and an OTLP export "enabled only when connection metadata supplies it" with a one second flush on the API server, without the set of command names, the activity source, or what connection metadata is on the server side; spec 0007 R26 describes these for the CLI. Fix: state in R9 and E7 the exact activity names and tags the server creates, and either define the export rule for the server or remove it from this slice.
- [x] S5 (context-gap): R8 and E6 give no route, request or response for the four commands and no mapping from each 13-4 reply to a status and reason. Fix: add that table to R8 and E6, marked as following the merged 13-4 names.

Author resolution: the exact DTO families, configuration, token-store interfaces, routes/bodies,
problem mapping, version header, revision/API contracts, and server activity rules are now stated
in the brief. `CallerContext` has one owner: 13-4 creates the Core type and dispatcher; 15-2
consumes it and declares no duplicate. The S5 bridge remains gated on that merge. OTLP export is
removed from this slice.

## Not checked

- Spec 0007's API section (R22 to R29, R34, the endpoint table) was read to confirm that the missing detail exists there; the brief was not compared with it rule by rule.
- `docs/briefs/TEMPLATE.md` asks for a self-contained brief with exact names and exact expected text; that is the standard the findings apply.
- 13-4 has no brief yet, so the seam named here (`ISeatCommandDispatcher`, `SeatUp`, `SeatDown`, `SeatSend`, `SeatCapture`) could not be compared with it.
- Size and order were not judged beyond the tie in the fourth finding: the stories cannot be sized until their surface is written down.
