namespace Aiakos.Data;

/// <summary>
/// A migration script supplied in code rather than embedded in this assembly. Production uses
/// none; tests use it to inject scripts (for example a broken one).
/// </summary>
/// <param name="Name">Script name as recorded in the journal. Scripts run in name order together
/// with the embedded ones, which are journaled by file name (<c>NNNN_snake_case.sql</c>).</param>
/// <param name="Sql">Script text.</param>
public sealed record MigrationScript(string Name, string Sql);

/// <summary>Options for <see cref="DatabaseMigrator"/>.</summary>
public sealed class DatabaseMigratorOptions
{
    /// <summary>Scripts to run in addition to the embedded migrations. Empty in production.</summary>
    public IList<MigrationScript> AdditionalScripts { get; } = [];
}

/// <summary>Outcome of a successful migration run.</summary>
/// <param name="AppliedScripts">Names of the scripts this run applied, in order; empty when the
/// database was already up to date.</param>
public sealed record MigrationResult(IReadOnlyList<string> AppliedScripts);

/// <summary>A migration failed. Scripts before the failed one stay applied; the failed one is rolled back.</summary>
public sealed class DatabaseMigrationException : Exception
{
    public DatabaseMigrationException()
    {
    }

    public DatabaseMigrationException(string message)
        : base(message)
    {
    }

    public DatabaseMigrationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Name of the script that failed, or <see langword="null"/> when the failure was outside a
    /// script (for example while reading the journal).
    /// </summary>
    public string? ScriptName { get; init; }
}
