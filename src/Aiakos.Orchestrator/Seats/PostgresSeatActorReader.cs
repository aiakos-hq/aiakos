using System.Collections.ObjectModel;
using System.Data;

using Aiakos.Contracts.Node.V1;

using Npgsql;
using NpgsqlTypes;

namespace Aiakos.Orchestrator.Seats;

public sealed class PostgresSeatActorReader(NpgsqlDataSource dataSource) : ISeatActorReader
{
    private const string SnapshotSql = """
        SELECT s.kind,s.harness,s.node_name,s.desired,s.retired_at,s.created_at,
               st.version,st.session,st.session_reason,st.session_since,
               st.activity,st.activity_detail,st.activity_reason,st.activity_since,
               st.resumability,st.resumability_reason,st.resumability_since,st.overlay,
               st.known_session,st.known_session_reason,st.known_activity,st.known_activity_detail,st.known_activity_reason,
               st.current_launch_id,st.current_session_id,st.pending_input_request,st.pre_compaction_activity,
               st.readiness_seen,st.node_instance_id,st.next_seq,st.last_source_seq,st.catch_up_seq,st.last_event_at,
               cs.session_id AS joined_session_id,cs.seat_id AS session_seat_id,cs.harness AS session_harness,
               cs.native_session_id,cl.launch_id AS joined_launch_id,cl.seat_id AS launch_seat_id,
               cl.mode AS launch_mode,cl.decision AS launch_decision,
               ls.session_id AS launch_session_id,ls.seat_id AS launch_session_seat_id,ls.harness AS launch_session_harness
        FROM aiakos.seat s
        LEFT JOIN aiakos.seat_state st ON st.tenant_id=s.tenant_id AND st.seat_id=s.seat_id
        LEFT JOIN aiakos.seat_session cs ON cs.tenant_id=st.tenant_id AND cs.session_id=st.current_session_id
        LEFT JOIN aiakos.seat_launch cl ON cl.tenant_id=st.tenant_id AND cl.launch_id=st.current_launch_id
        LEFT JOIN aiakos.seat_session ls ON ls.tenant_id=cl.tenant_id AND ls.session_id=cl.session_id
        WHERE s.tenant_id=@tenant AND s.seat_id=@seat
        """;

    public async Task<SeatActorSnapshot?> LoadAsync(SeatKey key, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct).ConfigureAwait(false);
        await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
            await readOnly.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        try
        {
            SeatActorSnapshot snapshot;
            bool hasState;
            await using (var command = new NpgsqlCommand(SnapshotSql, connection, transaction))
            {
                Identity(command, key);
                await using var row = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                if (!await row.ReadAsync(ct).ConfigureAwait(false))
                    return null;
                var kind = Text(row, "kind");
                if (kind is not ("agent" or "human")) throw InvalidSnapshot();
                var harness = OptionalText(row, "harness");
                var node = OptionalText(row, "node_name");
                var retired = !Null(row, "retired_at");
                hasState = !Null(row, "version");
                if (!hasState)
                {
                    if (kind != "human") throw InvalidSnapshot();
                    snapshot = new(key, kind, harness, node, retired, 0, null,
                        SeatState.Initial(Time(row, "created_at")), EmptyCommands());
                }
                else
                {
                    var state = Hydrate(row, key, harness);
                    snapshot = new(key, kind, harness, node, retired, Value<long>(row, "version"),
                        OptionalGuid(row, "current_session_id"), state, EmptyCommands());
                }
            }
            if (hasState)
            {
                var commands = new Dictionary<Guid, SeatStoredCommand>();
                await using var command = new NpgsqlCommand("""
                    SELECT command_id,launch_id,kind,status,outcome FROM aiakos.seat_command
                    WHERE tenant_id=@tenant AND seat_id=@seat ORDER BY command_id
                    """, connection, transaction);
                Identity(command, key);
                await using var rows = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await rows.ReadAsync(ct).ConfigureAwait(false))
                {
                    var id = Value<Guid>(rows, "command_id");
                    var kind = Text(rows, "kind") switch
                    {
                        "start" => SeatCommandKind.Start, "deliver" => SeatCommandKind.Deliver,
                        "keys" => SeatCommandKind.Keys, "capture" => SeatCommandKind.Capture,
                        "stop" => SeatCommandKind.Stop, _ => throw InvalidSnapshot()
                    };
                    var status = Text(rows, "status");
                    if (status is not ("pending" or "sent" or "completed" or "rejected" or "failed" or "timed-out" or "unknown"))
                        throw InvalidSnapshot();
                    commands.Add(id, new(id, OptionalGuid(rows, "launch_id"), kind, status, OptionalText(rows, "outcome")));
                }
                snapshot = snapshot with { Commands = new ReadOnlyDictionary<Guid, SeatStoredCommand>(commands) };
            }
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return snapshot;
        }
        catch (Exception exception) when (exception is InvalidCastException or ArgumentException or FormatException or OverflowException)
        {
            // Data conversion errors must not expose the underlying value or provider exception.
            throw InvalidSnapshot();
        }
    }

    public async Task<IReadOnlyList<SeatKey>> GetStartupSeatsAsync(CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT s.tenant_id,s.seat_id FROM aiakos.seat s
            LEFT JOIN aiakos.seat_state st ON st.tenant_id=s.tenant_id AND st.seat_id=s.seat_id
            WHERE s.kind='agent' AND s.retired_at IS NULL AND (st.current_launch_id IS NOT NULL OR s.desired='up')
            ORDER BY s.tenant_id,s.seat_id
            """);
        await using var rows = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var keys = new List<SeatKey>();
        while (await rows.ReadAsync(ct).ConfigureAwait(false))
            keys.Add(new(rows.GetGuid(0), rows.GetGuid(1)));
        return keys.AsReadOnly();
    }

    public async Task<SeatNativeSession?> FindNativeSessionAsync(Guid tenantId, string harness,
        string nativeSessionId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ValidateText(harness);
        ValidateText(nativeSessionId);
        await using var command = dataSource.CreateCommand("""
            SELECT seat_id,session_id FROM aiakos.seat_session
            WHERE tenant_id=@tenant AND harness=@harness AND native_session_id=@native
            """);
        command.Parameters.AddWithValue("tenant", tenantId);
        command.Parameters.AddWithValue("harness", harness);
        command.Parameters.AddWithValue("native", nativeSessionId);
        await using var row = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await row.ReadAsync(ct).ConfigureAwait(false) ? new(row.GetGuid(0), row.GetGuid(1)) : null;
    }

    public async Task<IReadOnlySet<long>> GetCommittedSequencesAsync(SeatKey key, Guid nodeInstanceId,
        IReadOnlyList<long> sequences, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (sequences.Count == 0) return new HashSet<long>();
        await using var command = dataSource.CreateCommand("""
            SELECT seq FROM aiakos.seat_event
            WHERE tenant_id=@tenant AND seat_id=@seat AND node_instance_id=@epoch AND seq=ANY(@sequences)
            """);
        Identity(command, key);
        command.Parameters.AddWithValue("epoch", nodeInstanceId);
        command.Parameters.AddWithValue("sequences", NpgsqlDbType.Array | NpgsqlDbType.Bigint, sequences.ToArray());
        await using var rows = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var found = new HashSet<long>();
        while (await rows.ReadAsync(ct).ConfigureAwait(false)) found.Add(rows.GetInt64(0));
        return found;
    }

    private static SeatState Hydrate(NpgsqlDataReader row, SeatKey key, string? harness)
    {
        var desired = Text(row, "desired") switch { "up" => SeatDesired.Up, "down" => SeatDesired.Down, _ => throw InvalidSnapshot() };
        var session = OptionalGuid(row, "current_session_id");
        if (session is not null && (OptionalGuid(row, "joined_session_id") != session ||
            OptionalGuid(row, "session_seat_id") != key.SeatId || OptionalText(row, "session_harness") != harness))
            throw InvalidSnapshot();
        CurrentLaunch? launch = null;
        if (OptionalGuid(row, "current_launch_id") is { } launchId)
        {
            if (OptionalGuid(row, "joined_launch_id") != launchId || OptionalGuid(row, "launch_seat_id") != key.SeatId ||
                OptionalGuid(row, "launch_session_id") is null || OptionalGuid(row, "launch_session_seat_id") != key.SeatId ||
                OptionalText(row, "launch_session_harness") != harness)
                throw InvalidSnapshot();
            var mode = Text(row, "launch_mode") switch
            {
                "fresh" => LaunchMode.Fresh, "resume" => LaunchMode.Resume, "fork" => LaunchMode.Fork, _ => throw InvalidSnapshot()
            };
            var reused = Text(row, "launch_decision") switch
            {
                "new-session" or "fresh-explicit" => false,
                "no-conversation-yet" or "resume" or "resume-unverified" => true,
                _ => throw InvalidSnapshot()
            };
            launch = new(launchId, mode, reused, desired == SeatDesired.Down);
        }
        return new SeatState
        {
            Session = Session(Text(row, "session")), SessionReason = OptionalText(row, "session_reason"), SessionSince = Time(row, "session_since"),
            Activity = Activity(Text(row, "activity")), ActivityDetail = OptionalText(row, "activity_detail"),
            ActivityReason = OptionalText(row, "activity_reason"), ActivitySince = Time(row, "activity_since"),
            Resumability = Resumability(Text(row, "resumability")), ResumabilityReason = OptionalText(row, "resumability_reason"),
            ResumabilitySince = Time(row, "resumability_since"),
            Overlay = OptionalText(row, "overlay") switch
            {
                null => null, "node-link-lost" => SeatOverlay.NodeLinkLost, "orchestrator-restarted" => SeatOverlay.OrchestratorRestarted,
                _ => throw InvalidSnapshot()
            },
            KnownSession = Session(Text(row, "known_session")), KnownSessionReason = OptionalText(row, "known_session_reason"),
            KnownActivity = Activity(Text(row, "known_activity")), KnownActivityDetail = OptionalText(row, "known_activity_detail"),
            KnownActivityReason = OptionalText(row, "known_activity_reason"), Desired = desired, Launch = launch,
            NativeSessionId = OptionalText(row, "native_session_id"), PendingInputRequest = OptionalText(row, "pending_input_request"),
            PreCompactionActivity = OptionalText(row, "pre_compaction_activity") is { } activity ? Activity(activity) : null,
            ReadinessSeen = Value<bool>(row, "readiness_seen"), NodeInstanceId = OptionalGuid(row, "node_instance_id"),
            NextSeq = Value<long>(row, "next_seq"), LastSourceSeq = Value<long>(row, "last_source_seq"),
            CatchUpSeq = Null(row, "catch_up_seq") ? null : Value<long>(row, "catch_up_seq"),
            LastEventAt = Null(row, "last_event_at") ? null : Time(row, "last_event_at")
        };
    }

    private static SessionValue Session(string value) => value switch
    {
        "absent" => SessionValue.Absent, "starting" => SessionValue.Starting, "present" => SessionValue.Present,
        "exited" => SessionValue.Exited, "unknown" => SessionValue.Unknown, _ => throw InvalidSnapshot()
    };
    private static ActivityValue Activity(string value) => value switch
    {
        "none" => ActivityValue.None, "idle" => ActivityValue.Idle, "working" => ActivityValue.Working,
        "needs-input" => ActivityValue.NeedsInput, "unknown" => ActivityValue.Unknown, _ => throw InvalidSnapshot()
    };
    private static ResumabilityValue Resumability(string value) => value switch
    {
        "none" => ResumabilityValue.None, "fresh-only" => ResumabilityValue.FreshOnly, "resumable" => ResumabilityValue.Resumable,
        "lost" => ResumabilityValue.Lost, "unknown" => ResumabilityValue.Unknown, _ => throw InvalidSnapshot()
    };
    private static InvalidOperationException InvalidSnapshot() => new("INVALID_SEAT_SNAPSHOT");
    private static ReadOnlyDictionary<Guid, SeatStoredCommand> EmptyCommands() =>
        new ReadOnlyDictionary<Guid, SeatStoredCommand>(new Dictionary<Guid, SeatStoredCommand>());
    private static bool Null(NpgsqlDataReader row, string name) => row.IsDBNull(row.GetOrdinal(name));
    private static T Value<T>(NpgsqlDataReader row, string name) => row.GetFieldValue<T>(row.GetOrdinal(name));
    private static string Text(NpgsqlDataReader row, string name) => Value<string>(row, name);
    private static string? OptionalText(NpgsqlDataReader row, string name) => Null(row, name) ? null : Text(row, name);
    private static Guid? OptionalGuid(NpgsqlDataReader row, string name) => Null(row, name) ? null : Value<Guid>(row, name);
    private static DateTimeOffset Time(NpgsqlDataReader row, string name) => Value<DateTimeOffset>(row, name);
    private static void Identity(NpgsqlCommand command, SeatKey key)
    {
        command.Parameters.AddWithValue("tenant", key.TenantId);
        command.Parameters.AddWithValue("seat", key.SeatId);
    }
    private static void ValidateText(string value)
    {
        if (value is null) throw InvalidSnapshot();
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\0') throw InvalidSnapshot();
            if (char.IsHighSurrogate(value[i]))
            {
                if (++i >= value.Length || !char.IsLowSurrogate(value[i])) throw InvalidSnapshot();
            }
            else if (char.IsLowSurrogate(value[i])) throw InvalidSnapshot();
        }
    }
}
