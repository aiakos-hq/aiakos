using System.Diagnostics;
using System.Net;

using Aiakos.Contracts.Node.V1;
using Aiakos.Orchestrator;
using Aiakos.Orchestrator.Link;

using Akka.Actor;

using Grpc.Core;
using Grpc.Net.Client;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aiakos.Orchestrator.Tests.Link;

public sealed class NodeLinkHandshakeTests
{
    [Fact]
    public void ExposesTheAuthenticatedNodeLinkService()
    {
        var service = typeof(NodeTokenRegistry).Assembly.GetType(
            "Aiakos.Orchestrator.Link.NodeLinkService", throwOnError: false);

        Assert.NotNull(service);
        Assert.True(typeof(Aiakos.Contracts.Node.V1.NodeLinkService.NodeLinkServiceBase).IsAssignableFrom(service));
    }

    [Fact]
    public async Task WelcomesAnAuthenticatedNodeWithNegotiatedIdentityLimitsAndReplay()
    {
        var app = new RecordingApplication();
        using var traceListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Aiakos.Orchestrator.Link",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(traceListener);
        using var actorSystem = ActorSystem.Create("node-link-test");
        await using var server = await StartServerAsync(app, actorSystem);
        var address = ServerAddress(server);
        using var channel = GrpcChannel.ForAddress(address);
        var client = new Aiakos.Contracts.Node.V1.NodeLinkService.NodeLinkServiceClient(channel);
        var call = client.Connect(new Metadata { { "authorization", "Bearer node-secret" } },
            cancellationToken: TestContext.Current.CancellationToken);
        await call.RequestStream.WriteAsync(new ConnectRequest { Hello = Hello("1.3") }, TestContext.Current.CancellationToken);

        Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        var welcome = Assert.IsType<Welcome>(call.ResponseStream.Current.Welcome);
        Assert.Equal("opaque-node", welcome.NodeId);
        Assert.True(Guid.TryParse(welcome.ConnectionId, out var connectionId));
        Assert.NotEqual(Guid.Empty, connectionId);
        Assert.Equal(new ProtocolVersion { Major = 1, Minor = 0 }, welcome.Protocol);
        Assert.Equal(TimeSpan.FromSeconds(5), welcome.HeartbeatInterval.ToTimeSpan());
        Assert.Equal(TimeSpan.FromSeconds(15), welcome.LivenessTimeout.ToTimeSpan());
        Assert.Equal((uint)4194304, welcome.Limits.MaxMessageBytes);
        Assert.Equal((uint)64, welcome.Limits.MaxInflightCommands);
        Assert.Equal((uint)10000, welcome.Limits.EventBufferCapacity);
        Assert.Single(welcome.Replay, replay => replay.SeatId == "seat-a" && replay.NextSeq == 4);
        Assert.NotEqual(default, welcome.ServerTime);

        await call.RequestStream.WriteAsync(new ConnectRequest { Heartbeat = new Heartbeat() }, TestContext.Current.CancellationToken);
        Assert.False(app.RequestSeen.Task.IsCompleted);
        var eventRequest = new ConnectRequest
        {
            Trace = new TraceContext
            {
                Traceparent = "00-33333333333333333333333333333333-3333333333333333-01",
                Tracestate = "tenant=test",
            },
            SeatEvent = new SeatEvent(),
        };
        await call.RequestStream.WriteAsync(eventRequest, TestContext.Current.CancellationToken);
        var received = await app.RequestSeen.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(ConnectRequest.BodyOneofCase.SeatEvent, received.BodyCase);
        Assert.Equal("node-link.receive", app.ReceiveActivityName);
        Assert.Equal("33333333333333333333333333333333", app.ReceiveTraceId);
        Assert.Equal("3333333333333333", app.ReceiveParentSpanId);
        await call.RequestStream.WriteAsync(new ConnectRequest { Goodbye = new Goodbye() }, TestContext.Current.CancellationToken);
        await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken);
        Assert.Equal([NodeLinkState.Connected, NodeLinkState.Disconnected], app.States);
        Assert.Equal("friendly-node", Assert.Single(app.Identities).NodeName);
        call.Dispose();
    }

    [Fact]
    public async Task CanonicalizesAlternateGuidSpellingsBeforeReplayAndForwarding()
    {
        const string canonical = "12345678-90ab-4cde-8f01-234567890abc";
        var app = new RecordingApplication();
        using var actorSystem = ActorSystem.Create("node-link-canonical-id-test");
        await using var server = await StartServerAsync(app, actorSystem);
        using var channel = GrpcChannel.ForAddress(ServerAddress(server));
        var client = new Aiakos.Contracts.Node.V1.NodeLinkService.NodeLinkServiceClient(channel);
        var call = client.Connect(new Metadata { { "authorization", "Bearer node-secret" } },
            cancellationToken: TestContext.Current.CancellationToken);
        var hello = Hello("1.0");
        hello.NodeInstanceId = "{12345678-90AB-4CDE-8F01-234567890ABC}";
        await call.RequestStream.WriteAsync(new ConnectRequest { Hello = hello }, TestContext.Current.CancellationToken);
        Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        Assert.Equal(canonical, Assert.Single(app.ReplayInstanceIds));

        await call.RequestStream.WriteAsync(new ConnectRequest { SeatEvent = new SeatEvent() }, TestContext.Current.CancellationToken);
        await app.RequestSeen.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(canonical, Assert.Single(app.ReceivedInstanceIds));
        call.Dispose();
    }

    [Theory]
    [InlineData(null, StatusCode.Unauthenticated, "Node authentication failed.")]
    [InlineData("Basic node-secret", StatusCode.Unauthenticated, "Node authentication failed.")]
    [InlineData("Bearer wrong", StatusCode.Unauthenticated, "Node authentication failed.")]
    [InlineData("Bearer node-secret", StatusCode.FailedPrecondition, "Hello must be the first node message.")]
    public async Task RejectsInvalidAuthorizationAndNonHelloFirstMessage(string? authorization,
        StatusCode code, string detail)
    {
        var app = new RecordingApplication();
        using var actorSystem = ActorSystem.Create("node-link-reject-test");
        await using var server = await StartServerAsync(app, actorSystem);
        var address = ServerAddress(server);
        using var channel = GrpcChannel.ForAddress(address);
        var client = new Aiakos.Contracts.Node.V1.NodeLinkService.NodeLinkServiceClient(channel);
        var headers = new Metadata();
        if (authorization is not null)
            headers.Add("authorization", authorization);
        var call = client.Connect(headers, cancellationToken: TestContext.Current.CancellationToken);
        if (authorization == "Bearer node-secret")
            await call.RequestStream.WriteAsync(new ConnectRequest { Heartbeat = new Heartbeat() }, TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
            await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        Assert.Equal(code, exception.StatusCode);
        Assert.Equal(detail, exception.Status.Detail);
        Assert.Empty(app.Identities);
        call.Dispose();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RejectsAnEmptyOrSilentFirstMessageWithTheHelloError(bool completeRequestStream)
    {
        var app = new RecordingApplication();
        using var actorSystem = ActorSystem.Create("node-link-empty-hello-test");
        await using var server = await StartServerAsync(app, actorSystem,
            helloTimeout: completeRequestStream ? null : TimeSpan.FromMilliseconds(100));
        using var channel = GrpcChannel.ForAddress(ServerAddress(server));
        var client = new Aiakos.Contracts.Node.V1.NodeLinkService.NodeLinkServiceClient(channel);
        var call = client.Connect(new Metadata { { "authorization", "Bearer node-secret" } },
            cancellationToken: TestContext.Current.CancellationToken);
        if (completeRequestStream)
            await call.RequestStream.CompleteAsync();

        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
            await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        Assert.Equal(StatusCode.FailedPrecondition, exception.StatusCode);
        Assert.Equal("Hello must be the first node message.", exception.Status.Detail);
        Assert.Empty(app.Identities);
        call.Dispose();
    }

    [Theory]
    [InlineData("Bearer node-secret", "name-mismatch", 1U, 0U,
        StatusCode.PermissionDenied, "Node name does not match registration.")]
    [InlineData("Bearer node-secret", "friendly-node", 2U, 0U,
        StatusCode.FailedPrecondition, "Unsupported node protocol.")]
    [InlineData("Bearer node-secret", "friendly-node", 1U, 0U,
        StatusCode.FailedPrecondition, "Invalid node instance ID.")]
    public async Task RejectsHelloNameProtocolAndInstanceIdWithFixedStatuses(string authorization,
        string name, uint major, uint minor, StatusCode code, string detail)
    {
        var app = new RecordingApplication();
        using var actorSystem = ActorSystem.Create("node-link-invalid-hello-test");
        await using var server = await StartServerAsync(app, actorSystem);
        using var channel = GrpcChannel.ForAddress(ServerAddress(server));
        var client = new Aiakos.Contracts.Node.V1.NodeLinkService.NodeLinkServiceClient(channel);
        var call = client.Connect(new Metadata { { "authorization", authorization } },
            cancellationToken: TestContext.Current.CancellationToken);
        var hello = Hello("1.0");
        hello.NodeName = name;
        hello.Protocol = new ProtocolVersion { Major = major, Minor = minor };
        if (name == "friendly-node" && major == 1U)
            hello.NodeInstanceId = Guid.Empty.ToString("D");
        await call.RequestStream.WriteAsync(new ConnectRequest { Hello = hello }, TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
            await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        Assert.Equal(code, exception.StatusCode);
        Assert.Equal(detail, exception.Status.Detail);
        Assert.Empty(app.Identities);
        call.Dispose();
    }

    [Fact]
    public async Task RejectsDuplicateAuthorizationEntriesBeforeReadingHello()
    {
        var app = new RecordingApplication();
        using var actorSystem = ActorSystem.Create("node-link-duplicate-auth-test");
        await using var server = await StartServerAsync(app, actorSystem);
        using var channel = GrpcChannel.ForAddress(ServerAddress(server));
        var client = new Aiakos.Contracts.Node.V1.NodeLinkService.NodeLinkServiceClient(channel);
        var call = client.Connect(new Metadata
        {
            { "authorization", "Bearer node-secret" },
            { "authorization", "Bearer node-secret" },
        }, cancellationToken: TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
            await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        Assert.Equal(StatusCode.Unauthenticated, exception.StatusCode);
        Assert.Equal("Node authentication failed.", exception.Status.Detail);
        Assert.Empty(app.Identities);
        call.Dispose();
    }

    private static Hello Hello(string protocol) => new()
    {
        Protocol = new ProtocolVersion
        {
            Major = uint.Parse(protocol.Split('.')[0], System.Globalization.CultureInfo.InvariantCulture),
            Minor = uint.Parse(protocol.Split('.')[1], System.Globalization.CultureInfo.InvariantCulture),
        },
        NodeName = "friendly-node",
        NodeInstanceId = "b1fc5444-96b2-42fc-a7d4-6e6e0d71d908",
    };

    private static async Task<WebApplication> StartServerAsync(RecordingApplication application, ActorSystem actorSystem,
        TimeSpan? helloTimeout = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0,
            listen => listen.Protocols = HttpProtocols.Http2));
        builder.Services.AddGrpc();
        builder.Services.AddSingleton(new NodeTokenRegistry([new NodeRegistration
        {
            Id = "opaque-node", Name = "friendly-node", Token = "node-secret",
        }]));
        builder.Services.AddSingleton<INodeLinkApplication>(application);
        // Only the silent-client test needs a short deadline; validation tests use the normal timeout.
        if (helloTimeout is { } timeout)
            builder.Services.Configure<NodeLinkOptions>(options => options.HelloTimeout = timeout);
        builder.Services.AddSingleton(actorSystem);
        builder.Services.AddSingleton<NodeLinkRegistry>();
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton<Aiakos.Orchestrator.Link.NodeLinkService>();
        var app = builder.Build();
        app.MapGrpcService<Aiakos.Orchestrator.Link.NodeLinkService>();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private static string ServerAddress(WebApplication app)
    {
        var addresses = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!;
        return addresses.Addresses.Single();
    }

    private sealed class RecordingApplication : INodeLinkApplication
    {
        public List<NodeIdentity> Identities { get; } = [];
        public List<NodeLinkState> States { get; } = [];
        public List<string> ReplayInstanceIds { get; } = [];
        public List<string> ReceivedInstanceIds { get; } = [];
        public string? ReceiveActivityName { get; private set; }
        public string? ReceiveTraceId { get; private set; }
        public string? ReceiveParentSpanId { get; private set; }
        public TaskCompletionSource<ConnectRequest> RequestSeen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<ReplayFrom>> GetReplayAsync(NodeIdentity identity, Hello hello, CancellationToken ct)
        {
            Identities.Add(identity);
            ReplayInstanceIds.Add(hello.NodeInstanceId);
            return Task.FromResult<IReadOnlyList<ReplayFrom>>([new ReplayFrom { SeatId = "seat-a", NextSeq = 4 }]);
        }

        public Task ReceiveAsync(NodeIdentity identity, string nodeInstanceId, ConnectRequest request, CancellationToken ct)
        {
            ReceivedInstanceIds.Add(nodeInstanceId);
            ReceiveActivityName = Activity.Current?.OperationName;
            ReceiveTraceId = Activity.Current?.TraceId.ToString();
            ReceiveParentSpanId = Activity.Current?.ParentSpanId.ToString();
            RequestSeen.TrySetResult(request);
            return Task.CompletedTask;
        }

        public Task StateChangedAsync(NodeIdentity identity, NodeLinkState state, CancellationToken ct)
        {
            States.Add(state);
            return Task.CompletedTask;
        }
    }
}
