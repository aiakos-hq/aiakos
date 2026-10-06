using System.Collections;
using System.Diagnostics;
using System.Text.Json;

using Aiakos.Contracts.Node.V1;

using Google.Protobuf;

using Npgsql;
using NpgsqlTypes;

namespace Aiakos.Orchestrator.Seats;

public sealed class PostgresSeatActorWriter(NpgsqlDataSource dataSource) : ISeatActorWriter
{
    public async Task<SeatStoreReceipt> CommitAsync(SeatActorSnapshot before,
        IReadOnlyList<SeatAppliedInput> inputs, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var accepted = inputs.Where(input => input.Step.Disposition != EventDisposition.Duplicate).ToArray();
        foreach (var input in accepted)
            ValidateStage(before, input);
        if (accepted.Length == 0 || !accepted.Any(input => input.Event is not null ||
            input.Step.State != before.State || input.Step.Transitions.Count != 0))
            return new(before.Version, before.CurrentSessionId);

        await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            string? usage = null;
            var hasUsage = false;
            foreach (var input in accepted)
            {
                Guid? eventId = null;
                if (input.Event is { } value)
                {
                    eventId = Guid.CreateVersion7();
                    var opaque = Body(value) is null || ContainsNul(Body(value)!);
                    await WriteEventAsync(connection, transaction, before.Key, input, eventId.Value, opaque, ct)
                        .ConfigureAwait(false);
                    if (!opaque && value.Harness?.Usage is { } telemetry &&
                        input.Input is EventReceived received && received.LaunchId is { } launchId &&
                        launchId == input.Step.State.Launch?.LaunchId)
                    {
                        usage = UsageJson(telemetry);
                        hasUsage = true;
                    }
                }
                foreach (var transition in input.Step.Transitions)
                    await WriteTransitionAsync(connection, transaction, before.Key, input, transition, eventId, ct)
                        .ConfigureAwait(false);
            }
            var final = accepted[^1];
            await UpdateStateAsync(connection, transaction, before, final, hasUsage, usage, ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return new(checked(before.Version + 1), before.CurrentSessionId);
        }
        catch (PostgresException exception) when (exception.SqlState is "23514" or "23502" or "23503")
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw new SeatStoreRejectedException();
        }
        catch (PostgresException exception) when (exception.SqlState == "23505")
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw new InvalidOperationException("SEAT_VERSION_CONFLICT");
        }
    }

    // This finding belongs to the next writer stage; never claim it was recorded here.
    public Task RecordActorStoppedAsync(SeatKey key, DateTimeOffset at, CancellationToken ct) =>
        Task.FromException(new SeatStoreRejectedException());

    private static void ValidateStage(SeatActorSnapshot before, SeatAppliedInput input)
    {
        if (input.Step.Findings.Count != 0 || input.Step.Effects.Any(effect => effect is not RequestCapture) ||
            input.Step.State.NativeSessionId != before.State.NativeSessionId ||
            input.Input is EventReceived { Body: LaunchResultBody or StartNotCompletedBody or
                StopResultBody or StopNotCompletedBody or ProcessExitedBody })
            throw new SeatStoreRejectedException();
        if (input.Event is { } value)
            ValidateText(value);
        var state = input.Step.State;
        foreach (var text in new[] { state.SessionReason, state.ActivityDetail, state.ActivityReason,
            state.ResumabilityReason, state.KnownSessionReason, state.KnownActivityDetail,
            state.KnownActivityReason, state.PendingInputRequest, state.NativeSessionId })
            ValidateStoredText(text);
        foreach (var transition in input.Step.Transitions)
        {
            ValidateStoredText(transition.Axis);
            ValidateStoredText(transition.From);
            ValidateStoredText(transition.To);
            ValidateStoredText(transition.Reason);
        }
    }

    private static async Task WriteEventAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        SeatKey key, SeatAppliedInput input, Guid eventId, bool opaque, CancellationToken ct)
    {
        var value = input.Event!;
        if (input.Input is not EventReceived received || input.Step.Disposition is not { } disposition)
            throw new SeatStoreRejectedException();
        var harness = opaque ? null : value.Harness;
        var body = opaque || harness is not null ? null : JsonFormatter.Default.Format(Body(value)!);
        var raw = opaque ? value.ToByteArray() : harness?.Raw.ToByteArray();
        var type = opaque ? "unknown" : value.BodyCase switch
        {
            SeatEvent.BodyOneofCase.Harness => "harness",
            SeatEvent.BodyOneofCase.CommandResult => "command-result",
            SeatEvent.BodyOneofCase.SessionObserved => "session-observed",
            SeatEvent.BodyOneofCase.ProcessExited => "process-exited",
            SeatEvent.BodyOneofCase.Gap => "gap",
            _ => "unknown"
        };
        string? origin = harness?.Origin switch { EventOrigin.Live => "live", EventOrigin.Resync => "resync", _ => null };
        var trace = input.TraceParent is { } parent && ActivityContext.TryParse(parent, null, true, out _) ? parent : null;
        await InsertAsync(connection, transaction, "seat_event",
        [
            Uuid("tenant_id", key.TenantId), Uuid("event_id", eventId), Uuid("seat_id", key.SeatId),
            Uuid("node_instance_id", received.NodeInstanceId), Number("seq", received.Seq),
            Number("source_seq", received.SourceSeq == 0 ? null : received.SourceSeq),
            Uuid("launch_id", received.LaunchId), Text("body_type", type),
            Text("kind", harness is null ? null : SeatVocabulary.ToStored(harness.Kind)),
            Text("native_name", harness?.NativeName), Text("origin", origin),
            Text("native_session_id", harness?.NativeSessionId),
            Json("attributes", harness is null ? null : JsonSerializer.Serialize(harness.Attributes)),
            Json("usage", harness?.Usage is { } usage ? UsageJson(usage) : null), Json("body", body),
            new("raw", NpgsqlDbType.Bytea, raw),
            Text("raw_content_type", opaque ? "application/x-protobuf" : harness?.RawContentType),
            Bool("raw_truncated", !opaque && harness?.RawTruncated == true),
            new("raw_size", NpgsqlDbType.Integer, opaque ? raw!.Length : harness is null ? null : checked((int)harness.RawSize)),
            Text("disposition", SeatVocabulary.ToStored(disposition)),
            Time("observed_at", value.ObservedAt?.ToDateTimeOffset()), Time("received_at", input.At), Text("traceparent", trace)
        ], ct).ConfigureAwait(false);
    }

    private static Task WriteTransitionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        SeatKey key, SeatAppliedInput input, SeatTransition transition, Guid? eventId, CancellationToken ct)
    {
        var cause = input.Event is not null ? "event" : input.Input switch
        {
            QuietTimeoutFired or LaunchWatchdogFired or UnknownProlongedFired => "timer",
            OrchestratorRestarted => "restart",
            _ => "link"
        };
        return InsertAsync(connection, transaction, "seat_transition",
        [
            Uuid("tenant_id", key.TenantId), Uuid("transition_id", Guid.CreateVersion7()), Uuid("seat_id", key.SeatId),
            Text("axis", transition.Axis), Bool("reported", transition.Reported), Text("from_value", transition.From),
            Text("to_value", transition.To), Text("reason", transition.Reason), Text("cause_type", cause),
            Uuid("cause_event_id", eventId), Uuid("cause_command_id", null),
            Uuid("launch_id", input.Step.State.Launch?.LaunchId), Time("at", input.At)
        ], ct);
    }

    private static async Task UpdateStateAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        SeatActorSnapshot before, SeatAppliedInput final, bool hasUsage, string? usage, CancellationToken ct)
    {
        var state = final.Step.State;
        Column[] columns =
        [
            Text("session", SeatVocabulary.ToStored(state.Session)), Text("session_reason", state.SessionReason),
            Time("session_since", state.SessionSince), Text("activity", SeatVocabulary.ToStored(state.Activity)),
            Text("activity_detail", state.ActivityDetail), Text("activity_reason", state.ActivityReason),
            Time("activity_since", state.ActivitySince), Text("resumability", SeatVocabulary.ToStored(state.Resumability)),
            Text("resumability_reason", state.ResumabilityReason), Time("resumability_since", state.ResumabilitySince),
            Text("overlay", state.Overlay is { } overlay ? SeatVocabulary.ToStored(overlay) : null),
            Text("known_session", SeatVocabulary.ToStored(state.KnownSession)), Text("known_session_reason", state.KnownSessionReason),
            Text("known_activity", SeatVocabulary.ToStored(state.KnownActivity)), Text("known_activity_detail", state.KnownActivityDetail),
            Text("known_activity_reason", state.KnownActivityReason), Uuid("current_launch_id", state.Launch?.LaunchId),
            Uuid("current_session_id", before.CurrentSessionId), Text("pending_input_request", state.PendingInputRequest),
            Text("pre_compaction_activity", state.PreCompactionActivity is { } activity ? SeatVocabulary.ToStored(activity) : null),
            Bool("readiness_seen", state.ReadinessSeen), Uuid("node_instance_id", state.NodeInstanceId),
            Number("next_seq", state.NextSeq), Number("last_source_seq", state.LastSourceSeq), Number("catch_up_seq", state.CatchUpSeq),
            Time("last_event_at", state.LastEventAt), Time("updated_at", final.At)
        ];
        // Identifiers come exclusively from this class's fixed column list, never from inputs.
        var assignments = string.Join(", ", columns.Select(column => $"{column.Name}=@{column.Name}"));
        await using var command = new NpgsqlCommand($"""
            UPDATE aiakos.seat_state SET {assignments}, usage=CASE WHEN @has_usage THEN @usage ELSE usage END,
                version=version+1 WHERE tenant_id=@tenant_id AND seat_id=@seat_id AND version=@version
            """, connection, transaction);
        Add(command, [.. columns, Bool("has_usage", hasUsage), Json("usage", usage), Uuid("tenant_id", before.Key.TenantId),
            Uuid("seat_id", before.Key.SeatId), Number("version", before.Version)]);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("SEAT_VERSION_CONFLICT");
    }

    private static async Task InsertAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string table, Column[] columns, CancellationToken ct)
    {
        var names = string.Join(",", columns.Select(column => column.Name));
        var parameters = string.Join(",", columns.Select(column => "@" + column.Name));
        await using var command = new NpgsqlCommand($"INSERT INTO aiakos.{table} ({names}) VALUES ({parameters})", connection, transaction);
        Add(command, columns);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static string UsageJson(Usage usage)
    {
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        if (usage.HasContextUsedPercent) values.Add("context_used_percent", usage.ContextUsedPercent);
        if (usage.HasContextWindowTokens) values.Add("context_window_tokens", usage.ContextWindowTokens);
        if (usage.HasContextUsedTokens) values.Add("context_used_tokens", usage.ContextUsedTokens);
        if (usage.HasCostUsd && double.IsFinite(usage.CostUsd)) values.Add("cost_usd", usage.CostUsd);
        if (usage.ModelId.Length != 0) values.Add("model_id", usage.ModelId);
        return JsonSerializer.Serialize(values);
    }

    private static IMessage? Body(SeatEvent value) => value.BodyCase switch
    {
        SeatEvent.BodyOneofCase.Harness => value.Harness,
        SeatEvent.BodyOneofCase.CommandResult => value.CommandResult,
        SeatEvent.BodyOneofCase.SessionObserved => value.SessionObserved,
        SeatEvent.BodyOneofCase.ProcessExited => value.ProcessExited,
        SeatEvent.BodyOneofCase.Gap => value.Gap,
        _ => null
    };

    private static IEnumerable<string> Strings(object? value)
    {
        if (value is string text) yield return text;
        else if (value is IMessage message)
        {
            foreach (var field in message.Descriptor.Fields.InFieldNumberOrder())
                foreach (var nested in Strings(field.Accessor.GetValue(message))) yield return nested;
        }
        else if (value is IEnumerable items && value is not ByteString)
        {
            foreach (var item in items)
            {
                // Protobuf map fields enumerate KeyValuePair values; both sides may contain text.
                if (item is KeyValuePair<string, string> pair)
                {
                    yield return pair.Key;
                    yield return pair.Value;
                }
                else foreach (var nested in Strings(item)) yield return nested;
            }
        }
    }

    private static bool ContainsNul(IMessage body) => Strings(body).Any(text => text.Contains('\0'));

    private static void ValidateStoredText(string? value)
    {
        if (value?.Contains('\0') == true) throw new SeatStoreRejectedException();
        ValidateText(value);
    }

    private static void ValidateText(object? value)
    {
        foreach (var text in Strings(value))
        {
            for (var i = 0; i < text.Length; i++)
            {
                if (char.IsHighSurrogate(text[i]))
                {
                    if (++i >= text.Length || !char.IsLowSurrogate(text[i])) throw new SeatStoreRejectedException();
                }
                else if (char.IsLowSurrogate(text[i])) throw new SeatStoreRejectedException();
            }
        }
    }

    private sealed record Column(string Name, NpgsqlDbType Type, object? Value);
    private static Column Text(string name, string? value) => new(name, NpgsqlDbType.Text, value);
    private static Column Json(string name, string? value) => new(name, NpgsqlDbType.Jsonb, value);
    private static Column Uuid(string name, Guid? value) => new(name, NpgsqlDbType.Uuid, value);
    private static Column Number(string name, long? value) => new(name, NpgsqlDbType.Bigint, value);
    private static Column Time(string name, DateTimeOffset? value) => new(name, NpgsqlDbType.TimestampTz, value);
    private static Column Bool(string name, bool value) => new(name, NpgsqlDbType.Boolean, value);
    private static void Add(NpgsqlCommand command, Column[] columns)
    {
        foreach (var column in columns)
            command.Parameters.AddWithValue(column.Name, column.Type, column.Value ?? DBNull.Value);
    }
}
