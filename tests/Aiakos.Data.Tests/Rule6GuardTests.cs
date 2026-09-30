using Aiakos.Data.Tests.Infrastructure;

using Npgsql;

namespace Aiakos.Data.Tests;

/// <summary>Rule 6 guard (spec 0001 R33, AC15).</summary>
public sealed class Rule6GuardTests(DatabaseFixture db) : IClassFixture<DatabaseFixture>, IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await Migrations.CreateMigrator(db.DataSource).MigrateAsync(Ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task EveryTableInSchemaAiakosHasNonNullUuidTenantId()
    {
        Assert.True(await Rule6Guard.CountTablesAsync(db.DataSource, Ct) > 0);
        Assert.Empty(await Rule6Guard.FindViolationsAsync(db.DataSource, Ct));
    }

    [Fact]
    public async Task GuardReportsTablesWithoutValidTenantId()
    {
        // A scratch database, so the violations never leak into the real-schema test.
        await using var dataSource = NpgsqlDataSource.Create(await db.Postgres.CreateDatabaseAsync(Ct));
        await Migrations.CreateMigrator(dataSource).MigrateAsync(Ct);
        await using (var command = dataSource.CreateCommand("""
            CREATE TABLE aiakos.scratch_missing (id uuid PRIMARY KEY);
            CREATE TABLE aiakos.scratch_nullable (tenant_id uuid);
            CREATE TABLE aiakos.scratch_text (tenant_id text NOT NULL);
            CREATE TABLE aiakos.scratch_ok (tenant_id uuid NOT NULL);
            """))
        {
            await command.ExecuteNonQueryAsync(Ct);
        }

        var violations = await Rule6Guard.FindViolationsAsync(dataSource, Ct);

        Assert.Equal(["scratch_missing", "scratch_nullable", "scratch_text"], violations);
    }
}
