using System.Collections.Concurrent;

using Akka.Actor;
using Akka.TestKit;

using Aiakos.Contracts.Node.V1;
using Aiakos.Orchestrator.Seats;
using Aiakos.Orchestrator.Tests.Seats;

using Google.Protobuf.WellKnownTypes;

namespace Aiakos.Orchestrator.Tests.Seats;

public sealed class SeatActorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid SeatId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid OtherSeatId = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly Guid EpochId = Guid.Parse("44444444-4444-4444-8444-444444444444");
    private static readonly Guid LaunchId = Guid.Parse("55555555-5555-4555-8555-555555555555");
    private static readonly Guid SessionId = Guid.Parse("66666666-6666-4666-8666-666666666666");
    private static readonly Guid OtherSessionId = Guid.Parse("77777777-7777-4777-8777-777777777777");
    private const string Harness = "test-harness";
    private const string Node = "node-a";

    [Fact]
    public async Task SerializesCommitsAndCompletesOnlyAfterEachCommit()
    {
        var key = new SeatKey(TenantId, SeatId);
        var reader = new FakeReader(Snapshot(key));
        var writer = new FakeWriter(reader);
        await using var rig = await ActorRig.StartAsync(key, reader, writer, [new TestProfile()], new FixedTimeProvider());
        var barrier = writer.BlockNextCommit();
        var first = new TaskCompletionSource<SeatInputCommitted>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<SeatInputCommitted>(TaskCreationOptions.RunContinuationsAsynchronously);

        rig.Actor.Tell(new SeatInputRequest(new NodeLinkLost(), first));
        await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        rig.Actor.Tell(new SeatInputRequest(new OrchestratorRestarted(), second));

        Assert.False(first.Task.IsCompleted);
        Assert.False(second.Task.IsCompleted);
        Assert.Equal(1, writer.CommitCalls);

        barrier.Release.TrySetResult();
        var firstResult = await first.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var secondResult = await second.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var initialState = Snapshot(key).State;
        var firstExpected = SeatStateMachine.Apply(initialState, new NodeLinkLost(), new TestProfile(), Now).State;
        var secondExpected = SeatStateMachine.Apply(firstExpected, new OrchestratorRestarted(), new TestProfile(), Now).State;

        Assert.Equal(8, firstResult.Version);
        Assert.Equal(9, secondResult.Version);
        Assert.Equal(secondExpected, secondResult.State);
        Assert.Equal(new SeatInput[] { new NodeLinkLost(), new OrchestratorRestarted() },
            writer.PersistedInputs.Select(item => item.Input));
        Assert.Equal(2, writer.CommitCalls);
    }

    [Fact]
    public async Task AllowsDifferentSeatsToCommitConcurrently()
    {
        var keyA = new SeatKey(TenantId, SeatId);
        var keyB = new SeatKey(TenantId, OtherSeatId);
        var reader = new FakeReader(Snapshot(keyA), Snapshot(keyB));
        var writer = new FakeWriter(reader);
        await using var rig = await ActorRig.StartAsync(keyA, reader, writer, [new TestProfile()], new FixedTimeProvider());
        var actorB = await rig.SpawnAsync(keyB, reader, writer, [new TestProfile()], new FixedTimeProvider());
        var barrier = writer.BlockCommits(2);
        var resultA = new TaskCompletionSource<SeatInputCommitted>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resultB = new TaskCompletionSource<SeatInputCommitted>(TaskCreationOptions.RunContinuationsAsynchronously);

        rig.Actor.Tell(new SeatInputRequest(new NodeLinkLost(), resultA));
        actorB.Tell(new SeatInputRequest(new NodeLinkLost(), resultB));

        await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(2, writer.CommitCalls);
        barrier.Release.TrySetResult();
        Assert.Equal(8, (await resultA.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).Version);
        Assert.Equal(8, (await resultB.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).Version);
    }

    [Fact]
    public async Task InvalidEventsAreRejectedWithoutCommitAndZeroLaunchUuidIsAccepted()
    {
        var key = new SeatKey(TenantId, SeatId);
        var reader = new FakeReader(Snapshot(key));
        var writer = new FakeWriter(reader);
        await using var rig = await ActorRig.StartAsync(key, reader, writer, [new TestProfile()], new FixedTimeProvider());

        await AssertEventRejected(rig.Actor, new SeatEventRequest(Node, Guid.Empty, [], null,
            NewCompletion<SeatEventsCommitted>()), "INVALID_SEAT_EVENT");
        await AssertEventRejected(rig.Actor, new SeatEventRequest(string.Empty, EpochId, [], null,
            NewCompletion<SeatEventsCommitted>()), "INVALID_SEAT_EVENT");
        await AssertEventRejected(rig.Actor, new SeatEventRequest(Node, EpochId, null!, null,
            NewCompletion<SeatEventsCommitted>()), "INVALID_SEAT_EVENT");
        await AssertEventRejected(rig.Actor, new SeatEventRequest(Node, EpochId, [null!], null,
            NewCompletion<SeatEventsCommitted>()), "INVALID_SEAT_EVENT");

        var invalid = new[]
        {
            Event(0),
            Event((ulong)long.MaxValue),
            Event(1, sourceSequence: (ulong)long.MaxValue + 1),
            Event(1, seatId: OtherSeatId),
            Event(1, launchId: "not-a-guid"),
            Event(1, observedAt: new Timestamp { Seconds = 253_402_300_800 }),
            Event(1, harness: new HarnessEvent { NativeName = "\ud800" })
        };
        foreach (var invalidEvent in invalid)
        {
            await AssertEventRejected(rig.Actor,
                new SeatEventRequest(Node, EpochId, [invalidEvent], null, NewCompletion<SeatEventsCommitted>()),
                "INVALID_SEAT_EVENT");
        }

        Assert.Equal(0, writer.CommitCalls);
        var valid = Event(1, launchId: Guid.Empty.ToString("D"),
            harness: new HarnessEvent { NativeName = "valid\ufffe\U0001f680" });
        var completion = NewCompletion<SeatEventsCommitted>();
        rig.Actor.Tell(new SeatEventRequest(Node, EpochId, [valid], null, completion));
        var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ThroughSeq);
        Assert.Single(writer.PersistedInputs);
        Assert.Equal("valid\ufffe\U0001f680", writer.PersistedInputs[0].Event!.Harness.NativeName);
        Assert.Equal(2, reader.LoadCount);
    }

    [Fact]
    public async Task MissingProfileAndUnsupportedInputFailSafelyWithoutWriting()
    {
        var key = new SeatKey(TenantId, SeatId);
        var reader = new FakeReader(Snapshot(key));
        var writer = new FakeWriter(reader);
        await using var rig = await ActorRig.StartAsync(key, reader, writer, [], new FixedTimeProvider());

        var eventCompletion = NewCompletion<SeatEventsCommitted>();
        rig.Actor.Tell(new SeatEventRequest(Node, EpochId, [Event(1)], null, eventCompletion));
        await AssertCode(eventCompletion.Task, "HARNESS_PROFILE_UNAVAILABLE");

        var inputCompletion = NewCompletion<SeatInputCommitted>();
        rig.Actor.Tell(new SeatInputRequest(new DownRequested(), inputCompletion));
        await AssertCode(inputCompletion.Task, "HARNESS_PROFILE_UNAVAILABLE");
        Assert.Equal(0, writer.CommitCalls);
        Assert.Equal(1, reader.LoadCount);

        var supportedReader = new FakeReader(Snapshot(key));
        var supportedWriter = new FakeWriter(supportedReader);
        await using var supportedRig = await ActorRig.StartAsync(key, supportedReader, supportedWriter,
            [new TestProfile()], new FixedTimeProvider());
        var unsupported = NewCompletion<SeatInputCommitted>();
        supportedRig.Actor.Tell(new SeatInputRequest(new DownRequested(), unsupported));
        await AssertCode(unsupported.Task, "SEAT_INPUT_NOT_SUPPORTED");
        Assert.Equal(0, supportedWriter.CommitCalls);
    }

    [Fact]
    public async Task RejectsHumanRetiredAndWrongNodeRoutesBeforeWriting()
    {
        var key = new SeatKey(TenantId, SeatId);

        var humanReader = new FakeReader(Snapshot(key, kind: "human"));
        var humanWriter = new FakeWriter(humanReader);
        await using (var humanRig = await ActorRig.StartAsync(key, humanReader, humanWriter,
                         [new TestProfile()], new FixedTimeProvider()))
        {
            var completion = NewCompletion<SeatInputCommitted>();
            humanRig.Actor.Tell(new SeatInputRequest(new NodeLinkLost(), completion));
            await AssertCode(completion.Task, "HUMAN_SEAT");
            Assert.Equal(0, humanWriter.CommitCalls);
        }

        var retiredReader = new FakeReader(Snapshot(key, retired: true));
        var retiredWriter = new FakeWriter(retiredReader);
        await using (var retiredRig = await ActorRig.StartAsync(key, retiredReader, retiredWriter,
                         [new TestProfile()], new FixedTimeProvider()))
        {
            var completion = NewCompletion<SeatInputCommitted>();
            retiredRig.Actor.Tell(new SeatInputRequest(new NodeLinkLost(), completion));
            await AssertCode(completion.Task, "SEAT_RETIRED");
            Assert.Equal(0, retiredWriter.CommitCalls);
        }

        var nodeReader = new FakeReader(Snapshot(key));
        var nodeWriter = new FakeWriter(nodeReader);
        await using (var nodeRig = await ActorRig.StartAsync(key, nodeReader, nodeWriter,
                         [new TestProfile()], new FixedTimeProvider()))
        {
            var completion = NewCompletion<SeatEventsCommitted>();
            nodeRig.Actor.Tell(new SeatEventRequest("other-node", EpochId, [Event(1)], null, completion));
            await AssertCode(completion.Task, "SEAT_NODE_MISMATCH");
            Assert.Equal(0, nodeWriter.CommitCalls);
        }
    }

    [Fact]
    public async Task NulBodyBecomesObservationGapAndTheFollowingEventCommits()
    {
        var key = new SeatKey(TenantId, SeatId);
        var reader = new FakeReader(Snapshot(key));
        var writer = new FakeWriter(reader);
        await using var rig = await ActorRig.StartAsync(key, reader, writer, [new TestProfile()], new FixedTimeProvider());
        var nul = Event(1, harness: new HarnessEvent { NativeSessionId = "bad\0body" });
        var following = Event(2, harness: new HarnessEvent
        {
            Kind = HarnessEventKind.Telemetry,
            NativeSessionId = "native-old",
            Attributes = { ["model"] = "safe" }
        });
        var completion = NewCompletion<SeatEventsCommitted>();

        rig.Actor.Tell(new SeatEventRequest(Node, EpochId, [nul, following], "trace-parent", completion));

        var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(2, result.ThroughSeq);
        Assert.Equal(EpochId, result.NodeInstanceId);
        Assert.Equal(2, writer.PersistedInputs.Length);
        Assert.Equal(new ObservationGapBody(GapReason.IngestUnavailable),
            Assert.IsType<EventReceived>(writer.PersistedInputs[0].Input).Body);
        Assert.Contains(writer.PersistedInputs[0].Step.Findings,
            finding => finding.Kind == SeatVocabulary.FindingObservationGap && finding.Open);
        Assert.Equal(3, reader.GetSnapshot(key)!.State.NextSeq);
        Assert.Equal(ActivityValue.Unknown, reader.GetSnapshot(key)!.State.KnownActivity);
        Assert.Equal(1, writer.CommitCalls);
    }

    [Fact]
    public async Task ForeignRotatedNativeIdBecomesGapAndPreservesNativePointer()
    {
        var key = new SeatKey(TenantId, SeatId);
        var snapshot = Snapshot(key, resumability: ResumabilityValue.FreshOnly);
        var reader = new FakeReader(snapshot);
        reader.SetNativeOwner(TenantId, Harness, "native-new", OtherSeatId, OtherSessionId);
        var writer = new FakeWriter(reader);
        await using var rig = await ActorRig.StartAsync(key, reader, writer, [new TestProfile()], new FixedTimeProvider());
        var rotated = Event(1, harness: new HarnessEvent
        {
            Kind = HarnessEventKind.SessionStarted,
            NativeSessionId = "native-new",
            Attributes = { ["source"] = "clear", ["previous_session_id"] = "native-old" }
        });
        var expectedRotation = SeatStateMachine.Apply(snapshot.State, Assert.IsType<EventReceived>(SeatWireInput.Map(
            EpochId, rotated, snapshot.Commands)), new TestProfile(), Now);
        Assert.IsType<AdoptRotatedSession>(Assert.Single(expectedRotation.Effects));
        var completion = NewCompletion<SeatEventsCommitted>();

        rig.Actor.Tell(new SeatEventRequest(Node, EpochId, [rotated], null, completion));

        await completion.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(1, reader.NativeSessionLookupCount);
        var applied = Assert.Single(writer.PersistedInputs);
        Assert.Equal(new ObservationGapBody(GapReason.IngestUnavailable), Assert.IsType<EventReceived>(applied.Input).Body);
        Assert.Equal("native-new", applied.Event!.Harness.NativeSessionId);
        Assert.Equal(EventDisposition.Evidence, applied.Step.Disposition);
        Assert.Equal("native-old", reader.GetSnapshot(key)!.State.NativeSessionId);
        Assert.Equal(ActivityValue.Unknown, reader.GetSnapshot(key)!.State.KnownActivity);
        Assert.Equal(SeatVocabulary.ActivityReasonObservationGap, reader.GetSnapshot(key)!.State.KnownActivityReason);
        Assert.Equal(ResumabilityValue.Unknown, reader.GetSnapshot(key)!.State.Resumability);
        Assert.Equal(SeatVocabulary.ResumabilityReasonObservationGap, reader.GetSnapshot(key)!.State.ResumabilityReason);
        Assert.Single(applied.Step.Findings, finding =>
            finding.Kind == SeatVocabulary.FindingSessionIdMismatch && finding.Open);
        Assert.Equal(1, reader.NativeSessionLookupCount);
    }

    [Fact]
    public async Task RetryableWriteFailureRestartsReloadsAndDuplicateReplayDoesNotAddRows()
    {
        var key = new SeatKey(TenantId, SeatId);
        var reader = new FakeReader(Snapshot(key));
        var writer = new FakeWriter(reader);
        await using var rig = await ActorRig.StartAsync(key, reader, writer, [new TestProfile()], new FixedTimeProvider());
        var reloadBarrier = reader.BlockNextLoad();
        writer.FailNextCommit();
        var failed = NewCompletion<SeatEventsCommitted>();
        var eventValue = Event(1);
        rig.Actor.Tell(new SeatEventRequest(Node, EpochId, [eventValue], null, failed));

        var failure = await Assert.ThrowsAsync<SeatCommitFailedException>(async () =>
            await failed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal("SEAT_COMMIT_FAILED", failure.Message);
        Assert.Null(failure.InnerException);
        await reloadBarrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        reloadBarrier.Release.TrySetResult();
        var reloaded = await rig.NextLoadedAsync();
        Assert.Equal(key, reloaded.Key);

        var retry = NewCompletion<SeatEventsCommitted>();
        rig.Actor.Tell(new SeatEventRequest(Node, EpochId, [eventValue], null, retry));
        Assert.Equal(1, (await retry.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).ThroughSeq);
        var replay = NewCompletion<SeatEventsCommitted>();
        rig.Actor.Tell(new SeatEventRequest(Node, EpochId, [eventValue], null, replay));
        await replay.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Single(writer.PersistedInputs);
        Assert.Equal(3, writer.CommitCalls);
        Assert.Equal(4, reader.LoadCount);
    }

    [Fact]
    public async Task PermanentStoreRejectionDoesNotRestartActor()
    {
        var key = new SeatKey(TenantId, SeatId);
        var reader = new FakeReader(Snapshot(key));
        var writer = new FakeWriter(reader);
        await using var rig = await ActorRig.StartAsync(key, reader, writer, [new TestProfile()], new FixedTimeProvider());
        writer.RejectNextCommit();
        var rejected = NewCompletion<SeatInputCommitted>();
        rig.Actor.Tell(new SeatInputRequest(new NodeLinkLost(), rejected));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await rejected.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal("SEAT_COMMIT_REJECTED", exception.Message);
        Assert.Null(exception.InnerException);

        var accepted = NewCompletion<SeatInputCommitted>();
        rig.Actor.Tell(new SeatInputRequest(new NodeLinkLost(), accepted));
        Assert.Equal(8, (await accepted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)).Version);
        Assert.Equal(2, reader.LoadCount);
        Assert.Equal(2, writer.CommitCalls);
    }

    [Fact]
    public async Task EmptyEventBatchReturnsCurrentCursorWithoutWriting()
    {
        var key = new SeatKey(TenantId, SeatId);
        var reader = new FakeReader(Snapshot(key) with { State = Snapshot(key).State with { NextSeq = 4 } });
        var writer = new FakeWriter(reader);
        await using var rig = await ActorRig.StartAsync(key, reader, writer, [new TestProfile()], new FixedTimeProvider());
        var completion = NewCompletion<SeatEventsCommitted>();

        rig.Actor.Tell(new SeatEventRequest(Node, EpochId, [], null, completion));

        var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(3, result.ThroughSeq);
        Assert.Equal(EpochId, result.NodeInstanceId);
        Assert.Equal(0, writer.CommitCalls);
    }

    private static async Task AssertEventRejected(IActorRef actor, SeatEventRequest request, string code)
    {
        actor.Tell(request);
        await AssertCode(request.Completion.Task, code);
    }

    private static async Task AssertCode(Task task, string code)
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(code, exception.Message);
        Assert.Null(exception.InnerException);
    }

    private static TaskCompletionSource<T> NewCompletion<T>() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static SeatEvent Event(ulong sequence, Guid? seatId = null, string? launchId = null,
        ulong sourceSequence = 0, Timestamp? observedAt = null, HarnessEvent? harness = null)
    {
        var result = new SeatEvent
        {
            SeatId = (seatId ?? SeatId).ToString("D"),
            LaunchId = launchId ?? LaunchId.ToString("D"),
            Seq = sequence,
            SourceSeq = sourceSequence
        };
        if (observedAt is not null)
        {
            result.ObservedAt = observedAt;
        }

        if (harness is not null)
        {
            result.Harness = harness;
        }

        return result;
    }

    private static SeatActorSnapshot Snapshot(SeatKey? key = null,
        ResumabilityValue resumability = ResumabilityValue.Resumable, string kind = "agent",
        string? harness = Harness, string? node = Node, bool retired = false)
    {
        var actualKey = key ?? new SeatKey(TenantId, SeatId);
        var state = SeatState.Initial(Now) with
        {
            Session = SessionValue.Present,
            SessionReason = null,
            KnownSession = SessionValue.Present,
            KnownSessionReason = null,
            Activity = ActivityValue.Idle,
            ActivityDetail = null,
            ActivityReason = null,
            KnownActivity = ActivityValue.Idle,
            KnownActivityDetail = null,
            KnownActivityReason = null,
            Resumability = resumability,
            ResumabilityReason = null,
            Overlay = null,
            Desired = SeatDesired.Up,
            Launch = new CurrentLaunch(LaunchId, LaunchMode.Fresh, false, false),
            NativeSessionId = "native-old",
            NodeInstanceId = EpochId,
            NextSeq = 1,
            LastEventAt = Now
        };
        return new SeatActorSnapshot(actualKey, kind, harness, node, retired, 7, SessionId, state,
            new Dictionary<Guid, SeatStoredCommand>());
    }

    private sealed class TestProfile : IHarnessStateProfile
    {
        public string Harness => SeatActorTests.Harness;
        public string NewNativeSessionId() => "native-generated";
        public bool IsValidNativeSessionId(string id) => id.Length > 0;
        public bool IsReadiness(HarnessEventKind kind, IReadOnlyDictionary<string, string> attributes) =>
            kind == HarnessEventKind.SessionStarted && attributes.TryGetValue("source", out var source) &&
            source is "startup" or "clear";
        public bool IsConversationEvidence(HarnessEventKind kind, IReadOnlyDictionary<string, string> attributes) =>
            kind is HarnessEventKind.PromptSubmitted or HarnessEventKind.TurnEnded;
        public bool IsSessionRotation(HarnessEventKind kind, IReadOnlyDictionary<string, string> attributes) =>
            kind == HarnessEventKind.SessionStarted && attributes.TryGetValue("source", out var source) && source == "clear" &&
            attributes.ContainsKey("previous_session_id");
        public bool FreshRelaunchReusesSessionId => true;
        public bool EmitsInputResolved => false;
        public TimeSpan ReadyTimeout => TimeSpan.FromSeconds(15);
        public TimeSpan ConfirmTimeout => TimeSpan.FromSeconds(5);
        public TimeSpan QuietTimeout => TimeSpan.FromMinutes(10);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeReader(params SeatActorSnapshot[] snapshots) : ISeatActorReader
    {
        private readonly ConcurrentDictionary<SeatKey, SeatActorSnapshot> _snapshots =
            new(snapshots.Select(snapshot => new KeyValuePair<SeatKey, SeatActorSnapshot>(snapshot.Key, snapshot)));
        private readonly ConcurrentDictionary<(SeatKey Key, Guid Epoch, long Seq), byte> _committed = new();
        private readonly ConcurrentDictionary<(Guid Tenant, string Harness, string NativeId), SeatNativeSession> _nativeOwners = new();
        private readonly object _loadLock = new();
        private LoadBarrier? _nextLoadBarrier;
        private int _loadCount;
        private int _nativeLookupCount;
        private int _committedQueryCount;

        internal int LoadCount => Volatile.Read(ref _loadCount);
        internal int NativeSessionLookupCount => Volatile.Read(ref _nativeLookupCount);
        internal int CommittedSequenceQueryCount => Volatile.Read(ref _committedQueryCount);

        public async Task<SeatActorSnapshot?> LoadAsync(SeatKey key, CancellationToken ct)
        {
            Interlocked.Increment(ref _loadCount);
            LoadBarrier? barrier;
            lock (_loadLock)
            {
                barrier = _nextLoadBarrier;
                _nextLoadBarrier = null;
            }

            if (barrier is not null)
            {
                barrier.Entered.TrySetResult();
                await barrier.Release.Task.WaitAsync(ct);
            }

            return _snapshots.TryGetValue(key, out var snapshot) ? snapshot : null;
        }

        public Task<IReadOnlyList<SeatKey>> GetStartupSeatsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SeatKey>>(_snapshots.Keys.ToArray());

        public Task<SeatNativeSession?> FindNativeSessionAsync(Guid tenantId, string harness,
            string nativeSessionId, CancellationToken ct)
        {
            Interlocked.Increment(ref _nativeLookupCount);
            _nativeOwners.TryGetValue((tenantId, harness, nativeSessionId), out var owner);
            return Task.FromResult(owner);
        }

        public Task<IReadOnlySet<long>> GetCommittedSequencesAsync(SeatKey key, Guid nodeInstanceId,
            IReadOnlyList<long> sequences, CancellationToken ct)
        {
            Interlocked.Increment(ref _committedQueryCount);
            IReadOnlySet<long> found = sequences.Where(sequence =>
                _committed.ContainsKey((key, nodeInstanceId, sequence))).ToHashSet();
            return Task.FromResult(found);
        }

        internal void Save(SeatActorSnapshot snapshot) => _snapshots[snapshot.Key] = snapshot;
        internal SeatActorSnapshot? GetSnapshot(SeatKey key) => _snapshots.TryGetValue(key, out var value) ? value : null;
        internal void AddCommitted(SeatKey key, Guid epoch, long sequence) => _committed.TryAdd((key, epoch, sequence), 0);
        internal void SetNativeOwner(Guid tenant, string harness, string nativeId, Guid seat, Guid session) =>
            _nativeOwners[(tenant, harness, nativeId)] = new SeatNativeSession(seat, session);

        internal LoadBarrier BlockNextLoad()
        {
            var barrier = new LoadBarrier();
            lock (_loadLock)
            {
                _nextLoadBarrier = barrier;
            }

            return barrier;
        }
    }

    private sealed class FakeWriter(FakeReader reader) : ISeatActorWriter
    {
        private readonly object _lock = new();
        private CommitBarrier? _commitBarrier;
        private bool _failNext;
        private bool _rejectNext;
        private int _commitCalls;
        private readonly List<SeatAppliedInput> _persistedInputs = [];

        internal int CommitCalls => Volatile.Read(ref _commitCalls);
        internal SeatAppliedInput[] PersistedInputs
        {
            get
            {
                lock (_lock)
                {
                    return _persistedInputs.ToArray();
                }
            }
        }

        public async Task<SeatStoreReceipt> CommitAsync(SeatActorSnapshot before,
            IReadOnlyList<SeatAppliedInput> inputs, CancellationToken ct)
        {
            Interlocked.Increment(ref _commitCalls);
            CommitBarrier? barrier;
            bool fail;
            bool reject;
            lock (_lock)
            {
                barrier = _commitBarrier;
                if (barrier is not null && barrier.RegisterCall())
                {
                    _commitBarrier = null;
                }

                fail = _failNext;
                reject = _rejectNext;
                _failNext = false;
                _rejectNext = false;
            }

            if (barrier is not null)
            {
                await barrier.WaitAsync(ct);
            }

            if (fail)
            {
                throw new IOException("private storage details");
            }

            if (reject)
            {
                throw new SeatStoreRejectedException();
            }

            lock (_lock)
            {
                var persisted = inputs.Where(static input => input.Event is null ||
                    input.Step.Disposition != EventDisposition.Duplicate).ToArray();
                _persistedInputs.AddRange(persisted);
                foreach (var input in persisted)
                {
                    if (input.Input is EventReceived received)
                    {
                        reader.AddCommitted(before.Key, received.NodeInstanceId, received.Seq);
                    }
                }

                var state = persisted.Length == 0 ? before.State : persisted[^1].Step.State;
                var version = persisted.Length == 0 ? before.Version : before.Version + 1;
                var updated = before with { Version = version, State = state };
                reader.Save(updated);
                return new SeatStoreReceipt(version, updated.CurrentSessionId);
            }
        }

        public Task RecordActorStoppedAsync(SeatKey key, DateTimeOffset at, CancellationToken ct) => Task.CompletedTask;

        internal CommitBarrier BlockNextCommit() => BlockCommits(1);

        internal CommitBarrier BlockCommits(int calls)
        {
            var barrier = new CommitBarrier(calls);
            lock (_lock)
            {
                _commitBarrier = barrier;
            }

            return barrier;
        }

        internal void FailNextCommit()
        {
            lock (_lock)
            {
                _failNext = true;
            }
        }

        internal void RejectNextCommit()
        {
            lock (_lock)
            {
                _rejectNext = true;
            }
        }
    }

    private sealed class LoadBarrier
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class CommitBarrier(int expectedCalls)
    {
        private int _calls;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal bool RegisterCall()
        {
            if (Interlocked.Increment(ref _calls) >= expectedCalls)
            {
                Entered.TrySetResult();
                return true;
            }

            return false;
        }

        internal async Task WaitAsync(CancellationToken ct)
        {
            if (Volatile.Read(ref _calls) >= expectedCalls)
            {
                Entered.TrySetResult();
            }

            await Release.Task.WaitAsync(ct);
        }
    }

    private sealed class ActorRig : IAsyncDisposable
    {
        private readonly ActorSystem _system;
        private readonly SeatTestKit _testKit;
        private readonly TestProbe _probe;
        private readonly IActorRef _host;

        private ActorRig(ActorSystem system, SeatTestKit testKit, TestProbe probe, IActorRef host, IActorRef actor)
        {
            _system = system;
            _testKit = testKit;
            _probe = probe;
            _host = host;
            Actor = actor;
        }

        internal IActorRef Actor { get; }

        internal static async Task<ActorRig> StartAsync(SeatKey key, FakeReader reader, FakeWriter writer,
            IReadOnlyList<IHarnessStateProfile> profiles, TimeProvider timeProvider)
        {
            var system = ActorSystem.Create($"seat-actor-tests-{Guid.NewGuid():N}");
            var testKit = new SeatTestKit(system);
            var probe = testKit.CreateTestProbe();
            var host = system.ActorOf(Props.Create(() => new SeatTestHost(probe.Ref)), "seat-test-host");
            var spawn = new TaskCompletionSource<IActorRef>(TaskCreationOptions.RunContinuationsAsynchronously);
            host.Tell(new SpawnSeat(key, reader, writer, profiles, timeProvider, spawn));
            var actor = await spawn.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var rig = new ActorRig(system, testKit, probe, host, actor);
            var loaded = await rig.NextLoadedAsync();
            Assert.Equal(key, loaded.Key);
            return rig;
        }

        internal async Task<IActorRef> SpawnAsync(SeatKey key, FakeReader reader, FakeWriter writer,
            IReadOnlyList<IHarnessStateProfile> profiles, TimeProvider timeProvider)
        {
            var spawn = new TaskCompletionSource<IActorRef>(TaskCreationOptions.RunContinuationsAsynchronously);
            _host.Tell(new SpawnSeat(key, reader, writer, profiles, timeProvider, spawn));
            var actor = await spawn.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var loaded = await NextLoadedAsync();
            Assert.Equal(key, loaded.Key);
            return actor;
        }

        internal Task<SeatChildLoaded> NextLoadedAsync() =>
            Task.FromResult(_probe.ExpectMsg<SeatChildLoaded>(cancellationToken: TestContext.Current.CancellationToken));

        public async ValueTask DisposeAsync()
        {
            _ = _testKit;
            await _system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));
            await _system.WhenTerminated.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private sealed record SpawnSeat(SeatKey Key, ISeatActorReader Reader, ISeatActorWriter Writer,
        IReadOnlyList<IHarnessStateProfile> Profiles, TimeProvider TimeProvider,
        TaskCompletionSource<IActorRef> Completion);

    private sealed class SeatTestHost : ReceiveActor
    {
        private readonly IActorRef _observer;

        public SeatTestHost(IActorRef observer)
        {
            _observer = observer;
            Receive<SpawnSeat>(request =>
            {
                var child = Context.ActorOf(Props.Create(() => new SeatActor(request.Key, request.Reader,
                    request.Writer, request.Profiles, request.TimeProvider)), $"seat-{Guid.NewGuid():N}");
                request.Completion.TrySetResult(child);
            });
            Receive<SeatChildLoaded>(notice =>
            {
                _observer.Tell(notice);
                notice.Ready.TrySetResult();
            });
        }

        protected override SupervisorStrategy SupervisorStrategy() =>
            new OneForOneStrategy(10, TimeSpan.FromMinutes(1), static _ => Directive.Restart);
    }

    private sealed class SeatTestKit(ActorSystem system) : TestKitBase(new XunitV3TestKitAssertions(), system, "seat-actor-tests");
}
