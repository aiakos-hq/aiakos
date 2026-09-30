using System.Net;
using System.Threading.Channels;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aiakos.Node.Tests;

public sealed class OrchestratorConnectionTests
{
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task ConnectsReportsUnavailableWhenTheServerStopsAndReconnectsWhenItStartsAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var server = await StartHealthServerAsync(port: 0, cancellationToken);
        var port = BoundPort(server);

        using var logs = new CapturingLoggerProvider();
        using var loggerFactory = new LoggerFactory([logs]);
        using var metrics = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var telemetry = new NodeTelemetry(metrics.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        var options = Options.Create(new NodeOptions
        {
            OrchestratorUrl = $"http://127.0.0.1:{port}",
            Home = "unused",
            NodeId = "test-node",
        });
        var backoff = new Backoff(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(200), 0.0);
        using var connection = new OrchestratorConnection(options, backoff, telemetry, loggerFactory.CreateLogger<OrchestratorConnection>());

        var states = Channel.CreateUnbounded<ConnectionState>();
        connection.StateChanged += (_, state) => states.Writer.TryWrite(state);

        await connection.StartAsync(cancellationToken);
        try
        {
            await WaitForAsync(states, ConnectionState.Connected, cancellationToken);
            Assert.True(telemetry.Connected);

            await server.StopAsync(cancellationToken);
            await server.DisposeAsync();
            await WaitForAsync(states, ConnectionState.Unavailable, cancellationToken);
            Assert.False(telemetry.Connected);

            server = await StartHealthServerAsync(port, cancellationToken);
            await WaitForAsync(states, ConnectionState.Connected, cancellationToken);
            Assert.True(telemetry.Connected);
        }
        finally
        {
            TestContext.Current.TestOutputHelper?.WriteLine(logs.Text);
            await connection.StopAsync(CancellationToken.None);
            await server.DisposeAsync();
        }

        var text = logs.Text;
        Assert.Contains($"connecting to http://127.0.0.1:{port}", text, StringComparison.Ordinal);
        Assert.Contains($"connected to http://127.0.0.1:{port}", text, StringComparison.Ordinal);
        Assert.Contains("unavailable", text, StringComparison.Ordinal);
        Assert.Contains("next retry in", text, StringComparison.Ordinal);
        Assert.True(connection.ExecuteTask?.IsCompletedSuccessfully, "The loop ends cleanly on stop.");
    }

    [Fact]
    public async Task KeepsRetryingWithGrowingDelaysWhileNothingListens()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var logs = new CapturingLoggerProvider();
        using var loggerFactory = new LoggerFactory([logs]);
        using var metrics = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var telemetry = new NodeTelemetry(metrics.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        var options = Options.Create(new NodeOptions { OrchestratorUrl = $"http://127.0.0.1:{FreePort()}", Home = "unused", NodeId = "n" });
        var backoff = new Backoff(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(80), 0.0);
        using var connection = new OrchestratorConnection(options, backoff, telemetry, loggerFactory.CreateLogger<OrchestratorConnection>());

        var unavailable = 0;
        using var enough = new SemaphoreSlim(0);
        connection.StateChanged += (_, state) =>
        {
            if (state == ConnectionState.Unavailable && Interlocked.Increment(ref unavailable) == 4)
            {
                enough.Release();
            }
        };

        await connection.StartAsync(cancellationToken);
        Assert.True(await enough.WaitAsync(StepTimeout, cancellationToken));
        await connection.StopAsync(CancellationToken.None);

        var retries = logs.Lines.Where(static l => l.Contains("next retry in", StringComparison.Ordinal)).Take(4).ToArray();
        Assert.Equal(4, retries.Length);
        Assert.True(connection.ExecuteTask?.IsCompletedSuccessfully);
        Assert.False(telemetry.Connected);
    }

    private static async Task WaitForAsync(Channel<ConnectionState> states, ConnectionState expected, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(StepTimeout);
        while (await states.Reader.ReadAsync(timeout.Token) != expected)
        {
        }
    }

    private static int BoundPort(WebApplication app) =>
        new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First()).Port;

    private static int FreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task<WebApplication> StartHealthServerAsync(int port, CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.Configure<HostOptions>(static o => o.ShutdownTimeout = TimeSpan.FromSeconds(1));
        builder.WebHost.ConfigureKestrel(kestrel =>
            kestrel.Listen(IPAddress.Loopback, port, listen => listen.Protocols = HttpProtocols.Http2));
        builder.Services.AddGrpc();
        // With no registered check the aggregate status is UNKNOWN; the orchestrator has ServiceDefaults' "self".
        builder.Services.AddGrpcHealthChecks()
            .AddCheck("self", static () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy());

        var app = builder.Build();
        app.MapGrpcHealthChecksService();

        // Rebinding the same port right after a stop can briefly fail on some platforms.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await app.StartAsync(cancellationToken);
                break;
            }
            catch (IOException) when (attempt < 20)
            {
                await Task.Delay(100, cancellationToken);
            }
        }

        return app;
    }
}
