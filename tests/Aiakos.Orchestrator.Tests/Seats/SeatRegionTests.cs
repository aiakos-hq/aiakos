using System.Collections.Concurrent;
using System.Threading.Channels;

using Akka.Actor;
using Akka.TestKit;

using Aiakos.Contracts.Node.V1;
using Aiakos.Orchestrator.Seats;

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aiakos.Orchestrator.Tests.Seats;

public sealed class SeatRegionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid OtherTenant = Guid.Parse("12121212-1212-4212-8212-121212121212");
    private static readonly Guid SeatA = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid SeatB = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid HumanSeat = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid RetiredSeat = Guid.Parse("55555555-5555-4555-8555-555555555555");

    [Fact]
    public async Task DirectConstructionStartsEagerlyBeforeExternalRequestsAndContinuesAfterFailedKey()
    {
        var keyA = new SeatKey(Tenant, SeatA);
        var keyB = new SeatKey(Tenant, SeatB);
        var reader = new FakeReader(Snapshot(keyA), Snapshot(keyB), startupSeats: [keyA, keyB])
        {
            StartupRead = new(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        reader.FailLoadsFor(keyB);
        var writer = new FakeWriter(reader);
        await using var rig = await RegionRig.StartAsync(reader, writer, startExplicitly: false);
        var pending = rig.Gateway.ApplyAsync(Tenant, SeatA, new NodeLinkLost(), CancellationToken.None);
        await reader.StartupEntered.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        Assert.False(pending.IsCompleted);
        Assert.Equal(0, writer.TotalCommitCount);
        reader.StartupRead.TrySetResult([keyA, keyB]);
        await writer.ActorStoppedFinding.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        Assert.Equal(8, (await pending.WaitAsync(Deadline, TestContext.Current.CancellationToken)).Version);
        Assert.Null(await rig.GetSeatAsync(SeatB));
        Assert.Equal(1, writer.RecordActorStoppedCount);
        Assert.Equal(1, writer.CommitCount(keyA));
        Assert.Equal(0, writer.CommitCount(keyB));
    }

    [Fact]
    public async Task RoutesByTenantSeatAndKeepsDifferentSeatCommitsIndependent()
    {
        var keyA = new SeatKey(Tenant, SeatA);
        var keyB = new SeatKey(Tenant, SeatB);
        var reader = new FakeReader(Snapshot(keyA), Snapshot(keyB));
        var writer = new FakeWriter(reader);
        await using var rig = await RegionRig.StartAsync(reader, writer);
        var barrier = writer.BlockSeat(keyA);

        var firstA = rig.Gateway.ApplyAsync(Tenant, SeatA, new NodeLinkLost(), CancellationToken.None);
        await barrier.Entered.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        var secondA = rig.Gateway.ApplyAsync(Tenant, SeatA, new OrchestratorRestarted(), CancellationToken.None);
        var resultB = await rig.Gateway.ApplyAsync(Tenant, SeatB, new NodeLinkLost(), CancellationToken.None)
            .WaitAsync(Deadline, TestContext.Current.CancellationToken);

        Assert.Equal(8, resultB.Version);
        Assert.False(firstA.IsCompleted);
        Assert.False(secondA.IsCompleted);
        var actorA = await rig.ResolveSeatAsync(SeatA);
        Assert.Equal("22222222-2222-4222-8222-222222222222", actorA.Path.Name);
        Assert.Equal("33333333-3333-4333-8333-333333333333",
            (await rig.ResolveSeatAsync(SeatB)).Path.Name);

        barrier.Release.TrySetResult();
        Assert.Equal(8, (await firstA.WaitAsync(Deadline, TestContext.Current.CancellationToken)).Version);
        Assert.Equal(9, (await secondA.WaitAsync(Deadline, TestContext.Current.CancellationToken)).Version);
        Assert.Equal(2, writer.CommitCount(keyA));
        Assert.Equal(1, writer.CommitCount(keyB));

        await AssertFailure("SEAT_NOT_FOUND", rig.Gateway.ApplyAsync(OtherTenant, SeatA,
            new NodeLinkLost(), CancellationToken.None));
        Assert.Same(actorA, await rig.ResolveSeatAsync(SeatA));
    }

    [Fact]
    public async Task RejectsHumanRetiredAndMissingSeatsWithoutCreatingChildren()
    {
        var human = Snapshot(new(Tenant, HumanSeat)) with { Kind = "human" };
        var retired = Snapshot(new(Tenant, RetiredSeat)) with { Retired = true };
        var reader = new FakeReader(human, retired);
        var writer = new FakeWriter(reader);
        await using var rig = await RegionRig.StartAsync(reader, writer);

        await AssertFailure("HUMAN_SEAT", rig.Gateway.ApplyAsync(Tenant, HumanSeat,
            new NodeLinkLost(), CancellationToken.None));
        await AssertFailure("SEAT_RETIRED", rig.Gateway.ApplyAsync(Tenant, RetiredSeat,
            new NodeLinkLost(), CancellationToken.None));
        await AssertFailure("SEAT_NOT_FOUND", rig.Gateway.ApplyAsync(Tenant, SeatA,
            new NodeLinkLost(), CancellationToken.None));

        await AssertNoSeatChildAsync(rig, HumanSeat);
        await AssertNoSeatChildAsync(rig, RetiredSeat);
        await AssertNoSeatChildAsync(rig, SeatA);
        Assert.Equal(0, writer.TotalCommitCount);
    }

    [Fact]
    public async Task EagerlyCreatesOnlyStartupSeatsAfterEligibilityChecks()
    {
        var keyA = new SeatKey(Tenant, SeatA);
        var keyB = new SeatKey(Tenant, SeatB);
        var reader = new FakeReader(Snapshot(keyA), Snapshot(keyB),
            startupSeats: [keyA, keyB, new(Tenant, HumanSeat), new(Tenant, RetiredSeat)]);
        reader.SetSnapshot(Snapshot(new(Tenant, HumanSeat)) with { Kind = "human" });
        reader.SetSnapshot(Snapshot(new(Tenant, RetiredSeat)) with { Retired = true });
        var writer = new FakeWriter(reader);
        await using var rig = await RegionRig.StartAsync(reader, writer);

        Assert.Equal("22222222-2222-4222-8222-222222222222", (await rig.ResolveSeatAsync(SeatA)).Path.Name);
        Assert.Equal("33333333-3333-4333-8333-333333333333", (await rig.ResolveSeatAsync(SeatB)).Path.Name);
        await AssertNoSeatChildAsync(rig, HumanSeat);
        await AssertNoSeatChildAsync(rig, RetiredSeat);
        Assert.Equal(0, writer.TotalCommitCount);
    }

    [Fact]
    public async Task StartupLoadFailureIsRecordedAndDoesNotBlockOtherStartupSeats()
    {
        var keyA = new SeatKey(Tenant, SeatA);
        var keyB = new SeatKey(Tenant, SeatB);
        var reader = new FakeReader(Snapshot(keyA), Snapshot(keyB), startupSeats: [keyA, keyB]);
        reader.FailLoadsFor(keyA);
        var writer = new FakeWriter(reader);
        await using var rig = await RegionRig.StartAsync(reader, writer);

        Assert.Null(await rig.GetSeatAsync(SeatA));
        Assert.Equal("33333333-3333-4333-8333-333333333333", (await rig.ResolveSeatAsync(SeatB)).Path.Name);
        await writer.ActorStoppedFinding.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        Assert.Equal(1, writer.RecordActorStoppedCount);
    }

    [Fact]
    public async Task FailedInitialLoadKeepsChildNameReservedUntilTermination()
    {
        var keyA = new SeatKey(Tenant, SeatA);
        var keyB = new SeatKey(Tenant, SeatB);
        var failLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseStop = new ManualResetEventSlim();
        var reader = new FakeReader(Snapshot(keyA), Snapshot(keyB), startupSeats: [keyA]);
        reader.LoadOverride = async (key, count, ct) =>
        {
            if (key == keyA && count == 2)
            {
                // Keep the failed child's name alive while the region handles the next request.
                ct.Register(() =>
                {
                    stopEntered.TrySetResult();
                    releaseStop.Wait(Deadline);
                });
                await failLoad.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
                throw new InvalidOperationException("private reader detail");
            }
            return reader.Snapshot(key);
        };
        var writer = new FakeWriter(reader);
        await using var rig = await RegionRig.StartAsync(reader, writer, startExplicitly: false);
        try
        {
            failLoad.TrySetResult();
            await writer.ActorStoppedFinding.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
            await stopEntered.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);

            await AssertFailure("SEAT_ACTOR_UNAVAILABLE", rig.Gateway.ApplyAsync(Tenant, SeatA,
                new OrchestratorRestarted(), CancellationToken.None));
            await AssertFailure("SEAT_NOT_FOUND", rig.Gateway.ApplyAsync(OtherTenant, SeatA,
                new OrchestratorRestarted(), CancellationToken.None));
            Assert.NotNull(await rig.GetSeatAsync(SeatA));
            Assert.Equal(8, (await rig.Gateway.ApplyAsync(Tenant, SeatB, new NodeLinkLost(), CancellationToken.None)
                .WaitAsync(Deadline, TestContext.Current.CancellationToken)).Version);
            Assert.Equal(0, writer.CommitCount(keyA));
            Assert.Equal(1, writer.RecordActorStoppedCount);
        }
        finally
        {
            releaseStop.Set();
        }
    }

    [Fact]
    public async Task ClonesEventsBeforeAdmissionAndKeepsCallerCancellationLocal()
    {
        var key = new SeatKey(Tenant, SeatA);
        var reader = new FakeReader(Snapshot(key));
        var writer = new FakeWriter(reader);
        await using var rig = await RegionRig.StartAsync(reader, writer);
        var barrier = writer.BlockSeat(key);

        using var preCanceled = new CancellationTokenSource();
        preCanceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rig.Gateway.ApplyAsync(
            Tenant, SeatA, new NodeLinkLost(), preCanceled.Token));
        await AssertFailure("SEAT_INPUT_NOT_SUPPORTED", rig.Gateway.ApplyAsync(
            Tenant, SeatA, new DownRequested(), CancellationToken.None));
        Assert.Null(await rig.GetSeatAsync(SeatA));

        var original = new SeatEvent
        {
            SeatId = SeatA.ToString("D"),
            Seq = 1,
            ObservedAt = Timestamp.FromDateTime(Now.UtcDateTime),
            Harness = new HarnessEvent
            {
                Kind = HarnessEventKind.PromptSubmitted,
                NativeSessionId = "native-session",
                Attributes = { ["phase"] = "before" },
                Raw = ByteString.CopyFrom([1, 2, 3]),
            },
        };
        using var callerCancellation = new CancellationTokenSource();
        var pending = rig.Gateway.CommitAsync(Tenant, SeatA, "node-a", Guid.NewGuid(), [original], null,
            callerCancellation.Token);
        await barrier.Entered.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        original.Harness.Attributes["phase"] = "after";
        original.Harness.Raw = ByteString.CopyFrom([9]);
        callerCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await pending.WaitAsync(Deadline, TestContext.Current.CancellationToken));
        Assert.False(barrier.CancellationObserved.Task.IsCompleted);
        barrier.Release.TrySetResult();
        Assert.Equal(9, (await rig.Gateway.ApplyAsync(Tenant, SeatA, new OrchestratorRestarted(), CancellationToken.None)
            .WaitAsync(Deadline, TestContext.Current.CancellationToken)).Version);

        var eventCommit = writer.CommittedEvents.Single();
        Assert.Equal("before", eventCommit.Harness.Attributes["phase"]);
        Assert.Equal(new byte[] { 1, 2, 3 }, eventCommit.Harness.Raw.ToByteArray());
        Assert.False(barrier.CancellationObserved.Task.IsCompleted);
        Assert.Equal(9, reader.Snapshot(key).Version);
    }

    [Fact]
    public async Task GatewayTimeoutDoesNotRetryAndLateCommitCanFinish()
    {
        var key = new SeatKey(Tenant, SeatA);
        var reader = new FakeReader(Snapshot(key));
        var writer = new FakeWriter(reader);
        await using var rig = await RegionRig.StartAsync(reader, writer,
            requestTimeout: TimeSpan.FromMilliseconds(500));
        var barrier = writer.BlockSeat(key);

        var pending = rig.Gateway.ApplyAsync(Tenant, SeatA, new NodeLinkLost(), CancellationToken.None);
        await barrier.Entered.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        await AssertFailure("SEAT_ACTOR_UNAVAILABLE", pending);
        Assert.Equal(1, writer.CommitCount(key));

        barrier.Release.TrySetResult();
        var result = await rig.Gateway.ApplyAsync(Tenant, SeatA, new OrchestratorRestarted(), CancellationToken.None)
            .WaitAsync(Deadline, TestContext.Current.CancellationToken);
        Assert.Equal(9, result.Version);
        Assert.Equal(2, writer.CommitCount(key));
    }

    [Fact]
    public async Task ReloadObserversRunAfterRestartAndDisposeIdempotently()
    {
        var key = new SeatKey(Tenant, SeatA);
        var reader = new FakeReader(Snapshot(key));
        var writer = new FakeWriter(reader);
        await using var rig = await RegionRig.StartAsync(reader, writer);
        var notices = Channel.CreateUnbounded<SeatActorReloaded>();
        var observed = 0;
        var throwing = rig.Gateway.Subscribe(_ => throw new InvalidOperationException("private observer detail"));
        var healthy = rig.Gateway.Subscribe(notice =>
        {
            Interlocked.Increment(ref observed);
            notices.Writer.TryWrite(notice);
        });
        await rig.WaitForRegionAsync();

        writer.FailNextCommits(1);
        await Assert.ThrowsAsync<SeatCommitFailedException>(async () =>
            await rig.Gateway.ApplyAsync(Tenant, SeatA, new NodeLinkLost(), CancellationToken.None)
                .WaitAsync(Deadline, TestContext.Current.CancellationToken));
        var reload = await notices.Reader.ReadAsync(TestContext.Current.CancellationToken)
            .AsTask().WaitAsync(Deadline, TestContext.Current.CancellationToken);

        Assert.Equal(new SeatActorReloaded(Tenant, SeatA, 7), reload);
        Assert.Equal(1, Volatile.Read(ref observed));

        throwing.Dispose();
        throwing.Dispose();
        healthy.Dispose();
        healthy.Dispose();
        await rig.WaitForRegionAsync();
        writer.FailNextCommits(1);
        await Assert.ThrowsAsync<SeatCommitFailedException>(async () =>
            await rig.Gateway.ApplyAsync(Tenant, SeatA, new NodeLinkLost(), CancellationToken.None)
                .WaitAsync(Deadline, TestContext.Current.CancellationToken));
        var result = await rig.Gateway.ApplyAsync(Tenant, SeatA, new NodeLinkLost(), CancellationToken.None)
            .WaitAsync(Deadline, TestContext.Current.CancellationToken);

        Assert.Equal(8, result.Version);
        Assert.Equal(1, Volatile.Read(ref observed));
        Assert.False(notices.Reader.TryRead(out _));
    }

    [Fact]
    public async Task RestartsTenTimesThenStopsAndRecreatesTheSeatActor()
    {
        var key = new SeatKey(Tenant, SeatA);
        var reader = new FakeReader(Snapshot(key));
        var writer = new FakeWriter(reader) { FailActorStoppedFinding = true };
        await using var rig = await RegionRig.StartAsync(reader, writer);
        var notices = Channel.CreateUnbounded<SeatActorReloaded>();
        using var subscription = rig.Gateway.Subscribe(notice => notices.Writer.TryWrite(notice));
        await rig.WaitForRegionAsync();

        for (var attempt = 0; attempt < 10; attempt++)
        {
            writer.FailNextCommits(1);
            await Assert.ThrowsAsync<SeatCommitFailedException>(async () =>
                await rig.Gateway.ApplyAsync(Tenant, SeatA, new NodeLinkLost(), CancellationToken.None)
                    .WaitAsync(Deadline, TestContext.Current.CancellationToken));
            var reloaded = await notices.Reader.ReadAsync(TestContext.Current.CancellationToken)
                .AsTask().WaitAsync(Deadline, TestContext.Current.CancellationToken);
            Assert.Equal(new SeatActorReloaded(Tenant, SeatA, 7), reloaded);
        }

        writer.FailNextCommits(1);
        await Assert.ThrowsAsync<SeatCommitFailedException>(async () =>
            await rig.Gateway.ApplyAsync(Tenant, SeatA, new NodeLinkLost(), CancellationToken.None)
                .WaitAsync(Deadline, TestContext.Current.CancellationToken));
        await writer.ActorStoppedFinding.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        var recreated = await rig.Gateway.ApplyAsync(Tenant, SeatA, new NodeLinkLost(), CancellationToken.None)
            .WaitAsync(Deadline, TestContext.Current.CancellationToken);

        Assert.Equal(8, recreated.Version);
        Assert.Equal(12, writer.CommitCount(key));
        Assert.Equal(1, writer.RecordActorStoppedCount);
        Assert.False(notices.Reader.TryRead(out _));
    }

    [Fact]
    public async Task StoppingRegionCancelsAdmittedStoreWorkAndFailsWaitingCaller()
    {
        var key = new SeatKey(Tenant, SeatA);
        var reader = new FakeReader(Snapshot(key));
        var writer = new FakeWriter(reader);
        await using var rig = await RegionRig.StartAsync(reader, writer);
        var barrier = writer.BlockSeat(key);
        var pending = rig.Gateway.ApplyAsync(Tenant, SeatA, new NodeLinkLost(), CancellationToken.None);
        await barrier.Entered.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);

        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Region.Tell(new StopSeatRegion(stopped));
        await stopped.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        await AssertFailure("SEAT_ACTOR_UNAVAILABLE", pending);
        Assert.True(barrier.CancellationObserved.Task.IsCompleted);
    }

    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    private static SeatActorSnapshot Snapshot(SeatKey key) => new(key, "agent", "claude-code", "node-a",
        false, 7, Guid.Parse("66666666-6666-4666-8666-666666666666"),
        SeatState.Initial(Now) with
        {
            Session = SessionValue.Present,
            KnownSession = SessionValue.Present,
            Activity = ActivityValue.Idle,
            KnownActivity = ActivityValue.Idle,
            Resumability = ResumabilityValue.Resumable,
            Desired = SeatDesired.Up,
            NativeSessionId = "native-session",
        }, new Dictionary<Guid, SeatStoredCommand>());

    private static async Task AssertFailure<T>(string code, Task<T> task)
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await task.WaitAsync(Deadline, TestContext.Current.CancellationToken));
        Assert.Equal(code, error.Message);
        Assert.Null(error.InnerException);
    }

    private static async Task AssertNoSeatChildAsync(RegionRig rig, Guid seatId)
    {
        Assert.Null(await rig.GetSeatAsync(seatId));
    }

    private sealed class RegionRig : IAsyncDisposable
    {
        private readonly RegionTestKit _testKit;
        private RegionRig(ActorSystem system, RegionTestKit testKit, IActorRef region, SeatActorGateway gateway)
        {
            System = system;
            _testKit = testKit;
            Region = region;
            Gateway = gateway;
        }

        public ActorSystem System { get; }
        public IActorRef Region { get; }
        public SeatActorGateway Gateway { get; }

        public static async Task<RegionRig> StartAsync(FakeReader reader, FakeWriter writer,
            TimeProvider? gatewayTimeProvider = null, TimeSpan? requestTimeout = null, bool startExplicitly = true)
        {
            var system = ActorSystem.Create($"seat-region-tests-{Guid.NewGuid():N}");
            var testKit = new RegionTestKit(system);
            var region = system.ActorOf(Props.Create(() => new SeatRegion(reader, writer,
                new IHarnessStateProfile[] { new ClaudeLike() }, TimeProvider.System)), "seats");
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (startExplicitly)
            {
                region.Tell(new StartSeatRegion(ready));
                await ready.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
            }
            return new(system, testKit, region, new SeatActorGateway(region,
                gatewayTimeProvider ?? TimeProvider.System, requestTimeout ?? TimeSpan.FromSeconds(30)));
        }

        public TestProbe CreateProbe() => _testKit.CreateTestProbe();

        public async Task WaitForRegionAsync()
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Region.Tell(new SeatRegionBarrier(completion));
            await completion.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        }

        public async Task<IActorRef?> GetSeatAsync(Guid seatId)
        {
            var completion = new TaskCompletionSource<IActorRef?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Region.Tell(new GetSeatActorRef(seatId, completion));
            return await completion.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        }

        public async Task<IActorRef> ResolveSeatAsync(Guid seatId) =>
            await GetSeatAsync(seatId) ?? throw new InvalidOperationException("Expected a seat actor.");

        public async ValueTask DisposeAsync()
        {
            _ = _testKit;
            await System.Terminate().WaitAsync(Deadline, TestContext.Current.CancellationToken);
            await System.WhenTerminated.WaitAsync(Deadline, TestContext.Current.CancellationToken);
        }
    }

    private sealed class FakeReader(params SeatActorSnapshot[] snapshots) : ISeatActorReader
    {
        private readonly ConcurrentDictionary<SeatKey, SeatActorSnapshot> _snapshots =
            new(snapshots.ToDictionary(static snapshot => snapshot.Key));
        private readonly ConcurrentDictionary<SeatKey, int> _loads = new();
        private readonly IReadOnlyList<SeatKey> _startupSeats = [];
        private readonly ConcurrentDictionary<SeatKey, byte> _failingLoads = new();

        public FakeReader(SeatActorSnapshot first, SeatActorSnapshot second, IReadOnlyList<SeatKey> startupSeats) : this(first, second)
        {
            _startupSeats = startupSeats;
        }

        public Task<SeatActorSnapshot?> LoadAsync(SeatKey key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var count = _loads.AddOrUpdate(key, 1, static (_, count) => count + 1);
            if (LoadOverride is { } load)
                return load(key, count, ct);
            if (_failingLoads.ContainsKey(key))
                return Task.FromException<SeatActorSnapshot?>(new InvalidOperationException("private reader detail"));
            _snapshots.TryGetValue(key, out var snapshot);
            return Task.FromResult(snapshot);
        }

        public Func<SeatKey, int, CancellationToken, Task<SeatActorSnapshot?>>? LoadOverride { get; set; }

        public TaskCompletionSource<IReadOnlyList<SeatKey>>? StartupRead { get; init; }
        public TaskCompletionSource StartupEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<SeatKey>> GetStartupSeatsAsync(CancellationToken ct)
        {
            StartupEntered.TrySetResult();
            return StartupRead?.Task.WaitAsync(ct) ?? Task.FromResult(_startupSeats);
        }

        public Task<SeatNativeSession?> FindNativeSessionAsync(Guid tenantId, string harness,
            string nativeSessionId, CancellationToken ct) => Task.FromResult<SeatNativeSession?>(null);

        public Task<IReadOnlySet<long>> GetCommittedSequencesAsync(SeatKey key, Guid nodeInstanceId,
            IReadOnlyList<long> sequences, CancellationToken ct) =>
            Task.FromResult<IReadOnlySet<long>>(new HashSet<long>());

        public int LoadCount(SeatKey key) => _loads.TryGetValue(key, out var count) ? count : 0;

        public void SetSnapshot(SeatActorSnapshot snapshot) => _snapshots[snapshot.Key] = snapshot;

        public SeatActorSnapshot Snapshot(SeatKey key) => _snapshots[key];

        public void FailLoadsFor(SeatKey key) => _failingLoads[key] = 0;
    }

    private sealed class FakeWriter(FakeReader reader) : ISeatActorWriter
    {
        private readonly ConcurrentDictionary<SeatKey, int> _commits = new();
        private readonly ConcurrentDictionary<SeatKey, TaskCompletionSource> _blockEntered = new();
        private readonly ConcurrentDictionary<SeatKey, TaskCompletionSource> _blockRelease = new();
        private readonly ConcurrentDictionary<SeatKey, TaskCompletionSource> _blockCancellation = new();
        private readonly ConcurrentQueue<SeatEvent> _committedEvents = new();
        private int _failuresRemaining;
        private int _recordActorStoppedCount;

        public TaskCompletionSource ActorStoppedFinding { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool FailActorStoppedFinding { get; set; }
        public int RecordActorStoppedCount => Volatile.Read(ref _recordActorStoppedCount);
        public IReadOnlyList<SeatEvent> CommittedEvents => _committedEvents.ToArray();

        public int TotalCommitCount => _commits.Values.Sum();

        public Task<SeatStoreReceipt> CommitAsync(SeatActorSnapshot before,
            IReadOnlyList<SeatAppliedInput> inputs, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var count = _commits.AddOrUpdate(before.Key, 1, static (_, current) => current + 1);
            if (TakeFailure())
                return Task.FromException<SeatStoreReceipt>(new InvalidOperationException("private writer detail"));
            if (_blockEntered.TryGetValue(before.Key, out var entered) &&
                _blockRelease.TryGetValue(before.Key, out var release) && count == 1)
            {
                entered.TrySetResult();
                return CompleteAfterReleaseAsync(before, inputs,
                    new CommitBarrier(entered, release, _blockCancellation[before.Key]), ct);
            }

            return Task.FromResult(Commit(before, inputs));
        }

        public Task RecordActorStoppedAsync(SeatKey key, DateTimeOffset at, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _recordActorStoppedCount);
            ActorStoppedFinding.TrySetResult();
            return FailActorStoppedFinding
                ? Task.FromException(new InvalidOperationException("private finding detail"))
                : Task.CompletedTask;
        }

        public int CommitCount(SeatKey key) => _commits.TryGetValue(key, out var count) ? count : 0;

        public CommitBarrier BlockSeat(SeatKey key)
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancellationObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _blockEntered[key] = entered;
            _blockRelease[key] = release;
            _blockCancellation[key] = cancellationObserved;
            return new(entered, release, cancellationObserved);
        }

        public void FailNextCommits(int count) => Interlocked.Add(ref _failuresRemaining, count);

        private async Task<SeatStoreReceipt> CompleteAfterReleaseAsync(SeatActorSnapshot before,
            IReadOnlyList<SeatAppliedInput> inputs, CommitBarrier barrier, CancellationToken ct)
        {
            try
            {
                await barrier.Release.Task.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                barrier.CancellationObserved.TrySetResult();
                throw;
            }
            return Commit(before, inputs);
        }

        private SeatStoreReceipt Commit(SeatActorSnapshot before, IReadOnlyList<SeatAppliedInput> inputs)
        {
            var last = inputs.LastOrDefault(static input => input.Step.Disposition != EventDisposition.Duplicate);
            var state = last?.Step.State ?? before.State;
            foreach (var item in inputs)
                if (item.Event is { } value)
                    _committedEvents.Enqueue(value);
            var version = before.Version + (last is null ? 0 : 1);
            var updated = before with { State = state, Version = version };
            reader.SetSnapshot(updated);
            return new(version, updated.CurrentSessionId);
        }

        private bool TakeFailure()
        {
            while (true)
            {
                var current = Volatile.Read(ref _failuresRemaining);
                if (current <= 0)
                    return false;
                if (Interlocked.CompareExchange(ref _failuresRemaining, current - 1, current) == current)
                    return true;
            }
        }

    }

    private sealed record CommitBarrier(TaskCompletionSource Entered, TaskCompletionSource Release,
        TaskCompletionSource CancellationObserved);

    private sealed class RegionTestKit(ActorSystem system)
        : TestKitBase(new XunitV3TestKitAssertions(), system, "seat-region-tests");
}
