---
id: 15-2
title: "#15 slice 2 — local API contracts, authentication and rig revisions"
issue: 15
status: approved
route: impl
paths: [src/Aiakos.Spec/, tests/Aiakos.Spec.Tests/, src/Aiakos.Api.Contracts/, src/Aiakos.Orchestrator/, src/Aiakos.Data/, tests/Aiakos.Orchestrator.Tests/, tests/Aiakos.Data.Tests/, Aiakos.slnx]
date: 2026-10-06
---

# Brief: #15 slice 2 — local API contracts, authentication and rig revisions

Part of #15 and spec 0007 R22–R29, R34, R48. The CLI transport and instance host consume this
slice; this slice does not implement the CLI, instance host, Docker, WSL, node, or seat actor.
Where silent, reject malformed input with the existing problem-details shape and do not write.

## Goal and prerequisites

Provide the loopback HTTP/JSON contract, bearer-token caller context, UUID address resolution,
read endpoints, and append-only rig revision registration. Existing `SeatQueries`, tenant
migration, `ResolvedRig` canonical hashes, and seat schema are on main. The 13-4 SeatActor slice
owns the transport-free command dispatcher and exact command messages. Its bridge is a later
story in this slice and is gated on 13-4 merging; tests use ports only and never fake production.

The required 13-4 seam is: `Aiakos.Core.CallerContext(Guid TenantId, string User, string Sender)`;
`Aiakos.Core.SeatCommandEnvelope(Guid TenantId, Guid SeatId, object Message)`;
`Aiakos.Core.ISeatCommandDispatcher.DispatchAsync(SeatCommandEnvelope, CallerContext,
CancellationToken): Task<object>`; and immutable command/reply records for `SeatUp(bool Fresh,string? Note)`,
`SeatDown`, `SeatSend(string Body,bool Force)`, `SeatCapture(int HistoryLines)`, with accepted,
already-up/no-op, rejected, capture-completed and capture-timeout replies. Reply records live in
`Aiakos.Orchestrator.Seats`: `SeatCommandAccepted(Guid? LaunchId, Guid CommandId)`,
`SeatAlreadyUp(Guid LaunchId)`, `SeatAlreadyDown`, `SeatCommandRejected(string Reason)`,
`SeatCaptureCompleted(Aiakos.Contracts.Node.V1.PaneCapture Capture)`, and
`SeatCommandTimedOut(string Reason)` (capture timeout reason `CAPTURE_TIMEOUT`). Up, down and
send complete after admission; capture waits for its committed terminal result. The API bridge passes
the authenticated context and never takes identity from request JSON. If 13-4 changes these exact
names before merge, this bridge story follows the merged 13-4 contract; it does not invent a
second dispatcher.

## Files

| Path | Purpose |
|---|---|
| src/Aiakos.Api.Contracts/ | Request/response records and source-generated JSON context |
| src/Aiakos.Orchestrator/ | API host, authentication, address resolver, read/registration and gated command bridge |
| src/Aiakos.Data/ | append-only rig revision repository and migration |
| tests/Aiakos.Orchestrator.Tests/, tests/Aiakos.Data.Tests/ | contract, auth, routing and revision tests |
| Aiakos.slnx | register contracts/tests |

No secrets in DTOs, no provider payloads, no new package except the centrally managed API contract
dependencies already used by the solution. Do not change existing migrations.

## Public surface

`Aiakos.Api.Contracts` contains immutable records for Version, problem details, seat status/detail,
launch, command, node, rig registration and every endpoint in spec 0007's API table. A source-
generated `JsonSerializerContext` emits snake_case v1 JSON. `CallerContext` is the sole identity
source. `ISeatAddressResolver` resolves `<seat>@<rig>` under a tenant and distinguishes unknown
rig/seat from ambiguous short addresses. `IRigRevisionRepository` appends and reads revisions.

## General rules

G1. Every request is loopback-only HTTP/1.1 under `/v1`; API startup rejects a non-loopback bind.
G2. Bearer token comparison is constant-time, missing/wrong token returns 401 with no detail, and
    no token, tenant, sender, or secret value is logged or serialized.
G3. JSON is one compact UTF-8 document, snake_case, top-level `api: "v1"` where specified; problem
    responses are `application/problem+json` with `reason`, `detail`, `retryable` only.
G4. Cancellation and malformed bodies stop before lookup/write; no partial revision or command.

## Changes

C1. Add the API contract project and source-generated serializer; no existing node or orchestrator
    wire contract changes.
C2. Add migration `0003_rig_revision.sql` with `aiakos.rig_revision(tenant_id, revision_id,
    rig_id, revision, spec_hash, binding_hash, tool_version, resolved jsonb, source_path,
    source_commit, source_dirty, created_by, created_at)`, foreign key `(tenant_id,rig_id)` to
    `aiakos.rig`, unique `(tenant_id,rig_id,revision)` and `(tenant_id,rig_id,spec_hash,binding_hash)`,
    then add nullable `current_revision_id` and its foreign key to `aiakos.rig`; all in one
    transaction, with no existing migration edited. `resolved` stores complete canonical file contents.

## Rules

R1. `GET /v1/version` returns `{ "api":"v1", "version": <server version> }`; `GET /health` and
    `/alive` retain existing behavior.
R2. Authentication maps the configured API token to `CallerContext(DefaultTenant, "user:<Windows username>", configured operator)`; request JSON cannot override any field.
R3. Address resolution accepts full `member@rig`; a short member is accepted only when unique in
    the tenant. Unknown rig/seat is 404 `NOT_FOUND`; ambiguous short member is 400 `AMBIGUOUS_SEAT`.
R4. Read endpoints return `SeatStatusRow`, `SeatDetail`, `SeatLaunchRow`, `SeatCommandRow` and
    node rows from existing readers, preserving DB order and omitting secret/token hashes.
R5. `PUT /v1/rigs/{rig}` parses and canonicalizes its `Resolved` JSON with the existing
    `ResolvedRig` canonicalizer and recomputes both hashes; submitted hashes are never trusted.
    Mismatch returns 400 `HASH_MISMATCH` with no writes. A new pair appends a revision and updates
    `aiakos.rig.current_revision_id` in one transaction. Upsert every desired seat's address, kind,
    parameters and hashes; retire a removed seat only when its session is absent or exited. A running
    removal aborts the transaction with `SEAT_REMOVED_WHILE_RUNNING`, leaving all tables unchanged.
R6. Problem mapping is exact: validation 400, unknown address 404, actor/domain rejection 409,
    authentication 401; `retryable` is true only for transient command/host outcomes.
R7. `GET /v1/seats`, `/v1/seats/{address}`, `/v1/launches/{id}`, `/v1/commands/{id}`, and
    `/v1/nodes` are read-only and work across CLI/server patch versions; changing commands require
    same major.minor and otherwise return 409 `VERSION_INCOMPATIBLE` with both versions.
R8. The gated bridge maps `up`, `down`, `send`, and `capture` request DTOs to the merged 13-4
    dispatcher with CallerContext, maps replies and fixed reasons, and never acknowledges a
    command before the dispatcher completes. Until 13-4 merges, no production bridge is present.
R9. API activity uses `ActivitySource("Aiakos.Api")`, one `api.<command>` activity per route, and
    continues incoming `traceparent`. Names are `version`, `seats.list`, `seat.detail`, `launch.get`,
    `command.get`, `nodes.list`, `rig.put`, `seat.up`, `seat.down`, `seat.send`, and `seat.capture`.
    Tags are only `api.command`, `http.route`, and `caller.user`; body and bearer token never enter
    spans or logs. This slice does not export OTLP.

## Expected outputs: exact text

| ID | Input | Expected |
|---|---|---|
| `E1` | version/auth matrix | exact v1 version JSON; missing/wrong bearer is 401 with empty detail |
| `E2` | address matrix | full address resolves; unique short resolves; ambiguous is `AMBIGUOUS_SEAT`; unknown is `NOT_FOUND` |
| `E3` | read endpoint fixture | ordered status/detail/launch/command/node JSON with no token hashes or secret paths |
| `E4` | revision registration matrix | new pair revision1, duplicate pair same revision, changed pair revision2, mismatch `HASH_MISMATCH`, running removal `SEAT_REMOVED_WHILE_RUNNING` and zero writes |
| `E5` | problem/version matrix | exact status/reason/detail/retryable mapping and major.minor gate |
| `E6` | 13-4 port fake bridge | context and immutable command mapping; accepted/rejected/capture replies; no bridge before dependency |
| `E7` | trace/cancellation matrix | one root continuation, no sensitive values, cancellation before lookup/write |

E4 uses these exact documents: revision 1 request `{"resolved":"{...}","tool_version":"2.0","source_path":"rig.yaml","source_commit":"abc","source_dirty":false,"spec_hash":"<recomputed>","binding_hash":"<recomputed>"}` returns HTTP 200 `{"revision_id":"<uuid>","revision":1,"spec_changed":true,"binding_changed":true,"seats":[]}`; repeating the same pair returns the same revision and both changed flags false; a changed pair returns revision 2. A submitted hash mismatch returns HTTP 400 `{"reason":"HASH_MISMATCH","detail":"Resolved rig hashes do not match.","retryable":false}`. Removing a running seat returns HTTP 409 `{"reason":"SEAT_REMOVED_WHILE_RUNNING","detail":"A running seat cannot be removed.","retryable":false}` and leaves all rows unchanged.

## Tests

T1. Contract golden tests cover source-generated snake_case JSON, nullability, problem media type,
    and immutable DTO snapshots.
T2. Auth/address tests use fake token store and tenant fixtures; test full/short/ambiguous/unknown
    addresses, constant-time comparison behavior, and no sensitive output.
T3. Read tests use existing SeatQueries fakes and assert ordering and omission of hashes/secrets.
T4. Real Postgres migration/repository tests assert revision uniqueness, append-only numbering,
    hash mismatch, retirement guard and transaction rollback.
T5. API integration tests assert loopback bind, status/reason mapping and version gate.
T6. After 13-4 merges, fake-port bridge tests assert every command/reply and cancellation; no
    production dispatcher double is allowed.
T7. Activity tests assert trace continuation and bounded export without body/token attributes.

## Done and out of scope

Build and tests have zero warnings/errors; all existing suites remain green. No API client command
implementation, instance host/configuration, Docker/WSL supervision, node transport, actor
dispatcher implementation, secret generation, or release packaging belongs here.

## Exact contract detail

`Aiakos.Api.Contracts` records are immutable. They are `VersionResponse(string Api,string Version)`,
`ProblemResponse(string Reason,string Detail,bool Retryable)`,
`RegisterRigRequest(string Resolved,string ToolVersion,string SourcePath,string? SourceCommit,
bool SourceDirty,string SpecHash,string BindingHash)`, `RegisterRigResponse(Guid RevisionId,int
Revision,bool SpecChanged,bool BindingChanged,IReadOnlyList<SeatRegistrationResponse> Seats)`,
`SeatRegistrationResponse(string Address,string Kind,bool Retired,bool Drifted)`,
`UpRequest(bool Fresh,string? Note)`, `SendRequest(string Body,bool Force)`,
`CaptureRequest(int HistoryLines)`, `AcceptedResponse(Guid LaunchId,Guid CommandId)`,
`CommandAcceptedResponse(Guid CommandId)`,
`AlreadyUpResponse(Guid LaunchId)`, `NoOpResponse`, `CaptureResponse(string Text,bool Truncated,
bool PaneDead)`, `CaptureTimeoutResponse`, and response records mirroring existing `SeatStatusRow`,
`SeatDetail`, `SeatLaunchRow`, `SeatCommandRow` and node fields in their declared order.
The JSON context emits `api,version`, `problem` fields, and response properties in declaration
order using snake_case. `CallerContext` is created exactly once by 13-4; 15-2 consumes it and
declares no duplicate.

Configuration is `Aiakos:Api:TokenFile`, `Aiakos:Api:Operator`, `Aiakos:Api:Port` (default
`InstanceDefaults.ReleasedPortBase + ApiPortOffset`).
`IApiTokenStore.ReadAsync(CancellationToken)` returns the token; `IApiCallerContextFactory.Create
(string token)` returns the 13-4 context or null. `ISeatAddressResolver.ResolveAsync(Guid tenantId,
string address,CancellationToken ct)` returns `SeatAddressResolution(Guid? SeatId,string? Rig,string?
Member,bool Ambiguous)`. `IRigRevisionRepository.AppendAsync(Guid tenantId,RigRegistration
registration,CallerContext caller,CancellationToken ct)` returns `RigRevisionReceipt`. A non-loopback bind fails startup with exactly
`API endpoint must bind to loopback.`

Routes are exact: `GET /v1/version`→VersionResponse; `GET /v1/seats?rig=<name>`→ordered
SeatStatusResponse[]; `GET /v1/seats/{address}`→SeatDetailResponse; `GET /v1/launches/{id}`→
LaunchResponse; `GET /v1/commands/{id}`→CommandResponse; `GET /v1/nodes`→`NodeResponse(string Node,bool Connected,DateTimeOffset? Since,string? NodeInstanceId,string? Version,IReadOnlyList<string> Capabilities,string? LastError)[]`;
`PUT /v1/rigs/{rig}`→RegisterRigResponse (200); `POST /v1/seats/{address}/up`→202 AcceptedResponse or
200 AlreadyUpResponse; `/down`→202 CommandAcceptedResponse or 200 NoOpResponse; `/send`→202
CommandAcceptedResponse; `/capture`→200 CaptureResponse or 504 CaptureTimeoutResponse. Bodies are the request records above. Changing routes
require `X-Aiakos-Client-Version` and matching major.minor.

Problem mapping is exact: `UNAUTHORIZED` 401 empty detail non-retryable; `INVALID_REQUEST` 400 `Request body is invalid.` non-retryable; `NOT_FOUND` 404
`Seat or rig was not found.`; `AMBIGUOUS_SEAT` 400 `Seat address is ambiguous.`; `HASH_MISMATCH`
400 `Resolved rig hashes do not match.`; `SEAT_REMOVED_WHILE_RUNNING` 409 `A running seat cannot
be removed.`; `VERSION_INCOMPATIBLE` 409 `CLI and instance versions are incompatible.`;
`SEAT_REJECTED` 409 `The seat rejected the command.`; `HOST_UNAVAILABLE` 503 `The instance host is unavailable.`
retryable. No other reason is emitted.

Server tracing uses `ActivitySource("Aiakos.Api")`, one `api.<command>` activity for each route,
propagates `traceparent`, and tags only `api.command`, `http.route`, and `caller.user`.
It never exports body/token attributes; this slice does not add OTLP export.

## Third-pass contract details

`RegisterRigRequest` carries `IReadOnlyDictionary<string,byte[]> FilesBySha256`, with lowercase SHA-256 hex keys and raw UTF-8 bytes. Each file is at most 1 MiB and the total is at most 16 MiB. Every canonical path/hash/size entry must have exactly one matching map entry; verify byte length and SHA-256 before any write. Reject violations with fixed `FILE_CONTENT_INVALID` and detail `Rig file contents do not match the canonical form.`; never include file bytes in errors.

`Aiakos.Spec.RigHashVerifier.Verify(string resolvedJson, string specHash, string bindingHash)` returns `RigHashVerification(bool Valid,string SpecHash,string BindingHash)`. It parses and validates the request's canonical JSON and recomputes both hashes through the existing `RigCanonicalizer`/`CanonicalJson`; no orchestrator hash implementation is permitted. `RigRegistration` is `(string Rig,string Resolved,string ToolVersion,string SourcePath,string? SourceCommit,bool SourceDirty,string SpecHash,string BindingHash,IReadOnlyDictionary<string,byte[]> FilesBySha256)`; `RigRevisionReceipt` is `(Guid RevisionId,int Revision,bool SpecChanged,bool BindingChanged,IReadOnlyList<SeatRegistrationResponse> Seats)`.

Item ownership additions: C3 and R10 belong to S1; E8 is the exact verifier result described above; T8 pins verifier hashes to the 14-4 loader goldens.

C3. The public verifier is implemented only in Aiakos.Spec.
R10. The verifier returns the typed result and uses the existing canonicalizer.
E8. The verifier returns the loader-golden hash pair.
T8. Tests pin verifier output to the 14-4 goldens.

E8 uses the 14-4 minimal canonical JSON and its published `spec_hash` and `binding_hash`: matching values return `Valid=true` and the two recomputed hashes; changing either submitted hash returns `Valid=false` with the same recomputed pair.

Registration file values are arbitrary binary bytes (not UTF-8); keys are lowercase SHA-256 hex. `FILE_CONTENT_INVALID` is HTTP 400, non-retryable, with detail `Rig file contents do not match the canonical form.`. The revision migration uses explicit PostgreSQL types: tenant_id/rig_id/revision_id uuid, revision integer, hashes/tool_version/source_path/source_commit/created_by text, resolved jsonb, source_dirty boolean, created_at timestamptz.

The registration transaction upserts existing `aiakos.seat` columns `(tenant_id,seat_id,rig_id,member,address,kind,harness,node_name,spec_hash,binding_hash,parameters,updated_at)`. Inserts set all of these plus `retired_at NULL` and timestamps; updates change rig identity, address, kind, harness, node_name, hashes, parameters and `updated_at`. Registration never changes `desired`, `desired_at`, or `desired_by`; retirement sets `retired_at` only under the absent/exited guard and never deletes rows.

Malformed JSON in `RigHashVerifier.Verify` throws `FormatException` with exact message `Resolved JSON is invalid.`; it does not return a result or write. E8 asserts this exception, while a well-formed hash mismatch returns `Valid=false` with both recomputed hashes.
