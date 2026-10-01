using Npgsql;

using Testcontainers.PostgreSql;

namespace Aiakos.Testing;

/// <summary>
/// One Postgres container per test assembly (spec 0001, Design → Tests). Test classes get a
/// fresh database each through <see cref="CreateDatabaseAsync"/>. Shared by the data and
/// orchestrator test projects (linked source file).
/// </summary>
public sealed class PostgresContainerFixture : IAsyncLifetime
{
    /// <summary>Postgres image, pinned to the major version the AppHost uses (spec 0001 R12).</summary>
    public const string Image = "postgres:18";

    private readonly PostgreSqlContainer container = new PostgreSqlBuilder(Image).Build();

    public async ValueTask InitializeAsync()
    {
        await container.StartAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await container.DisposeAsync();
    }

    /// <summary>Creates an empty database with a unique name and returns its connection string.</summary>
    public async Task<string> CreateDatabaseAsync(CancellationToken cancellationToken = default)
    {
        var name = $"test_{Guid.NewGuid():N}";
        await using (var dataSource = NpgsqlDataSource.Create(container.GetConnectionString()))
        await using (var command = dataSource.CreateCommand($"CREATE DATABASE {name}"))
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return new NpgsqlConnectionStringBuilder(container.GetConnectionString()) { Database = name }.ConnectionString;
    }
}
