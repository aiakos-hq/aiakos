# Story review: slice 12-2

Reviewed at commit 3c7bcefa94f45a2fda63f7f85010c530dc72774e. Stories: 6. Check: ok.

## Findings

- [ ] S6 (context-gap): R9 gives `aiakos.node.hooks.latency` a new meaning (local body-read/enqueue time) while spec 0005 R38 defines it as "relay stamp to ingest", and the relay has no time stamp, so the brief changes the spec under the same name. Fix: keep R9 as the proposal but add the matching edit of spec 0005 R38 to this analysis branch, in the form the maintainer chooses (question sent to the maintainer; see artifacts/handoffs/architect-12-2-maintainer.md in the main checkout).
- [x] S6 (context-gap): R9 names the histogram `hooks.latency` without the `aiakos.node.` prefix and gives no unit, no ActivitySource name for `hook_ingest.receive`, and no `name` tag for a 401 other than an unknown token (missing, malformed, multiple, expired), so E9's "exact" telemetry cannot be written as a test. Fix: write the full instrument name and unit, the ActivitySource name, and the `name` tag for every 401 case in R9.
- [x] S3 (context-gap): R6 logs `HOOK_CONSUMER_FAILED` and E9 scans logs, but the exact `HookIngest` constructor takes no logger, so the implementer must choose a sink and the tests cannot capture it. Fix: add the logging seam to the public surface (for example an `ILoggerFactory` constructor parameter) or state the sink and how a test reads it.
- [x] S3 (context-gap): E5 and E6 do not say which kind their posts use, and S5 later coalesces `status` posts of one seat, so an S3 test that sends several `status` posts within 1 s and expects each delivered turns red when S5 merges. Fix: state in E5/E6 that multi-post cases use kind `hook` and that `status` is posted at most once per seat in S3's tests.
- [x] S3 (context-gap): R6 does not say whether the in-memory channel is bounded, nor the answer when it is full, although a blocked consumer with 1 MiB bodies is exactly E6's case. Fix: state in R6 that the channel is unbounded, or its capacity and the exact status returned when it is full.
- [x] S3 (context-gap): R5 and T3 ask for "full lifecycle" and port-binding checks without exact expected results for a port outside 0..65535, `BaseUri` before `StartAsync`, a second `StartAsync`, `StopAsync` before start, and the exception type for an occupied port. Fix: give the exact exception type (and message where fixed) for each in R5/E5, or remove the cases from T3.
- [x] S1 (judgment-gap): R2 requires buffering stdin in a temporary file but names no directory or mode, adds `mktemp` to the spec's dependencies (`sh`, `curl`, `flock`), and leaves a prompt on disk when Claude kills the hook with SIGKILL, while `curl --data-binary @-` reading the relay's own stdin keeps every byte without a file. Fix: replace the temporary file in R2 with curl reading stdin directly (draining stdin on the no-post paths), or state the directory, mode 0600 and the expected result after SIGKILL.
- [x] S1 (judgment-gap): R1 and E1 name `RelativePath`, but the generated `Aiakos.Contracts.Node.V1.SeatFile` has `Path` (and `Root` is `FileRoot.SeatHome`, `Content` holds the bytes). Fix: use the generated names `Root=FileRoot.SeatHome`, `Path`, `Content`, `Mode`, `Expand` in R1 and E1.
- [x] S2 (context-gap): R4 does not say which error wins when one `Register` call collides on both token and LaunchId, nor whether a token or LaunchId may be registered again after its entry expired and was removed. Fix: state the order of the two checks and the answer for reuse after expiry in R4, with a case in E4.
- [x] S4 (context-gap): the counter is per seat but R7 tracks per launch, and R4 lets an ended launch L1 and a new launch L2 of one seat coexist for 60 s, so L1 posting 10, L2 posting 11 and 12, then L1 posting 13 reports a gap for L1 although nothing was lost; E7 does not list this case. Fix: add the interleaved case to E7 with the intended answer, and if that answer is "no gap" change R7's scope accordingly.

## Not checked

- `bash tools/story.sh show 12-2 <n>` for each story: I read brief.md and stories.md directly, not the six generated story texts.
- Whether S1's acceptance tests can be one gate: E1 is in Aiakos.Orchestrator.Tests and E2/E3 are in Aiakos.Node.Tests, and the gate scripts I have seen run one test project.
- Whether S1 (R1, R2, R3 with shell, flock and /proc tests) fits one Sonnet run; I did not ask for a split.
- ADR 0028 and the parts of spec 0005 outside R16–R19, R24, R37, R38, AC4, AC5 and the relay listing.
- Existing Node code beyond NodeTelemetry.cs and Aiakos.Node.csproj; the 12-1 brief beyond its relay lines.
- That E6's p99 < 50 ms bar is stable on the gate machine.

## Author resolution, round 1

Nine findings resolved in the brief. R9 latency meaning remains open pending the already-routed maintainer answer. Generated SeatFile names corrected; relay uses direct stdin with no temporary payload; logger and exact lifecycle/registry/coalescing test seams added. Telemetry names/unit/source/401 tags explicit. Per-launch interleaving output is deliberately one apparent gap, following accepted spec R19; its limitation is explicit rather than changing scope without a spec decision.
