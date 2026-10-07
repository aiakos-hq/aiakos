# Story review: slice 15-4

Reviewed at commit 1063b39. Stories: 22. Check: ok.

## Findings

- [x] S7, S8, S9 (context-gap): "Public surface" declares no type for database preparation, the instance lock, the host, the node process factory, connection file discovery, logging/telemetry or the admin host port, so the acceptance tests that are written before the run cannot compile against a "smallest private implementation", and E7 and E9 have no entry point before S9 wires the commands. Fix: declare the public types, constructors and ports that each of S7–S9 owns, as the brief does for S1–S6.
  Author resolution 1: Public surface now declares typed process/database/lock/node/log/telemetry/connection/host/admin/client/application ports and constructors; each story owns its behavior entry.
- [x] S1 (context-gap): R2's "must not equal any corresponding derived port" reads as a same-offset comparison, which only catches equal bases, so bases 7180 and 7181 (7181+0 = 7180+1) and base 5170 against dev (5170+10 = 5180) are accepted. Fix: say that the six-port sets must be disjoint, name the dev port set as reserved, and add the 7180/7181 case to E1.
  Author resolution 2: R2 uses disjoint complete six-port sets, reserves dev set, and E1 adds bases7180/7181 and5170/dev collisions.
- [x] S1 (context-gap): R2 and T1 require sibling discovery under `userProfile` with a malformed-sibling error, but no S1 type reads the disk (`Validate` takes a list) and R4 in S2 repeats "validates against siblings". Fix: declare the discovery method in the public surface under S1, or move the discovery sentences and the malformed-sibling test to R4/T2.
  Author resolution 3: Sibling discovery/malformed-file behavior moved to R4 and E2/T2; S1 Validate stays pure.
- [x] S1 (context-gap): R3 rejects a "nonabsolute" connection-string path without defining absolute, and the gate runs on Linux, where `C:\secrets\db` is not rooted and `/x` is. Fix: define the accepted form as exact text (for example a Windows drive-rooted pattern) and give one accepted and one rejected value in E1.
  Author resolution 4: R3 fixes Windows drive-rooted accepted form; E1 accepts C:\secrets\db and rejects db.txt and /secrets/db on every OS.
- [x] S1, S2, S3 (context-gap): the brief does not say how failures are delivered: `Resolve` with an invalid slug, `InitializeAsync` (returns `bool`) for the lock, ACL and validation failures, and `EnsureAsync` (returns an unexplained `string`) for `Running runtime copy cannot be replaced.` and for a rejected link or version. Fix: name the exception type and message for each, and say what `EnsureAsync` returns.
  Author resolution 5: R1/R4/R5 fix exception types/messages; EnsureAsync returns absolute runtime directory.
- [x] S2 (context-gap): `InstanceInitializer` is declared with no constructor, but E2/T2 need to count random generation, observe ACL-before-content and inject the "security port" on Linux. Fix: declare the constructor and the random and ACL port interfaces in the public surface.
  Author resolution 6: Initializer declares random/security constructor ports; E2/T2 cover zero RNG and ACL-before-content.
- [x] S2 (judgment-gap): R4 refuses force "while instance.lock is held", but the lock's path, format and holding mechanism are defined only in R11 (S7), which S2 cannot depend on. Fix: state in R4 the lock path and the exact test for "held", and make R11 refer back to it.
  Author resolution 7: R4 independently defines lock path and writer-share test; R17 later implements the reusable lock.
- [x] S3 (context-gap): R5 keeps the "two most recently successfully published" copies but gives no ordering source, while also saying timestamps do not count for copy decisions. Fix: name what orders the copies (for example a publish marker written at publication) and state the outcome for three copies in E3.
  Author resolution 8: R5 uses .published-at UTC marker and tie order; E3 fixes A/B/C protected-copy outcomes.
- [x] S4 (context-gap): the note names a "merged 15-2 API hosting story" that does not exist in 15-2's split, and C1 does not say where a 15-2 story merged after S4 registers its services, since 15-2's brief knows nothing of the composition methods. Fix: list the exact 15-2 stories S4 waits for, or state that C1 extracts `Program` as it stands and later 15-2 stories register through the composition.
  Author resolution 9: C1 extracts current Program with no external API prerequisite; later API additions use shared composition.
- [x] S5 (context-gap): C2 says to reuse the existing WSL tests and delegate the existing install helper, but those are in `tests/Aiakos.Hosting.Wsl.Tests/` and `src/Aiakos.AppHost/NodeDeployment.cs`, neither of which is in `paths` (the gate fails a file outside them), and the Files table places them under `tests/Aiakos.AppHost.Tests/` and `Hosting.Wsl`. Fix: add the paths the story must touch and correct the Files row, or state that both stay untouched.
  Author resolution 10: Paths/File table include Hosting.Wsl.Tests and AppHost.NodeDeployment delegation.
- [x] S5 (context-gap): "retain existing signatures but move implementations" with "forwarding facades where required" leaves the implementer to choose the namespace of the moved public types and whether `Aiakos.Hosting.Wsl` keeps duplicate types, type forwards or wrappers. Fix: state the namespace of each moved type and the one facade mechanism.
  Author resolution 11: Exact shared namespace/type list and compatibility wrappers/adapters specified; no type-forward choice.
- [x] S5 (context-gap): `WslEnvironment.Compose` fixes the prefixes to `AIAKOS_`/`OTEL_`, but the existing composition takes configurable prefixes with default `OTEL_`, `AIAKOS_`, `DOTNET_` and matches existing names case-insensitively, so `Hosting.Wsl` cannot delegate without changing C2's "existing public caller behavior". Fix: give `Compose` a prefixes parameter, or state that `Hosting.Wsl` keeps its own composition, and state the case rule.
  Author resolution 12: Compose declares caller prefixes and OrdinalIgnoreCase existing-name behavior; R6 and Hosting defaults agree.
- [x] S5 (context-gap): R7's "caller" that compares `VERSION` and returns `The packaged WSL node is unavailable.` has no declared type although E5 tests it, and "do not install while an owned node is running" can only be enforced by the host in S7. Fix: declare the installer method S5 owns and move the running-node sentence to R11.
  Author resolution 13: NodeInstaller exact method/return/runner scripts declared; R11 owns install-before-node ordering.
- [x] S5 (context-gap): the story holds two concerns, the extraction of a library with two new projects (C2, R6) and a new node installer (R7), under one matrix output. Fix: split it into an extraction story and an install story, each with its own output and test.
  Author resolution 14: New S5 extraction and S6 installation own separate E5/T5 and E10/T10.
- [x] S6 (context-gap): R9 puts the start-failure text "in host status", but `NodeSupervisor` exposes only `LastExitCode`, and the brief does not say whether a start failure is retried under R8 or how `RunAsync` completes after exit 2/3 and after cancellation. Fix: add the member that carries the failure, and state the retry rule and both completions.
  Author resolution 15: R9 adds LastError, start-failure no retry/normal completion, exit2/3 normal and cancellation cleanup then throw.
- [x] S7 (context-gap): the story holds four concerns in three dense rules: the Docker database, the instance lock, the in-process orchestrator host with its listeners, and the WSL node process factory; that is too much for one Sonnet-level run. Fix: split R11 into a lock rule and a host rule and make four stories, each with its own output and test.
  Author resolution 16: First S7 split into independent lock/database/node/host composition stories with individual output/test IDs.
- [x] S7 (judgment-gap): the sentence "CLI gains references to Core, Wsl and Orchestrator" is in C3, owned by S9, but S7 builds the host in `src/Aiakos.Cli` on the C1 composition and the WSL library. Fix: move the project references into an item that S7 (or its first split story) owns.
  Author resolution 17: C4 project-reference story precedes host adapter stories; C3 only wires application.
- [x] S7 (context-gap): R10 gives no exact `docker` argv: the volume's mount target (it differs for `postgres:18`), the database and user names, the inspect fields compared, and the readiness probe with its bound are all left to the implementer, and a wrong mount target loses the owner's data on recreation. Fix: give the exact argv lists and the readiness timeout with its error text.
  Author resolution 18: R10 fixes Docker18 mount /var/lib/postgresql, user/database, inspect fields, argv, timeout/readiness message.
- [x] S7 (context-gap): R11 holds the lock with an exclusive handle and also has a second host read the holder's pid and version from it, which an exclusive handle prevents on Windows, and no exact text is given for the unreadable case. Fix: state the share mode that allows reading and the exact message when pid or version is unknown.
  Author resolution 19: R17 uses writer FileShare.Read, permits readers and fixes unreadable holder message.
- [x] S7 (context-gap): R11 passes the hook port "through environment" but R12's exact environment list has no variable for it, and R12 gives no command or bound for the "bounded WSL helper" that resolves the Linux home. Fix: name the variable in R12 and give the helper's exact argv and timeout.
  Author resolution 20: R12 fixes AIAKOS_HOOK_PORT and home helper argv/10s timeout; current Node consumer absence is named partial dependency, not implemented here.
- [x] S7, S8 (judgment-gap): R12's "OTEL_* when configured" and R11's host take the endpoint that R16 chooses, and R16 captures the node output that R12 redirects, but S7 and S8 do not depend on each other and no item says who wires them. Fix: make the later of the two depend on the earlier and name the wiring in its rule.
  Author resolution 21: Host S19 depends on logging/telemetry/node concerns and R11 explicitly passes endpoint/logs into node factory.
- [x] S8 (context-gap): R16 names the dashboard container `aiakos-instance-dashboard`, so two instances with `dashboard=true` collide (spec 0007 R21 has `aiakos-<instance>-dashboard`), and it gives no image, container ports, mismatch rule or place in the R14 shutdown order. Fix: derive the name from the slug and give the image, the exact port mapping and when the container is stopped.
  Author resolution 22: R19 derives dashboard slug name, pins verified13.5.2 image/mappings/inspection and stop position after database.
- [x] S8 (judgment-gap): E9 includes "non-Windows guard exact G2 before effects", but the command boundary that applies G2 is wired in S9, so S8 cannot produce that output. Fix: move the platform case to E8.
  Author resolution 23: G2 platform output moved to E18/T18 final wiring, E9 now logging only.
- [x] S8 (context-gap): the story holds two concerns, file logging with retention and redaction, and telemetry with a Docker dashboard container. Fix: split R16 and E9 into a logging story and a telemetry story.
  Author resolution 24: R16 logs and R19 telemetry are independent S13/S14 with E9/T9 and E19/T19.
- [x] S9 (context-gap): the story holds readiness and the connection file, discovery, the detached start with its timeout, two admin routes in the orchestrator, status rendering with exit codes, and the wiring of five commands; that is not one concern or one run. Fix: split R13 into publish/discovery and detached start, and make separate stories for R14, for R15 and for C3.
  Author resolution 25: Connection R13, starter R18, admin R14, renderer R15 and application C3/R21 split into separate stories.
- [x] S9 (context-gap): `instance status`, `stop` and `start` must call the admin routes with the token and the version header, but the brief says 15-3 owns the HTTP client and "no story here implements" it, and 15-3 is not briefed. Fix: name the 15-3 story as an external prerequisite, or declare a minimal admin client that S9 owns.
  Author resolution 26: R20 declares minimal admin-only client, no unbriefed15-3 dependency.
- [x] S9 (context-gap): C3 does not say how `CliApplication(GitEnvChecker)` receives the instance services and a platform check, so the implementer either changes a 15-1 public constructor (turning 15-1's committed tests red) or hard-wires real services, and on the Linux gate every lifecycle command then stops at G2. Fix: state the new constructor or factory as a change to 15-1 and the port that decides the platform.
  Author resolution 27: C3 preserves old constructor and adds injectable overload; IInstancePlatform and IInstanceCommands enable Linux command tests.
- [x] S9 (context-gap): R14 and R15 give no exact text for the status JSON (shapes of `ports`, `database`, `nodes`, `seat_counts`), for the human lines after `version_match`, for where the keep-seats warning is carried, or for the stderr lines of exit 3, 4 and 5. Fix: add a golden status document, a golden human output and one exact line per error outcome.
  Author resolution 28: Admin DTO fields plus E15 body and E16 API/text goldens, R21 fixed error lines and warning location declared.
- [x] S9 (context-gap): R14 adds reason `SEATS_RUNNING` with an extra `seats` field, while 15-2 says no other reason is emitted and a problem has only `reason`, `detail`, `retryable`; the admin DTOs have no stated project (`src/Aiakos.Api.Contracts/` is not in `paths`), and `GET /v1/admin/status` is not in spec 0007's API table. Fix: add a `C` item for the 15-2 change and the spec amendment, and name the project of the DTOs.
  Author resolution 29: C5 owns admin-only exception/new DTOs and spec API amendment; paths include Api.Contracts/spec0007.
- [x] S9 (context-gap): R13 does not say when `RuntimeCopy.EnsureAsync` runs, what its source directory and `runningVersion` are, or the exact file and argv that are launched (a tool install has `aiakos.dll`, not an executable), and no rule maps `instance init` options to `Operator` and `Telemetry`, for which 15-1's grammar has no option. Fix: state the start sequence with exact argv and the source of each configuration field.
  Author resolution 30: R18 fixes source/version/runtime/dotnet dll argv sequence; R21 maps init fields from parser/platform with telemetry false/null.
- [x] S9 (context-gap): after a host crash `connection.json` stays, and when its pid is reused by another process R13 calls the host live, so `start` prints a status and never starts and no rule covers a refused connection. Fix: require a held `instance.lock` as well as a live pid, and state the outcome when `api_url` does not answer.
  Author resolution 31: R13 requires pid AND held lock; R18 unreachable held host unavailable and never replaced.
- [x] S9 (context-gap): the brief gives no answer for when `instance stop` prints `Instance stopped.` (after the 202, or after the lock is released, and with what bound), nor for `--instance dev instance status`, which 15-1 accepts and which receives 503 `HOST_UNAVAILABLE` from the AppHost stack. Fix: state the wait, its timeout and error text, and the exit code and output for the 503.
  Author resolution 32: R21 fixes stop wait250ms/30s and timeout, successful message only after release; dev503 unavailable3 line fixed.

## Not checked

- No build, test or gate was run; this is a reading of the brief, the split and the code they name.
- 15-1 and 15-2 are not implemented on `main` (only 15-1-1 is in progress) and 13-4 is not briefed, so the seams S4, S7 and S9 wait for (`CliApplication`, the API host, authentication, the version gate, `CallerContext`) were read from their briefs only.
- Windows behaviour: file share modes on the lock, ACL calls, the detached process and the held `wsl.exe` child were not run.
- Whether the CLI tool project can reference `Aiakos.Orchestrator` (a web project) and still pack and install as 15-1 R9 requires.
- Serilog: the two package versions exist on nuget.org; their build under this repository's warnings-as-errors was not tried.
- Spec 0007 was read for R6–R21, instance files, ports, the API table and the start sequence only; ADR 0034 and the rest of the spec were not read.
- The story texts from `tools/story.sh show` were checked for which items they carry (S2 and S9), not read in full for each story.

## Round 2: resolution 1063b39

The whole brief and the 22-story split were read again. The 32 round-1 resolutions are accepted,
with the residues named below: the public seams exist for every story, the large stories are
split, the admin client is owned here, the paths cover what is touched, the port sets are
compared as sets, the Docker argv and the `postgres:18` mount target are exact, the dashboard is
per instance, and discovery needs a held lock as well as a live pid. Twelve points are open; the
first was run, the others are read.

- [x] S11, S2 (judgment-gap): R17 holds the lock with `FileAccess.ReadWrite, FileShare.Read` and R4 tests "held" the same way, but on Linux, where the gate runs, .NET takes a shared lock for any share mode other than `FileShare.None`: a second writer with `FileShare.Read` opened the file while the first held it (run on this machine), so E11 "second writer refused" and E2 "force lock refusal" cannot pass. Fix: use the rule of `src/Aiakos.Node/InstanceLock.cs` (`FileShare.Read` on Windows, `FileShare.None` elsewhere, holder read without a lock) in R17 and R4, and say what E11 expects for the holder text on Linux.
- [x] S12, S14 (context-gap): R10 and R19 run every `docker` command with a 10 s bound, but `docker create` pulls `postgres:18` or the dashboard image when it is not on the machine, which takes longer on a first start, so the first `instance start` fails with `Instance process timed out.`. Fix: add an explicit `pull` step with its own bound and error text, or give `create` a bound that covers a pull.
- [x] S2, S12 (context-gap): R4's `force=true` "explicitly recreates secrets", including `postgres-password`, while R10 keeps the existing volume, whose database still has the old password; after `instance init --force` the orchestrator can no longer authenticate and no rule gives the outcome. Fix: keep `postgres-password` on force when it exists, or state the refusal and its exact text.
- [x] S16, S18, S19 (judgment-gap): E8 asserts "connection only after healthy+connected" and E15 asserts "stop order exact R14", but `ConnectionStore` only writes what it is given and the routes are tested with a fake control: both sequences are the host's (R11, S19), and R11 does not say how `RequestStopAsync(keepSeats, keepDatabase)` ends `InstanceHost.RunAsync` or carries `keep_database`; R11 also cites "R18 readiness" where R13 is meant. Fix: move the two sequence cases to E12, and state in R11 the stop path from the control to the host with the order.
- [x] S19 (context-gap): the story holds the host sequencing over fake ports, the real `InstanceOrchestratorFactory` (in-process `WebApplication`, configuration keys, two Kestrel listeners, tested against Postgres) and the production host control (status from `SeatQueries` and `NodeLinkRegistry`, blocking addresses, stop); the control is also passed into the factory before the application whose services it reads exists. Fix: make the factory with the control implementation its own story before the host story, and say how the control obtains the application's services.
- [x] S4, S18 (context-gap): C1 says a 15-2 story merged after S4 has its "Program-only additions" moved into the composition "in the same later story", which names no story; 15-2's implementers read only 15-2's brief, so the routes land in `Program` and the in-process host of S19 has no API. Fix: name S18 as the story that moves any `Program`-only 15-2 registration into the composition, and add to E15 that `/v1/version` answers through `MapAiakosOrchestrator` alone.
- [x] S13, S19 (context-gap): R16 pins `Serilog.Extensions.Hosting`, but `HostLogs` only writes the messages it is handed and the host builder is created in S19, so nothing uses the package and no rule says whether the in-process orchestrator's `ILogger` output goes to `host-*.log` or passes the redaction. Fix: state in R11 that the factory routes `ILogger` output through `HostLogs` (and owns the hosting package there), or remove the package and say that this output is not written.
- [x] S15, S19 (context-gap): `AIAKOS_HOOK_PORT` is a name this brief invents for a node setting that #12 owns (spec 0001 and spec 0005 R18 say only that the node's hook port is the port base + 10; the node reads no such variable today), and R11 calls the missing consumer "partial", which in the workflow means acceptance tests that cannot pass, while here they pass. Fix: lead decides whether this slice fixes the variable name for 12-2 to follow or leaves it out until 12-2; either way replace "partial" by a named backlog item.
- [x] S22 (context-gap): no rule says where `InstanceHostRequest.PayloadDirectory` comes from in production (R5 mentions `node/linux-x64` inside the runtime copy), nor what the `string` of `IInstanceWslPreparation.PrepareAsync` is. Fix: state both in R21 and R11.
- [x] S6 (context-gap): E10 expects "staging/chmod/VERSION/rename", but R7 now writes `VERSION` by a separate command after the unchanged script has replaced `node`; E10 also still carries the `WSLENV` case, which is E5's. Fix: write E10's sequence as R7 has it and drop the `WSLENV` clause.
- [x] S16 (context-gap): E8 asserts "publication bytes exact", but there is no golden for `connection.json` and R13 does not fix the form of `started_at` (spec 0007 shows `Z`, the status golden shows `+00:00`). Fix: add the golden document for the status fixture.
- [x] S22 (context-gap): with R13 a connection counts only when `instance.lock` is held, and the AppHost holds no such lock in `~/.aiakos-dev`, so `--instance dev instance status` reports `Instance is unavailable.` with exit 3 while the dev stack runs; R21's "dev status 503" case is never reached. Fix: give the dev instance its own exact line and exit code that do not claim the stack is down, or exempt dev from the lock condition and state the 503 outcome.

Architect, round 2. Run: `tools/story.sh check 15-4` (ok, 22 stories, 74 items); a .NET probe of
file share modes on Linux (first finding); the registry tag list of
`mcr.microsoft.com/dotnet/aspire-dashboard` (13.5.2 is listed). Read: the brief and split at
1063b39, `src/Aiakos.Node/InstanceLock.cs`, spec 0001 (instance table) and spec 0005 R18 for the
hook port. Not checked: the story texts from `tools/story.sh show`; Windows behaviour (share
modes, ACLs, detached process); whether the CLI packs with the ASP.NET framework reference;
nothing was built. This was the second round: the hook-port point needs lead's decision, the
other eleven are the author's.

Author round-2 resolutions: Linux/Windows lock modes and raw holder read (R4/R17/E2/E11);
explicit bounded image pulls (R10/R19/E7/E19); force preserves persisted database password
(R4/E2); readiness and stop ordering assigned to host (R11/E12), connection/routes test only
own effects (E8/E15); factory/control/stop signal split into S19 before host S20 (R24/E23/T23),
post-Build binding and scoped queries; S18 owns late Program-only15-2 migration; ILogger provider
redacts through R16 and factory registration R24, unused Hosting package removed; lead decision
qitem-20261007150317-7e7074ab716d7897 omits hook variable and records backlog #12 (12-2);
production payload and returned absolute Linux instance home fixed (R21/R11); E10 follows
script replacement then VERSION and leaves environment composition to E5; E8 connection golden
fixes timestamp bytes; dev-only live-pid discovery without lock plus exact live503 outcome
(R13/R20/R21/E8/E18). These are proposed resolutions, pending architect verification.
