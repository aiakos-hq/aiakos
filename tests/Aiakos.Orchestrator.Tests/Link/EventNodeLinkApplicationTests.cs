using Aiakos.Contracts.Node.V1;
using Aiakos.Orchestrator.Link;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aiakos.Orchestrator.Tests.Link;

public sealed class EventNodeLinkApplicationTests
{
    private static readonly Guid Epoch = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Seat = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Tenant = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly NodeIdentity Identity = new("node-id", Tenant, "node-name");

    [Fact]
    public async Task DelegatesReplayAndStateWithClonedMessagesAndResults()
    {
        var committer = new FakeCommitter();
        var hello = new Hello { NodeName = "node-name", NodeInstanceId = Epoch.ToString("D") };
        var persisted = new List<ReplayFrom> { new() { SeatId = Seat.ToString("D"), NextSeq = 3 } };
        committer.Replay = (_, receivedHello, _) =>
        {
            Assert.NotSame(hello, receivedHello);
            return Task.FromResult<IReadOnlyList<ReplayFrom>>(persisted);
        };
        var app = new EventNodeLinkApplication(committer);

        var replay = await app.GetReplayAsync(Identity, hello, CancellationToken.None);
        hello.NodeName = "changed";
        replay[0].NextSeq = 9;
        Assert.Equal("node-name", Assert.IsType<Hello>(committer.ReplayHello).NodeName);
        Assert.Equal(3UL, persisted[0].NextSeq);
        Assert.Equal(3UL, (await app.GetReplayAsync(Identity, new Hello(), CancellationToken.None))[0].NextSeq);

        await app.StateChangedAsync(Identity, NodeLinkState.Unknown, CancellationToken.None);
        Assert.Equal((Identity, NodeLinkState.Unknown), committer.LastStateChange);
    }

    [Fact]
    public async Task ReceiveEventAwaitsCommitAndSnapshotsTheRequestAndAcknowledgement()
    {
        var committer = new FakeCommitter();
        var entered = new TaskCompletionSource<ConnectRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<EventAck?>(TaskCreationOptions.RunContinuationsAsynchronously);
        committer.Commit = (_, _, clone, _) =>
        {
            entered.SetResult(clone);
            return finish.Task;
        };
        var app = new EventNodeLinkApplication(committer);
        var request = Request();
        var originalBytes = request.ToByteArray();
        var resultTask = app.ReceiveEventAsync(Identity, Epoch.ToString("D"), request, CancellationToken.None);

        var forwarded = await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.NotSame(request, forwarded);
        Assert.NotSame(request.SeatEvent, forwarded.SeatEvent);
        Assert.False(resultTask.IsCompleted);
        Assert.Equal(originalBytes, forwarded.ToByteArray());

        forwarded.SeatEvent.SeatId = Guid.NewGuid().ToString("D");
        forwarded.Trace.Traceparent = "provider-mutated";
        Assert.Equal(originalBytes, request.ToByteArray());

        var committed = new EventAck { SeatId = Seat.ToString("D"), ThroughSeq = 1 };
        finish.SetResult(committed);
        var returned = await resultTask;
        Assert.NotNull(returned);
        Assert.NotSame(committed, returned);
        committed.ThroughSeq = 8;
        Assert.Equal(1UL, returned.ThroughSeq);
    }

    [Fact]
    public async Task PreservesNullAcknowledgementAndRoutesNonEventsAsNoOps()
    {
        var committer = new FakeCommitter { Commit = (_, _, _, _) => Task.FromResult<EventAck?>(null) };
        var app = new EventNodeLinkApplication(committer);

        Assert.Null(await app.ReceiveEventAsync(Identity, Epoch.ToString("D"), Request(), CancellationToken.None));
        Assert.Null(await app.ReceiveEventAsync(Identity, Epoch.ToString("D"),
            Request(launchId: Guid.NewGuid().ToString("D")), CancellationToken.None));
        await app.ReceiveAsync(Identity, Epoch.ToString("D"), new ConnectRequest { Heartbeat = new Heartbeat() }, CancellationToken.None);
        Assert.Equal(2, committer.CommitCount);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => app.ReceiveAsync(
            Identity, Epoch.ToString("D"), Request(), CancellationToken.None));
        Assert.Equal("Seat events require committed acknowledgement.", exception.Message);
        Assert.Equal(2, committer.CommitCount);
    }

    [Fact]
    public async Task RejectsInvalidEventsBeforeInvokingCommitter()
    {
        var committer = new FakeCommitter();
        var app = new EventNodeLinkApplication(committer);
        var invalidRequests = new ConnectRequest[]
        {
            Request(seatId: ""),
            Request(seatId: Guid.Empty.ToString("D")),
            Request(launchId: "not-a-guid"),
            Request(seq: 0),
            Request(seq: (ulong)long.MaxValue),
            Request(sourceSeq: (ulong)long.MaxValue + 1),
            Request(omitTimestamp: true),
            Request(observedAt: new Timestamp { Seconds = 253402300800 }),
            null!,
        };

        foreach (var request in invalidRequests)
        {
            var exception = await Assert.ThrowsAsync<ArgumentException>(() => app.ReceiveEventAsync(
                Identity, Epoch.ToString("D"), request, CancellationToken.None));
            Assert.Equal("Invalid seat event.", exception.Message);
            Assert.Null(exception.ParamName);
            Assert.Null(exception.InnerException);
        }

        var unknownBody = new ConnectRequest
        {
            SeatEvent = new SeatEvent
            {
                SeatId = Seat.ToString("D"), Seq = 1, ObservedAt = Timestamp.FromDateTime(DateTime.UnixEpoch)
            }
        };
        Assert.Null(await app.ReceiveEventAsync(Identity, Epoch.ToString("D"), unknownBody, CancellationToken.None));
        Assert.Equal(1, committer.CommitCount);

        var invalidEpoch = await Assert.ThrowsAsync<ArgumentException>(() => app.ReceiveEventAsync(
            Identity, "bad-epoch", Request(), CancellationToken.None));
        Assert.Equal("Invalid seat event.", invalidEpoch.Message);
        Assert.Equal(1, committer.CommitCount);
        var emptyEpoch = await Assert.ThrowsAsync<ArgumentException>(() => app.ReceiveEventAsync(
            Identity, Guid.Empty.ToString("D"), Request(), CancellationToken.None));
        Assert.Equal("Invalid seat event.", emptyEpoch.Message);
        Assert.Equal(1, committer.CommitCount);
    }

    [Fact]
    public async Task RejectsInvalidCommittedAcknowledgementsAndReplayWithoutFallback()
    {
        foreach (var ack in new EventAck?[]
        {
            new() { SeatId = Guid.NewGuid().ToString("D"), ThroughSeq = 1 },
            new() { SeatId = Seat.ToString("D"), ThroughSeq = 0 },
            new() { SeatId = Seat.ToString("D"), ThroughSeq = (ulong)long.MaxValue },
        })
        {
            var app = new EventNodeLinkApplication(new FakeCommitter { Commit = (_, _, _, _) => Task.FromResult(ack) });
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => app.ReceiveEventAsync(
                Identity, Epoch.ToString("D"), Request(), CancellationToken.None));
            Assert.Equal("Invalid committed event acknowledgement.", exception.Message);
        }

        foreach (var replay in new IReadOnlyList<ReplayFrom>[]
        {
            [new ReplayFrom { SeatId = "bad-seat", NextSeq = 1 }],
            [new ReplayFrom { SeatId = Seat.ToString("D"), NextSeq = 0 }],
            [new ReplayFrom { SeatId = Seat.ToString("D"), NextSeq = (ulong)long.MaxValue + 1 }],
            [new ReplayFrom { SeatId = Seat.ToString("D"), NextSeq = 1 }, new ReplayFrom { SeatId = Seat.ToString("D"), NextSeq = 2 }],
        })
        {
            var app = new EventNodeLinkApplication(new FakeCommitter
            {
                Replay = (_, _, _) => Task.FromResult(replay)
            });
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => app.GetReplayAsync(
                Identity, new Hello(), CancellationToken.None));
            Assert.Equal("Invalid committed event replay.", exception.Message);
        }
    }

    [Fact]
    public async Task PropagatesCancellationWithoutStartingACommitWhenAlreadyCancelled()
    {
        var committer = new FakeCommitter
        {
            Commit = async (_, _, _, ct) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return null;
            }
        };
        var app = new EventNodeLinkApplication(committer);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => app.ReceiveEventAsync(
            Identity, Epoch.ToString("D"), Request(), cancellation.Token));
        Assert.Equal(0, committer.CommitCount);
    }

    [Fact]
    public async Task PropagatesCancellationFromAnInFlightCommitWithoutReturningAnAcknowledgement()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var committer = new FakeCommitter
        {
            Commit = async (_, _, _, ct) =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return null;
            }
        };
        var app = new EventNodeLinkApplication(committer);
        using var cancellation = new CancellationTokenSource();
        var pending = app.ReceiveEventAsync(Identity, Epoch.ToString("D"), Request(), cancellation.Token);

        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, committer.CommitCount);
    }

    [Fact]
    public async Task PropagatesCommitterFailureWithoutCreatingAnAcknowledgement()
    {
        var failure = new InvalidOperationException("fake commit failure");
        var committer = new FakeCommitter
        {
            Commit = (_, _, _, _) => Task.FromException<EventAck?>(failure)
        };
        var app = new EventNodeLinkApplication(committer);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => app.ReceiveEventAsync(
            Identity, Epoch.ToString("D"), Request(), CancellationToken.None));

        Assert.Same(failure, actual);
        Assert.Equal(1, committer.CommitCount);
    }

    private static ConnectRequest Request(string? seatId = null, string launchId = "", ulong seq = 1,
        ulong sourceSeq = 0, Timestamp? observedAt = null, bool omitTimestamp = false)
    {
        var timestamp = observedAt ?? Timestamp.FromDateTime(DateTime.UnixEpoch);
        var eventBytes = new SeatEvent
        {
            SeatId = seatId ?? Seat.ToString("D"), LaunchId = launchId, Seq = seq,
            SourceSeq = sourceSeq, ObservedAt = omitTimestamp ? null : timestamp,
            Harness = new HarnessEvent { Harness = "test", Kind = HarnessEventKind.Other, Raw = ByteString.CopyFrom([1, 2]) }
        }.ToByteArray();
        var withUnknownField = new byte[eventBytes.Length + 3];
        eventBytes.CopyTo(withUnknownField, 0);
        withUnknownField[^3] = 0x98;
        withUnknownField[^2] = 0x06;
        withUnknownField[^1] = 0x07;
        var seatEvent = SeatEvent.Parser.ParseFrom(withUnknownField);
        return new ConnectRequest
        {
            Trace = new TraceContext { Traceparent = "00-11111111111111111111111111111111-2222222222222222-01", Tracestate = "k=v" },
            SeatEvent = seatEvent,
        };
    }

    private sealed class FakeCommitter : INodeEventCommitter
    {
        public Func<NodeIdentity, Hello, CancellationToken, Task<IReadOnlyList<ReplayFrom>>> Replay { get; set; } =
            static (_, _, _) => Task.FromResult<IReadOnlyList<ReplayFrom>>([]);
        public Func<NodeIdentity, string, ConnectRequest, CancellationToken, Task<EventAck?>> Commit { get; set; } =
            static (_, _, _, _) => Task.FromResult<EventAck?>(null);
        public int CommitCount { get; private set; }
        public Hello? ReplayHello { get; private set; }
        public (NodeIdentity Identity, NodeLinkState State)? LastStateChange { get; private set; }

        public Task<IReadOnlyList<ReplayFrom>> GetReplayAsync(NodeIdentity identity, Hello hello, CancellationToken ct)
        {
            ReplayHello = hello;
            return Replay(identity, hello, ct);
        }

        public Task<EventAck?> CommitAsync(NodeIdentity identity, string nodeInstanceId, ConnectRequest request, CancellationToken ct)
        {
            CommitCount++;
            return Commit(identity, nodeInstanceId, request, ct);
        }

        public Task StateChangedAsync(NodeIdentity identity, NodeLinkState state, CancellationToken ct)
        {
            LastStateChange = (identity, state);
            return Task.CompletedTask;
        }
    }
}
