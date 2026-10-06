using Aiakos.Contracts.Node.V1;
using Aiakos.Data;
using Aiakos.Orchestrator.Seats;
using Aiakos.Testing;

using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

namespace Aiakos.Orchestrator.Tests.Seats;

public sealed class PostgresSeatActorReaderTests(PostgresContainerFixture postgres)
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid OtherTenant = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Seat = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid Session = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid Launch = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid Epoch = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly DateTimeOffset At = DateTimeOffset.Parse("2026-10-06T00:00:00Z",
        System.Globalization.CultureInfo.InvariantCulture);
    private const string Native = "native\ufffe\uffff😀e\u0301";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReadSnapshotHydratesAllPropertiesMetadataAndCommandsWithoutWrites()
    {
        await using var db = await SeedAsync();
        await CurrentAsync(db);
        await SqlAsync(db, "UPDATE aiakos.seat SET retired_at=@at WHERE tenant_id=@tenant AND seat_id=@seat");
        var kinds = new[] { "start", "deliver", "keys", "capture", "stop" };
        for (var i = 0; i < kinds.Length; i++)
        {
            await using var command = db.CreateCommand("""
                INSERT INTO aiakos.seat_command(tenant_id,command_id,seat_id,launch_id,kind,status,outcome,payload,requested_by)
                  VALUES (@tenant,@id,@seat,@launch,@kind,'completed',@outcome,'{}','test')
                """);
            command.Parameters.AddWithValue("tenant", Tenant);
            command.Parameters.AddWithValue("id", CommandId(i));
            command.Parameters.AddWithValue("seat", Seat);
            command.Parameters.AddWithValue("launch", Launch);
            command.Parameters.AddWithValue("kind", kinds[i]);
            command.Parameters.AddWithValue("outcome", NpgsqlTypes.NpgsqlDbType.Text, i == 1 ? "confirmed" : DBNull.Value);
            await command.ExecuteNonQueryAsync(Ct);
        }
        var reader = new PostgresSeatActorReader(db);
        var loaded = await reader.LoadAsync(new(Tenant, Seat), Ct);
        Assert.NotNull(loaded);
        Assert.Equal(new SeatKey(Tenant, Seat), loaded.Key);
        Assert.Equal("agent", loaded.Kind);
        Assert.Equal("test", loaded.Harness);
        Assert.Equal("node", loaded.Node);
        Assert.True(loaded.Retired);
        Assert.Equal(7, loaded.Version);
        Assert.Equal(Session, loaded.CurrentSessionId);
        Assert.Equal(new SeatState
        {
            Session = SessionValue.Unknown, SessionReason = "orchestrator-restarted", SessionSince = At.AddSeconds(1),
            Activity = ActivityValue.Unknown, ActivityDetail = null, ActivityReason = "orchestrator-restarted", ActivitySince = At.AddSeconds(2),
            Resumability = ResumabilityValue.Resumable, ResumabilityReason = null, ResumabilitySince = At.AddSeconds(3),
            Overlay = SeatOverlay.OrchestratorRestarted, KnownSession = SessionValue.Present, KnownSessionReason = null,
            KnownActivity = ActivityValue.Working, KnownActivityDetail = "tool:edit\ufffe", KnownActivityReason = null,
            Desired = SeatDesired.Down, Launch = new(Launch, LaunchMode.Resume, true, true), NativeSessionId = Native,
            PendingInputRequest = "request😀", PreCompactionActivity = ActivityValue.Working, ReadinessSeen = true,
            NodeInstanceId = Epoch, NextSeq = 9, LastSourceSeq = 6, CatchUpSeq = 8, LastEventAt = At.AddSeconds(4)
        }, loaded.State);
        Assert.Equal(5, loaded.Commands.Count);
        for (var i = 0; i < kinds.Length; i++)
            Assert.Equal(new SeatStoredCommand(CommandId(i), Launch, (SeatCommandKind)i, "completed", i == 1 ? "confirmed" : null),
                loaded.Commands[CommandId(i)]);
        Assert.Null(await reader.LoadAsync(new(OtherTenant, Seat), Ct));
        Assert.Null(await reader.LoadAsync(new(Tenant, CommandId(9)), Ct));
        Assert.Equal(7L, await ScalarAsync(db, "SELECT version FROM aiakos.seat_state"));
        Assert.Equal(0L, await ScalarAsync(db, "SELECT count(*) FROM aiakos.seat_event"));
        Assert.Equal(0L, await ScalarAsync(db, "SELECT count(*) FROM aiakos.seat_transition"));
    }

    [Fact]
    public async Task HumanAndNullableAgentSnapshotsKeepInitialAndOptionalValues()
    {
        await using var db = await SeedAsync();
        var human = CommandId(7);
        await AddSeatAsync(db, Tenant, human, "human", false, "down");
        var reader = new PostgresSeatActorReader(db);
        var loadedHuman = await reader.LoadAsync(new(Tenant, human), Ct);
        Assert.NotNull(loadedHuman);
        Assert.Equal("human", loadedHuman.Kind);
        Assert.Null(loadedHuman.Harness);
        Assert.Null(loadedHuman.Node);
        Assert.Equal(0, loadedHuman.Version);
        Assert.Equal(SeatState.Initial(At), loadedHuman.State);
        Assert.Null(loadedHuman.CurrentSessionId);
        Assert.Empty(loadedHuman.Commands);
        await StateAsync(db, Seat);
        var agent = await reader.LoadAsync(new(Tenant, Seat), Ct);
        Assert.NotNull(agent);
        Assert.Equal(SeatState.Initial(At), agent.State);
        Assert.Null(agent.CurrentSessionId);
        Assert.Empty(agent.Commands);
    }

    [Fact]
    public async Task ReadStartupSelectsEligibleAgentsAcrossTenantsInOrder()
    {
        await using var db = await SeedAsync();
        await CurrentAsync(db);
        var up = CommandId(1);
        var idle = CommandId(2);
        var human = CommandId(3);
        var retired = CommandId(4);
        var other = CommandId(5);
        await AddSeatAsync(db, Tenant, up, "agent", false, "up");
        await AddSeatAsync(db, Tenant, idle, "agent", false, "down");
        await AddSeatAsync(db, Tenant, human, "human", false, "up");
        await AddSeatAsync(db, Tenant, retired, "agent", true, "up");
        await AddSeatAsync(db, OtherTenant, other, "agent", false, "up");
        var keys = await new PostgresSeatActorReader(db).GetStartupSeatsAsync(Ct);
        Assert.Equal(new[] { new SeatKey(Tenant, Seat), new(Tenant, up), new(OtherTenant, other) }, keys);
    }

    [Fact]
    public async Task NativeAndSequenceLookupsUseExactTenantIdentityAndKeysOnly()
    {
        await using var db = await SeedAsync();
        await CurrentAsync(db);
        var reader = new PostgresSeatActorReader(db);
        Assert.Equal(new SeatNativeSession(Seat, Session), await reader.FindNativeSessionAsync(Tenant, "test", Native, Ct));
        Assert.Null(await reader.FindNativeSessionAsync(OtherTenant, "test", Native, Ct));
        Assert.Null(await reader.FindNativeSessionAsync(Tenant, "Test", Native, Ct));
        Assert.Null(await reader.FindNativeSessionAsync(Tenant, "test", "native\ufffe\uffff😀é", Ct));
        var oldEpoch = CommandId(8);
        await using var command = db.CreateCommand("""
            INSERT INTO aiakos.seat_event(tenant_id,event_id,seat_id,node_instance_id,seq,body_type,raw,disposition)
              VALUES (@tenant,@one,@seat,@epoch,1,'unknown',decode('00ff','hex'),'evidence'),
                     (@tenant,@two,@seat,@epoch,3,'unknown',decode('ff00','hex'),'evidence'),
                     (@tenant,@three,@seat,@old,2,'unknown',decode('ffff','hex'),'evidence')
            """);
        command.Parameters.AddWithValue("tenant", Tenant);
        command.Parameters.AddWithValue("seat", Seat);
        command.Parameters.AddWithValue("epoch", Epoch);
        command.Parameters.AddWithValue("old", oldEpoch);
        command.Parameters.AddWithValue("one", CommandId(1));
        command.Parameters.AddWithValue("two", CommandId(2));
        command.Parameters.AddWithValue("three", CommandId(3));
        await command.ExecuteNonQueryAsync(Ct);
        Assert.Equal(new long[] { 1, 3 }, (await reader.GetCommittedSequencesAsync(new(Tenant, Seat), Epoch, [1, 1, 2, 3, 4], Ct)).Order());
        Assert.Equal(new long[] { 2 }, (await reader.GetCommittedSequencesAsync(new(Tenant, Seat), oldEpoch, [1, 2, 3], Ct)).Order());
        Assert.Empty(await reader.GetCommittedSequencesAsync(new(OtherTenant, Seat), Epoch, [1, 3], Ct));
        Assert.Empty(await reader.GetCommittedSequencesAsync(new(Tenant, CommandId(9)), Epoch, [1, 3], Ct));
        Assert.Empty(await reader.GetCommittedSequencesAsync(new(Tenant, Seat), Epoch, [], Ct));
        foreach (var invalid in new[] { "bad\0id", "bad\ud800id" })
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => reader.FindNativeSessionAsync(Tenant, "test", invalid, Ct));
            Assert.Equal("INVALID_SEAT_SNAPSHOT", exception.Message);
            Assert.Null(exception.InnerException);
        }
    }

    [Fact]
    public async Task CorruptSnapshotsHaveFixedFailureIncludingUnrepresentableDatabaseTimes()
    {
        await using var db = await SeedAsync();
        var reader = new PostgresSeatActorReader(db);
        await AssertInvalidAsync();
        await CurrentAsync(db);
        await SqlAsync(db, "UPDATE aiakos.seat_state SET known_activity='invalid-value' WHERE tenant_id=@tenant AND seat_id=@seat");
        await AssertInvalidAsync();
        await SqlAsync(db, "UPDATE aiakos.seat_state SET known_activity='working',session_since='12000-01-01 UTC'::timestamptz WHERE tenant_id=@tenant AND seat_id=@seat");
        await AssertInvalidAsync();
        await SqlAsync(db, "UPDATE aiakos.seat_state SET session_since=@at WHERE tenant_id=@tenant AND seat_id=@seat");
        var foreignSeat = CommandId(7);
        await AddSeatAsync(db, Tenant, foreignSeat, "agent", false, "down");
        await SqlAsync(db, "UPDATE aiakos.seat_session SET seat_id=@foreign WHERE tenant_id=@tenant AND session_id=@session", foreignSeat);
        await AssertInvalidAsync();

        async Task AssertInvalidAsync()
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => reader.LoadAsync(new(Tenant, Seat), Ct));
            Assert.Equal("INVALID_SEAT_SNAPSHOT", exception.Message);
            Assert.Null(exception.InnerException);
        }
    }

    [Theory]
    [InlineData("fresh", "new-session", "up", LaunchMode.Fresh, false, false)]
    [InlineData("fresh", "fresh-explicit", "down", LaunchMode.Fresh, false, true)]
    [InlineData("fork", "resume-unverified", "up", LaunchMode.Fork, true, false)]
    [InlineData("resume", "no-conversation-yet", "down", LaunchMode.Resume, true, true)]
    public async Task LaunchMetadataUsesExactModeDecisionAndDesired(string mode, string decision, string desired,
        LaunchMode expectedMode, bool reused, bool stopRequested)
    {
        await using var db = await SeedAsync();
        await CurrentAsync(db);
        await using var command = db.CreateCommand("""
            UPDATE aiakos.seat_launch SET mode=@mode,decision=@decision WHERE tenant_id=@tenant AND launch_id=@launch;
            UPDATE aiakos.seat SET desired=@desired WHERE tenant_id=@tenant AND seat_id=@seat
            """);
        command.Parameters.AddWithValue("tenant", Tenant);
        command.Parameters.AddWithValue("launch", Launch);
        command.Parameters.AddWithValue("seat", Seat);
        command.Parameters.AddWithValue("mode", mode);
        command.Parameters.AddWithValue("decision", decision);
        command.Parameters.AddWithValue("desired", desired);
        await command.ExecuteNonQueryAsync(Ct);
        var snapshot = await new PostgresSeatActorReader(db).LoadAsync(new(Tenant, Seat), Ct);
        Assert.NotNull(snapshot);
        Assert.Equal(new CurrentLaunch(Launch, expectedMode, reused, stopRequested), snapshot.State.Launch);
        Assert.Equal(desired == "up" ? SeatDesired.Up : SeatDesired.Down, snapshot.State.Desired);
    }

    private static Guid CommandId(int value) => Guid.Parse($"77777777-7777-4777-8777-{value:D12}");

    private async Task<NpgsqlDataSource> SeedAsync()
    {
        var db = NpgsqlDataSource.Create(await postgres.CreateDatabaseAsync(Ct));
        await new DatabaseMigrator(db, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(Ct);
        foreach (var tenant in new[] { Tenant, OtherTenant })
        {
            await using var command = db.CreateCommand("""
                INSERT INTO aiakos.tenant(tenant_id,name,slug) VALUES (@tenant,'Reader',@slug);
                INSERT INTO aiakos.rig(tenant_id,rig_id,name,spec_hash,binding_hash,tool_version,resolved)
                  VALUES (@tenant,@tenant,'reader-rig','s','b','test','{}')
                """);
            command.Parameters.AddWithValue("tenant", tenant);
            command.Parameters.AddWithValue("slug", "reader-" + tenant.ToString("N"));
            await command.ExecuteNonQueryAsync(Ct);
        }
        await AddSeatAsync(db, Tenant, Seat, "agent", false, "down");
        return db;
    }

    private static async Task AddSeatAsync(NpgsqlDataSource db, Guid tenant, Guid seat, string kind, bool retired, string desired)
    {
        await using var command = db.CreateCommand("""
            INSERT INTO aiakos.seat(tenant_id,seat_id,rig_id,member,address,kind,harness,node_name,desired,spec_hash,binding_hash,created_at,retired_at)
              VALUES (@tenant,@seat,@tenant,@member,@member,@kind,@harness,@node,@desired,'s','b',@at,@retired)
            """);
        command.Parameters.AddWithValue("tenant", tenant);
        command.Parameters.AddWithValue("seat", seat);
        command.Parameters.AddWithValue("member", seat.ToString());
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.AddWithValue("harness", NpgsqlTypes.NpgsqlDbType.Text, kind == "agent" ? "test" : DBNull.Value);
        command.Parameters.AddWithValue("node", NpgsqlTypes.NpgsqlDbType.Text, kind == "agent" ? "node" : DBNull.Value);
        command.Parameters.AddWithValue("desired", desired);
        command.Parameters.AddWithValue("at", At);
        command.Parameters.AddWithValue("retired", NpgsqlTypes.NpgsqlDbType.TimestampTz, retired ? At : DBNull.Value);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private static Task StateAsync(NpgsqlDataSource db, Guid seat) => SqlAsync(db, """
        INSERT INTO aiakos.seat_state(tenant_id,seat_id,version,session,session_reason,session_since,activity,activity_detail,activity_reason,activity_since,
          resumability,resumability_reason,resumability_since,overlay,known_session,known_session_reason,known_activity,known_activity_detail,known_activity_reason,
          current_launch_id,current_session_id,pending_input_request,pre_compaction_activity,readiness_seen,node_instance_id,next_seq,last_source_seq,catch_up_seq,last_event_at)
          VALUES (@tenant,@seat,7,'absent',NULL,@at,'none',NULL,NULL,@at,'none',NULL,@at,NULL,'absent',NULL,'none',NULL,NULL,NULL,NULL,NULL,NULL,false,NULL,1,0,NULL,NULL)
        """, seat: seat);

    private static async Task CurrentAsync(NpgsqlDataSource db)
    {
        await SqlAsync(db, """
            INSERT INTO aiakos.seat_session(tenant_id,session_id,seat_id,harness,native_session_id,decision)
              VALUES (@tenant,@session,@seat,'test',@native,'new-session');
            INSERT INTO aiakos.seat_launch(tenant_id,launch_id,seat_id,session_id,mode,decision,decided_by,command_id,node_name,spec_hash,binding_hash,seat_token_hash)
              VALUES (@tenant,@launch,@seat,@session,'resume','resume','test',@command,'node','s','b',decode('00','hex'))
            """);
        await StateAsync(db, Seat);
        await SqlAsync(db, """
            UPDATE aiakos.seat_state SET session='unknown',session_reason='orchestrator-restarted',session_since=@at+interval '1 second',
              activity='unknown',activity_reason='orchestrator-restarted',activity_since=@at+interval '2 seconds',
              resumability='resumable',resumability_since=@at+interval '3 seconds',overlay='orchestrator-restarted',
              known_session='present',known_activity='working',known_activity_detail=@detail,current_session_id=@session,current_launch_id=@launch,
              pending_input_request='request😀',pre_compaction_activity='working',readiness_seen=true,node_instance_id=@epoch,
              next_seq=9,last_source_seq=6,catch_up_seq=8,last_event_at=@at+interval '4 seconds'
              WHERE tenant_id=@tenant AND seat_id=@seat
            """);
    }

    private static async Task SqlAsync(NpgsqlDataSource db, string sql, Guid? foreign = null, Guid? seat = null)
    {
        await using var command = db.CreateCommand(sql);
        command.Parameters.AddWithValue("tenant", Tenant);
        command.Parameters.AddWithValue("seat", seat ?? Seat);
        command.Parameters.AddWithValue("session", Session);
        command.Parameters.AddWithValue("launch", Launch);
        command.Parameters.AddWithValue("epoch", Epoch);
        command.Parameters.AddWithValue("command", CommandId(0));
        command.Parameters.AddWithValue("at", At);
        command.Parameters.AddWithValue("native", Native);
        command.Parameters.AddWithValue("detail", "tool:edit\ufffe");
        command.Parameters.AddWithValue("foreign", foreign ?? Seat);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<object?> ScalarAsync(NpgsqlDataSource db, string sql)
    {
        await using var command = db.CreateCommand(sql);
        return await command.ExecuteScalarAsync(Ct);
    }
}
