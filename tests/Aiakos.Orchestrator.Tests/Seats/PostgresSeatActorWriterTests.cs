using Aiakos.Contracts.Node.V1;
using Aiakos.Data;
using Aiakos.Orchestrator.Seats;
using Aiakos.Testing;

using Google.Protobuf;

using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

namespace Aiakos.Orchestrator.Tests.Seats;

public sealed class PostgresSeatActorWriterTests(PostgresContainerFixture postgres)
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Seat = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Epoch = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Launch = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid Session = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-10-06T00:00:00Z",
        System.Globalization.CultureInfo.InvariantCulture);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WriteAtomicLinksEachTransitionAndRollsBackInvalidFinalState()
    {
        await using var db = await SeedAsync();
        var writer = new PostgresSeatActorWriter(db);
        var before = Snapshot();
        var first = Applied(1, before.State with { Activity = ActivityValue.Working,
            KnownActivity = ActivityValue.Working, NextSeq = 2 },
            new SeatEvent { Seq = 1, LaunchId = Launch.ToString(), SessionObserved = new() { MatchesExpected = true } });
        var second = Applied(2, first.Step.State with { Activity = ActivityValue.Idle,
            KnownActivity = ActivityValue.Idle, NextSeq = 3 },
            new SeatEvent { Seq = 2, LaunchId = Launch.ToString(), SessionObserved = new() { MatchesExpected = false } });
        var broken = second with { Step = second.Step with { State = second.Step.State with
            { Session = SessionValue.Unknown, SessionReason = null } } };
        var rejected = await Assert.ThrowsAsync<SeatStoreRejectedException>(() => writer.CommitAsync(before, [first, broken], Ct));
        Assert.Equal("SEAT_COMMIT_REJECTED", rejected.Message);
        Assert.Null(rejected.InnerException);
        Assert.Equal(0L, await ScalarAsync(db, "SELECT count(*) FROM aiakos.seat_event"));
        Assert.Equal(0L, await ScalarAsync(db, "SELECT count(*) FROM aiakos.seat_transition"));
        Assert.Equal(7L, await ScalarAsync(db, "SELECT version FROM aiakos.seat_state"));

        Assert.Equal(new SeatStoreReceipt(8, Session), await writer.CommitAsync(before, [first, second], Ct));
        await using var command = db.CreateCommand("""
            SELECT e.seq, t.axis, t.from_value, t.to_value, t.reason, t.cause_type,
                   t.cause_command_id, t.launch_id, e.source_seq, e.received_at
            FROM aiakos.seat_transition t JOIN aiakos.seat_event e
              ON e.tenant_id=t.tenant_id AND e.event_id=t.cause_event_id ORDER BY e.seq
            """);
        await using var rows = await command.ExecuteReaderAsync(Ct);
        for (var seq = 1L; seq <= 2; seq++)
        {
            Assert.True(await rows.ReadAsync(Ct));
            Assert.Equal(seq, rows.GetInt64(0));
            Assert.Equal("activity", rows.GetString(1));
            Assert.Equal(seq == 1 ? "idle" : "working", rows.GetString(2));
            Assert.Equal(seq == 1 ? "working" : "idle", rows.GetString(3));
            Assert.True(rows.IsDBNull(4));
            Assert.Equal("event", rows.GetString(5));
            Assert.True(rows.IsDBNull(6));
            Assert.Equal(Launch, rows.GetGuid(7));
            Assert.True(rows.IsDBNull(8));
            Assert.Equal(At.AddSeconds(seq), rows.GetFieldValue<DateTimeOffset>(9));
        }
        Assert.False(await rows.ReadAsync(Ct));
        await rows.DisposeAsync();
        Assert.Equal(3L, await ScalarAsync(db, "SELECT next_seq FROM aiakos.seat_state"));
        Assert.Equal("idle", await ScalarAsync(db, "SELECT activity FROM aiakos.seat_state"));
    }

    [Fact]
    public async Task WriteConflictAndDuplicateOnlyDoNotLeaveEvidenceAndOneRacerWins()
    {
        await using var db = await SeedAsync();
        var writer = new PostgresSeatActorWriter(db);
        var before = Snapshot();
        var input = Applied(1, before.State with { NextSeq = 2 }, new SeatEvent { Seq = 1 });
        var outcomes = await Task.WhenAll(AttemptAsync(), AttemptAsync());
        Assert.Single(outcomes, outcome => outcome == "ok");
        Assert.Single(outcomes, outcome => outcome == "SEAT_VERSION_CONFLICT");
        Assert.Equal(1L, await ScalarAsync(db, "SELECT count(*) FROM aiakos.seat_event"));
        Assert.Equal(8L, await ScalarAsync(db, "SELECT version FROM aiakos.seat_state"));
        var conflictInput = Applied(2, before.State with { NextSeq = 3 }, new SeatEvent { Seq = 2 });
        var conflict = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.CommitAsync(before, [conflictInput], Ct));
        Assert.Equal("SEAT_VERSION_CONFLICT", conflict.Message);
        Assert.Null(conflict.InnerException);
        var duplicate = input with { Step = input.Step with { Disposition = EventDisposition.Duplicate } };
        Assert.Equal(new SeatStoreReceipt(7, Session), await writer.CommitAsync(before, [duplicate], Ct));
        var current = before with { Version = 8, State = input.Step.State };
        Assert.Equal(new SeatStoreReceipt(9, Session), await writer.CommitAsync(current, [duplicate, conflictInput], Ct));
        Assert.Equal(2L, await ScalarAsync(db, "SELECT count(*) FROM aiakos.seat_event"));

        async Task<string> AttemptAsync()
        {
            try { await writer.CommitAsync(before, [input], Ct); return "ok"; }
            catch (InvalidOperationException exception) { return exception.Message; }
        }
    }

    [Fact]
    public async Task WriteEvidencePreservesBinaryUnicodeUsageAndOpaqueBodies()
    {
        await using var db = await SeedAsync();
        var before = Snapshot();
        var harness = new SeatEvent { Seq = 1, SourceSeq = 4, LaunchId = Launch.ToString(), Harness = new()
        {
            Kind = HarnessEventKind.Telemetry, NativeName = "statusLine", NativeSessionId = "native\ufffe\uffff😀",
            Raw = ByteString.CopyFrom([0, 255, 1]), RawContentType = "application/json", RawSize = 9,
            RawTruncated = true, Origin = EventOrigin.Resync,
            Usage = new() { ContextUsedPercent = 0, CostUsd = 1.25, ModelId = "model" }
        } };
        harness.Harness.Attributes.Add("key", "quoted\"\ufffe😀");
        var unknown = SeatEvent.Parser.ParseFrom(new byte[] { 0x18, 0x02, 0xa0, 0x06, 0x07 });
        var nul = new SeatEvent { Seq = 3, Harness = new() { NativeName = "a\0b" } };
        var known = new SeatEvent { Seq = 4, SessionObserved = new() { NativeSessionId = "native", MatchesExpected = true } };
        var staleUsage = new SeatEvent { Seq = 5, LaunchId = Guid.Parse("99999999-9999-4999-8999-999999999999").ToString(),
            Harness = new() { Usage = new() { CostUsd = double.NaN } } };
        var events = new[] { harness, unknown, nul, known, staleUsage };
        var inputs = events.Select((value, i) => Applied(i + 1, before.State with { NextSeq = i + 2 }, value) with
            { TraceParent = i == 0 ? "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01" : "invalid\0trace" }).ToArray();
        inputs[0] = inputs[0] with { Step = inputs[0].Step with { Disposition = EventDisposition.Late } };
        inputs[4] = inputs[4] with { Step = inputs[4].Step with { Disposition = EventDisposition.StaleLaunch } };
        await new PostgresSeatActorWriter(db).CommitAsync(before, inputs, Ct);
        await using var command = db.CreateCommand("SELECT body_type, kind, native_session_id, attributes->>'key', usage, raw, raw_content_type, raw_truncated, raw_size, traceparent, body, source_seq, origin FROM aiakos.seat_event ORDER BY seq");
        await using var rows = await command.ExecuteReaderAsync(Ct);
        Assert.True(await rows.ReadAsync(Ct));
        Assert.Equal("harness", rows.GetString(0));
        Assert.Equal("telemetry", rows.GetString(1));
        Assert.Equal("native\ufffe\uffff😀", rows.GetString(2));
        Assert.Equal("quoted\"\ufffe😀", rows.GetString(3));
        Assert.Equal(new byte[] { 0, 255, 1 }, rows.GetFieldValue<byte[]>(5));
        Assert.Equal("application/json", rows.GetString(6));
        Assert.True(rows.GetBoolean(7));
        Assert.Equal(9, rows.GetInt32(8));
        Assert.Equal(inputs[0].TraceParent, rows.GetString(9));
        Assert.True(rows.IsDBNull(10));
        Assert.Equal(4L, rows.GetInt64(11));
        Assert.Equal("resync", rows.GetString(12));
        using (var usage = System.Text.Json.JsonDocument.Parse(rows.GetString(4)))
        {
            Assert.Equal(3, usage.RootElement.EnumerateObject().Count());
            Assert.Equal(0, usage.RootElement.GetProperty("context_used_percent").GetInt32());
            Assert.Equal(1.25, usage.RootElement.GetProperty("cost_usd").GetDouble());
            Assert.Equal("model", usage.RootElement.GetProperty("model_id").GetString());
        }
        foreach (var value in new[] { unknown, nul })
        {
            Assert.True(await rows.ReadAsync(Ct));
            Assert.Equal("unknown", rows.GetString(0));
            foreach (var column in new[] { 1, 2, 3, 4, 9, 10, 11, 12 }) Assert.True(rows.IsDBNull(column));
            Assert.Equal(value.ToByteArray(), rows.GetFieldValue<byte[]>(5));
            Assert.Equal("application/x-protobuf", rows.GetString(6));
            Assert.False(rows.GetBoolean(7));
            Assert.Equal(value.CalculateSize(), rows.GetInt32(8));
        }
        Assert.True(await rows.ReadAsync(Ct));
        Assert.Equal("session-observed", rows.GetString(0));
        Assert.True(rows.IsDBNull(5));
        using var body = System.Text.Json.JsonDocument.Parse(rows.GetString(10));
        Assert.True(body.RootElement.GetProperty("matchesExpected").GetBoolean());
        Assert.True(await rows.ReadAsync(Ct));
        Assert.Equal("harness", rows.GetString(0));
        Assert.Equal("unknown", rows.GetString(1));
        Assert.Equal("{}", rows.GetString(4));
        Assert.True(rows.IsDBNull(9));
        Assert.True(rows.IsDBNull(12));
        await rows.DisposeAsync();
        using var latestUsage = System.Text.Json.JsonDocument.Parse((string)(await ScalarAsync(db, "SELECT usage FROM aiakos.seat_state"))!);
        Assert.Equal("model", latestUsage.RootElement.GetProperty("model_id").GetString());
        Assert.Equal(1L, await ScalarAsync(db, "SELECT count(*) FROM aiakos.seat_event WHERE disposition='late'"));
        Assert.Equal(1L, await ScalarAsync(db, "SELECT count(*) FROM aiakos.seat_event WHERE disposition='stale-launch'"));
    }

    [Fact]
    public async Task NonEventStateUpdatePersistsEveryMutableFieldAndTransitionCause()
    {
        await using var db = await SeedAsync();
        var before = Snapshot();
        var state = before.State with
        {
            Session = SessionValue.Unknown, SessionReason = "node-link-lost", SessionSince = At.AddSeconds(6),
            Activity = ActivityValue.Unknown, ActivityReason = "node-link-lost", ActivitySince = At.AddSeconds(7),
            Resumability = ResumabilityValue.Unknown, ResumabilityReason = "observation-gap", ResumabilitySince = At.AddSeconds(8),
            Overlay = SeatOverlay.NodeLinkLost, KnownActivity = ActivityValue.NeedsInput, KnownActivityDetail = "question",
            PendingInputRequest = "request\ufffe", PreCompactionActivity = ActivityValue.Working, ReadinessSeen = true,
            NodeInstanceId = Guid.Parse("88888888-8888-4888-8888-888888888888"), NextSeq = 12,
            LastSourceSeq = 9, CatchUpSeq = 11, LastEventAt = At.AddSeconds(5)
        };
        SeatInput[] causes = [new NodeLinkLost(), new OrchestratorRestarted(), new QuietTimeoutFired()];
        var inputs = causes.Select((cause, i) => new SeatAppliedInput(cause,
            new SeatStep(state, null, null, [new("activity", true, "idle", "unknown", "node-link-lost", "R16")], [], []),
            At.AddSeconds(i), null, null)).ToArray();
        await new PostgresSeatActorWriter(db).CommitAsync(before, inputs, Ct);
        await using var command = db.CreateCommand("SELECT row_to_json(s)::text FROM aiakos.seat_state s");
        using var row = System.Text.Json.JsonDocument.Parse((string)(await command.ExecuteScalarAsync(Ct))!);
        var expected = new Dictionary<string, string?>
        {
            ["session"] = "unknown", ["session_reason"] = "node-link-lost", ["activity"] = "unknown",
            ["activity_detail"] = null, ["activity_reason"] = "node-link-lost", ["resumability"] = "unknown",
            ["resumability_reason"] = "observation-gap", ["overlay"] = "node-link-lost", ["known_session"] = "present",
            ["known_session_reason"] = null, ["known_activity"] = "needs-input", ["known_activity_detail"] = "question",
            ["known_activity_reason"] = null, ["pending_input_request"] = "request\ufffe", ["pre_compaction_activity"] = "working"
        };
        foreach (var pair in expected) Assert.Equal(pair.Value, row.RootElement.GetProperty(pair.Key).GetString());
        Assert.Equal(8, row.RootElement.GetProperty("version").GetInt64());
        Assert.Equal(12, row.RootElement.GetProperty("next_seq").GetInt64());
        Assert.Equal(9, row.RootElement.GetProperty("last_source_seq").GetInt64());
        Assert.Equal(11, row.RootElement.GetProperty("catch_up_seq").GetInt64());
        Assert.True(row.RootElement.GetProperty("readiness_seen").GetBoolean());
        Assert.Equal(Launch, row.RootElement.GetProperty("current_launch_id").GetGuid());
        Assert.Equal(Session, row.RootElement.GetProperty("current_session_id").GetGuid());
        Assert.Equal(state.NodeInstanceId, row.RootElement.GetProperty("node_instance_id").GetGuid());
        Assert.Equal(At.AddSeconds(6), row.RootElement.GetProperty("session_since").GetDateTimeOffset());
        Assert.Equal(At.AddSeconds(7), row.RootElement.GetProperty("activity_since").GetDateTimeOffset());
        Assert.Equal(At.AddSeconds(8), row.RootElement.GetProperty("resumability_since").GetDateTimeOffset());
        Assert.Equal(At.AddSeconds(5), row.RootElement.GetProperty("last_event_at").GetDateTimeOffset());
        Assert.Equal(At.AddSeconds(2), row.RootElement.GetProperty("updated_at").GetDateTimeOffset());
        Assert.Equal("down", await ScalarAsync(db, "SELECT desired FROM aiakos.seat"));
        Assert.Equal(0L, await ScalarAsync(db, "SELECT count(*) FROM aiakos.seat_event"));
        await using var transitions = db.CreateCommand("SELECT cause_type,cause_event_id,cause_command_id FROM aiakos.seat_transition ORDER BY at");
        await using var rows = await transitions.ExecuteReaderAsync(Ct);
        foreach (var cause in new[] { "link", "restart", "timer" })
        {
            Assert.True(await rows.ReadAsync(Ct));
            Assert.Equal(cause, rows.GetString(0));
            Assert.True(rows.IsDBNull(1));
            Assert.True(rows.IsDBNull(2));
        }
    }

    [Fact]
    public async Task StageGuardsAndInvalidScalarTextRejectWithoutWritesAndEmptyInputIsNoop()
    {
        await using var db = await SeedAsync();
        var before = Snapshot();
        var writer = new PostgresSeatActorWriter(db);
        var input = Applied(1, before.State with { NextSeq = 2 }, new SeatEvent { Seq = 1 });
        var finding = input with { Step = input.Step with { Findings = [new("observation-gap", true)] } };
        var rotation = input with { Step = input.Step with { Effects = [new AdoptRotatedSession("new", "old")] } };
        var result = input with { Input = new EventReceived(Epoch, 1, 0, Launch, new ProcessExitedBody(0, null)),
            Event = new SeatEvent { Seq = 1, ProcessExited = new() { ExitCode = 0 } } };
        var surrogate = input with { Event = new SeatEvent { Seq = 1, Harness = new() { NativeName = "\ud800" } } };
        var nulState = input with { Step = input.Step with { State = input.Step.State with { PendingInputRequest = "a\0b" } } };
        foreach (var rejected in new[] { result, surrogate, nulState })
            await Assert.ThrowsAsync<SeatStoreRejectedException>(() => writer.CommitAsync(before, [rejected], Ct));
        await Assert.ThrowsAsync<SeatStoreRejectedException>(() => writer.RecordActorStoppedAsync(before.Key, At, Ct));
        Assert.Equal(new SeatStoreReceipt(7, Session), await writer.CommitAsync(before, [], Ct));
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.CommitAsync(before, [], canceled.Token));
        Assert.Equal(7L, await ScalarAsync(db, "SELECT version FROM aiakos.seat_state"));
        Assert.Equal(0L, await ScalarAsync(db, "SELECT count(*) FROM aiakos.seat_event"));
    }

    [Fact]
    public async Task FindingsMergeResolveReopenAndClearActorStoppedOnNextCommit()
    {
        await using var db = await SeedAsync();
        var before = Snapshot();
        var writer = new PostgresSeatActorWriter(db);
        var inputs = new[]
        {
            FindingInput(before.State, SeatVocabulary.FindingObservationGap, true, At.AddSeconds(1)),
            FindingInput(before.State, SeatVocabulary.FindingObservationGap, true, At.AddSeconds(2)),
            FindingInput(before.State, SeatVocabulary.FindingObservationGap, false, At.AddSeconds(3)),
            FindingInput(before.State, SeatVocabulary.FindingObservationGap, false, At.AddSeconds(4)),
            FindingInput(before.State, SeatVocabulary.FindingNodeNotConnected, false, At.AddSeconds(5)),
            FindingInput(before.State, SeatVocabulary.FindingObservationGap, true, At.AddSeconds(6)),
            FindingInput(before.State, SeatVocabulary.FindingActorStopped, true, At.AddSeconds(7))
        };

        var receipt = await writer.CommitAsync(before, inputs, Ct);

        Assert.Equal(new SeatStoreReceipt(8, Session), receipt);
        await using (var command = db.CreateCommand("""
            SELECT kind,severity,status,summary,evidence,launch_id,occurrences,first_seen_at,last_seen_at,resolved_at,resolved_reason,seat_id,node_name
            FROM aiakos.seat_finding ORDER BY kind,first_seen_at
            """))
        await using (var rows = await command.ExecuteReaderAsync(Ct))
        {
            Assert.True(await rows.ReadAsync(Ct));
            Assert.Equal(SeatVocabulary.FindingActorStopped, rows.GetString(0));
            Assert.Equal("error", rows.GetString(1));
            Assert.Equal("open", rows.GetString(2));
            Assert.Equal("Seat finding: actor-stopped.", rows.GetString(3));
            Assert.True(rows.IsDBNull(4));
            Assert.Equal(Launch, rows.GetGuid(5));
            Assert.Equal(1, rows.GetInt32(6));
            Assert.Equal(At.AddSeconds(7), rows.GetFieldValue<DateTimeOffset>(7));
            Assert.Equal(At.AddSeconds(7), rows.GetFieldValue<DateTimeOffset>(8));
            Assert.True(rows.IsDBNull(9));
            Assert.True(rows.IsDBNull(10));
            Assert.Equal(Seat, rows.GetGuid(11));
            Assert.True(rows.IsDBNull(12));

            Assert.True(await rows.ReadAsync(Ct));
            Assert.Equal(SeatVocabulary.FindingObservationGap, rows.GetString(0));
            Assert.Equal("warning", rows.GetString(1));
            Assert.Equal("resolved", rows.GetString(2));
            Assert.Equal("Seat finding: observation-gap.", rows.GetString(3));
            Assert.True(rows.IsDBNull(4));
            Assert.Equal(Launch, rows.GetGuid(5));
            Assert.Equal(2, rows.GetInt32(6));
            Assert.Equal(At.AddSeconds(1), rows.GetFieldValue<DateTimeOffset>(7));
            Assert.Equal(At.AddSeconds(2), rows.GetFieldValue<DateTimeOffset>(8));
            Assert.Equal(At.AddSeconds(3), rows.GetFieldValue<DateTimeOffset>(9));
            Assert.Equal("state-changed", rows.GetString(10));
            Assert.Equal(Seat, rows.GetGuid(11));
            Assert.True(rows.IsDBNull(12));

            Assert.True(await rows.ReadAsync(Ct));
            Assert.Equal(SeatVocabulary.FindingObservationGap, rows.GetString(0));
            Assert.Equal("warning", rows.GetString(1));
            Assert.Equal("open", rows.GetString(2));
            Assert.Equal("Seat finding: observation-gap.", rows.GetString(3));
            Assert.True(rows.IsDBNull(4));
            Assert.Equal(Launch, rows.GetGuid(5));
            Assert.Equal(1, rows.GetInt32(6));
            Assert.Equal(At.AddSeconds(6), rows.GetFieldValue<DateTimeOffset>(7));
            Assert.Equal(At.AddSeconds(6), rows.GetFieldValue<DateTimeOffset>(8));
            Assert.True(rows.IsDBNull(9));
            Assert.True(rows.IsDBNull(10));
            Assert.Equal(Seat, rows.GetGuid(11));
            Assert.True(rows.IsDBNull(12));
            Assert.False(await rows.ReadAsync(Ct));
        }

        var nextBefore = before with { Version = receipt.Version };
        var work = Applied(1, before.State with { NextSeq = 2 }, new SeatEvent { Seq = 1 });
        var nextReceipt = await writer.CommitAsync(nextBefore, [work], Ct);
        Assert.Equal(new SeatStoreReceipt(9, Session), nextReceipt);
        await using var verify = db.CreateCommand("SELECT status,resolved_reason,resolved_at FROM aiakos.seat_finding WHERE tenant_id=@tenant AND seat_id=@seat AND kind=@kind");
        verify.Parameters.AddWithValue("tenant", Tenant);
        verify.Parameters.AddWithValue("seat", Seat);
        verify.Parameters.AddWithValue("kind", SeatVocabulary.FindingActorStopped);
        await using var stoppedRows = await verify.ExecuteReaderAsync(Ct);
        Assert.True(await stoppedRows.ReadAsync(Ct));
        Assert.Equal("resolved", stoppedRows.GetString(0));
        Assert.Equal("state-changed", stoppedRows.GetString(1));
        Assert.Equal(At.AddSeconds(1), stoppedRows.GetFieldValue<DateTimeOffset>(2));
        Assert.False(await stoppedRows.ReadAsync(Ct));
        Assert.Equal(0L, await ScalarAsync(db, "SELECT count(*) FROM aiakos.seat_finding WHERE tenant_id='11111111-1111-4111-8111-111111111111' AND seat_id IS NULL"));
        Assert.Equal(0L, await ScalarAsync(db, "SELECT count(*) FROM aiakos.seat_finding WHERE tenant_id='11111111-1111-4111-8111-111111111111' AND kind='node-not-connected'"));
    }

    [Fact]
    public async Task RotationPersistsNewSessionAndUpdatesConversationAndLostHistory()
    {
        await using var db = await SeedAsync();
        await using (var seed = db.CreateCommand("""
            UPDATE aiakos.seat_session SET conversation_at=@conversation,lost_at=@lost,created_at=@created
            WHERE tenant_id=@tenant AND session_id=@session
            """))
        {
            seed.Parameters.AddWithValue("conversation", At.AddMinutes(-2));
            seed.Parameters.AddWithValue("lost", At.AddMinutes(-1));
            seed.Parameters.AddWithValue("created", At.AddMinutes(-3));
            seed.Parameters.AddWithValue("tenant", Tenant);
            seed.Parameters.AddWithValue("session", Session);
            await seed.ExecuteNonQueryAsync(Ct);
        }
        var before = Snapshot();
        var writer = new PostgresSeatActorWriter(db);
        const string newNativeId = "native-rotated";
        var rotation = RotationInput(before, 1, newNativeId, before.State.NativeSessionId!, At.AddSeconds(1));

        var receipt = await writer.CommitAsync(before, [rotation], Ct);

        Assert.Equal(8, receipt.Version);
        Assert.NotNull(receipt.CurrentSessionId);
        Assert.NotEqual(Session, receipt.CurrentSessionId);
        await using (var command = db.CreateCommand("""
            SELECT session_id,seat_id,harness,native_session_id,decision,previous_session_id,conversation_at,lost_at,created_at
            FROM aiakos.seat_session WHERE tenant_id=@tenant AND native_session_id=@native
            """))
        {
            command.Parameters.AddWithValue("tenant", Tenant);
            command.Parameters.AddWithValue("native", newNativeId);
            await using var rows = await command.ExecuteReaderAsync(Ct);
            Assert.True(await rows.ReadAsync(Ct));
            Assert.Equal(receipt.CurrentSessionId, rows.GetGuid(0));
            Assert.Equal(Seat, rows.GetGuid(1));
            Assert.Equal("test", rows.GetString(2));
            Assert.Equal(newNativeId, rows.GetString(3));
            Assert.Equal("harness-cleared", rows.GetString(4));
            Assert.Equal(Session, rows.GetGuid(5));
            Assert.True(rows.IsDBNull(6));
            Assert.True(rows.IsDBNull(7));
            Assert.Equal(At.AddSeconds(1), rows.GetFieldValue<DateTimeOffset>(8));
            Assert.False(await rows.ReadAsync(Ct));
        }
        Assert.Equal(receipt.CurrentSessionId,
            Guid.Parse((string)(await ScalarAsync(db, "SELECT current_session_id::text FROM aiakos.seat_state"))!));
        Assert.Equal(Launch,
            Guid.Parse((string)(await ScalarAsync(db, "SELECT current_launch_id::text FROM aiakos.seat_state"))!));
        Assert.Equal(Session,
            Guid.Parse((string)(await ScalarAsync(db, "SELECT session_id::text FROM aiakos.seat_launch WHERE tenant_id='11111111-1111-4111-8111-111111111111'"))!));
        await using (var oldHistory = db.CreateCommand("SELECT conversation_at,lost_at FROM aiakos.seat_session WHERE tenant_id=@tenant AND session_id=@session"))
        {
            oldHistory.Parameters.AddWithValue("tenant", Tenant);
            oldHistory.Parameters.AddWithValue("session", Session);
            await using var oldRows = await oldHistory.ExecuteReaderAsync(Ct);
            Assert.True(await oldRows.ReadAsync(Ct));
            Assert.Equal(At.AddMinutes(-2), oldRows.GetFieldValue<DateTimeOffset>(0));
            Assert.Equal(At.AddMinutes(-1), oldRows.GetFieldValue<DateTimeOffset>(1));
        }

        var current = before with
        {
            Version = receipt.Version,
            CurrentSessionId = receipt.CurrentSessionId,
            State = rotation.Step.State
        };
        var replay = rotation with { Step = rotation.Step with { Disposition = EventDisposition.Duplicate } };
        Assert.Equal(receipt, await writer.CommitAsync(current, [replay], Ct));
        Assert.Equal(2L, await ScalarAsync(db, "SELECT count(*) FROM aiakos.seat_session WHERE tenant_id='11111111-1111-4111-8111-111111111111'"));

        var reusedId = Guid.Parse("88888888-8888-4888-8888-888888888888");
        await using (var seedReuse = db.CreateCommand("""
            INSERT INTO aiakos.seat_session
                (tenant_id,session_id,seat_id,harness,native_session_id,decision,previous_session_id,conversation_at,lost_at,created_at)
            VALUES (@tenant,@id,@seat,'test','native-reused','fresh-explicit',@previous,NULL,NULL,@created)
            """))
        {
            seedReuse.Parameters.AddWithValue("tenant", Tenant);
            seedReuse.Parameters.AddWithValue("id", reusedId);
            seedReuse.Parameters.AddWithValue("seat", Seat);
            seedReuse.Parameters.AddWithValue("previous", Session);
            seedReuse.Parameters.AddWithValue("created", At.AddMinutes(-5));
            await seedReuse.ExecuteNonQueryAsync(Ct);
        }
        var reuse = RotationInput(current, 2, "native-reused", newNativeId, At.AddSeconds(2));
        var reuseReceipt = await writer.CommitAsync(current, [reuse], Ct);
        Assert.Equal(new SeatStoreReceipt(9, reusedId), reuseReceipt);
        await using (var reused = db.CreateCommand("SELECT decision,previous_session_id,conversation_at,lost_at,created_at FROM aiakos.seat_session WHERE tenant_id=@tenant AND session_id=@session"))
        {
            reused.Parameters.AddWithValue("tenant", Tenant);
            reused.Parameters.AddWithValue("session", reusedId);
            await using var reusedRows = await reused.ExecuteReaderAsync(Ct);
            Assert.True(await reusedRows.ReadAsync(Ct));
            Assert.Equal("fresh-explicit", reusedRows.GetString(0));
            Assert.Equal(Session, reusedRows.GetGuid(1));
            Assert.True(reusedRows.IsDBNull(2));
            Assert.True(reusedRows.IsDBNull(3));
            Assert.Equal(At.AddMinutes(-5), reusedRows.GetFieldValue<DateTimeOffset>(4));
        }

        var reusedCurrent = current with { Version = reuseReceipt.Version, CurrentSessionId = reusedId, State = reuse.Step.State };
        var resumable = InputState(reusedCurrent.State with { Resumability = ResumabilityValue.Resumable,
            ResumabilityReason = null }, At.AddSeconds(3));
        var lost = InputState(reusedCurrent.State with { Resumability = ResumabilityValue.Lost,
            ResumabilityReason = "resume-lost" }, At.AddSeconds(4));
        var historyReceipt = await writer.CommitAsync(reusedCurrent, [resumable, lost], Ct);
        Assert.Equal(new SeatStoreReceipt(10, reusedId), historyReceipt);
        await using var history = db.CreateCommand("SELECT conversation_at,lost_at FROM aiakos.seat_session WHERE tenant_id=@tenant AND session_id=@session");
        history.Parameters.AddWithValue("tenant", Tenant);
        history.Parameters.AddWithValue("session", reusedId);
        await using var historyRows = await history.ExecuteReaderAsync(Ct);
        Assert.True(await historyRows.ReadAsync(Ct));
        Assert.Equal(At.AddSeconds(3), historyRows.GetFieldValue<DateTimeOffset>(0));
        Assert.Equal(At.AddSeconds(4), historyRows.GetFieldValue<DateTimeOffset>(1));
    }

    [Fact]
    public async Task ForeignOwnerRotationRollsBackEarlierEvidenceAndFindings()
    {
        await using var db = await SeedAsync();
        var foreignSeat = Guid.Parse("99999999-9999-4999-8999-999999999999");
        await using (var seed = db.CreateCommand("""
            INSERT INTO aiakos.seat(tenant_id,seat_id,rig_id,member,address,kind,harness,node_name,spec_hash,binding_hash)
            VALUES (@tenant,@seat,@rig,'foreign','foreign@writer-rig','agent','test','node','s','b');
            INSERT INTO aiakos.seat_session(tenant_id,session_id,seat_id,harness,native_session_id,decision)
            VALUES (@tenant,@session,@seat,'test','native-foreign','new-session')
            """))
        {
            seed.Parameters.AddWithValue("tenant", Tenant);
            seed.Parameters.AddWithValue("seat", foreignSeat);
            seed.Parameters.AddWithValue("rig", Guid.Parse("66666666-6666-4666-8666-666666666666"));
            seed.Parameters.AddWithValue("session", Guid.Parse("aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"));
            await seed.ExecuteNonQueryAsync(Ct);
        }
        var before = Snapshot();
        var first = Applied(1, before.State with { NextSeq = 2, Resumability = ResumabilityValue.Lost,
            ResumabilityReason = "resume-lost" }, new SeatEvent { Seq = 1 });
        first = first with { Step = first.Step with { Findings = [new(SeatVocabulary.FindingObservationGap, true)] } };
        var rotation = RotationInput(before, 2, "native-foreign", before.State.NativeSessionId!, At.AddSeconds(2));
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PostgresSeatActorWriter(db).CommitAsync(before, [first, rotation], Ct));
        Assert.Equal("SEAT_SESSION_CONFLICT", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Equal(0L, await ScalarAsync(db, "SELECT count(*) FROM aiakos.seat_event"));
        Assert.Equal(0L, await ScalarAsync(db, "SELECT count(*) FROM aiakos.seat_finding WHERE tenant_id='11111111-1111-4111-8111-111111111111' AND kind='observation-gap'"));
        Assert.Equal(7L, await ScalarAsync(db, "SELECT version FROM aiakos.seat_state"));
        Assert.Equal(Session.ToString(), await ScalarAsync(db, "SELECT current_session_id::text FROM aiakos.seat_state"));
        Assert.Equal(DBNull.Value, await ScalarAsync(db, "SELECT lost_at FROM aiakos.seat_session WHERE tenant_id='11111111-1111-4111-8111-111111111111' AND session_id='55555555-5555-4555-8555-555555555555'"));
    }

    [Fact]
    public async Task RawSizeBoundsAndDefaultPayloadPreserveOtherHarnessFields()
    {
        await using var db = await SeedAsync();
        var before = Snapshot();
        uint[] sizes = [0, int.MaxValue, (uint)int.MaxValue + 1, uint.MaxValue];
        var events = sizes.Select((size, i) => new SeatEvent
        {
            Seq = (ulong)i + 1, LaunchId = Launch.ToString(), Harness = new()
            {
                RawSize = size, NativeName = "statusLine", Kind = HarnessEventKind.Telemetry,
                RawContentType = "application/json", RawTruncated = true, Origin = EventOrigin.Live,
                Usage = new() { ModelId = "model" }
            }
        }).Append(new SeatEvent { Seq = 5, Harness = new() }).ToArray();
        var inputs = events.Select((value, i) => Applied(i + 1, before.State with { NextSeq = i + 2 }, value)).ToArray();
        Assert.Equal(new SeatStoreReceipt(8, Session), await new PostgresSeatActorWriter(db).CommitAsync(before, inputs, Ct));
        await using var command = db.CreateCommand("SELECT body_type, raw, raw_content_type, raw_size, raw_truncated, native_name, kind, origin, usage FROM aiakos.seat_event ORDER BY seq");
        await using var rows = await command.ExecuteReaderAsync(Ct);
        for (var i = 0; i < events.Length; i++)
        {
            Assert.True(await rows.ReadAsync(Ct));
            Assert.Equal("harness", rows.GetString(0));
            Assert.Empty(rows.GetFieldValue<byte[]>(1));
            Assert.Equal(i == 4 ? "" : "application/json", rows.GetString(2));
            if (i is 2 or 3) Assert.True(rows.IsDBNull(3));
            else Assert.Equal(i == 1 ? int.MaxValue : 0, rows.GetInt32(3));
            Assert.Equal(i != 4, rows.GetBoolean(4));
            Assert.Equal(i == 4 ? "" : "statusLine", rows.GetString(5));
            Assert.Equal(i == 4 ? "unknown" : "telemetry", rows.GetString(6));
            if (i != 4)
            {
                Assert.Equal("live", rows.GetString(7));
                using var usage = System.Text.Json.JsonDocument.Parse(rows.GetString(8));
                Assert.Equal("model", usage.RootElement.GetProperty("model_id").GetString());
            }
        }
        Assert.False(await rows.ReadAsync(Ct));
    }

    [Fact]
    public async Task NestedTimestampBoundsAreKnownOrOriginalOpaqueEvidenceForEveryPath()
    {
        await using var db = await SeedAsync();
        var before = Snapshot();
        var timestamps = new Google.Protobuf.WellKnownTypes.Timestamp?[]
        {
            null,
            new() { Seconds = -62135596800, Nanos = 0 },
            new() { Seconds = 253402300799, Nanos = 999999999 },
            new() { Seconds = 1, Nanos = -1 },
            new() { Seconds = 1, Nanos = 1000000000 },
            new() { Seconds = -62135596801 },
            new() { Seconds = 253402300800 }
        };
        var events = new List<SeatEvent>();
        foreach (var timestamp in timestamps)
        {
            events.Add(new() { CommandResult = new() { Status = CommandStatus.Completed,
                Capture = new() { Text = "capture", CapturedAt = timestamp } } });
            events.Add(new() { CommandResult = new() { Status = CommandStatus.Completed,
                Launch = new() { Evidence = new() { Text = "evidence", CapturedAt = timestamp } } } });
            events.Add(new() { Gap = new() { From = timestamp } });
            events.Add(new() { Gap = new() { To = timestamp } });
        }
        var inputs = events.Select((value, i) =>
        {
            value.Seq = (ulong)i + 1;
            value.SourceSeq = 9;
            value.LaunchId = Launch.ToString();
            value.ObservedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(At);
            var input = Applied(i + 1, before.State with { NextSeq = i + 2 }, value);
            // Malformed launch results must be classified before the later result-stage guard.
            if (i >= 12 && value.CommandResult?.Launch is not null)
                input = input with { Input = new EventReceived(Epoch, i + 1, 9, Launch,
                    new LaunchResultBody(LaunchOutcome.Unknown, "", null)) };
            return input;
        }).ToArray();
        await new PostgresSeatActorWriter(db).CommitAsync(before, inputs, Ct);
        await using var command = db.CreateCommand("SELECT body_type, body, raw, raw_content_type, raw_size, raw_truncated, kind, native_name, native_session_id, attributes, usage, origin, node_instance_id, source_seq, launch_id, observed_at, disposition FROM aiakos.seat_event ORDER BY seq");
        await using var rows = await command.ExecuteReaderAsync(Ct);
        for (var i = 0; i < events.Count; i++)
        {
            Assert.True(await rows.ReadAsync(Ct));
            if (i < 12)
            {
                Assert.Equal(i % 4 < 2 ? "command-result" : "gap", rows.GetString(0));
                if (i % 4 < 2) Assert.Equal(events[i].CommandResult, CommandResult.Parser.ParseJson(rows.GetString(1)));
                else Assert.Equal(events[i].Gap, ObservationGap.Parser.ParseJson(rows.GetString(1)));
                Assert.True(rows.IsDBNull(2));
            }
            else
            {
                Assert.Equal("unknown", rows.GetString(0));
                Assert.True(rows.IsDBNull(1));
                Assert.Equal(events[i].ToByteArray(), rows.GetFieldValue<byte[]>(2));
                Assert.Equal("application/x-protobuf", rows.GetString(3));
                Assert.Equal(events[i].CalculateSize(), rows.GetInt32(4));
                Assert.False(rows.GetBoolean(5));
                for (var column = 6; column <= 11; column++) Assert.True(rows.IsDBNull(column));
            }
            Assert.Equal(Epoch, rows.GetGuid(12));
            Assert.Equal(9L, rows.GetInt64(13));
            Assert.Equal(Launch, rows.GetGuid(14));
            Assert.Equal(At, rows.GetFieldValue<DateTimeOffset>(15));
            Assert.Equal("applied", rows.GetString(16));
        }
        Assert.False(await rows.ReadAsync(Ct));
        await rows.DisposeAsync();
        Assert.Equal(8L, await ScalarAsync(db, "SELECT version FROM aiakos.seat_state"));
        Assert.Equal(0L, await ScalarAsync(db, "SELECT count(*) FROM aiakos.seat_command"));
    }

    [Fact]
    public async Task InvalidEnvelopeTimestampRejectsSafelyBeforeAnyWritesButDuplicateSkipsValidation()
    {
        await using var db = await SeedAsync();
        var before = Snapshot();
        var writer = new PostgresSeatActorWriter(db);
        var valid = Applied(1, before.State with { NextSeq = 2 }, new SeatEvent { Seq = 1 });
        foreach (var timestamp in new Google.Protobuf.WellKnownTypes.Timestamp[]
        {
            new() { Nanos = -1 }, new() { Nanos = 1000000000 },
            new() { Seconds = -62135596801 }, new() { Seconds = 253402300800 },
            new() { Seconds = long.MaxValue }
        })
        {
            var invalid = Applied(2, before.State with { NextSeq = 3 }, new SeatEvent { Seq = 2, ObservedAt = timestamp });
            var exception = await Assert.ThrowsAsync<SeatStoreRejectedException>(() => writer.CommitAsync(before, [valid, invalid], Ct));
            Assert.Equal("SEAT_COMMIT_REJECTED", exception.Message);
            Assert.Null(exception.InnerException);
            Assert.Equal(new SeatStoreReceipt(7, Session), await writer.CommitAsync(before,
                [invalid with { Step = invalid.Step with { Disposition = EventDisposition.Duplicate } }], Ct));
        }
        Assert.Equal(7L, await ScalarAsync(db, "SELECT version FROM aiakos.seat_state"));
        Assert.Equal(0L, await ScalarAsync(db, "SELECT count(*) FROM aiakos.seat_event"));
        Assert.Equal(0L, await ScalarAsync(db, "SELECT count(*) FROM aiakos.seat_transition"));
    }

    private static SeatActorSnapshot Snapshot()
    {
        var state = new SeatState
        {
            Session = SessionValue.Present, SessionReason = null, SessionSince = At,
            Activity = ActivityValue.Idle, ActivityDetail = null, ActivityReason = null, ActivitySince = At,
            Resumability = ResumabilityValue.Resumable, ResumabilityReason = null, ResumabilitySince = At,
            Overlay = null, KnownSession = SessionValue.Present, KnownSessionReason = null,
            KnownActivity = ActivityValue.Idle, KnownActivityDetail = null, KnownActivityReason = null,
            Desired = SeatDesired.Down, Launch = new(Launch, LaunchMode.Fresh, false, false),
            NativeSessionId = Session.ToString(), PendingInputRequest = null, PreCompactionActivity = null,
            ReadinessSeen = false, NodeInstanceId = Epoch, NextSeq = 1, LastSourceSeq = 0,
            CatchUpSeq = null, LastEventAt = null
        };
        return new(new(Tenant, Seat), "agent", "test", "node", false, 7, Session, state,
            new Dictionary<Guid, SeatStoredCommand>());
    }

    private static SeatAppliedInput Applied(long seq, SeatState state, SeatEvent value)
    {
        var transitions = seq <= 2 && value.SessionObserved is not null
            ? new[] { new SeatTransition("activity", true, seq == 1 ? "idle" : "working", seq == 1 ? "working" : "idle", null, "A2") }
            : Array.Empty<SeatTransition>();
        return new(new EventReceived(Epoch, seq, checked((long)value.SourceSeq),
            string.IsNullOrEmpty(value.LaunchId) ? null : Guid.Parse(value.LaunchId), new UnknownBody()),
            new(state, null, EventDisposition.Applied, transitions, [], []), At.AddSeconds(seq), value, null);
    }

    private static SeatAppliedInput FindingInput(SeatState state, string kind, bool open, DateTimeOffset at) =>
        new(new NodeLinkLost(), new(state, null, null, [], [new(kind, open)], []), at, null, null);

    private static SeatAppliedInput InputState(SeatState state, DateTimeOffset at) =>
        new(new NodeLinkLost(), new(state, null, null, [], [], []), at, null, null);

    private static SeatAppliedInput RotationInput(SeatActorSnapshot before, long seq, string nativeId,
        string previousNativeId, DateTimeOffset at)
    {
        var value = new SeatEvent
        {
            Seq = (ulong)seq,
            LaunchId = Launch.ToString(),
            Harness = new() { Kind = HarnessEventKind.SessionStarted, NativeSessionId = nativeId }
        };
        var state = before.State with
        {
            NativeSessionId = nativeId,
            Resumability = ResumabilityValue.FreshOnly,
            ResumabilityReason = "harness-cleared",
            NextSeq = seq + 1
        };
        return new(new EventReceived(Epoch, seq, 0, Launch, new HarnessBody(HarnessEventKind.SessionStarted,
                nativeId, new Dictionary<string, string>(StringComparer.Ordinal))),
            new(state, null, EventDisposition.Late, [], [], [new AdoptRotatedSession(nativeId, previousNativeId)]),
            at, value, null);
    }

    private async Task<NpgsqlDataSource> SeedAsync()
    {
        var db = NpgsqlDataSource.Create(await postgres.CreateDatabaseAsync(Ct));
        await new DatabaseMigrator(db, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(Ct);
        await using var command = db.CreateCommand("""
            INSERT INTO aiakos.tenant(tenant_id,name,slug) VALUES (@tenant,'Writer','writer');
            INSERT INTO aiakos.rig(tenant_id,rig_id,name,spec_hash,binding_hash,tool_version,resolved)
              VALUES (@tenant,@rig,'writer-rig','s','b','test','{}');
            INSERT INTO aiakos.seat(tenant_id,seat_id,rig_id,member,address,kind,harness,node_name,spec_hash,binding_hash)
              VALUES (@tenant,@seat,@rig,'writer','writer@writer-rig','agent','test','node','s','b');
            INSERT INTO aiakos.seat_session(tenant_id,session_id,seat_id,harness,native_session_id,decision)
              VALUES (@tenant,@session,@seat,'test',@native,'new-session');
            INSERT INTO aiakos.seat_launch(tenant_id,launch_id,seat_id,session_id,mode,decision,decided_by,command_id,node_name,spec_hash,binding_hash,seat_token_hash)
              VALUES (@tenant,@launch,@seat,@session,'fresh','new-session','test',@command,'node','s','b',decode('00','hex'));
            INSERT INTO aiakos.seat_state(tenant_id,seat_id,version,session,session_since,activity,activity_since,
              resumability,resumability_since,known_session,known_activity,current_launch_id,current_session_id,node_instance_id)
              VALUES (@tenant,@seat,7,'present',@at,'idle',@at,'resumable',@at,'present','idle',@launch,@session,@epoch);
            """);
        command.Parameters.AddWithValue("tenant", Tenant);
        command.Parameters.AddWithValue("rig", Guid.Parse("66666666-6666-4666-8666-666666666666"));
        command.Parameters.AddWithValue("seat", Seat);
        command.Parameters.AddWithValue("session", Session);
        command.Parameters.AddWithValue("native", Session.ToString());
        command.Parameters.AddWithValue("launch", Launch);
        command.Parameters.AddWithValue("command", Guid.Parse("77777777-7777-4777-8777-777777777777"));
        command.Parameters.AddWithValue("epoch", Epoch);
        command.Parameters.AddWithValue("at", At);
        await command.ExecuteNonQueryAsync(Ct);
        return db;
    }

    private static async Task<object?> ScalarAsync(NpgsqlDataSource db, string sql)
    {
        await using var command = db.CreateCommand(sql);
        return await command.ExecuteScalarAsync(Ct);
    }
}
