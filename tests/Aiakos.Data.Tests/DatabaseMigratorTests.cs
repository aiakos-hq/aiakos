using Aiakos.Core;
using Aiakos.Data.Tests.Infrastructure;

using Npgsql;

namespace Aiakos.Data.Tests;

public sealed class DatabaseMigratorTests(DatabaseFixture db) : IClassFixture<DatabaseFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MigratingEmptyDatabaseCreatesDefaultTenantAndJournalAndSecondRunIsNoOp()
    {
        var first = await Migrations.CreateMigrator(db.DataSource).MigrateAsync(Ct);

        Assert.Equal(Migrations.Embedded, first.AppliedScripts);
        Assert.Contains(Migrations.TenantScript, first.AppliedScripts);

        await using (var command = db.DataSource.CreateCommand("SELECT tenant_id, slug, name FROM aiakos.tenant"))
        await using (var reader = await command.ExecuteReaderAsync(Ct))
        {
            Assert.True(await reader.ReadAsync(Ct));
            Assert.Equal(TenantIds.Default, reader.GetGuid(0));
            Assert.Equal(TenantIds.DefaultSlug, reader.GetString(1));
            Assert.Equal("Default tenant", reader.GetString(2));
            Assert.False(await reader.ReadAsync(Ct));
        }

        Assert.Equal(Migrations.Embedded, await Migrations.JournalAsync(db.DataSource, Ct));

        var second = await Migrations.CreateMigrator(db.DataSource).MigrateAsync(Ct);

        Assert.Empty(second.AppliedScripts);
        Assert.Equal(Migrations.Embedded, await Migrations.JournalAsync(db.DataSource, Ct));
    }

    [Fact]
    public async Task ConcurrentMigratorsAllSucceedAndApplyEachScriptOnce()
    {
        var connectionString = await db.Postgres.CreateDatabaseAsync(Ct);
        var dataSources = Enumerable.Range(0, 4).Select(_ => NpgsqlDataSource.Create(connectionString)).ToList();
        try
        {
            var results = await Task.WhenAll(dataSources.Select(ds => Task.Run(() => Migrations.CreateMigrator(ds).MigrateAsync(Ct), Ct)));

            // Exactly one migrator applied the scripts; the others waited on the lock and found nothing to do.
            Assert.Single(results, static r => r.AppliedScripts.Count > 0);
            Assert.Equal(Migrations.Embedded, results.SelectMany(static r => r.AppliedScripts));
            Assert.Equal(Migrations.Embedded, await Migrations.JournalAsync(dataSources[0], Ct));
        }
        finally
        {
            foreach (var ds in dataSources)
            {
                await ds.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task FailingScriptIsRolledBackAndReportedByName()
    {
        const string broken = "Aiakos.Data.Migrations.9999_broken.sql";
        var connectionString = await db.Postgres.CreateDatabaseAsync(Ct);
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        var options = new DatabaseMigratorOptions();
        options.AdditionalScripts.Add(new MigrationScript(
            broken,
            "CREATE TABLE aiakos.half_done (tenant_id uuid NOT NULL);\nSELEC 1;"));

        var error = await Assert.ThrowsAsync<DatabaseMigrationException>(
            () => Migrations.CreateMigrator(dataSource, options).MigrateAsync(Ct));

        Assert.Equal(broken, error.ScriptName);
        Assert.Contains(broken, error.Message, StringComparison.Ordinal);
        Assert.IsType<PostgresException>(error.InnerException);

        // Earlier scripts stay applied; the failed script left nothing behind.
        Assert.Equal(Migrations.Embedded, await Migrations.JournalAsync(dataSource, Ct));
        await using var command = dataSource.CreateCommand("SELECT to_regclass('aiakos.half_done') IS NULL");
        Assert.True((bool)(await command.ExecuteScalarAsync(Ct))!);
    }
}
