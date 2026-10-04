using System.Net;
using System.Threading.Channels;

using Aiakos.Contracts.Node.V1;
using Aiakos.Node.Link;

using Grpc.Core;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aiakos.Node.Tests;

public sealed class OrchestratorConnectionTests
{
    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task HealthServiceAloneDoesNotConnectThenConnectStreamRecoversAfterRestart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var server = await StartServerAsync(port: 0, nodeLink: false, cancellationToken);
        var port = BoundPort(server);
        using var logs = new CapturingLoggerProvider();
        using var loggerFactory = new LoggerFactory([logs]);
        using var metrics = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var telemetry = new NodeTelemetry(metrics.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        var options = Options.Create(new NodeOptions
        {
            OrchestratorUrl = $"http://127.0.0.1:{port}", Home = "unused", NodeId = "test-node", NodeToken = "secret-token",
        });
        using var connection = new OrchestratorConnection(options, new NodeReconnectDelay(static () => 0.1), new TestLinkSource(), telemetry, loggerFactory.CreateLogger<OrchestratorConnection>());
        var states = Channel.CreateUnbounded<ConnectionState>();
        connection.StateChanged += (_, state) => states.Writer.TryWrite(state);

        await connection.StartAsync(cancellationToken);
        try
        {
            await WaitForAsync(states, ConnectionState.Unavailable, cancellationToken);
            Assert.False(telemetry.Connected);
            await server.StopAsync(cancellationToken);
            await server.DisposeAsync();

            server = await StartServerAsync(port, nodeLink: true, cancellationToken);
            await WaitForAsync(states, ConnectionState.Connected, cancellationToken);
            Assert.True(telemetry.Connected);

            await server.StopAsync(cancellationToken);
            await server.DisposeAsync();
            await WaitForAsync(states, ConnectionState.Unavailable, cancellationToken);

            server = await StartServerAsync(port, nodeLink: true, cancellationToken);
            await WaitForAsync(states, ConnectionState.Connected, cancellationToken);
            Assert.True(telemetry.Connected);
        }
        finally
        {
            await connection.StopAsync(CancellationToken.None);
            await server.DisposeAsync();
        }

        Assert.Contains("connected to http://127.0.0.1:", logs.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-token", logs.Text, StringComparison.Ordinal);
        Assert.True(connection.ExecuteTask?.IsCompletedSuccessfully, "The loop ends cleanly on stop.");
    }

    [Fact]
    public async Task KeepsRetryingWhileNothingListens()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var logs = new CapturingLoggerProvider();
        using var loggerFactory = new LoggerFactory([logs]);
        using var metrics = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var telemetry = new NodeTelemetry(metrics.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        var options = Options.Create(new NodeOptions { OrchestratorUrl = $"http://127.0.0.1:{FreePort()}", Home = "unused", NodeId = "n", NodeToken = "secret" });
        using var connection = new OrchestratorConnection(options, new NodeReconnectDelay(static () => 0.1), new TestLinkSource(), telemetry, loggerFactory.CreateLogger<OrchestratorConnection>());

        var unavailable = 0;
        using var enough = new SemaphoreSlim(0);
        connection.StateChanged += (_, state) =>
        {
            if (state == ConnectionState.Unavailable && Interlocked.Increment(ref unavailable) == 4) enough.Release();
        };

        await connection.StartAsync(cancellationToken);
        Assert.True(await enough.WaitAsync(StepTimeout, cancellationToken));
        await connection.StopAsync(CancellationToken.None);

        Assert.Equal(4, logs.Lines.Count(static l => l.Contains("next retry in", StringComparison.Ordinal)));
        Assert.True(connection.ExecuteTask?.IsCompletedSuccessfully);
        Assert.False(telemetry.Connected);
        Assert.DoesNotContain("secret", logs.Text, StringComparison.Ordinal);
    }

    private static async Task WaitForAsync(Channel<ConnectionState> states, ConnectionState expected, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(StepTimeout);
        while (await states.Reader.ReadAsync(timeout.Token) != expected) { }
    }

    private static int BoundPort(WebApplication app) =>
        new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First()).Port;

    private static int FreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task<WebApplication> StartServerAsync(int port, bool nodeLink, CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, port, listen => listen.Protocols = HttpProtocols.Http2));
        builder.Services.AddGrpc();
        builder.Services.AddGrpcHealthChecks().AddCheck("self", static () => HealthCheckResult.Healthy());
        var app = builder.Build();
        if (nodeLink) app.MapGrpcService<FakeNodeLinkService>();
        app.MapGrpcHealthChecksService();
        app.MapGet("/", () => Results.Ok());
        for (var attempt = 1; ; attempt++)
        {
            try { await app.StartAsync(cancellationToken); break; }
            catch (IOException) when (attempt < 20) { await Task.Delay(100, cancellationToken); }
        }
        return app;
    }

    public sealed class FakeNodeLinkService : NodeLinkService.NodeLinkServiceBase
    {
        public override async Task Connect(IAsyncStreamReader<ConnectRequest> requestStream, IServerStreamWriter<ConnectResponse> responseStream, ServerCallContext context)
        {
            if (!await requestStream.MoveNext(context.CancellationToken) || requestStream.Current.BodyCase != ConnectRequest.BodyOneofCase.Hello)
                throw new RpcException(new Status(StatusCode.FailedPrecondition, "Expected hello."));
            await responseStream.WriteAsync(new ConnectResponse
            {
                Welcome = new Welcome
                {
                    Protocol = new ProtocolVersion { Major = 1 }, NodeId = "node-1",
                    HeartbeatInterval = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(TimeSpan.FromSeconds(30)),
                    LivenessTimeout = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(TimeSpan.FromSeconds(45)),
                },
            });
            while (await requestStream.MoveNext(context.CancellationToken))
                if (requestStream.Current.BodyCase == ConnectRequest.BodyOneofCase.Goodbye) return;
        }
    }

    private sealed class TestLinkSource : INodeLinkSource
    {
        public Hello CreateHello(string nodeInstanceId) => new()
        {
            Protocol = new ProtocolVersion { Major = 1, Minor = 0 }, NodeName = "test-node", NodeInstanceId = nodeInstanceId,
        };
        public Heartbeat CreateHeartbeat() => new();
        public Task WelcomeAsync(Welcome welcome, CancellationToken ct) => Task.CompletedTask;
        public Task ReceiveAsync(ConnectResponse response, CancellationToken ct) => Task.CompletedTask;
    }
}
