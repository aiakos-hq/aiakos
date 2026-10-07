using System.Collections;
using System.Diagnostics;
using System.Text.Json;

using Aiakos.Contracts.Node.V1;

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

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
            input.Step.State != before.State || input.Step.Transitions.Count != 0 ||
            input.Step.Findings.Count != 0 || input.Step.Effects.Any(effect => effect is AdoptRotatedSession)))
            return new(before.Version, before.CurrentSessionId);

        await using var connection = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            string? usage = null;
            var hasUsage = false;
            var currentSessionId = before.CurrentSessionId;
            var previousState = before.State;
            var wrote = false;
            var actorStoppedCleared = false;
            foreach (var input in accepted)
            {
                if (!actorStoppedCleared)
                {
                    actorStoppedCleared = true;
                    wrote |= await ResolveFindingAsync(connection, transaction, before.Key,
                        SeatVocabulary.FindingActorStopped, input.At, ct).ConfigureAwait(false);
                }
                Guid? eventId = null;
                if (input.Event is { } value)
                {
                    eventId = Guid.CreateVersion7();
                    var opaque = IsOpaque(value);
                    await WriteEventAsync(connection, transaction, before.Key, input, eventId.Value, opaque, ct)
                        .ConfigureAwait(false);
                    wrote = true;
                    if (!opaque && value.Harness?.Usage is { } telemetry &&
                        input.Input is EventReceived received && received.LaunchId is { } launchId &&
                        launchId == input.Step.State.Launch?.LaunchId)
                    {
                        usage = UsageJson(telemetry);
                        hasUsage = true;
                    }
                }
                foreach (var transition in input.Step.Transitions)
                {
                    await WriteTransitionAsync(connection, transaction, before.Key, input, transition, eventId, ct)
                        .ConfigureAwait(false);
                    wrote = true;
                }
                foreach (var finding in input.Step.Findings)
                    wrote |= await WriteFindingAsync(connection, transaction, before.Key, input, finding, ct)
                        .ConfigureAwait(false);
                foreach (var effect in input.Step.Effects)
                {
                    if (effect is AdoptRotatedSession rotated)
                    {
                        if (!string.Equals(previousState.NativeSessionId, rotated.PreviousNativeSessionId,
                                StringComparison.Ordinal) ||
                            !string.Equals(input.Step.State.NativeSessionId, rotated.NativeSessionId,
                                StringComparison.Ordinal))
                            throw new SeatStoreRejectedException();
                        var adopted = await AdoptSessionAsync(connection, transaction, before, rotated,
                            input.At, ct).ConfigureAwait(false);
                        currentSessionId = adopted;
                        wrote = true;
                    }
                }
                wrote |= input.Step.State != previousState;
                if (previousState.Resumability != input.Step.State.Resumability)
                {
                    if (input.Step.State.Resumability == ResumabilityValue.Resumable)
                        wrote |= await SetSessionHistoryAsync(connection, transaction, before.Key,
                            currentSessionId, "conversation_at", input.At, ct).ConfigureAwait(false);
                    else if (input.Step.State.Resumability == ResumabilityValue.Lost)
                        wrote |= await SetSessionHistoryAsync(connection, transaction, before.Key,
                            currentSessionId, "lost_at", input.At, ct).ConfigureAwait(false);
                }
                previousState = input.Step.State;
            }
            var final = accepted[^1];
            if (hasUsage || wrote)
            {
                await UpdateStateAsync(connection, transaction, before, final, hasUsage, usage,
                    currentSessionId, ct).ConfigureAwait(false);
                wrote = true;
            }
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return new(wrote ? checked(before.Version + 1) : before.Version, currentSessionId);
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
        if (input.Event?.ObservedAt is { } observedAt && !IsValidTimestamp(observedAt))
            throw new SeatStoreRejectedException();
        var opaque = input.Event is { } evidence && IsOpaque(evidence);
        if (input.Step.Effects.Any(effect => effect is not RequestCapture and not AdoptRotatedSession) ||
            (!opaque && input.Input is EventReceived { Body: LaunchResultBody or StartNotCompletedBody or
                StopResultBody or StopNotCompletedBody or ProcessExitedBody }))
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
            new("raw_size", NpgsqlDbType.Integer, opaque ? raw!.Length : harness is null || harness.RawSize > int.MaxValue ? null : (int)harness.RawSize),
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

    private static async Task<bool> WriteFindingAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        SeatKey key, SeatAppliedInput input, FindingChange finding, CancellationToken ct)
    {
        if (!finding.Open)
            return await ResolveFindingAsync(connection, transaction, key, finding.Kind, input.At, ct)
                .ConfigureAwait(false);

        var severity = finding.Kind is SeatVocabulary.FindingLaunchRejected or SeatVocabulary.FindingLaunchFailed or
            SeatVocabulary.FindingResumeLost or SeatVocabulary.FindingSessionIdMismatch or
            SeatVocabulary.FindingTurnFailed or SeatVocabulary.FindingActorStopped ? "error" : "warning";
        await using var command = new NpgsqlCommand("""
            INSERT INTO aiakos.seat_finding
                (tenant_id,finding_id,seat_id,node_name,kind,severity,status,summary,evidence,launch_id,
                 occurrences,first_seen_at,last_seen_at)
            VALUES (@tenant,@id,@seat,NULL,@kind,@severity,'open',@summary,NULL,@launch,1,@at,@at)
            ON CONFLICT (tenant_id,seat_id,kind) WHERE status='open' AND seat_id IS NOT NULL
            DO UPDATE SET occurrences=aiakos.seat_finding.occurrences+1,
                          last_seen_at=EXCLUDED.last_seen_at,launch_id=EXCLUDED.launch_id
            """, connection, transaction);
        command.Parameters.AddWithValue("tenant", key.TenantId);
        command.Parameters.AddWithValue("id", Guid.CreateVersion7());
        command.Parameters.AddWithValue("seat", key.SeatId);
        command.Parameters.AddWithValue("kind", finding.Kind);
        command.Parameters.AddWithValue("severity", severity);
        command.Parameters.AddWithValue("summary", $"Seat finding: {finding.Kind}.");
        command.Parameters.AddWithValue("launch", input.Step.State.Launch?.LaunchId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("at", input.At);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return true;
    }

    private static async Task<bool> ResolveFindingAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        SeatKey key, string kind, DateTimeOffset at, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE aiakos.seat_finding SET status='resolved',resolved_at=@at,resolved_reason='state-changed'
            WHERE tenant_id=@tenant AND seat_id=@seat AND kind=@kind AND status='open'
            """, connection, transaction);
        command.Parameters.AddWithValue("at", at);
        command.Parameters.AddWithValue("tenant", key.TenantId);
        command.Parameters.AddWithValue("seat", key.SeatId);
        command.Parameters.AddWithValue("kind", kind);
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 0;
    }

    private static async Task<Guid> AdoptSessionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        SeatActorSnapshot before, AdoptRotatedSession effect, DateTimeOffset at, CancellationToken ct)
    {
        await using var previous = new NpgsqlCommand("""
            SELECT session_id FROM aiakos.seat_session
            WHERE tenant_id=@tenant AND seat_id=@seat AND harness=@harness AND native_session_id=@native
            """, connection, transaction);
        previous.Parameters.AddWithValue("tenant", before.Key.TenantId);
        previous.Parameters.AddWithValue("seat", before.Key.SeatId);
        previous.Parameters.AddWithValue("harness", before.Harness ?? (object)DBNull.Value);
        previous.Parameters.AddWithValue("native", effect.PreviousNativeSessionId);
        var previousValue = await previous.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (previousValue is not Guid previousId) throw new SeatStoreRejectedException();

        var newId = Guid.CreateVersion7();
        await using var insert = new NpgsqlCommand("""
            INSERT INTO aiakos.seat_session
                (tenant_id,session_id,seat_id,harness,native_session_id,decision,previous_session_id,created_at)
            VALUES (@tenant,@id,@seat,@harness,@native,'harness-cleared',@previous,@at)
            ON CONFLICT (tenant_id,harness,native_session_id) DO NOTHING
            RETURNING session_id
            """, connection, transaction);
        insert.Parameters.AddWithValue("tenant", before.Key.TenantId);
        insert.Parameters.AddWithValue("id", newId);
        insert.Parameters.AddWithValue("seat", before.Key.SeatId);
        insert.Parameters.AddWithValue("harness", before.Harness ?? (object)DBNull.Value);
        insert.Parameters.AddWithValue("native", effect.NativeSessionId);
        insert.Parameters.AddWithValue("previous", previousId);
        insert.Parameters.AddWithValue("at", at);
        if (await insert.ExecuteScalarAsync(ct).ConfigureAwait(false) is Guid insertedId) return insertedId;

        await using var owner = new NpgsqlCommand("""
            SELECT seat_id,session_id FROM aiakos.seat_session
            WHERE tenant_id=@tenant AND harness=@harness AND native_session_id=@native
            """, connection, transaction);
        owner.Parameters.AddWithValue("tenant", before.Key.TenantId);
        owner.Parameters.AddWithValue("harness", before.Harness ?? (object)DBNull.Value);
        owner.Parameters.AddWithValue("native", effect.NativeSessionId);
        await using var row = await owner.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await row.ReadAsync(ct).ConfigureAwait(false)) throw new SeatStoreRejectedException();
        if (row.GetGuid(0) != before.Key.SeatId)
        {
            await row.DisposeAsync().ConfigureAwait(false);
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw new InvalidOperationException("SEAT_SESSION_CONFLICT");
        }
        return row.GetGuid(1);
    }

    private static async Task<bool> SetSessionHistoryAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        SeatKey key, Guid? sessionId, string column, DateTimeOffset at, CancellationToken ct)
    {
        if (sessionId is null) return false;
        if (column is not ("conversation_at" or "lost_at")) throw new ArgumentOutOfRangeException(nameof(column));
        await using var command = new NpgsqlCommand($"""
            UPDATE aiakos.seat_session SET {column}=COALESCE({column},@at)
            WHERE tenant_id=@tenant AND session_id=@session AND {column} IS NULL
            """, connection, transaction);
        command.Parameters.AddWithValue("at", at);
        command.Parameters.AddWithValue("tenant", key.TenantId);
        command.Parameters.AddWithValue("session", sessionId.Value);
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 0;
    }

    private static async Task UpdateStateAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        SeatActorSnapshot before, SeatAppliedInput final, bool hasUsage, string? usage,
        Guid? currentSessionId, CancellationToken ct)
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
            Uuid("current_session_id", currentSessionId), Text("pending_input_request", state.PendingInputRequest),
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

    private static bool IsOpaque(SeatEvent value) => Body(value) is not { } body ||
        ContainsNul(body) || ContainsInvalidTimestamp(body);

    private static bool IsValidTimestamp(Timestamp value) =>
        value.Seconds is >= -62135596800 and <= 253402300799 &&
        value.Nanos is >= 0 and <= 999999999;

    private static bool ContainsInvalidTimestamp(object? value)
    {
        if (value is Timestamp timestamp) return !IsValidTimestamp(timestamp);
        if (value is IMessage message)
            return message.Descriptor.Fields.InFieldNumberOrder()
                .Any(field => ContainsInvalidTimestamp(field.Accessor.GetValue(message)));
        if (value is IEnumerable items && value is not string && value is not ByteString)
            foreach (var item in items)
                if (ContainsInvalidTimestamp(item)) return true;
        return false;
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
