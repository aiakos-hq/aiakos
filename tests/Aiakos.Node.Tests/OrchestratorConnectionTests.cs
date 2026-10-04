using System.Net;
using System.Threading.Channels;

using Aiakos.Contracts.Node.V1;
using Aiakos.Node.Link;

using Grpc.Core;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
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
    public async Task ConnectsWithBearerHelloAndSendsPeriodicHeartbeat()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = new ProbeNodeLinkService();
        var server = await StartNodeLinkServerAsync(service, cancellationToken);
        var port = new Uri(server.Urls.Single()).Port;
        using var logs = new CapturingLoggerProvider();
        using var loggerFactory = new LoggerFactory([logs]);
        using var metrics = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var telemetry = new NodeTelemetry(metrics.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        var source = new ProbeNodeLinkSource();
        var options = Options.Create(new NodeOptions
        {
            OrchestratorUrl = $"http://127.0.0.1:{port}", Home = "unused", NodeId = "test-node", NodeToken = "test-secret",
        });
        using var connection = new OrchestratorConnection(options, new NodeReconnectDelay(static () => 0), source,
            telemetry, loggerFactory.CreateLogger<OrchestratorConnection>());

        var states = Channel.CreateUnbounded<ConnectionState>();
        connection.StateChanged += (_, state) => states.Writer.TryWrite(state);
        await connection.StartAsync(cancellationToken);
        try
        {
            await WaitForAsync(states, ConnectionState.Connected, cancellationToken);
            Assert.True(telemetry.Connected);
            Assert.True(await service.HeartbeatSeen.Task.WaitAsync(StepTimeout, cancellationToken));
            Assert.True(Guid.TryParse((await service.Hello.Task.WaitAsync(StepTimeout, cancellationToken)).NodeInstanceId, out var instanceId));
            Assert.NotEqual(Guid.Empty, instanceId);
            Assert.Equal("Bearer test-secret", service.Authorization);
            Assert.True(source.WelcomeSeen);
        }
        finally
        {
            await connection.StopAsync(CancellationToken.None);
            await server.DisposeAsync();
        }

        Assert.DoesNotContain("test-secret", logs.Text, StringComparison.Ordinal);
        Assert.True(connection.ExecuteTask?.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task KeepsRetryingWhenTheServerIsUnavailableWithoutLoggingPeerDetails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var logs = new CapturingLoggerProvider();
        using var loggerFactory = new LoggerFactory([logs]);
        using var metrics = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var telemetry = new NodeTelemetry(metrics.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
        var options = Options.Create(new NodeOptions
        {
            OrchestratorUrl = $"http://127.0.0.1:{FreePort()}", Home = "unused", NodeId = "n", NodeToken = "test-secret",
        });
        using var connection = new OrchestratorConnection(options, new NodeReconnectDelay(static () => 0),
            new ProbeNodeLinkSource(), telemetry, loggerFactory.CreateLogger<OrchestratorConnection>());
        using var enough = new SemaphoreSlim(0);
        var unavailable = 0;
        connection.StateChanged += (_, state) =>
        {
            if (state == ConnectionState.Unavailable && Interlocked.Increment(ref unavailable) == 3)
                enough.Release();
        };

        await connection.StartAsync(cancellationToken);
        Assert.True(await enough.WaitAsync(StepTimeout, cancellationToken));
        await connection.StopAsync(CancellationToken.None);

        Assert.True(connection.ExecuteTask?.IsCompletedSuccessfully);
        Assert.False(telemetry.Connected);
        Assert.DoesNotContain("test-secret", logs.Text, StringComparison.Ordinal);
    }

    private static async Task WaitForAsync(Channel<ConnectionState> states, ConnectionState expected,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(StepTimeout);
        while (await states.Reader.ReadAsync(timeout.Token) != expected)
        {
        }
    }

    private static int FreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task<WebApplication> StartNodeLinkServerAsync(ProbeNodeLinkService service,
        CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel =>
            kestrel.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http2));
        builder.Services.AddGrpc();
        builder.Services.AddSingleton(service);
        var app = builder.Build();
        app.MapGrpcService<ProbeNodeLinkService>();
        await app.StartAsync(cancellationToken);
        return app;
    }

    private sealed class ProbeNodeLinkSource : INodeLinkSource
    {
        public bool WelcomeSeen { get; private set; }

        public Hello CreateHello(string nodeInstanceId) => new()
        {
            Protocol = new ProtocolVersion { Major = 1, Minor = 0 },
            NodeName = "test-node",
            NodeInstanceId = nodeInstanceId,
            Limits = new Limits { MaxMessageBytes = 4194304, MaxInflightCommands = 64, EventBufferCapacity = 10000 },
        };

        public Heartbeat CreateHeartbeat() => new();

        public Task WelcomeAsync(Welcome welcome, CancellationToken ct)
        {
            WelcomeSeen = true;
            return Task.CompletedTask;
        }

        public Task ReceiveAsync(ConnectResponse response, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class ProbeNodeLinkService : NodeLinkService.NodeLinkServiceBase
    {
        public TaskCompletionSource<Hello> Hello { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> HeartbeatSeen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? Authorization { get; private set; }

        public override async Task Connect(IAsyncStreamReader<ConnectRequest> requestStream,
            IServerStreamWriter<ConnectResponse> responseStream, ServerCallContext context)
        {
            Authorization = context.RequestHeaders.Single(entry => entry.Key == "authorization").Value;
            if (!await requestStream.MoveNext(context.CancellationToken))
                throw new RpcException(new Status(StatusCode.FailedPrecondition, "Missing hello."));
            Hello.TrySetResult(requestStream.Current.Hello);
            await responseStream.WriteAsync(new ConnectResponse
            {
                Welcome = new Welcome
                {
                    HeartbeatInterval = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(TimeSpan.FromMilliseconds(20)),
                },
            });
            while (await requestStream.MoveNext(context.CancellationToken))
            {
                if (requestStream.Current.BodyCase == ConnectRequest.BodyOneofCase.Heartbeat)
                {
                    HeartbeatSeen.TrySetResult(true);
                    return;
                }
            }
        }
    }
}
