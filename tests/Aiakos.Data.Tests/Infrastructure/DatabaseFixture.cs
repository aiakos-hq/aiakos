using Aiakos.Testing;

using Npgsql;

namespace Aiakos.Data.Tests.Infrastructure;

/// <summary>A fresh, empty database per test class.</summary>
public sealed class DatabaseFixture(PostgresContainerFixture postgres) : IAsyncLifetime
{
    private NpgsqlDataSource? dataSource;

    public PostgresContainerFixture Postgres { get; } = postgres;

    public string ConnectionString { get; private set; } = "";

    public NpgsqlDataSource DataSource => dataSource ?? throw new InvalidOperationException("Fixture not initialized.");

    public async ValueTask InitializeAsync()
    {
        ConnectionString = await Postgres.CreateDatabaseAsync(TestContext.Current.CancellationToken);
        dataSource = NpgsqlDataSource.Create(ConnectionString);
    }

    public async ValueTask DisposeAsync()
    {
        if (dataSource is not null)
        {
            await dataSource.DisposeAsync();
        }
    }
}
