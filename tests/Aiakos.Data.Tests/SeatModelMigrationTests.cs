using Aiakos.Core;
using Aiakos.Data.Tests.Infrastructure;

using Npgsql;

namespace Aiakos.Data.Tests;

public sealed class SeatModelMigrationTests(DatabaseFixture db) : IClassFixture<DatabaseFixture>, IAsyncLifetime
{
    private static readonly string[] EventUniqueColumns = ["tenant_id", "node_instance_id", "seat_id", "seq"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await Migrations.CreateMigrator(db.DataSource).MigrateAsync(Ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task CreatesSeatTablesAndEnforcesTheirConstraints()
    {
        string[] expectedTables = [
            "rig", "seat", "seat_session", "seat_launch", "seat_command", "seat_event",
            "seat_transition", "seat_state", "seat_finding"];
        await using (var command = db.DataSource.CreateCommand(
                         "SELECT tablename FROM pg_tables WHERE schemaname = 'aiakos' AND (tablename LIKE 'seat%' OR tablename = 'rig')"))
        await using (var reader = await command.ExecuteReaderAsync(Ct))
        {
            var actual = new HashSet<string>(StringComparer.Ordinal);
            while (await reader.ReadAsync(Ct))
            {
                actual.Add(reader.GetString(0));
            }

            Assert.Equal(expectedTables.ToHashSet(StringComparer.Ordinal), actual);
        }

        await using (var command = db.DataSource.CreateCommand("""
                         SELECT array_agg(attribute.attname ORDER BY key_column.ordinality)
                         FROM pg_constraint AS constraint_row
                         CROSS JOIN LATERAL unnest(constraint_row.conkey) WITH ORDINALITY AS key_column(attnum, ordinality)
                         JOIN pg_attribute AS attribute
                           ON attribute.attrelid = constraint_row.conrelid AND attribute.attnum = key_column.attnum
                         WHERE constraint_row.conrelid = 'aiakos.seat_event'::regclass
                           AND constraint_row.contype = 'u'
                         GROUP BY constraint_row.oid
                         HAVING array_agg(attribute.attname ORDER BY key_column.ordinality) =
                             ARRAY['tenant_id', 'node_instance_id', 'seat_id', 'seq']::name[]
                         """))
        {
            var columns = await command.ExecuteScalarAsync(Ct);
            Assert.Equal(EventUniqueColumns,
                Assert.IsType<string[]>(columns));
        }

        var tenantId = TenantIds.Default;
        var rigId = Guid.CreateVersion7();
        var seatId = Guid.CreateVersion7();
        var nodeId = Guid.CreateVersion7();
        await using (var command = db.DataSource.CreateCommand("""
                         INSERT INTO aiakos.rig (tenant_id, rig_id, name, spec_hash, binding_hash, tool_version, resolved)
                         VALUES (@tenantId, @rigId, 'demo', 'spec', 'binding', 'test', '{}'::jsonb);
                         INSERT INTO aiakos.seat (tenant_id, seat_id, rig_id, member, address, kind, harness, desired, spec_hash, binding_hash)
                         VALUES (@tenantId, @seatId, @rigId, 'impl', 'impl@demo', 'agent', 'claude', 'up', 'spec', 'binding');
                         """))
        {
            command.Parameters.AddWithValue("tenantId", tenantId);
            command.Parameters.AddWithValue("rigId", rigId);
            command.Parameters.AddWithValue("seatId", seatId);
            await command.ExecuteNonQueryAsync(Ct);
        }

        var eventId = Guid.CreateVersion7();
        await using (var command = db.DataSource.CreateCommand("""
                         INSERT INTO aiakos.seat_event (tenant_id, event_id, seat_id, node_instance_id, seq, body_type, disposition)
                         VALUES (@tenantId, @eventId, @seatId, @nodeId, 1, 'gap', 'applied')
                         """))
        {
            command.Parameters.AddWithValue("tenantId", tenantId);
            command.Parameters.AddWithValue("eventId", eventId);
            command.Parameters.AddWithValue("seatId", seatId);
            command.Parameters.AddWithValue("nodeId", nodeId);
            Assert.Equal(1, await command.ExecuteNonQueryAsync(Ct));
        }

        await using (var command = db.DataSource.CreateCommand("""
                         INSERT INTO aiakos.seat_event (tenant_id, event_id, seat_id, node_instance_id, seq, body_type, disposition)
                         VALUES (@tenantId, @eventId, @seatId, @nodeId, 1, 'gap', 'applied') ON CONFLICT DO NOTHING
                         """))
        {
            command.Parameters.AddWithValue("tenantId", tenantId);
            command.Parameters.AddWithValue("eventId", Guid.CreateVersion7());
            command.Parameters.AddWithValue("seatId", seatId);
            command.Parameters.AddWithValue("nodeId", nodeId);
            Assert.Equal(0, await command.ExecuteNonQueryAsync(Ct));
        }

        await AssertConstraintViolationAsync("""
            INSERT INTO aiakos.seat_state (tenant_id, seat_id, version, session, session_since, activity,
                activity_since, resumability, resumability_since, known_session, known_activity)
            VALUES (@tenantId, @seatId, 0, 'running', now(), 'none', now(), 'none', now(), 'running', 'none')
            """, "23514", tenantId, seatId);
        await AssertConstraintViolationAsync("""
            INSERT INTO aiakos.seat_state (tenant_id, seat_id, version, session, session_since, activity,
                activity_since, resumability, resumability_since, known_session, known_activity)
            VALUES (@tenantId, @seatId, 0, 'unknown', now(), 'none', now(), 'none', now(), 'unknown', 'none')
            """, "23514", tenantId, seatId);
        await AssertConstraintViolationAsync("""
            INSERT INTO aiakos.seat_state (tenant_id, seat_id, version, session, session_since, activity,
                activity_since, resumability, resumability_since, known_session, known_activity)
            VALUES (@tenantId, @seatId, 0, 'absent', now(), 'idle', now(), 'none', now(), 'absent', 'idle')
            """, "23514", tenantId, seatId);

        await using (var command = db.DataSource.CreateCommand("""
                         INSERT INTO aiakos.seat_finding (tenant_id, finding_id, seat_id, kind, severity, status, summary)
                         VALUES (@tenantId, @firstId, @seatId, 'turn-failed', 'error', 'open', 'first');
                         INSERT INTO aiakos.seat_finding (tenant_id, finding_id, seat_id, kind, severity, status, summary)
                         VALUES (@resolvedIdTenant, @resolvedId, @seatId, 'activity-stale', 'warning', 'resolved', 'old');
                         INSERT INTO aiakos.seat_finding (tenant_id, finding_id, seat_id, kind, severity, status, summary)
                         VALUES (@tenantId, @newId, @seatId, 'activity-stale', 'warning', 'open', 'new');
                         """))
        {
            command.Parameters.AddWithValue("tenantId", tenantId);
            command.Parameters.AddWithValue("resolvedIdTenant", tenantId);
            command.Parameters.AddWithValue("seatId", seatId);
            command.Parameters.AddWithValue("firstId", Guid.CreateVersion7());
            command.Parameters.AddWithValue("resolvedId", Guid.CreateVersion7());
            command.Parameters.AddWithValue("newId", Guid.CreateVersion7());
            await command.ExecuteNonQueryAsync(Ct);
        }

        await AssertConstraintViolationAsync("""
            INSERT INTO aiakos.seat_finding (tenant_id, finding_id, seat_id, kind, severity, status, summary)
            VALUES (@tenantId, @findingId, @seatId, 'turn-failed', 'error', 'open', 'duplicate');
            """, "23505", tenantId, seatId, ("findingId", Guid.CreateVersion7()));

        var otherTenant = Guid.CreateVersion7();
        var foreignRigId = Guid.CreateVersion7();
        await using (var command = db.DataSource.CreateCommand("""
                         INSERT INTO aiakos.tenant (tenant_id, slug, name) VALUES (@tenantId, 'other-tenant', 'Other');
                         INSERT INTO aiakos.rig (tenant_id, rig_id, name, spec_hash, binding_hash, tool_version, resolved)
                         VALUES (@tenantId, @rigId, 'foreign', 'spec', 'binding', 'test', '{}'::jsonb);
                         """))
        {
            command.Parameters.AddWithValue("tenantId", otherTenant);
            command.Parameters.AddWithValue("rigId", foreignRigId);
            await command.ExecuteNonQueryAsync(Ct);
        }

        await AssertConstraintViolationAsync("""
            INSERT INTO aiakos.seat (tenant_id, seat_id, rig_id, member, address, kind, harness, spec_hash, binding_hash)
            VALUES (@tenantId, @seatId, @rigId, 'cross', 'cross@demo', 'agent', 'claude', 'spec', 'binding');
            """, "23503", tenantId, Guid.CreateVersion7(), ("rigId", foreignRigId));
    }

    private async Task AssertConstraintViolationAsync(
        string sql,
        string sqlState,
        Guid tenantId,
        Guid seatId,
        (string Name, Guid Value)? additional = null)
    {
        await using var command = db.DataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("tenantId", tenantId);
        command.Parameters.AddWithValue("seatId", seatId);
        if (additional is { } parameter)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }

        var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(Ct));
        Assert.Equal(sqlState, error.SqlState);
    }
}
