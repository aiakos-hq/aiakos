using Aiakos.Testing;

namespace Aiakos.Orchestrator.Tests.Infrastructure;

/// <summary>A fresh, empty database per test class.</summary>
public sealed class DatabaseFixture(PostgresContainerFixture postgres) : IAsyncLifetime
{
    public PostgresContainerFixture Postgres { get; } = postgres;

    public string ConnectionString { get; private set; } = "";

    public async ValueTask InitializeAsync()
    {
        ConnectionString = await Postgres.CreateDatabaseAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
