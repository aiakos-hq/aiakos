# Story review: slice 12-1

Reviewed at commit 69dee4e. Stories: 5. Check: ok.

## Findings

- [x] S4 (judgment-gap): R6 requires SeatDir, Workdir and ProjectionRoot to be absolute and to match R5's grammar `^/[A-Za-z0-9._/-]+$`, but the loader keeps node paths verbatim and accepts `~/` (14-2 AIK5002), and the default `seat_root` is `~/aiakos/seats`, so every launch of a rig with the default binding is `INVALID_LAUNCH`, although the argv and files use only the `${AIAKOS_SEAT_HOME}` placeholder and never these values. Fix: in R6 and E6 accept both anchors the loader allows (`/` and `~/`), or drop the check on values the result does not use, and state the result for a loader-valid path with a character outside the grammar (a space, non-ASCII).
- [ ] S2 (context-gap): R5 rejects an api-key secret source that is not absolute, so the source in spec 0003's own example (`~/.config/aiakos/secrets/anthropic_api_key`) is `INVALID_LAUNCH`, and E5 tests only an absolute path, so the brief does not say this is intended. Fix: add the `~/` source to R5 and E5 with its exact result, and if that result is a rejection, have `lead` confirm it, because spec 0005 R36 says only "abs path".
- [x] S2 (context-gap): R3 fixes the default System.Text.Json encoder, which writes `'` as `'` (and `<`, `>`, `&`, `+` and non-ASCII as escapes), while R5 and E5 give the helper as `cat '/keys/anthropic.key'`, so it is open whether that text is the file's bytes or the parsed value, and an implementer matching it byte for byte would change the encoder. Fix: state in R5 and E5 the exact bytes of the `apiKeyHelper` line in the file.

## Not checked

- `IHarnessStateProfile`, `SeatFile`, `FileRoot`, `Core.InputValidator.CheckBody`/`CheckLead` and the loader's permission modes were read in the source and match R1 to R3 and R7 by name; R2's event kinds were not compared with the generated `HarnessEventKind` enum value by value.
- The argv, environment, file list and settings keys in R3 to R7 were compared with spec 0005 R4 to R9 and R36 by reading; the hook list and matchers in R4 were not compared with the spec's settings example line by line.
- S1 and S2 are independent and both add the same `ProjectReference` to `Aiakos.Orchestrator.csproj`; I did not check how the second pull request merges.
- No architecture test restricts the Orchestrator's references (only the Node has one); read, not run.
- Nothing was built; no Claude, node or tmux was involved.

## Author resolutions, round 1

Finding1: R6/E6 accept both / and ~/ anchors, retain node-local tilde and placeholder argv;
loader-valid space/non-ASCII paths have exact INVALID_LAUNCH. No owner HOME lookup.
Finding3: R5/E5 separate parsed helper text from exact JSON bytes (\u0027, two-space
indent, LF, last property/no comma).
Finding2 remains open pending lead decision qitem-20261006081111-97450886: runtime-node
HOME helper for safe ~/ reference or expressly approved rejection. No choice inferred.
