using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

namespace Aiakos.Data.Tests.Infrastructure;

internal static class Migrations
{
    /// <summary>Journal name of the first migration.</summary>
    public const string TenantScript = "0001_tenant.sql";

    /// <summary>Names of every embedded migration, in order.</summary>
    public static IReadOnlyList<string> Embedded => DatabaseMigrator.EmbeddedScriptNames;

    public static DatabaseMigrator CreateMigrator(NpgsqlDataSource dataSource, DatabaseMigratorOptions? options = null) =>
        new(dataSource, NullLogger<DatabaseMigrator>.Instance, options);

    public static async Task<List<string>> JournalAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT scriptname FROM aiakos_meta.schema_versions ORDER BY schemaversionsid");
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var names = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
