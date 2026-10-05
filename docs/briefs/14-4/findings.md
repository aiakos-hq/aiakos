# Story review: slice 14-4

Reviewed at commit 784d9fe. Stories: 3. Check: ok.

## Findings

- [x] S1 (context-gap): R1 and `JSON-exact` require U+FFFD for an isolated surrogate, but a `JsonElement` can only hold one as an escape in parsed text (`"\ud800"`), and `GetString()` and `JsonProperty.Name` throw `InvalidOperationException` on it (run on .NET 10), so the implementer must decide between decoding `GetRawText()` by hand and an untestable rule. Fix: state in R1 that the value arrives as a `\uXXXX` escape and that Write decodes the raw text itself, with the exact input in `JSON-exact`, or remove the surrogate clause and variant.
- [x] S1 (context-gap): the brief gives no result for `CanonicalJson.Write` on input outside its subset (a non-integer or out-of-range number such as `1.5`, a `default(JsonElement)`, an object with duplicate or NFC-colliding keys such as `{"a":1,"a":2}`), and an exception on any input blocks a story at the gate. Fix: add to R1 the exact behaviour for each (the exception type and message, or the defined output) and a `JSON-exact` variant for it.
- [x] S3 (context-gap): C1 leaves "if one exists" open and gives no expected values for an earlier serialization golden of the full fixture (14-3 T4, spec AC1 `resolved.json`), which after R8 would contain that fixture's hashes, `Canonical` and `ToolVersion`, while R7 forbids taking expected hashes from the implementation. Fix: say in C1 how those expected values are obtained (for example a checked-in canonical JSON whose SHA-256 is computed outside the implementation) or state that no earlier test compares the added properties.
- [ ] S1 (context-gap): R1 now accepts the spelling `-0` (it matches `-?(0|[1-9][0-9]*)`) but gives no output for it, and writing the Int64 value gives `0` while writing the raw spelling gives `-0`. Fix: state the output for `-0` in R1 (or make it unsupported) and add it as a `JSON-exact` variant.

## Not checked

- 14-3-5 is not merged at this commit, so the record shapes were read from `docs/briefs/14-3/brief.md` only; whether its tests compare whole serialized rigs is unknown.
- The two minimal hashes were recomputed with `sha256sum` over the brief's exact bytes and match; the minimal JSON was compared with the fixture and 14-3 R10–R12 by reading, not by running a loader.
- Whether `tools/story.sh` holds S2 and S3 until 14-3-5 is merged; the tie is named only in the notes.
- The full fixture has no culture, guidance or skill files; R7's generated content was not tried.
- S2 (five rules, five outputs) was judged to fit one run; no split was asked for.

## Author resolutions (round 1)

- First finding: removed the U+FFFD repair rule. R1 explicitly translates malformed escaped value/key decoding failures to the uniform rejection contract; JSON-exact names exact valid-pair and invalid-surrogate inputs. No raw-text decoder is needed.
- Second finding: R1 rejects Undefined, noninteger lexical numbers, Int64 overflow, malformed surrogate strings/keys, duplicate and NFC-colliding keys with ArgumentException and exact message; JSON-exact names each variant.
- Third finding: C1 now names the predecessor-test inspection and independent golden derivation procedure, reviewed canonical JSON assets, independent sha256sum/hashlib, literal expected hashes, content-map derivation and a single assembly-version placeholder. Earlier field-only tests remain field-only; no new full golden is inferred.

Architect, round 1: the three resolutions were read at 784d9fe and are accepted; one new finding above comes from the changed R1.
