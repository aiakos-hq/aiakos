using Npgsql;

namespace Aiakos.Data.Tests.Infrastructure;

/// <summary>
/// Rule 6 (CLAUDE.md, spec 0001 R33): every table in schema <c>aiakos</c> has a
/// <c>tenant_id uuid NOT NULL</c> column. The journal in <c>aiakos_meta</c> is exempt (D4).
/// </summary>
internal static class Rule6Guard
{
    /// <summary>Returns the tables in schema <c>aiakos</c> that violate rule 6.</summary>
    public static async Task<List<string>> FindViolationsAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT c.relname
            FROM pg_catalog.pg_class c
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'aiakos'
              AND c.relkind IN ('r', 'p')
              AND NOT EXISTS (
                  SELECT 1
                  FROM pg_catalog.pg_attribute a
                  WHERE a.attrelid = c.oid
                    AND a.attname = 'tenant_id'
                    AND a.atttypid = 'uuid'::regtype
                    AND a.attnotnull
                    AND NOT a.attisdropped)
            ORDER BY c.relname
            """;

        await using var command = dataSource.CreateCommand(sql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var tables = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    /// <summary>Returns the number of tables in schema <c>aiakos</c>, so a pass is not vacuous.</summary>
    public static async Task<long> CountTablesAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT count(*)
            FROM pg_catalog.pg_class c
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'aiakos' AND c.relkind IN ('r', 'p')
            """;

        await using var command = dataSource.CreateCommand(sql);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
