using System.Diagnostics;
using Aiakos.Contracts.Node.V1;
using Aiakos.Node.Link;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aiakos.Node.Tests.Link;

public sealed class NodeEventBufferTests
{
    private const string SeatA = "11111111-1111-1111-1111-111111111111";
    private const string SeatB = "22222222-2222-2222-2222-222222222222";
    private const string LaunchA = "33333333-3333-3333-3333-333333333333";
    private const string LaunchB = "44444444-4444-4444-4444-444444444444";
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 0, 0, 0, TimeSpan.Zero);
    private static readonly byte[] UnknownField = [0xA2, 0x06, 0x03, 0x78, 0x79, 0x7a];

    [Fact]
    public void AssignsArrivalSequenceClonesEventsAndReportsInventoryAndBytes()
    {
        var time = new ManualTimeProvider(Now);
        using var owned = new NodeEventBuffer(timeProvider: time);
        var initialInventory = Inventory(SeatA);
        owned.RegisterSeat(initialInventory);
        initialInventory.LaunchId = LaunchA;

        var first = Event(SeatA, 9, LaunchA, HarnessEventKind.Other, [3, 1, 4]);
        first.Harness!.Attributes.Add("payload", "before");
        first.Harness.Raw = ByteString.CopyFrom([8, 9]);
        owned.Enqueue(first);
        var second = Event(SeatA, 1, LaunchA, HarnessEventKind.Other, [2]);
        second.ProcessExited = new ProcessExited { ExitCode = 0 };
        owned.Enqueue(second);

        first.Harness.Attributes["payload"] = "after";
        first.Harness.Raw = ByteString.CopyFrom([0]);
        var snapshot = owned.Snapshot(SeatA);
        Assert.Equal(new ulong[] { 1, 2 }, snapshot.Select(request => request.SeatEvent.Seq));
        Assert.Equal((ulong)9, snapshot[0].SeatEvent.SourceSeq);
        Assert.Equal("before", snapshot[0].SeatEvent.Harness.Attributes["payload"]);
        Assert.Equal(new byte[] { 8, 9 }, snapshot[0].SeatEvent.Harness.Raw.ToByteArray());
        Assert.False(snapshot[0].SeatEvent.ProcessExited?.HasExitCode ?? false);
        Assert.True(snapshot[1].SeatEvent.ProcessExited!.HasExitCode);
        Assert.Equal(0, snapshot[1].SeatEvent.ProcessExited.ExitCode);
        Assert.Equal(snapshot.Sum(request => (long)request.CalculateSize()), owned.BufferedBytes);
        Assert.Equal(2, owned.BufferedCount);

        snapshot[0].SeatEvent.Harness.Attributes["payload"] = "snapshot mutation";
        Assert.Equal("before", owned.Snapshot(SeatA)[0].SeatEvent.Harness.Attributes["payload"]);
        var inventory = Assert.Single(owned.Inventory());
        Assert.Equal((ulong)1, inventory.FirstBufferedSeq);
        Assert.Equal((ulong)2, inventory.LastSeq);
        Assert.Equal(string.Empty, inventory.LaunchId);

        var replacement = Inventory(SeatA, LaunchB, SessionLifecycle.Running);
        replacement.NativeSessionId = "native-id";
        owned.RegisterSeat(replacement);
        var updated = Assert.Single(owned.Inventory());
        Assert.Equal((ulong)2, updated.LastSeq);
        Assert.Equal((ulong)1, updated.FirstBufferedSeq);
        Assert.Equal(LaunchB, updated.LaunchId);
        Assert.Equal("native-id", updated.NativeSessionId);
        Assert.Equal(SessionLifecycle.Running, updated.Lifecycle);
    }

    [Fact]
    public void RejectsInvalidEventsWithoutMutatingSequenceOrBuffer()
    {
        using var buffer = new NodeEventBuffer();
        buffer.RegisterSeat(Inventory(SeatA));
        var invalid = new[]
        {
            Event("bad-seat", 0, "", HarnessEventKind.Other),
            Event(SeatB, 0, "", HarnessEventKind.Other),
            Event(SeatA, (ulong)long.MaxValue + 1, "", HarnessEventKind.Other),
            Event(SeatA, 0, "not-a-uuid", HarnessEventKind.Other),
            new SeatEvent { SeatId = SeatA, ObservedAt = new Timestamp { Seconds = 253402300800 } },
            new SeatEvent { SeatId = SeatA, ObservedAt = new Timestamp { Seconds = 1, Nanos = -1 } },
        };

        foreach (var value in invalid)
        {
            var error = Assert.Throws<ArgumentException>(() => buffer.Enqueue(value));
            Assert.Equal("Invalid seat event.", error.Message);
        }

        Assert.Equal(0, buffer.BufferedCount);
        Assert.Equal((ulong)0, Assert.Single(buffer.Inventory()).LastSeq);
        Assert.Throws<ArgumentException>(() => buffer.RegisterSeat(Inventory("not-a-uuid")));
    }

    [Fact]
    public void PreservesUnknownProtobufFieldsAndOnlyCapturesW3cTrace()
    {
        using var buffer = new NodeEventBuffer();
        buffer.RegisterSeat(Inventory(SeatA));
        var source = Event(SeatA, 2, "", HarnessEventKind.Other, [5]);
        var encoded = source.ToByteArray().Concat(UnknownField).ToArray();
        var withUnknown = SeatEvent.Parser.ParseFrom(encoded);

        buffer.Enqueue(withUnknown);

        var result = Assert.Single(buffer.Snapshot(SeatA)).SeatEvent;
        Assert.Equal((ulong)2, result.SourceSeq);
        Assert.True(result.ToByteArray().AsSpan().IndexOf(UnknownField) >= 0);
        var noActivity = Assert.Single(buffer.Snapshot(SeatA));
        Assert.NotNull(noActivity.Trace);
        Assert.Equal(string.Empty, noActivity.Trace.Traceparent);

        using var activity = new Activity("buffer-test").SetIdFormat(ActivityIdFormat.W3C).Start();
        buffer.Enqueue(Event(SeatA, 3, "", HarnessEventKind.Other));
        var traced = buffer.Snapshot(SeatA)[1].Trace;
        Assert.Equal(activity.Id, traced.Traceparent);
        Assert.Equal(activity.TraceStateString ?? string.Empty, traced.Tracestate);

        var opaqueBody = SeatEvent.Parser.ParseFrom(new SeatEvent
        {
            SeatId = SeatA,
            ObservedAt = Timestamp.FromDateTimeOffset(Now),
        }.ToByteArray().Concat(UnknownField).ToArray());
        buffer.Enqueue(opaqueBody);
        Assert.Equal(SeatEvent.BodyOneofCase.None, buffer.Snapshot(SeatA)[2].SeatEvent.BodyCase);
    }

    [Fact]
    public void ExhaustsAtLongMaxValueMinusOneWithoutMutation()
    {
        using var buffer = new NodeEventBuffer(null, null, (ulong)long.MaxValue - 1);
        buffer.RegisterSeat(Inventory(SeatA));
        var error = Assert.Throws<InvalidOperationException>(() => buffer.Enqueue(Event(SeatA, 0, "", HarnessEventKind.Other)));
        Assert.Equal("Event sequence exhausted.", error.Message);
        Assert.Equal(0, buffer.BufferedCount);
        Assert.Empty(buffer.Snapshot(SeatA));
        Assert.Equal((ulong)long.MaxValue - 1, Assert.Single(buffer.Inventory()).LastSeq);
    }

    [Fact]
    public void AcknowledgesCumulativelyAndReplayValidationIsAtomic()
    {
        using var buffer = new NodeEventBuffer();
        buffer.RegisterSeat(Inventory(SeatA));
        buffer.RegisterSeat(Inventory(SeatB));
        buffer.Enqueue(Event(SeatA, 0, "", HarnessEventKind.Other));
        buffer.Enqueue(Event(SeatA, 0, "", HarnessEventKind.Other));
        buffer.Enqueue(Event(SeatB, 0, "", HarnessEventKind.Other));

        buffer.Acknowledge(new EventAck { SeatId = SeatA, ThroughSeq = 1 });
        buffer.Acknowledge(new EventAck { SeatId = SeatA, ThroughSeq = 1 });
        Assert.Equal(new ulong[] { 2 }, buffer.Snapshot(SeatA).Select(request => request.SeatEvent.Seq));
        Assert.Equal((ulong)2, Assert.Single(buffer.Inventory(), value => value.SeatId == SeatA).LastSeq);

        var invalidAck = Assert.Throws<ArgumentException>(() => buffer.Acknowledge(
            new EventAck { SeatId = SeatB, ThroughSeq = 2 }));
        Assert.Equal("Invalid event acknowledgement.", invalidAck.Message);
        Assert.Throws<ArgumentException>(() => buffer.Acknowledge(new EventAck { SeatId = SeatA }));
        Assert.Throws<ArgumentException>(() => buffer.Acknowledge(new EventAck { SeatId = SeatB, ThroughSeq = 0 }));
        Assert.Throws<ArgumentException>(() => buffer.Acknowledge(new EventAck
        {
            SeatId = "55555555-5555-5555-5555-555555555555",
            ThroughSeq = 1,
        }));

        var invalidReplay = Assert.Throws<ArgumentException>(() => buffer.ApplyReplay(
        [
            new ReplayFrom { SeatId = SeatA, NextSeq = 2 },
            new ReplayFrom { SeatId = SeatB, NextSeq = 3 },
        ]));
        Assert.Equal("Invalid event replay checkpoint.", invalidReplay.Message);
        Assert.Equal(new ulong[] { 2 }, buffer.Snapshot(SeatA).Select(request => request.SeatEvent.Seq));
        Assert.Single(buffer.Snapshot(SeatB));

        buffer.ApplyReplay([new ReplayFrom { SeatId = SeatA, NextSeq = 2 }]);
        Assert.Single(buffer.Snapshot(SeatA));
        Assert.Single(buffer.Snapshot(SeatB));
        buffer.Acknowledge(new EventAck { SeatId = SeatA, ThroughSeq = 2 });
        buffer.Acknowledge(new EventAck { SeatId = SeatB, ThroughSeq = 1 });
        Assert.Equal(0, buffer.BufferedCount);
        Assert.All(buffer.Inventory(), inventory =>
        {
            Assert.Equal((ulong)0, inventory.FirstBufferedSeq);
            Assert.True(inventory.LastSeq > 0);
        });
    }

    [Fact]
    public async Task ReplaysByArrivalOrderAndKeepsEventsAcrossCancellationAndReconnect()
    {
        using var buffer = new NodeEventBuffer();
        buffer.RegisterSeat(Inventory(SeatA));
        buffer.RegisterSeat(Inventory(SeatB));
        buffer.Enqueue(Event(SeatA, 0, "", HarnessEventKind.Other));
        buffer.Enqueue(Event(SeatB, 0, "", HarnessEventKind.Other));
        buffer.Enqueue(Event(SeatA, 0, "", HarnessEventKind.Other));
        var reader = buffer.ReadAllAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        var waiting = reader.MoveNextAsync().AsTask();
        Assert.False(waiting.IsCompleted);

        buffer.ApplyReplay([new ReplayFrom { SeatId = SeatA, NextSeq = 1 }, new ReplayFrom { SeatId = SeatB, NextSeq = 1 }]);
        Assert.True(await waiting);
        Assert.Equal((SeatA, (ulong)1), (reader.Current.SeatEvent.SeatId, reader.Current.SeatEvent.Seq));
        buffer.Enqueue(Event(SeatA, 0, "", HarnessEventKind.Other));
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal((SeatB, (ulong)1), (reader.Current.SeatEvent.SeatId, reader.Current.SeatEvent.Seq));
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal((SeatA, (ulong)2), (reader.Current.SeatEvent.SeatId, reader.Current.SeatEvent.Seq));
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal((SeatA, (ulong)3), (reader.Current.SeatEvent.SeatId, reader.Current.SeatEvent.Seq));
        await reader.DisposeAsync();

        using var canceled = new CancellationTokenSource();
        var canceledReader = buffer.ReadAllAsync(canceled.Token).GetAsyncEnumerator(canceled.Token);
        var pending = canceledReader.MoveNextAsync().AsTask();
        Assert.False(pending.IsCompleted);
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await canceledReader.DisposeAsync();
        Assert.Equal(4, buffer.BufferedCount);

        await using var reconnected = buffer.ReadAllAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        var nextConnection = reconnected.MoveNextAsync().AsTask();
        Assert.False(nextConnection.IsCompleted);
        buffer.ApplyReplay([new ReplayFrom { SeatId = SeatA, NextSeq = 1 }, new ReplayFrom { SeatId = SeatB, NextSeq = 1 }]);
        Assert.True(await nextConnection);
        Assert.Equal((SeatA, (ulong)1), (reconnected.Current.SeatEvent.SeatId, reconnected.Current.SeatEvent.Seq));
    }

    [Fact]
    public async Task ReplayOmitsUnlistedSeatsUntilTheyReceiveACheckpoint()
    {
        using var buffer = new NodeEventBuffer();
        buffer.RegisterSeat(Inventory(SeatA));
        buffer.RegisterSeat(Inventory(SeatB));
        buffer.Enqueue(Event(SeatA, 0, "", HarnessEventKind.Other));
        buffer.Enqueue(Event(SeatB, 0, "", HarnessEventKind.Other));
        buffer.ApplyReplay([new ReplayFrom { SeatId = SeatA, NextSeq = 1 }]);
        await using var reader = buffer.ReadAllAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);

        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(SeatA, reader.Current.SeatEvent.SeatId);
        var waiting = reader.MoveNextAsync().AsTask();
        Assert.False(waiting.IsCompleted);
        buffer.ApplyReplay([new ReplayFrom { SeatId = SeatB, NextSeq = 1 }]);
        Assert.True(await waiting);
        Assert.Equal(SeatB, reader.Current.SeatEvent.SeatId);
    }

    [Fact]
    public async Task DisposeWakesAndClosesTheActiveReader()
    {
        var buffer = new NodeEventBuffer();
        buffer.RegisterSeat(Inventory(SeatA));
        buffer.ApplyReplay([new ReplayFrom { SeatId = SeatA, NextSeq = 1 }]);
        var reader = buffer.ReadAllAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        var pending = reader.MoveNextAsync().AsTask();
        Assert.False(pending.IsCompleted);

        buffer.Dispose();

        Assert.False(await pending);
        await reader.DisposeAsync();
    }

    [Fact]
    public async Task RecoveredRegistrationQueuesSingleRestartGapBeforeNewEvents()
    {
        var time = new ManualTimeProvider(Now);
        using var buffer = new NodeEventBuffer(timeProvider: time);
        buffer.RegisterSeat(Inventory(SeatA), recovered: true);
        buffer.RegisterSeat(Inventory(SeatA, LaunchA, SessionLifecycle.Running), recovered: true);
        buffer.Enqueue(Event(SeatA, 0, LaunchA, HarnessEventKind.Other));
        buffer.ApplyReplay([new ReplayFrom { SeatId = SeatA, NextSeq = 1 }]);

        var events = await ReadExactlyAsync(buffer, 2);

        Assert.Equal(new ulong[] { 1, 2 }, events.Select(request => request.SeatEvent.Seq));
        Assert.Equal(SeatEvent.BodyOneofCase.Gap, events[0].SeatEvent.BodyCase);
        Assert.Equal(GapReason.NodeRestarted, events[0].SeatEvent.Gap.Reason);
        Assert.Equal((ulong)0, events[0].SeatEvent.Gap.DroppedEvents);
        Assert.Equal(Now, events[0].SeatEvent.ObservedAt.ToDateTimeOffset());
        Assert.Equal(string.Empty, events[0].SeatEvent.LaunchId);
        Assert.Equal(SeatEvent.BodyOneofCase.Harness, events[1].SeatEvent.BodyCase);
    }

    private static async Task<IReadOnlyList<ConnectRequest>> ReadExactlyAsync(NodeEventBuffer buffer, int count)
    {
        var values = new List<ConnectRequest>();
        await using var enumerator = buffer.ReadAllAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator();
        for (var index = 0; index < count; index++)
        {
            Assert.True(await enumerator.MoveNextAsync());
            values.Add(enumerator.Current);
        }
        return values;
    }

    private static SeatInventory Inventory(string seatId, string launchId = "", SessionLifecycle lifecycle = SessionLifecycle.Unspecified) =>
        new() { SeatId = seatId, LaunchId = launchId, Lifecycle = lifecycle };

    private static SeatEvent Event(string seatId, ulong sourceSeq, string launchId,
        HarnessEventKind kind, byte[]? raw = null) => new()
    {
        SeatId = seatId,
        LaunchId = launchId,
        Seq = 999,
        SourceSeq = sourceSeq,
        ObservedAt = Timestamp.FromDateTimeOffset(Now),
        Harness = new HarnessEvent { Kind = kind, Raw = ByteString.CopyFrom(raw ?? []) },
    };

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
