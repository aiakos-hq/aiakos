using System.Diagnostics;
using System.Net;
using System.Collections.Concurrent;

using Aiakos.Contracts.Node.V1;
using Aiakos.Orchestrator.Link;

using Akka.Actor;

using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aiakos.Orchestrator.Tests.Link;

public sealed class NodeLinkEventAckTests
{
    private static readonly Guid Epoch = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Seat = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherSeat = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task EmitsAcknowledgementOnlyAfterEventCommitCompletes()
    {
        using var traceListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Aiakos.Orchestrator.Link",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(traceListener);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commit = new TaskCompletionSource<EventAck?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var committer = new FakeCommitter
        {
            Commit = (_, _, request, _) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    entered.TrySetResult();
                    return commit.Task;
                }

                secondEntered.TrySetResult();
                return Task.FromResult<EventAck?>(new EventAck { SeatId = request.SeatEvent.SeatId, ThroughSeq = 1 });
            }
        };
        using var actorSystem = ActorSystem.Create("node-link-event-ack-test");
        await using var server = await StartServerAsync(new EventNodeLinkApplication(committer), actorSystem);
        using var channel = GrpcChannel.ForAddress(ServerAddress(server));
        var client = new Aiakos.Contracts.Node.V1.NodeLinkService.NodeLinkServiceClient(channel);
        using var call = client.Connect(new Metadata { { "authorization", "Bearer node-secret" } },
            cancellationToken: TestContext.Current.CancellationToken);
        await call.RequestStream.WriteAsync(new ConnectRequest { Hello = Hello() }, TestContext.Current.CancellationToken);
        Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        Assert.Equal(ConnectResponse.BodyOneofCase.Welcome, call.ResponseStream.Current.BodyCase);

        var request = EventRequest();
        await call.RequestStream.WriteAsync(request, TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        await call.RequestStream.WriteAsync(EventRequest(OtherSeat), TestContext.Current.CancellationToken);
        var response = call.ResponseStream.MoveNext(TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        Assert.False(response.IsCompleted);
        Assert.Equal(1, Volatile.Read(ref calls));

        commit.SetResult(new EventAck { SeatId = Seat.ToString("D"), ThroughSeq = 1 });
        Assert.True(await response.WaitAsync(TestContext.Current.CancellationToken));
        Assert.Equal(ConnectResponse.BodyOneofCase.EventAck, call.ResponseStream.Current.BodyCase);
        Assert.Equal(Seat.ToString("D"), call.ResponseStream.Current.EventAck.SeatId);
        Assert.Equal(1UL, call.ResponseStream.Current.EventAck.ThroughSeq);
        Assert.Contains("33333333333333333333333333333333", call.ResponseStream.Current.Trace.Traceparent);

        await secondEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        Assert.Equal(ConnectResponse.BodyOneofCase.EventAck, call.ResponseStream.Current.BodyCase);
        Assert.Equal(OtherSeat.ToString("D"), call.ResponseStream.Current.EventAck.SeatId);
    }

    [Fact]
    public async Task ConvertsCommitFailureToFixedFailedPreconditionWithoutLeakingDetails()
    {
        var committer = new FakeCommitter
        {
            Commit = (_, _, _, _) => Task.FromException<EventAck?>(new InvalidOperationException("secret detail"))
        };
        using var actorSystem = ActorSystem.Create("node-link-event-failure-test");
        await using var server = await StartServerAsync(new EventNodeLinkApplication(committer), actorSystem);
        using var channel = GrpcChannel.ForAddress(ServerAddress(server));
        var client = new Aiakos.Contracts.Node.V1.NodeLinkService.NodeLinkServiceClient(channel);
        using var call = client.Connect(new Metadata { { "authorization", "Bearer node-secret" } },
            cancellationToken: TestContext.Current.CancellationToken);
        await call.RequestStream.WriteAsync(new ConnectRequest { Hello = Hello() }, TestContext.Current.CancellationToken);
        Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        await call.RequestStream.WriteAsync(EventRequest(), TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
            await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));

        Assert.Equal(StatusCode.FailedPrecondition, exception.StatusCode);
        Assert.Equal("Node event processing failed.", exception.Status.Detail);
        Assert.DoesNotContain("secret detail", exception.Status.Detail);
    }

    [Fact]
    public async Task PreservesNullAcknowledgementWithoutWritingAnAck()
    {
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var committer = new FakeCommitter
        {
            Commit = (_, _, request, _) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                    return Task.FromResult<EventAck?>(null);
                secondEntered.TrySetResult();
                return Task.FromResult<EventAck?>(new EventAck { SeatId = request.SeatEvent.SeatId, ThroughSeq = 1 });
            }
        };
        using var actorSystem = ActorSystem.Create("node-link-null-ack-test");
        await using var server = await StartServerAsync(new EventNodeLinkApplication(committer), actorSystem);
        using var channel = GrpcChannel.ForAddress(ServerAddress(server));
        var client = new Aiakos.Contracts.Node.V1.NodeLinkService.NodeLinkServiceClient(channel);
        using var call = client.Connect(new Metadata { { "authorization", "Bearer node-secret" } },
            cancellationToken: TestContext.Current.CancellationToken);
        await call.RequestStream.WriteAsync(new ConnectRequest { Hello = Hello() }, TestContext.Current.CancellationToken);
        Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        await call.RequestStream.WriteAsync(EventRequest(), TestContext.Current.CancellationToken);
        await call.RequestStream.WriteAsync(EventRequest(OtherSeat), TestContext.Current.CancellationToken);
        await secondEntered.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        Assert.Equal(ConnectResponse.BodyOneofCase.EventAck, call.ResponseStream.Current.BodyCase);
        Assert.Equal(OtherSeat.ToString("D"), call.ResponseStream.Current.EventAck.SeatId);
    }

    [Fact]
    public async Task LivenessExpiryCancelsPendingCommitAndClosesWithoutAcknowledgement()
    {
        var time = new ManualTimeProvider();
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var unknown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var states = new ConcurrentQueue<NodeLinkState>();
        var committer = new FakeCommitter
        {
            Commit = async (_, _, _, ct) =>
            {
                entered.TrySetResult(ct);
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return null;
            },
            StateChanged = (_, state, _) =>
            {
                states.Enqueue(state);
                if (state == NodeLinkState.Unknown)
                    unknown.TrySetResult();
                return Task.CompletedTask;
            }
        };
        using var actorSystem = ActorSystem.Create("node-link-event-timeout-test");
        await using var server = await StartServerAsync(new EventNodeLinkApplication(committer), actorSystem,
            time, TimeSpan.FromSeconds(5));
        using var channel = GrpcChannel.ForAddress(ServerAddress(server));
        var client = new Aiakos.Contracts.Node.V1.NodeLinkService.NodeLinkServiceClient(channel);
        using var call = client.Connect(new Metadata { { "authorization", "Bearer node-secret" } },
            cancellationToken: TestContext.Current.CancellationToken);
        await call.RequestStream.WriteAsync(new ConnectRequest { Hello = Hello() }, TestContext.Current.CancellationToken);
        Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        await call.RequestStream.WriteAsync(EventRequest(), TestContext.Current.CancellationToken);
        var callbackToken = await entered.Task.WaitAsync(TestContext.Current.CancellationToken);

        time.Advance(TimeSpan.FromSeconds(5));
        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
            await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        await unknown.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(StatusCode.Unavailable, exception.StatusCode);
        Assert.Equal("Node event commit timed out.", exception.Status.Detail);
        Assert.True(callbackToken.IsCancellationRequested);
        Assert.Equal(1, states.Count(state => state == NodeLinkState.Unknown));
    }

    [Fact]
    public async Task LivenessExpiryWinsWhenCommitCompletesAtTheSameTime()
    {
        var time = new ManualTimeProvider();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var commit = new TaskCompletionSource<EventAck?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var committer = new FakeCommitter
        {
            Commit = (_, _, _, _) =>
            {
                entered.TrySetResult();
                return commit.Task;
            }
        };
        using var actorSystem = ActorSystem.Create("node-link-event-timeout-priority-test");
        await using var server = await StartServerAsync(new EventNodeLinkApplication(committer), actorSystem,
            time, TimeSpan.FromSeconds(5));
        using var channel = GrpcChannel.ForAddress(ServerAddress(server));
        var client = new Aiakos.Contracts.Node.V1.NodeLinkService.NodeLinkServiceClient(channel);
        using var call = client.Connect(new Metadata { { "authorization", "Bearer node-secret" } },
            cancellationToken: TestContext.Current.CancellationToken);
        await call.RequestStream.WriteAsync(new ConnectRequest { Hello = Hello() }, TestContext.Current.CancellationToken);
        Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        await call.RequestStream.WriteAsync(EventRequest(), TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);

        time.Advance(TimeSpan.FromSeconds(5));
        commit.SetResult(new EventAck { SeatId = Seat.ToString("D"), ThroughSeq = 1 });
        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
            await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));

        Assert.Equal(StatusCode.Unavailable, exception.StatusCode);
        Assert.Equal("Node event commit timed out.", exception.Status.Detail);
    }

    [Fact]
    public async Task RequestCancellationCancelsPendingCommit()
    {
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var committer = new FakeCommitter
        {
            Commit = async (_, _, _, ct) =>
            {
                entered.TrySetResult(ct);
                try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
                catch (OperationCanceledException)
                {
                    callbackCanceled.TrySetResult();
                    throw;
                }
                return null;
            }
        };
        using var actorSystem = ActorSystem.Create("node-link-event-cancel-test");
        await using var server = await StartServerAsync(new EventNodeLinkApplication(committer), actorSystem);
        using var channel = GrpcChannel.ForAddress(ServerAddress(server));
        var client = new Aiakos.Contracts.Node.V1.NodeLinkService.NodeLinkServiceClient(channel);
        using var cancellation = new CancellationTokenSource();
        using var call = client.Connect(new Metadata { { "authorization", "Bearer node-secret" } },
            cancellationToken: cancellation.Token);
        await call.RequestStream.WriteAsync(new ConnectRequest { Hello = Hello() }, TestContext.Current.CancellationToken);
        Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        await call.RequestStream.WriteAsync(EventRequest(), TestContext.Current.CancellationToken);
        var callbackToken = await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        var response = call.ResponseStream.MoveNext(TestContext.Current.CancellationToken);

        cancellation.Cancel();
        var exception = await Assert.ThrowsAsync<RpcException>(async () => await response);
        await callbackCanceled.Task.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(StatusCode.Cancelled, exception.StatusCode);
        Assert.True(callbackToken.IsCancellationRequested);
    }

    [Fact]
    public async Task SupersessionCancelsPendingCommitAndKeepsGoodbyeBeforeAbortedStatus()
    {
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var committer = new FakeCommitter
        {
            Commit = async (_, _, _, ct) =>
            {
                entered.TrySetResult(ct);
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return null;
            }
        };
        using var actorSystem = ActorSystem.Create("node-link-event-supersede-test");
        await using var server = await StartServerAsync(new EventNodeLinkApplication(committer), actorSystem);
        using var channel = GrpcChannel.ForAddress(ServerAddress(server));
        var client = new Aiakos.Contracts.Node.V1.NodeLinkService.NodeLinkServiceClient(channel);
        using var oldCall = client.Connect(new Metadata { { "authorization", "Bearer node-secret" } },
            cancellationToken: TestContext.Current.CancellationToken);
        await oldCall.RequestStream.WriteAsync(new ConnectRequest { Hello = Hello() }, TestContext.Current.CancellationToken);
        Assert.True(await oldCall.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        await oldCall.RequestStream.WriteAsync(EventRequest(), TestContext.Current.CancellationToken);
        var callbackToken = await entered.Task.WaitAsync(TestContext.Current.CancellationToken);

        using var newCall = client.Connect(new Metadata { { "authorization", "Bearer node-secret" } },
            cancellationToken: TestContext.Current.CancellationToken);
        await newCall.RequestStream.WriteAsync(new ConnectRequest { Hello = Hello(Guid.NewGuid()) },
            TestContext.Current.CancellationToken);
        Assert.True(await newCall.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        Assert.Equal(ConnectResponse.BodyOneofCase.Welcome, newCall.ResponseStream.Current.BodyCase);

        Assert.True(await oldCall.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        Assert.Equal(ConnectResponse.BodyOneofCase.Goodbye, oldCall.ResponseStream.Current.BodyCase);
        Assert.Equal(GoodbyeReason.Superseded, oldCall.ResponseStream.Current.Goodbye.Reason);
        Assert.True(callbackToken.IsCancellationRequested);
        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
            await oldCall.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        Assert.Equal(StatusCode.Aborted, exception.StatusCode);
        Assert.Equal("Node link superseded.", exception.Status.Detail);
    }

    [Fact]
    public async Task ShutdownCancelsPendingCommitAndKeepsGoodbyeBeforeUnavailableStatus()
    {
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var committer = new FakeCommitter
        {
            Commit = async (_, _, _, ct) =>
            {
                entered.TrySetResult(ct);
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return null;
            }
        };
        using var actorSystem = ActorSystem.Create("node-link-event-shutdown-test");
        await using var server = await StartServerAsync(new EventNodeLinkApplication(committer), actorSystem);
        using var channel = GrpcChannel.ForAddress(ServerAddress(server));
        var client = new Aiakos.Contracts.Node.V1.NodeLinkService.NodeLinkServiceClient(channel);
        using var call = client.Connect(new Metadata { { "authorization", "Bearer node-secret" } },
            cancellationToken: TestContext.Current.CancellationToken);
        await call.RequestStream.WriteAsync(new ConnectRequest { Hello = Hello() }, TestContext.Current.CancellationToken);
        Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        await call.RequestStream.WriteAsync(EventRequest(), TestContext.Current.CancellationToken);
        var callbackToken = await entered.Task.WaitAsync(TestContext.Current.CancellationToken);

        server.Services.GetRequiredService<Microsoft.Extensions.Hosting.IHostApplicationLifetime>().StopApplication();
        Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        Assert.Equal(ConnectResponse.BodyOneofCase.Goodbye, call.ResponseStream.Current.BodyCase);
        Assert.Equal(GoodbyeReason.Shutdown, call.ResponseStream.Current.Goodbye.Reason);
        Assert.True(callbackToken.IsCancellationRequested);
        var exception = await Assert.ThrowsAsync<RpcException>(async () =>
            await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        Assert.Equal(StatusCode.Unavailable, exception.StatusCode);
        Assert.Equal("Orchestrator shutting down.", exception.Status.Detail);
    }

    [Fact]
    public async Task LivenessExpiryDoesNotEndAnOrdinaryApplicationCallback()
    {
        var time = new ManualTimeProvider();
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var unknown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var states = new ConcurrentQueue<NodeLinkState>();
        var application = new DelayedOrdinaryApplication(entered, finish, state =>
        {
            states.Enqueue(state);
            if (state == NodeLinkState.Unknown)
                unknown.TrySetResult();
        });
        using var actorSystem = ActorSystem.Create("node-link-ordinary-callback-test");
        await using var server = await StartServerAsync(application, actorSystem, time, TimeSpan.FromSeconds(5));
        using var channel = GrpcChannel.ForAddress(ServerAddress(server));
        var client = new Aiakos.Contracts.Node.V1.NodeLinkService.NodeLinkServiceClient(channel);
        using var call = client.Connect(new Metadata { { "authorization", "Bearer node-secret" } },
            cancellationToken: TestContext.Current.CancellationToken);
        await call.RequestStream.WriteAsync(new ConnectRequest { Hello = Hello() }, TestContext.Current.CancellationToken);
        Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        await call.RequestStream.WriteAsync(new ConnectRequest { CommandAck = new CommandAck() },
            TestContext.Current.CancellationToken);
        var callbackToken = await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        var pendingResponse = call.ResponseStream.MoveNext(TestContext.Current.CancellationToken);

        time.Advance(TimeSpan.FromSeconds(5));
        await unknown.Task.WaitAsync(TestContext.Current.CancellationToken);
        await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);

        Assert.False(pendingResponse.IsCompleted);
        Assert.False(callbackToken.IsCancellationRequested);
        Assert.Equal(1, states.Count(state => state == NodeLinkState.Unknown));

        finish.TrySetResult();
        await call.RequestStream.WriteAsync(new ConnectRequest { Goodbye = new Goodbye() },
            TestContext.Current.CancellationToken);
        await call.RequestStream.CompleteAsync();
        Assert.False(await pendingResponse.WaitAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BoundsConnectedStateCallbackAtLivenessExpiry()
    {
        var time = new ManualTimeProvider();
        var connectedEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var connectedCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var unknownObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var connectedCalls = 0;
        var committer = new FakeCommitter
        {
            StateChanged = (_, state, ct) =>
            {
                if (state == NodeLinkState.Connected && Interlocked.Increment(ref connectedCalls) == 1)
                {
                    connectedEntered.TrySetResult();
                    ct.Register(() => connectedCanceled.TrySetResult());
                    return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task;
                }
                if (state == NodeLinkState.Unknown)
                    unknownObserved.TrySetResult();
                return Task.CompletedTask;
            }
        };
        using var actorSystem = ActorSystem.Create("node-link-bounded-state-test");
        await using var server = await StartServerAsync(new EventNodeLinkApplication(committer), actorSystem,
            time, TimeSpan.FromSeconds(5));
        using var channel = GrpcChannel.ForAddress(ServerAddress(server));
        var client = new Aiakos.Contracts.Node.V1.NodeLinkService.NodeLinkServiceClient(channel);
        using var call = client.Connect(new Metadata { { "authorization", "Bearer node-secret" } },
            cancellationToken: TestContext.Current.CancellationToken);
        await call.RequestStream.WriteAsync(new ConnectRequest { Hello = Hello() }, TestContext.Current.CancellationToken);
        Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        await connectedEntered.Task.WaitAsync(TestContext.Current.CancellationToken);

        time.Advance(TimeSpan.FromSeconds(5));
        await connectedCanceled.Task.WaitAsync(TestContext.Current.CancellationToken);
        await unknownObserved.Task.WaitAsync(TestContext.Current.CancellationToken);

        await call.RequestStream.WriteAsync(new ConnectRequest { Goodbye = new Goodbye() },
            TestContext.Current.CancellationToken);
        await call.RequestStream.CompleteAsync();
        Assert.False(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task BoundsDisconnectedStateCallbackDuringCleanup()
    {
        var time = new ManualTimeProvider();
        var disconnectedEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var committer = new FakeCommitter
        {
            StateChanged = (_, state, _) =>
            {
                if (state == NodeLinkState.Disconnected)
                {
                    disconnectedEntered.TrySetResult();
                    return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task;
                }
                return Task.CompletedTask;
            }
        };
        using var actorSystem = ActorSystem.Create("node-link-bounded-cleanup-test");
        await using var server = await StartServerAsync(new EventNodeLinkApplication(committer), actorSystem,
            time, TimeSpan.FromSeconds(5));
        using var channel = GrpcChannel.ForAddress(ServerAddress(server));
        var client = new Aiakos.Contracts.Node.V1.NodeLinkService.NodeLinkServiceClient(channel);
        using var call = client.Connect(new Metadata { { "authorization", "Bearer node-secret" } },
            cancellationToken: TestContext.Current.CancellationToken);
        await call.RequestStream.WriteAsync(new ConnectRequest { Hello = Hello() }, TestContext.Current.CancellationToken);
        Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));
        await call.RequestStream.WriteAsync(new ConnectRequest { Goodbye = new Goodbye() },
            TestContext.Current.CancellationToken);
        await call.RequestStream.CompleteAsync();
        var response = call.ResponseStream.MoveNext(TestContext.Current.CancellationToken);
        await disconnectedEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.False(response.IsCompleted);
        time.Advance(TimeSpan.FromSeconds(5));

        Assert.False(await response.WaitAsync(TestContext.Current.CancellationToken));
    }

    private static Hello Hello(Guid? epoch = null) => new()
    {
        Protocol = new ProtocolVersion { Major = 1, Minor = 0 },
        NodeName = "friendly-node",
        NodeInstanceId = (epoch ?? Epoch).ToString("D"),
    };

    private static ConnectRequest EventRequest(Guid? seat = null) => new()
    {
        Trace = new TraceContext
        {
            Traceparent = "00-33333333333333333333333333333333-3333333333333333-01",
            Tracestate = "tenant=test",
        },
        SeatEvent = new SeatEvent
        {
            SeatId = (seat ?? Seat).ToString("D"),
            Seq = 1,
            ObservedAt = Timestamp.FromDateTime(DateTime.UnixEpoch),
        },
    };

    private static async Task<WebApplication> StartServerAsync(INodeLinkApplication application, ActorSystem actorSystem,
        TimeProvider? timeProvider = null, TimeSpan? livenessTimeout = null)
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
        builder.Services.AddSingleton(application);
        if (livenessTimeout is { } timeout)
            builder.Services.Configure<NodeLinkOptions>(options => options.LivenessTimeout = timeout);
        builder.Services.AddSingleton(actorSystem);
        builder.Services.AddSingleton<NodeLinkRegistry>();
        builder.Services.AddSingleton(timeProvider ?? TimeProvider.System);
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

    private sealed class FakeCommitter : INodeEventCommitter
    {
        public Func<NodeIdentity, string, ConnectRequest, CancellationToken, Task<EventAck?>> Commit { get; set; } =
            static (_, _, _, _) => Task.FromResult<EventAck?>(null);
        public Func<NodeIdentity, NodeLinkState, CancellationToken, Task> StateChanged { get; set; } =
            static (_, _, _) => Task.CompletedTask;

        public Task<IReadOnlyList<ReplayFrom>> GetReplayAsync(NodeIdentity identity, Hello hello, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ReplayFrom>>([]);

        public Task<EventAck?> CommitAsync(NodeIdentity identity, string nodeInstanceId, ConnectRequest request,
            CancellationToken ct) => Commit(identity, nodeInstanceId, request, ct);

        public Task StateChangedAsync(NodeIdentity identity, NodeLinkState state, CancellationToken ct) =>
            StateChanged(identity, state, ct);
    }

    private sealed class DelayedOrdinaryApplication(
        TaskCompletionSource<CancellationToken> entered,
        TaskCompletionSource finish,
        Action<NodeLinkState> stateChanged) : INodeLinkApplication
    {
        public Task<IReadOnlyList<ReplayFrom>> GetReplayAsync(NodeIdentity identity, Hello hello, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ReplayFrom>>([]);

        public async Task ReceiveAsync(NodeIdentity identity, string nodeInstanceId, ConnectRequest request,
            CancellationToken ct)
        {
            entered.TrySetResult(ct);
            await finish.Task.WaitAsync(ct);
        }

        public Task StateChangedAsync(NodeIdentity identity, NodeLinkState state, CancellationToken ct)
        {
            stateChanged(state);
            return Task.CompletedTask;
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly object _sync = new();
        private readonly List<ManualTimer> _timers = [];
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() { lock (_sync) return _timestamp; }
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(GetTimestamp());

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            return timer;
        }

        public void Advance(TimeSpan amount)
        {
            List<(TimerCallback Callback, object? State)> callbacks = [];
            lock (_sync)
            {
                _timestamp += amount.Ticks;
                foreach (var timer in _timers.ToArray())
                    if (timer.TakeIfDue(_timestamp) is { } callback) callbacks.Add(callback);
            }
            foreach (var (callback, state) in callbacks) callback(state);
        }

        private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
        {
            private long? _dueAt;
            private TimeSpan _period = Timeout.InfiniteTimeSpan;
            private bool _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (owner._sync)
                {
                    if (_disposed) return false;
                    _period = period;
                    _dueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner._timestamp + dueTime.Ticks;
                    if (!owner._timers.Contains(this)) owner._timers.Add(this);
                    return true;
                }
            }

            public void Dispose()
            {
                lock (owner._sync)
                {
                    _disposed = true;
                    _dueAt = null;
                    owner._timers.Remove(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public (TimerCallback Callback, object? State)? TakeIfDue(long now)
            {
                if (_disposed || _dueAt is not { } dueAt || now < dueAt) return null;
                _dueAt = _period == Timeout.InfiniteTimeSpan ? null : now + _period.Ticks;
                return (callback, state);
            }
        }
    }
}
