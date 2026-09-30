using System.Net.NetworkInformation;

using Aiakos.Data;
using Aiakos.Orchestrator.Tests.Infrastructure;

using Microsoft.Extensions.DependencyInjection;

namespace Aiakos.Orchestrator.Tests;

/// <summary>A failing migration stops startup; nothing is served (spec 0001 R28).</summary>
public sealed class BrokenMigrationTests(DatabaseFixture db) : IClassFixture<DatabaseFixture>
{
    private const string BrokenScript = "Aiakos.Data.Migrations.9999_broken.sql";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task StartupFailsWithTheFailedScriptName()
    {
        var factory = new OrchestratorFactory(db.ConnectionString)
        {
            ConfigureServices = services => services.AddSingleton(BrokenOptions("SELEC 1;")),
        };

        var error = Record.Exception(() => factory.CreateClient());
        await DisposeFailedFactoryAsync(factory);

        var migrationError = FindMigrationException(error);
        Assert.NotNull(migrationError);
        Assert.Equal(BrokenScript, migrationError.ScriptName);
    }

    [Fact]
    public async Task HealthIsNeverServedWhileMigratingOrAfterAFailure()
    {
        var port = OrchestratorFactory.FreePort();
        var factory = new OrchestratorFactory(db.ConnectionString)
        {
            Urls = [$"http://127.0.0.1:{port}"],

            // Slow enough that the probe runs while startup is in progress.
            ConfigureServices = services => services.AddSingleton(BrokenOptions("SELECT pg_sleep(2);\nSELEC 1;")),
        };
        factory.UseKestrel();

        using var stopProbe = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var probe = ProbeListenerAsync(port, stopProbe.Token);

        // In Kestrel mode WebApplicationFactory reports its own ObjectDisposedException instead of
        // the start failure, so this test only checks that startup failed; the error itself is
        // checked through the test server above.
        var error = Record.Exception(factory.StartServer);

        await stopProbe.CancelAsync();
        var (samples, listening) = await probe;
        await DisposeFailedFactoryAsync(factory);

        Assert.NotNull(error);
        Assert.True(samples > 10, $"The probe sampled only {samples} times.");
        Assert.Equal(0, listening);
    }

    private static DatabaseMigratorOptions BrokenOptions(string sql)
    {
        var options = new DatabaseMigratorOptions();
        options.AdditionalScripts.Add(new MigrationScript(BrokenScript, sql));
        return options;
    }

    private static async Task DisposeFailedFactoryAsync(OrchestratorFactory factory)
    {
        try
        {
            await factory.DisposeAsync();
        }
        catch (ObjectDisposedException)
        {
            // WebApplicationFactory stops a host whose start failed after its service provider is
            // disposed, and ServiceDefaults' TelemetryShutdownService resolves the telemetry
            // providers lazily in StoppedAsync. Only this test path hits it; a real process does
            // not stop a host that failed to start (RunAsync disposes it).
        }
    }

    private static DatabaseMigrationException? FindMigrationException(Exception? error) => error switch
    {
        DatabaseMigrationException migration => migration,
        AggregateException aggregate => aggregate.InnerExceptions.Select(FindMigrationException).FirstOrDefault(static e => e is not null),
        { InnerException: { } inner } => FindMigrationException(inner),
        _ => null,
    };

    /// <summary>
    /// Samples whether anything listens on the port. Polling the listener table is used instead
    /// of HTTP requests because a connect to a closed port takes about 2 s to fail on Windows.
    /// </summary>
    private static async Task<(int Samples, int Listening)> ProbeListenerAsync(int port, CancellationToken cancellationToken)
    {
        var samples = 0;
        var listening = 0;
        try
        {
            while (true)
            {
                samples++;
                if (IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(e => e.Port == port))
                {
                    listening++;
                }

                await Task.Delay(50, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return (samples, listening);
        }
    }
}
