# Story review: slice 12-2

Reviewed at commit ffebbcbc5ebc537605afc97f97026fafac97e620. Stories: 6. Check: ok.

## Findings

- [x] S6 (context-gap): R9 gives `aiakos.node.hooks.latency` a local meaning while spec 0005 R38 still says "relay stamp to ingest"; the maintainer chose option 1 on 2026-10-08 (qitem-20261008130658-f322ba86), so the spec must change on this branch. Fix: edit spec 0005 R38 here to "time from request received to enqueued, in the ingest; histogram", and remove from R9 and the S6 `notes` the sentences that call the meaning pending architect/maintainer review.
- [x] S6 (context-gap): round 1 left R9 writing the histogram as `hooks.latency` with no instrument unit, a start point of "request-body-read/enqueue" that can be read as either request received or body read, and no statement of which requests record it, so E9's "latency uses local elapsed duration" still cannot be an exact test. Fix: in R9 write `aiakos.node.hooks.latency`, unit `ms`, measured from request received to enqueued and recorded only for accepted (204) requests, and name those in E9.
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

## Architect, round 2

Read at ffebbcb. Eight round-1 resolutions accepted as written (SeatFile names, relay stdin, registry order and reuse, logger seam, channel bound, lifecycle errors, E5/E6 kinds, interleaved E7 case under spec 0005 R19's per-launch scope). The R9 naming finding stays checked for the ActivitySource name and the 401 `name` tag; its instrument name and unit are reopened as the second open finding above.

Not checked in round 2: the state of `HookIngest` after `HOOK_INGEST_BIND_FAILED` (R5 does not say whether a later `StartAsync` may retry; E5 has no case for it); `tools/story.sh show` output; whether `analysis-pr` accepts a spec file changed on the analysis branch.

## Author resolution, round 2

Both open S6 findings resolved following the maintainer's option 1 decision
(qitem-20261008130658-f322ba86). Spec 0005 R38 now measures time from request received
to enqueued, in the ingest. R9 and E9 specify `aiakos.node.hooks.latency`, unit `ms`,
request received to enqueued, accepted (204) requests only; E9 excludes 401/413 records.
R9 and S6 notes no longer describe the latency decision as pending review.
