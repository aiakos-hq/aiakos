using DbUp;
using DbUp.Engine;
using DbUp.Postgresql;

using Microsoft.Extensions.Logging;

using Npgsql;

namespace Aiakos.Data;

/// <summary>
/// Applies the embedded migrations (<c>Migrations/NNNN_snake_case.sql</c>) with DbUp
/// (spec 0001 R30, R31). Each script runs in its own transaction and is journaled in
/// <c>aiakos_meta.schema_versions</c>. The whole upgrade holds a Postgres advisory lock, so
/// concurrent migrators serialize and each script is applied once. The database must already
/// exist; the migrator never creates it.
/// </summary>
public sealed partial class DatabaseMigrator(
    NpgsqlDataSource dataSource,
    ILogger<DatabaseMigrator> logger,
    DatabaseMigratorOptions? options = null)
{
    /// <summary>Schema of the migration journal, the only table exempt from rule 6 (spec 0001 D4).</summary>
    public const string JournalSchema = "aiakos_meta";

    /// <summary>Table of the migration journal.</summary>
    public const string JournalTable = "schema_versions";

    /// <summary>Key of the advisory lock held during an upgrade: the ASCII bytes of "aiakos".</summary>
    public const long AdvisoryLockKey = 0x61_69_61_6B_6F_73;

    private const string EmbeddedPrefix = "Aiakos.Data.Migrations.";

    private readonly DatabaseMigratorOptions options = options ?? new DatabaseMigratorOptions();

    /// <summary>
    /// Applies pending migrations. Throws <see cref="DatabaseMigrationException"/> naming the
    /// failed script; scripts before it stay applied, the failed one is rolled back.
    /// </summary>
    public async Task<MigrationResult> MigrateAsync(CancellationToken cancellationToken)
    {
        // The lock is held on its own connection for the whole upgrade; DbUp opens other
        // connections from the data source. The server releases a session lock if this process dies.
        var lockConnection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (lockConnection.ConfigureAwait(false))
        {
            LogWaitingForLock(logger, AdvisoryLockKey);
            await ExecuteAsync(lockConnection, $"SELECT pg_advisory_lock({AdvisoryLockKey})", cancellationToken).ConfigureAwait(false);
            try
            {
                // DbUp creates the journal table but not its schema.
                await ExecuteAsync(lockConnection, $"CREATE SCHEMA IF NOT EXISTS {JournalSchema}", cancellationToken).ConfigureAwait(false);

                var result = await Task.Run(Upgrade, cancellationToken).ConfigureAwait(false);
                return ToMigrationResult(result);
            }
            finally
            {
                // Unlock explicitly so the pooled connection does not keep holding the lock.
                await ExecuteAsync(lockConnection, $"SELECT pg_advisory_unlock({AdvisoryLockKey})", CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    private DatabaseUpgradeResult Upgrade()
    {
        var builder = DeployChanges.To
            .PostgresqlDatabase(new PostgresqlConnectionManager(dataSource))
            .WithScriptsEmbeddedInAssembly(
                typeof(DatabaseMigrator).Assembly,
                static name => name.StartsWith(EmbeddedPrefix, StringComparison.Ordinal)
                    && name.EndsWith(".sql", StringComparison.Ordinal))
            .JournalToPostgresqlTable(JournalSchema, JournalTable)
            .WithTransactionPerScript()
            // DbUp's $name$ variables would clash with Postgres dollar quoting ($body$ ... $body$).
            .WithVariablesDisabled()
            .LogTo(logger);

        if (options.AdditionalScripts.Count > 0)
        {
            builder = builder.WithScripts(options.AdditionalScripts.Select(static s => new SqlScript(s.Name, s.Sql)));
        }

        return builder.Build().PerformUpgrade();
    }

    private MigrationResult ToMigrationResult(DatabaseUpgradeResult result)
    {
        if (!result.Successful)
        {
            var scriptName = result.ErrorScript?.Name;
            LogMigrationFailed(logger, result.Error, scriptName ?? "(none)");
            throw new DatabaseMigrationException(
                $"Database migration failed in script '{scriptName ?? "(none)"}': {result.Error?.Message}",
                result.Error ?? new InvalidOperationException("DbUp reported a failure without an error."))
            {
                ScriptName = scriptName,
            };
        }

        var applied = result.Scripts.Select(static s => s.Name).ToList();
        if (applied.Count == 0)
        {
            LogUpToDate(logger);
        }
        else
        {
            LogApplied(logger, applied.Count, applied);
        }

        return new MigrationResult(applied);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        var command = new NpgsqlCommand(sql, connection);
        await using (command.ConfigureAwait(false))
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Waiting for migration advisory lock {LockKey}")]
    private static partial void LogWaitingForLock(ILogger logger, long lockKey);

    [LoggerMessage(Level = LogLevel.Information, Message = "Database is up to date; no migration scripts to apply")]
    private static partial void LogUpToDate(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Applied {Count} migration script(s): {Scripts}")]
    private static partial void LogApplied(ILogger logger, int count, IReadOnlyList<string> scripts);

    [LoggerMessage(Level = LogLevel.Critical, Message = "Database migration failed in script {ScriptName}")]
    private static partial void LogMigrationFailed(ILogger logger, Exception? exception, string scriptName);
}
