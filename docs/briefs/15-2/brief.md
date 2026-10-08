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
rig/seat from ambiguous short addresses. `IRigRevisionRepository` selects or appends a revision through AppendAsync; no revision read method is added in this slice.

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
    transaction, with no existing migration edited. `resolved` stores canonical descriptors; binary contents are stored separately as specified below.

## Rules

R1. `GET /v1/version` returns `{ "api":"v1", "version": <server version> }`; `GET /health` and
    `/alive` retain existing behavior.
R2. Authentication maps the configured API token to `CallerContext(DefaultTenant, "user:<Windows username>", configured operator)`; request JSON cannot override any field.
R3. Address resolution accepts full `member@rig`; a short member is accepted only when unique in
    the tenant. Unknown rig/seat is 404 `NOT_FOUND`; ambiguous short member is 400 `AMBIGUOUS_SEAT`.
R4. Read endpoints return `SeatStatusRow`, `SeatDetail`, `SeatLaunchRow`, `SeatCommandRow` and
    node rows from the optional IApiNodeReader below, preserving reader order and omitting secret/token hashes.
R5. AppendAsync consumes the validated registration from R11. In one tenant-scoped
    transaction, create the rig when absent, select or append its hash-pair revision,
    persist new revision files, set current metadata, upsert desired seats and retire removed
    seats. Every successful call applies seats and current metadata, including a reused pair.
    The exact column sources, flags, state guard and receipt are defined in R5 below.
R6. Problem mapping is exact: validation 400, unknown address 404, actor/domain rejection 409,
    authentication 401; `retryable` follows the exact closed-contract flags below.
R7. `GET /v1/seats`, `/v1/seats/{address}`, `/v1/launches/{id}`, `/v1/commands/{id}`, and
    `/v1/nodes` are read-only and work across CLI/server patch versions; the gated routes listed below require
    same major.minor and otherwise return 409 `VERSION_INCOMPATIBLE` with the fixed detail below.
R8. The gated bridge maps `up`, `down`, `send`, and `capture` request DTOs to the merged 13-4
    dispatcher with CallerContext, maps replies and fixed reasons, and never acknowledges a
    command before the dispatcher completes. S6 waits for 13-4 S16; without a registered dispatcher it returns the exact HOST_UNAVAILABLE response below.
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
| `E4` | revision registration matrix | first rig/new pair revision1, duplicate same revision, A/B/A reuse and current-relative flags, binding-only flags, un-retire/drift/state guards, typed running-removal failure and zero writes |
| `E5` | problem/version matrix | exact status/reason/detail/retryable mapping and major.minor gate |
| `E6` | 13-4 port fake bridge | context and immutable command mapping; accepted/rejected/capture replies; missing-dispatcher response |
| `E7` | trace/cancellation matrix | one root continuation, no sensitive values, cancellation before lookup/write |

E4 contains repository receipts and the typed exception only; it has no HTTP input,
status, hash verification or problem body. E9 owns registration request/response JSON
and the HASH_MISMATCH and SEAT_REMOVED_WHILE_RUNNING HTTP bodies below.

## Tests

T1. Contract golden tests cover source-generated snake_case JSON, nullability, problem media type,
    and immutable DTO snapshots.
T2. Auth/address tests use fake token store and tenant fixtures; test full/short/ambiguous/unknown
    addresses, constant-time comparison behavior, and no sensitive output.
T3. Read tests use existing SeatQueries fakes and the optional IApiNodeReader and assert ordering and omission of hashes/secrets.
T4. Real Postgres migration/repository tests assert revision uniqueness, append-only numbering,
    binary file persistence, retirement guard and transaction rollback; T9 owns hash mismatch.
T5. API integration tests assert loopback bind, status/reason mapping and version gate.
T6. After 13-4 merges, fake-port bridge tests assert every command/reply and cancellation; no
    production dispatcher double is allowed.
T7. Activity tests assert trace continuation and absence of body/token attributes.

## Done and out of scope

Build and tests have zero warnings/errors; all existing suites remain green. No API client command
implementation, instance host/configuration, Docker/WSL supervision, node transport, actor
dispatcher implementation, secret generation, or release packaging belongs here.

## Exact contract detail

`Aiakos.Api.Contracts` records are immutable. They are `VersionResponse(string Api,string Version)`,
`ProblemResponse(string Reason,string Detail,bool Retryable)`,
`RegisterRigRequest(string Resolved,string ToolVersion,string SourcePath,string? SourceCommit,
bool SourceDirty,string SpecHash,string BindingHash,IReadOnlyDictionary<string,byte[]> FilesBySha256,
IReadOnlyList<Aiakos.Spec.ResolvedSeatParameters> SeatParameters)`, `RegisterRigResponse(Guid RevisionId,int
Revision,bool SpecChanged,bool BindingChanged,IReadOnlyList<SeatRegistrationResponse> Seats)`,
`SeatRegistrationResponse(string Address,string Kind,bool Retired,bool Drifted)`,
`UpRequest(bool Fresh,string? Note)`, `SendRequest(string Body,bool Force)`,
`CaptureRequest(int HistoryLines)`, `AcceptedResponse(Guid LaunchId,Guid CommandId)`,
`CommandAcceptedResponse(Guid CommandId)`,
`AlreadyUpResponse(Guid LaunchId)`, `NoOpResponse`, `CaptureResponse(string Text,bool Truncated,
bool PaneDead)`, `CaptureTimeoutResponse(string Reason,string Detail,bool Retryable)`, and response records mirroring existing `SeatStatusRow`,
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
CommandAcceptedResponse; `/capture`→200 CaptureResponse or 504 CaptureTimeoutResponse. Bodies are the request records above. The explicitly gated routes below
require `X-Aiakos-Client-Version` and matching major.minor.

Problem mapping is exact: `UNAUTHORIZED` 401 empty detail non-retryable; `INVALID_REQUEST` 400 `Request body is invalid.` non-retryable; `NOT_FOUND` 404
`Seat or rig was not found.`; `AMBIGUOUS_SEAT` 400 `Seat address is ambiguous.`; `HASH_MISMATCH`
400 `Resolved rig hashes do not match.`; `SEAT_REMOVED_WHILE_RUNNING` 409 `A running seat cannot
be removed.`; `VERSION_INCOMPATIBLE` 409 `CLI and instance versions are incompatible.`;
actor rejection reason unchanged, 409 `The seat rejected the command.`; `HOST_UNAVAILABLE` 503 `The instance host is unavailable.`
retryable. No other API-generated reason is emitted; actor rejection reasons pass through as specified below.

Server tracing uses `ActivitySource("Aiakos.Api")`, one `api.<command>` activity for each route,
propagates `traceparent`, and tags only `api.command`, `http.route`, and `caller.user`.
It never exports body/token attributes; this slice does not add OTLP export.

## Third-pass contract details

`RegisterRigRequest` carries `IReadOnlyDictionary<string,byte[]> FilesBySha256`, with lowercase SHA-256 hex keys and raw bytes. Each file is at most 1 MiB and the total is at most 16 MiB. Every canonical path/hash/size entry must have exactly one matching map entry; verify byte length and SHA-256 before any write. Reject violations with fixed `FILE_CONTENT_INVALID` and detail `Rig file contents do not match the canonical form.`; never include file bytes in errors.

`Aiakos.Spec.RigHashVerifier.Verify(string resolvedJson, string specHash, string bindingHash)` returns `RigHashVerification(bool Valid,string SpecHash,string BindingHash)`. It parses and validates the request's canonical JSON and recomputes both hashes through the existing `RigCanonicalizer`/`CanonicalJson`; no orchestrator hash implementation is permitted. `RigRegistration` is `(string Rig,string Resolved,string ToolVersion,string SourcePath,string? SourceCommit,bool SourceDirty,string SpecHash,string BindingHash,IReadOnlyDictionary<string,byte[]> FilesBySha256,IReadOnlyList<Aiakos.Spec.ResolvedSeatParameters> SeatParameters)`; `RigRevisionReceipt` is `(Guid RevisionId,int Revision,bool SpecChanged,bool BindingChanged,IReadOnlyList<SeatRegistrationResponse> Seats)`.

Item ownership additions: C3 and R10 belong to S1; E8 is the exact verifier result described above; T8 pins verifier hashes to the 14-4 loader goldens.

C3. The public verifier is implemented only in Aiakos.Spec.
R10. The verifier returns the typed result and uses the existing canonicalizer.
E8. The verifier returns the loader-golden hash pair.
T8. Tests pin verifier output to the 14-4 goldens.

E8 uses the 14-4 minimal canonical JSON and its published `spec_hash` and `binding_hash`: matching values return `Valid=true` and the two recomputed hashes; changing either submitted hash returns `Valid=false` with the same recomputed pair.

Registration file values are arbitrary binary bytes (not UTF-8); keys are lowercase SHA-256 hex. `FILE_CONTENT_INVALID` is HTTP 400, non-retryable, with detail `Rig file contents do not match the canonical form.`. The revision migration uses explicit PostgreSQL types: tenant_id/rig_id/revision_id uuid, revision integer, hashes/tool_version/source_path/source_commit/created_by text, resolved jsonb, source_dirty boolean, created_at timestamptz.

R5. Column sources and identity: registration.Rig is the route rig name and must equal
`Resolved.shared.name` (R11 rejects a mismatch as INVALID_REQUEST). Match the rig by
(tenant_id,name), retaining its rig_id, or insert a new Guid rig_id and columns tenant_id,
name, spec_hash, binding_hash, tool_version, resolved, created_at and updated_at from
registration and transaction time. A new rig's first revision is 1. Existing rig metadata
(spec_hash, binding_hash, tool_version, resolved, updated_at, current_revision_id) is updated
on every successful call; created_at and rig_id stay unchanged. Serialize registrations for
one tenant/name, including concurrent first registration, before reading current metadata.
For a new pair allocate MAX(revision)+1 under that serialization and insert revision metadata
from registration with created_by=caller.User and created_at=transaction time. Reusing a pair
never changes its revision metadata or files. Flags compare submitted hashes with the rig's
current hashes before this call, independently; both are true when the rig is new.

For each `Resolved.shared.seats[]`, member is `.id`, kind is `.kind`, address is
`member + "@" + registration.Rig`; agent harness is `.agent.harness` and node_name is
`Resolved.binding.placement[]`'s `.node` where `.seat` equals member. Human harness,
node_name and parameters are NULL. Agent parameters are the corresponding SeatParameters
record (Seat equals member), serialized with the contract's source-generated snake_case
context to jsonb. Seat spec_hash and binding_hash are registration's whole-rig hashes,
not newly computed per-seat hashes. Match (tenant_id,rig_id,member), retain seat_id and
created_at, or generate a new Guid seat_id and set created_at to transaction time.
Insert/update tenant_id, rig_id, member, address, kind, harness, node_name, spec_hash,
binding_hash, parameters and updated_at as above; every desired seat gets retired_at=NULL,
including a returning seat. Inserts leave desired='down' and desired_at/desired_by NULL;
updates never change desired, desired_at or desired_by. No seat/state/session/launch row is deleted.

For each existing seat omitted from the registration, read `aiakos.seat_state.session`
in this transaction; a missing state row counts as absent. Only absent or exited permits
retired_at=transaction time (retain an already retired timestamp). Starting, present and
unknown reject with `Aiakos.Api.Contracts.SeatRemovedWhileRunningException`, a sealed
Exception with parameterless constructor and exact Message `A running seat cannot be removed.`.
AppendAsync throws it after rolling back all writes; the exception is the S5/S7 shared
contract, not a receipt or HTTP response. Registration never creates or mutates seat_state.

The receipt's Seats lists desired seats in canonical shared.seats order, followed by removed
seats in ordinal member order. Retired is true exactly when retired_at is non-null. Drifted
is true exactly when seat_state.session is starting or present, current_launch_id identifies
its tenant/seat's seat_launch, and that launch's spec_hash or binding_hash differs from the
new seat hashes. Otherwise Drifted is false, including missing state or launch. No launch is
restarted or rewritten. Receipt RevisionId/Revision identify the selected stored pair.

Malformed JSON in `RigHashVerifier.Verify` throws `FormatException` with exact message `Resolved JSON is invalid.`; it does not return a result or write. E8 asserts this exception, while a well-formed hash mismatch returns `Valid=false` with both recomputed hashes.

## Round-7 closed contracts

R4. Node rows come from `Aiakos.Orchestrator.Api.IApiNodeReader` with member
`Task<IReadOnlyList<NodeResponse>> ListAsync(Guid tenantId,CancellationToken ct)`.
The route preserves provider order. With no registered provider, GET /v1/nodes returns
HTTP 200 `[]`; it does not invent connected nodes or require a database node table.
E3 includes the unregistered-provider fixture with exact response `[]`; T3 pins it.

R7. The version gate applies exactly to PUT /v1/rigs/{rig} and POST
/v1/seats/{address}/up, /down and /send. GET routes and POST /capture are ungated.
Missing, empty, multiple or unparsable X-Aiakos-Client-Version returns HTTP 400
`{"reason":"INVALID_REQUEST","detail":"Request body is invalid.","retryable":false}`
before lookup or write. Parse a System.Version with at least major and minor; compare
only major.minor. A differing pair returns HTTP 409
`{"reason":"VERSION_INCOMPATIBLE","detail":"CLI and instance versions are incompatible.","retryable":false}`.
E5 includes one fixture each for missing header, malformed header and unequal major.minor
with these exact bodies; equal major.minor with differing patch proceeds. Capture without
this header proceeds. T5 tests these policy decisions independently of producing routes.

R8. S2 waits for merged 13-4 S1 (Core protocol); S6 waits for merged 13-4 S16
(command service registration). Resolve the dispatcher optionally; an absent dispatcher
and a faulted dispatch task both return HOST_UNAVAILABLE below. Caller cancellation is
propagated as cancellation, not translated to a fault response. Never log exception details.
For any SeatCommandRejected, preserve its Reason byte-for-byte in the problem reason,
HTTP 409, detail `The seat rejected the command.`. Retryable is true exactly for
NODE_NOT_CONNECTED, SEAT_ACTOR_UNAVAILABLE, SEAT_COMMAND_SERVICE_UNAVAILABLE,
COMMAND_DISPATCH_FAILED, CAPTURE_FAILED, DELIVERY_IN_FLIGHT, SEAT_WORKING,
SEAT_NEEDS_INPUT, SEAT_STATE_UNKNOWN and SEAT_ACTIVITY_UNKNOWN; all other reasons,
including RESUME_LOST, are non-retryable. This flag describes transient state only and
never authorizes automatic resend. Unknown future rejection reasons pass through with false.

The following table is exhaustive; L and C are dispatcher-returned UUID strings, text is
JSON-escaped capture.Text, and capture flags come directly from PaneCapture.

| Command | Reply | Status | Exact JSON body |
|---|---|---|---|
| up | SeatCommandAccepted(L,C), L non-null | 202 | `{"launch_id":"L","command_id":"C"}` |
| up | SeatAlreadyUp(L) | 200 | `{"launch_id":"L"}` |
| down/send | SeatCommandAccepted(any,C) | 202 | `{"command_id":"C"}` |
| down | SeatAlreadyDown | 200 | `{}` |
| capture | SeatCaptureCompleted(capture) | 200 | `{"text":"text","truncated":false,"pane_dead":false}` (flags/text from capture) |
| capture | SeatCommandTimedOut("CAPTURE_TIMEOUT") | 504 | `{"reason":"CAPTURE_TIMEOUT","detail":"Seat capture timed out.","retryable":true}` |
| any | SeatCommandRejected("NODE_NOT_CONNECTED") | 409 | `{"reason":"NODE_NOT_CONNECTED","detail":"The seat rejected the command.","retryable":true}` |
| any | SeatCommandRejected("RESUME_LOST") | 409 | `{"reason":"RESUME_LOST","detail":"The seat rejected the command.","retryable":false}` |
| any | other SeatCommandRejected(reason) | 409 | `{"reason":"<reason>","detail":"The seat rejected the command.","retryable":<flag above>}` |
| any | missing dispatcher, faulted task, null/unknown object, wrong-command reply, up accepted with null LaunchId, or other timeout reason | 503 | `{"reason":"HOST_UNAVAILABLE","detail":"The instance host is unavailable.","retryable":true}` |

All error rows use application/problem+json, including CaptureTimeoutResponse. E6 comprises
every row of this table; T6 pins each row with fake ports, including faults and absent DI.
No dispatch reply is acknowledged until its task completes.

C2. Add in the same migration `aiakos.rig_revision_file(tenant_id uuid NOT NULL,
revision_id uuid NOT NULL, sha256 text NOT NULL, contents bytea NOT NULL,
PRIMARY KEY(tenant_id,revision_id,sha256), FOREIGN KEY(tenant_id,revision_id)
REFERENCES aiakos.rig_revision(tenant_id,revision_id))`. Add UNIQUE(tenant_id,revision_id)
to rig_revision for this FK. Persist each verified map entry exactly once with its raw bytes
in the revision transaction; duplicate hash-pair registration reuses the existing revision
and files. Canonical descriptors remain in resolved jsonb; file bytes never enter that JSON.

R11. The HTTP registration route owns request parsing, the R7 version gate, RigHashVerifier,
and file-map verification before calling AppendAsync. Reject malformed canonical JSON as
INVALID_REQUEST; reject hash mismatch as HASH_MISMATCH and invalid/missing/extra contents
as FILE_CONTENT_INVALID before any repository call. Registration carries required
`seat_parameters`, an array of the existing Aiakos.Spec.ResolvedSeatParameters serialized
snake_case, including its SpecHash, BindingHash and Projection properties. Add these records
and their nested types to the source-generated context. Require exactly one parameter record
per canonical agent seat and none for human/unknown seats, no duplicates; Seat, Rig, Node,
Harness, SpecHash and BindingHash must equal the canonical/registration sources named by R5.
Reject missing/null array, unmatched identities/hashes, missing agent placement or mismatched
route rig/shared.name as INVALID_REQUEST before AppendAsync. Remaining parameter values are
loader-produced launch data, persisted unchanged; this route does not reconstruct a loader
or load files from source_path. R11 catches only SeatRemovedWhileRunningException for the
SEAT_REMOVED_WHILE_RUNNING mapping below; cancellation propagates unchanged.

E4. Repository matrix (A=(s1,b1), B=(s2,b2), C=(s2,b3); hashes are distinct valid fixture
strings; each fixture has canonical descriptors and matching loader parameter records):

| Input | Exact repository result/state |
|---|---|
| no rig named fixture, register A | new rig and revision 1, receipt (idA,1,true,true,Seats); current=idA |
| A current, register A | receipt (idA,1,false,false,Seats); no revision/file append; seats still applied |
| A current, register B | receipt (idB,2,true,true,Seats); current=idB |
| B current, register A | receipt (idA,1,true,true,Seats); only two revisions; current=idA; A seats reapplied |
| B current, register C | receipt (idC,3,false,true,Seats); current=idC |
| retired member returns | same seat_id/address; retired_at=NULL; receipt Retired=false |
| present launch hashes A, desired B | Drifted=true; launch/state unchanged |
| present launch hashes B, desired B | Drifted=false |
| absent/exited/missing state, removed member | retired_at set; receipt Retired=true, Drifted=false; no state row created |
| starting/present/unknown state, removed member | SeatRemovedWhileRunningException with Message `A running seat cannot be removed.`; zero rig/revision/file/seat/state writes |

T4 pins every E4 row with real Postgres, including retained IDs, current metadata, independent
flags, parameter jsonb equality, agent/human column sources, raw binary bytes round-trip,
revision/file reuse and rollback. These are persistent regression tests committed by S5.

E9. HTTP fixtures use the registration DTO above: resolved is canonical JSON as a JSON
string, files_by_sha256 is a map of lowercase SHA-256 hex keys to base64 byte-array values,
seat_parameters is the matching loader array (empty for no agent seats). A first empty-seat
registration returns HTTP 200 `{"revision_id":"<uuid>","revision":1,"spec_changed":true,"binding_changed":true,"seats":[]}`;
an immediate repeat returns the same id/revision and false flags; other flags and seats are
copied exactly from the repository receipt. HASH_MISMATCH returns HTTP 400
`{"reason":"HASH_MISMATCH","detail":"Resolved rig hashes do not match.","retryable":false}`.
A repository SeatRemovedWhileRunningException returns HTTP 409
`{"reason":"SEAT_REMOVED_WHILE_RUNNING","detail":"A running seat cannot be removed.","retryable":false}`.
FILE_CONTENT_INVALID returns HTTP 400
`{"reason":"FILE_CONTENT_INVALID","detail":"Rig file contents do not match the canonical form.","retryable":false}`.
Malformed resolved JSON and every invalid parameter/identity case listed in R11 return HTTP
400 `{"reason":"INVALID_REQUEST","detail":"Request body is invalid.","retryable":false}`
without a repository call. T9 pins these with fake repository probes, including the named
exception; T1 pins seat_parameters serialization and the shared exception type.

T9. Pins E9 and version rejection before AppendAsync with fake repository probes.
