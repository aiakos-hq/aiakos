-- Rig instance (spec 0003 R26). Minimal: #15 may add revision history.
CREATE TABLE aiakos.rig (
    tenant_id     uuid        NOT NULL REFERENCES aiakos.tenant (tenant_id),
    rig_id        uuid        PRIMARY KEY,
    name          text        NOT NULL CHECK (name ~ '^[a-z][a-z0-9-]{1,39}$'),
    spec_hash     text        NOT NULL,
    binding_hash  text        NOT NULL,
    tool_version  text        NOT NULL,
    resolved      jsonb       NOT NULL,            -- canonical resolved rig, file contents by hash
    created_at    timestamptz NOT NULL DEFAULT now(),
    updated_at    timestamptz NOT NULL DEFAULT now(),
    UNIQUE (tenant_id, rig_id),
    UNIQUE (tenant_id, name)
);

-- Desired configuration per seat. Written by the up API only.
CREATE TABLE aiakos.seat (
    tenant_id     uuid        NOT NULL REFERENCES aiakos.tenant (tenant_id),
    seat_id       uuid        PRIMARY KEY,
    rig_id        uuid        NOT NULL,
    member        text        NOT NULL,            -- seat id in rig.yaml
    address       text        NOT NULL,            -- member@rig
    kind          text        NOT NULL CHECK (kind IN ('agent', 'human')),
    harness       text,                            -- null for human seats
    node_name     text,                            -- from the binding; null for human seats (D13)
    desired       text        NOT NULL DEFAULT 'down' CHECK (desired IN ('up', 'down')),
    desired_at    timestamptz,
    desired_by    text,                            -- CallerContext user
    spec_hash     text        NOT NULL,
    binding_hash  text        NOT NULL,
    parameters    jsonb,                           -- resolved seat parameter set (spec 0003 R27); no secrets
    retired_at    timestamptz,                     -- removed from rig.yaml; rows are never deleted
    created_at    timestamptz NOT NULL DEFAULT now(),
    updated_at    timestamptz NOT NULL DEFAULT now(),
    UNIQUE (tenant_id, seat_id),
    UNIQUE (tenant_id, address),
    UNIQUE (tenant_id, rig_id, member),
    FOREIGN KEY (tenant_id, rig_id) REFERENCES aiakos.rig (tenant_id, rig_id),
    CHECK ((kind = 'agent') = (harness IS NOT NULL))
);

-- One row per native conversation of a seat.
CREATE TABLE aiakos.seat_session (
    tenant_id          uuid        NOT NULL REFERENCES aiakos.tenant (tenant_id),
    session_id         uuid        PRIMARY KEY,
    seat_id            uuid        NOT NULL,
    harness            text        NOT NULL,
    native_session_id  text        NOT NULL,       -- orchestrator-generated (spike 0002); harness-chosen on rotation (U8)
    decision           text        NOT NULL CHECK (decision IN ('new-session', 'fresh-explicit', 'harness-cleared')),
    previous_session_id uuid,                      -- set for 'harness-cleared' (U8)
    conversation_at    timestamptz,                -- first conversation evidence (U2/U3)
    lost_at            timestamptz,                -- U4
    abandoned_at       timestamptz,                -- replaced by an explicit fresh start (R24)
    abandoned_reason   text,
    created_at         timestamptz NOT NULL DEFAULT now(),
    UNIQUE (tenant_id, session_id),
    UNIQUE (tenant_id, harness, native_session_id),
    FOREIGN KEY (tenant_id, seat_id) REFERENCES aiakos.seat (tenant_id, seat_id),
    FOREIGN KEY (tenant_id, previous_session_id) REFERENCES aiakos.seat_session (tenant_id, session_id)
);

-- One row per launch attempt (StartSeat.launch_id).
CREATE TABLE aiakos.seat_launch (
    tenant_id            uuid        NOT NULL REFERENCES aiakos.tenant (tenant_id),
    launch_id            uuid        PRIMARY KEY,
    seat_id              uuid        NOT NULL,
    session_id           uuid        NOT NULL,
    mode                 text        NOT NULL CHECK (mode IN ('fresh', 'resume', 'fork')),
    decision             text        NOT NULL CHECK (decision IN
                           ('new-session', 'no-conversation-yet', 'resume', 'resume-unverified', 'fresh-explicit')),
    decided_by           text        NOT NULL,     -- CallerContext user
    decision_note        text,
    command_id           uuid        NOT NULL,     -- the StartSeat command
    node_name            text        NOT NULL,
    spec_hash            text        NOT NULL,
    binding_hash         text        NOT NULL,
    seat_token_hash      bytea       NOT NULL,     -- SHA-256 of the per-launch seat token (spec 0002 R45)
    outcome              text        CHECK (outcome IN ('ready', 'failed', 'unknown', 'rejected')),
    outcome_reason       text,                     -- LaunchResult.reason or Error.reason
    observed_session_id  text,
    exit_code            int,
    exit_signal          int,
    evidence             text,                     -- pane capture text for failed/unknown, ≤ 1 MiB
    requested_at         timestamptz NOT NULL DEFAULT now(),
    outcome_at           timestamptz,
    ended_at             timestamptz,              -- exited or stopped
    end_reason           text,                     -- 'stopped', 'killed', 'exited', 'session-ended'
    UNIQUE (tenant_id, launch_id),
    FOREIGN KEY (tenant_id, seat_id)    REFERENCES aiakos.seat (tenant_id, seat_id),
    FOREIGN KEY (tenant_id, session_id) REFERENCES aiakos.seat_session (tenant_id, session_id)
);

-- Every command sent to a node for a seat (spec 0002 R13: persisted before sending).
CREATE TABLE aiakos.seat_command (
    tenant_id         uuid        NOT NULL REFERENCES aiakos.tenant (tenant_id),
    command_id        uuid        PRIMARY KEY,
    seat_id           uuid        NOT NULL,
    launch_id         uuid,
    kind              text        NOT NULL CHECK (kind IN ('start', 'deliver', 'keys', 'capture', 'stop')),
    status            text        NOT NULL CHECK (status IN
                        ('pending', 'sent', 'completed', 'rejected', 'failed', 'timed-out', 'unknown')),
    outcome           text,       -- deliver: confirmed | submitted-unconfirmed | not-delivered | failed | unknown
                                  -- stop: stopped | killed | not-running
    error_reason      text,
    node_instance_id  uuid,       -- instance it was first sent to (at-most-once rule, spec 0002 R18)
    attempts          int         NOT NULL DEFAULT 0,
    payload           jsonb       NOT NULL,        -- lead/body/expect_confirmation, keys, history_lines, grace; never secrets
    forced            boolean     NOT NULL DEFAULT false,  -- send --force (R29)
    turn_id           text,
    result            jsonb,      -- e.g. capture text
    requested_by      text        NOT NULL,
    traceparent       text,
    created_at        timestamptz NOT NULL DEFAULT now(),
    sent_at           timestamptz,
    completed_at      timestamptz,
    UNIQUE (tenant_id, command_id),
    FOREIGN KEY (tenant_id, seat_id)   REFERENCES aiakos.seat (tenant_id, seat_id),
    FOREIGN KEY (tenant_id, launch_id) REFERENCES aiakos.seat_launch (tenant_id, launch_id)
);
CREATE INDEX seat_command_open ON aiakos.seat_command (tenant_id, seat_id)
    WHERE status IN ('pending', 'sent');

-- Append-only evidence: every SeatEvent received (spec 0002 R26), exactly once.
CREATE TABLE aiakos.seat_event (
    tenant_id          uuid        NOT NULL REFERENCES aiakos.tenant (tenant_id),
    event_id           uuid        PRIMARY KEY,
    seat_id            uuid        NOT NULL,
    node_instance_id   uuid        NOT NULL,
    seq                bigint      NOT NULL CHECK (seq > 0),
    source_seq         bigint,                     -- null = absent (0 on the wire)
    launch_id          uuid,                       -- null = not attributable; no FK (stale or foreign launches are kept)
    body_type          text        NOT NULL CHECK (body_type IN
                         ('command-result', 'harness', 'session-observed', 'process-exited', 'gap', 'unknown')),
    kind               text,                       -- HarnessEventKind, lower-kebab, e.g. 'input-requested'
    native_name        text,
    origin             text        CHECK (origin IN ('live', 'resync')),
    native_session_id  text,
    attributes         jsonb,
    usage              jsonb,
    body               jsonb,                      -- non-harness bodies, decoded
    raw                bytea,                      -- HarnessEvent.raw (≤ 256 KiB) or the undecoded envelope for 'unknown'
    raw_content_type   text,
    raw_truncated      boolean     NOT NULL DEFAULT false,
    raw_size           int,
    disposition        text        NOT NULL CHECK (disposition IN
                         ('applied', 'late', 'stale-launch', 'orphan', 'evidence')),
    observed_at        timestamptz,                -- node clock; never used for ordering
    received_at        timestamptz NOT NULL DEFAULT now(),
    traceparent        text,
    UNIQUE (tenant_id, node_instance_id, seat_id, seq),
    FOREIGN KEY (tenant_id, seat_id) REFERENCES aiakos.seat (tenant_id, seat_id)
);
CREATE INDEX seat_event_by_seat ON aiakos.seat_event (tenant_id, seat_id, received_at);
CREATE INDEX seat_event_by_turn ON aiakos.seat_event (tenant_id, seat_id, launch_id, (attributes ->> 'turn_id'));

-- Append-only conclusions: every axis change and why.
CREATE TABLE aiakos.seat_transition (
    tenant_id         uuid        NOT NULL REFERENCES aiakos.tenant (tenant_id),
    transition_id     uuid        PRIMARY KEY,
    seat_id           uuid        NOT NULL,
    axis              text        NOT NULL CHECK (axis IN ('session', 'activity', 'resumability')),
    reported          boolean     NOT NULL,        -- false = last-known value changed under an overlay
    from_value        text        NOT NULL,
    to_value          text        NOT NULL,
    reason            text,                        -- unknown reason, detail or rule id (e.g. 'S6')
    cause_type        text        NOT NULL CHECK (cause_type IN
                        ('event', 'command', 'timer', 'link', 'restart')),
    cause_event_id    uuid,
    cause_command_id  uuid,
    launch_id         uuid,
    at                timestamptz NOT NULL DEFAULT now(),
    FOREIGN KEY (tenant_id, seat_id) REFERENCES aiakos.seat (tenant_id, seat_id)
);
CREATE INDEX seat_transition_by_seat ON aiakos.seat_transition (tenant_id, seat_id, at DESC);

-- Current state; one row per agent seat; written only by its SeatActor.
CREATE TABLE aiakos.seat_state (
    tenant_id                 uuid        NOT NULL REFERENCES aiakos.tenant (tenant_id),
    seat_id                   uuid        PRIMARY KEY,
    version                   bigint      NOT NULL,              -- optimistic concurrency (R5)
    session                   text        NOT NULL CHECK (session IN ('absent', 'starting', 'present', 'exited', 'unknown')),
    session_reason            text,
    session_since             timestamptz NOT NULL,
    activity                  text        NOT NULL CHECK (activity IN ('none', 'idle', 'working', 'needs-input', 'unknown')),
    activity_detail           text,
    activity_reason           text,
    activity_since            timestamptz NOT NULL,
    resumability              text        NOT NULL CHECK (resumability IN ('none', 'fresh-only', 'resumable', 'lost', 'unknown')),
    resumability_reason       text,
    resumability_since        timestamptz NOT NULL,
    overlay                   text        CHECK (overlay IN ('node-link-lost', 'orchestrator-restarted')),
    known_session             text        NOT NULL,              -- last-known values under the overlay
    known_session_reason      text,
    known_activity            text        NOT NULL,
    known_activity_detail     text,
    known_activity_reason     text,
    current_launch_id         uuid,
    current_session_id        uuid,
    pending_input_request     text,                              -- request id or '*'
    pre_compaction_activity   text,
    readiness_seen            boolean     NOT NULL DEFAULT false, -- for the current launch (R15)
    node_instance_id          uuid,                              -- epoch of next_seq
    next_seq                  bigint      NOT NULL DEFAULT 1,
    last_source_seq           bigint      NOT NULL DEFAULT 0,
    catch_up_seq              bigint,                            -- inventory last_seq to reach before clearing the overlay (R17)
    last_event_at             timestamptz,
    usage                     jsonb,                             -- latest Usage (context %, model, cost)
    updated_at                timestamptz NOT NULL DEFAULT now(),
    UNIQUE (tenant_id, seat_id),
    FOREIGN KEY (tenant_id, seat_id)            REFERENCES aiakos.seat (tenant_id, seat_id),
    FOREIGN KEY (tenant_id, current_launch_id)  REFERENCES aiakos.seat_launch (tenant_id, launch_id),
    FOREIGN KEY (tenant_id, current_session_id) REFERENCES aiakos.seat_session (tenant_id, session_id),
    CHECK (session <> 'unknown' OR session_reason IS NOT NULL),
    CHECK (activity <> 'unknown' OR activity_reason IS NOT NULL),
    CHECK (resumability <> 'unknown' OR resumability_reason IS NOT NULL),
    CHECK ((session IN ('absent', 'exited')) = (activity = 'none'))                -- R10
);

CREATE TABLE aiakos.seat_finding (
    tenant_id        uuid        NOT NULL REFERENCES aiakos.tenant (tenant_id),
    finding_id       uuid        PRIMARY KEY,
    seat_id          uuid,                        -- null for node-scoped findings
    node_name        text,
    kind             text        NOT NULL,
    severity         text        NOT NULL CHECK (severity IN ('info', 'warning', 'error')),
    status           text        NOT NULL CHECK (status IN ('open', 'resolved')),
    summary          text        NOT NULL,        -- no secrets, no payload bodies
    evidence         jsonb,                       -- event ids, command ids, capture excerpt reference
    launch_id        uuid,
    occurrences      int         NOT NULL DEFAULT 1,
    first_seen_at    timestamptz NOT NULL DEFAULT now(),
    last_seen_at     timestamptz NOT NULL DEFAULT now(),
    resolved_at      timestamptz,
    resolved_reason  text,
    FOREIGN KEY (tenant_id, seat_id) REFERENCES aiakos.seat (tenant_id, seat_id),
    CHECK (seat_id IS NOT NULL OR node_name IS NOT NULL)
);
CREATE UNIQUE INDEX seat_finding_one_open ON aiakos.seat_finding (tenant_id, seat_id, kind)
    WHERE status = 'open' AND seat_id IS NOT NULL;
