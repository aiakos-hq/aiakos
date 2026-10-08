# Story review: slice 12-3

Reviewed at commit 7d2f9795d791427e759481761aba2403301a9689. Stories: 5. Check: ok.

## Findings

- [x] S1 (context-gap): R4 states the interim result "PermissionRequest and permission_prompt are initially Other", T1 asks for "all R2 source/end cases" and G2 for permanent tests of every owned rule, so a permanent S1 test may pin PermissionRequest, permission_prompt, SessionStart/SessionEnd clear or statusLine as Other, and S3, S4 or S2 then turns an earlier test red. Fix: say in T1 and T2 that the interim OTHER fallback is asserted only with the unknown name of E1, never with a native name that a later story of this slice owns.
- [x] S5 (context-gap): S1 and S5 have no dependency on each other and may run in parallel, but both need the fixture loader and the solution-root finder in tests/Aiakos.Node.Tests/Harnesses/ClaudeCode/, whose owner and type name the brief does not give, so the second merge conflicts or defines the type twice. Fix: name the loader type and file in the brief and assign it to S1 with S1 in `depends` of S5, or give S5 its own named text-fixture loader and say so in its notes.
- [x] S1 (context-gap): G2 gives no answer for a body that is valid JSON but holds a string that cannot be decoded, such as `{"session_id":"\ud800"}` or raw bytes FF FE inside a string value: on net10 `JsonDocument.Parse` accepts both and `GetString` then throws InvalidOperationException (run in a scratch project), which is an exception on an input in S1 and again in the R8 string comparison of S3. Fix: add to G2 whether such a string makes the whole payload malformed (OTHER, state unchanged) or only that field absent, state the same for a string inside tool_input, and add the input to E1.
- [x] S2 (context-gap): R5 accepts a value "only if an integer" and E6 makes "fractional" absent, but does not say whether the tokens `16.0` and `1e2` are integers, and the implementer and the author of the acceptance tests can read it differently (`TryGetUInt32` refuses both). Fix: state in R5 that an integer is a number token without fraction or exponent, or the opposite, and add one such value to E6.

## Not checked

- The classifier needles of R12 against a real Claude 2.1.284 screen: "Not logged in", "Error parsing settings" and "Invalid settings" are in no spike; the brief calls them synthetic and leaves live observation to 12-5.
- Line wrapping of a needle in a narrow pane capture.
- The 12-2 brief and whether its IngestedPayload has Name and Body.
- The claims about what 11-2 has merged (the 11-4 dependency paragraph).
- Payload field names beyond spec 0005's mapping tables and the samples of spikes 0001, 0002 and 0005; no build, no test and no live Claude run.
- Growth of the open-tool state when a tool never finishes (a denied permission keeps its tool_input until /clear); it follows spec R23 and is a backlog matter, not a finding.

## Author resolution: round 1

All four context-gap findings addressed: G2/T1/T2 restrict permanent interim-fallback
assertions; T1 names and assigns ClaudeFixtureLoader to S1 and S5 depends on S1; G2/E1
make every undecodable JSON string/property name (including nested tool_input) a whole-payload
malformation before state mutation; R5/E6 reject fraction/exponent integer tokens, including
16.0 and 1e2. Items retain their existing IDs and ownership; no additional rule is introduced
outside those items. Architect re-review is pending.
