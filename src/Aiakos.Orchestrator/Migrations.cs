using System.Diagnostics;

using Aiakos.Data;

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Aiakos.Orchestrator;

/// <summary>Whether the startup migration has finished.</summary>
internal sealed class MigrationState
{
    private volatile bool completed;

    public bool Completed => completed;

    public void MarkCompleted() => completed = true;
}

/// <summary>
/// Applies pending migrations before anything else starts (spec 0001 R28). It is the first hosted
/// service, and hosted services start in order and before Kestrel accepts requests, so the
/// orchestrator never serves on an unmigrated database. A failure throws, which stops the host.
/// </summary>
internal sealed partial class MigrationHostedService(
    DatabaseMigrator migrator,
    MigrationState state,
    ILogger<MigrationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var activity = OrchestratorTelemetry.ActivitySource.StartActivity("db.migrate");
        try
        {
            var result = await migrator.MigrateAsync(cancellationToken);
            OrchestratorTelemetry.MigrationsApplied.Add(result.AppliedScripts.Count);
            state.MarkCompleted();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The migrator already logged the failed script and its error.
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            LogStartupAborted(logger);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Critical, Message = "Startup aborted: the database could not be migrated")]
    private static partial void LogStartupAborted(ILogger logger);
}

/// <summary>Unhealthy until the startup migration has finished (spec 0001 R29).</summary>
internal sealed class MigrationsHealthCheck(MigrationState state) : IHealthCheck
{
    /// <summary>Name of the check.</summary>
    public const string Name = "migrations";

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(state.Completed
            ? HealthCheckResult.Healthy("Migrations applied.")
            : HealthCheckResult.Unhealthy("Migrations not applied yet."));
}
