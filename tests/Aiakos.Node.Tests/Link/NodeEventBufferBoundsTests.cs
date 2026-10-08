using System.Diagnostics;
using Aiakos.Contracts.Node.V1;
using Aiakos.Node.Link;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aiakos.Node.Tests.Link;

public sealed class NodeEventBufferBoundsTests
{
    private const string A = "11111111-1111-1111-1111-111111111111";
    private const string B = "22222222-2222-2222-2222-222222222222";
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NormalizesRawAndBodyStringsWithoutMutatingOrLosingEvidence()
    {
        using var buffer = Buffer();
        foreach (var length in new[] { 262144, 262145 })
        {
            var value = Event(A);
            value.Harness.Raw = ByteString.CopyFrom(Enumerable.Repeat((byte)255, length).ToArray());
            value.Harness.RawSize = 17;
            value.Harness.RawContentType = "binary\0\ufffe\ud800";
            value.Harness.NativeSessionId = "native\udc00";
            value.Harness.Attributes.Add("key\ud800", "NUL\0\ufffe\uffff😀\ud800");
            value.Harness.Usage = new() { ModelId = "model\ud800", CostUsd = 1.25 };
            var unknown = new byte[] { 0xa0, 0x06, 0x07 };
            value = SeatEvent.Parser.ParseFrom(value.ToByteArray().Concat(unknown).ToArray());
            // Reintroduce fabricated surrogates after protobuf roundtrip.
            value.Harness.NativeName = "name\ud800";
            buffer.Enqueue(value);
            var stored = buffer.Snapshot(A)[^1].SeatEvent;
            Assert.Equal(262144, stored.Harness.Raw.Length);
            Assert.All(stored.Harness.Raw, item => Assert.Equal((byte)255, item));
            Assert.Equal(length > 262144, stored.Harness.RawTruncated);
            Assert.Equal(length > 262144 ? (uint)length : 17u, stored.Harness.RawSize);
            Assert.Equal("name\ufffd", stored.Harness.NativeName);
            Assert.Equal("name\ud800", value.Harness.NativeName);
            Assert.Equal("native\ufffd", stored.Harness.NativeSessionId);
            Assert.Equal("binary\0\ufffe\ufffd", stored.Harness.RawContentType);
            Assert.Equal("NUL\0\ufffe\uffff😀\ufffd", stored.Harness.Attributes["key\ufffd"]);
            Assert.Equal("model\ufffd", stored.Harness.Usage.ModelId);
            Assert.Equal(1.25, stored.Harness.Usage.CostUsd);
            Assert.True(stored.ToByteArray().AsSpan().IndexOf(unknown) >= 0);
            Assert.Equal(length, value.Harness.Raw.Length);
        }
        var capture = Event(A);
        capture.CommandResult = new() { Error = new() { Message = "error\ud800" },
            Capture = new() { Text = "capture\0\ufffe\udc00", CapturedAt = new() { Nanos = -1 } } };
        capture.CommandResult.Error.Metadata.Add("map\ud800", "value\udc00");
        buffer.Enqueue(capture);
        var result = buffer.Snapshot(A)[^1].SeatEvent.CommandResult;
        Assert.Equal("capture\0\ufffe\ufffd", result.Capture.Text);
        Assert.Equal(-1, result.Capture.CapturedAt.Nanos);
        Assert.Equal("error\ufffd", result.Error.Message);
        Assert.Equal("value\ufffd", result.Error.Metadata["map\ufffd"]);
    }

    [Fact]
    public void RejectsUtf8AttributeAndEnvelopeLimitsBeforeAllocationWithFixedOutcome()
    {
        using var buffer = Buffer();
        var valid = Event(A);
        valid.Harness.Attributes.Add("key", new string('é', 512));
        buffer.Enqueue(valid);
        var invalid = Event(A);
        invalid.Harness.Attributes.Add("key", new string('é', 513));
        var oversized = Event(A);
        oversized.CommandResult = new() { Capture = new() { Text = new string('x', 4194304) } };
        foreach (var value in new[] { invalid, oversized })
        {
            var exception = Assert.Throws<ArgumentException>(() => buffer.Enqueue(value));
            Assert.Equal("Invalid seat event.", exception.Message);
            Assert.Null(exception.InnerException);
            Assert.Equal(1, buffer.BufferedCount);
            Assert.Equal(1ul, buffer.Inventory().Single(seat => seat.SeatId == A).LastSeq);
        }
        buffer.Enqueue(Event(A));
        Assert.Equal(new ulong[] { 1, 2 }, buffer.Snapshot(A).Select(item => item.SeatEvent.Seq));
    }

    [Fact]
    public void MeasuresAssignedEnvelopeAndTraceBeforeMutation()
    {
        using var buffer = Buffer();
        var value = Event(A);
        value.CommandResult = new() { Capture = new() { Text = new string('x', 4194100) } };
        value.Seq = 1;
        var measured = new ConnectRequest { SeatEvent = value, Trace = new() };
        value.CommandResult.Capture.Text += new string('x', 4194304 - measured.CalculateSize());
        Assert.Equal(4194304, measured.CalculateSize());
        value.Seq = ulong.MaxValue;
        buffer.Enqueue(value);
        Assert.Equal(4194304, Assert.Single(buffer.Snapshot(A)).CalculateSize());

        using var activity = new Activity("size-test").SetIdFormat(ActivityIdFormat.W3C).Start();
        var error = Assert.Throws<ArgumentException>(() => buffer.Enqueue(value));
        Assert.Equal("Invalid seat event.", error.Message);
        Assert.Null(error.InnerException);
        Assert.Equal(1ul, buffer.Inventory().Single(seat => seat.SeatId == A).LastSeq);
        Assert.Single(buffer.Snapshot(A));
    }

    [Fact]
    public void AttributeKeysAreNotValueLimitedAndOptionalEvidenceStaysAbsent()
    {
        using var buffer = Buffer();
        var value = Event(A);
        value.Harness.Attributes.Add(new string('x', 1025), "allowed");
        value.Harness.RawTruncated = true;
        value.Harness.RawSize = uint.MaxValue;
        buffer.Enqueue(value);
        var stored = Assert.Single(buffer.Snapshot(A)).SeatEvent.Harness;
        Assert.Equal("allowed", stored.Attributes[new string('x', 1025)]);
        Assert.True(stored.RawTruncated);
        Assert.Equal(uint.MaxValue, stored.RawSize);
        Assert.Null(stored.Usage);
    }

    [Theory]
    [InlineData(1, 2, 1024)]
    [InlineData(2, 1, 1024)]
    [InlineData(2, 2, 1023)]
    public void InvalidOptionsHaveFixedOutcome(int perSeat, int total, long bytes)
    {
        var exception = Assert.Throws<ArgumentException>(() => new NodeEventBuffer(new()
            { MaxEventsPerSeat = perSeat, MaxEvents = total, MaxBytes = bytes }));
        Assert.Equal("Invalid event buffer options.", exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void PerSeatEvictionLeavesOtherSeatUntouchedAndSnapshotsOptions()
    {
        var options = new NodeEventBufferOptions { MaxEventsPerSeat = 2, MaxEvents = 10 };
        using var buffer = Buffer(options);
        options.MaxEventsPerSeat = 100;
        buffer.Enqueue(Event(B));
        for (var i = 0; i < 3; i++) buffer.Enqueue(Event(A));
        Assert.Equal(new ulong[] { 2, 3 }, buffer.Snapshot(A).Select(item => item.SeatEvent.Seq));
        Assert.Equal(1ul, Assert.Single(buffer.Snapshot(B)).SeatEvent.Seq);
        Ack(buffer, A, 2);
        var retained = buffer.Snapshot(A);
        Assert.Equal(new ulong[] { 3, 4 }, retained.Select(item => item.SeatEvent.Seq));
        AssertOverflow(retained[1], 1);
        Assert.Equal(Now, retained[1].SeatEvent.ObservedAt.ToDateTimeOffset());
        Assert.Equal("", retained[1].SeatEvent.LaunchId);
        Assert.Equal(1ul, Assert.Single(buffer.Snapshot(B)).SeatEvent.Seq);
    }

    [Fact]
    public void CoalescingCountsTelemetryOnlyAndPreservesNewestAndOtherKinds()
    {
        using var buffer = Buffer(new() { MaxEventsPerSeat = 3, MaxEvents = 20 });
        buffer.Enqueue(Event(A));
        for (var i = 0; i < 3; i++) buffer.Enqueue(Event(A, HarnessEventKind.Telemetry));
        var retained = buffer.Snapshot(A);
        Assert.Equal(new ulong[] { 1, 4, 5 }, retained.Select(item => item.SeatEvent.Seq));
        Assert.Equal(HarnessEventKind.Other, retained[0].SeatEvent.Harness.Kind);
        Assert.Equal(HarnessEventKind.Telemetry, retained[1].SeatEvent.Harness.Kind);
        AssertOverflow(retained[2], 2);
        // Losing an overflow gap carries its represented observations, not a count of one.
        for (var i = 0; i < 3; i++) buffer.Enqueue(Event(A, HarnessEventKind.SessionEnded));
        Assert.Equal(new ulong[] { 6, 7, 8 }, buffer.Snapshot(A).Select(item => item.SeatEvent.Seq));
        Assert.All(buffer.Snapshot(A), item => Assert.Equal(HarnessEventKind.SessionEnded, item.SeatEvent.Harness.Kind));
        Ack(buffer, A, 8);
        AssertOverflow(Assert.Single(buffer.Snapshot(A)), 4);
        Assert.Equal(9ul, Assert.Single(buffer.Snapshot(A)).SeatEvent.Seq);
    }

    [Fact]
    public async Task ProviderRateHoldsOnlyItsSeatAndCancellationRetainsReplayEvidence()
    {
        var time = new ManualTimeProvider();
        using var buffer = Buffer(timeProvider: time);
        buffer.Enqueue(Event(A, HarnessEventKind.Telemetry));
        buffer.Enqueue(Event(A, HarnessEventKind.Telemetry));
        buffer.Enqueue(Event(A));
        buffer.Enqueue(Event(B));
        Assert.Equal(0, time.ActiveTimers);
        buffer.ApplyReplay([new() { SeatId = A, NextSeq = 1 }, new() { SeatId = B, NextSeq = 1 }]);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var reader = buffer.ReadAllAsync(cancellation.Token).GetAsyncEnumerator(cancellation.Token);
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal((A, 1ul), (reader.Current.SeatEvent.SeatId, reader.Current.SeatEvent.Seq));
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(B, reader.Current.SeatEvent.SeatId);
        var pending = reader.MoveNextAsync().AsTask();
        Assert.False(pending.IsCompleted);
        Assert.Equal(1, time.ActiveTimers);
        time.Advance(TimeSpan.FromMilliseconds(999));
        Assert.False(pending.IsCompleted);
        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(await pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal((A, 2ul), (reader.Current.SeatEvent.SeatId, reader.Current.SeatEvent.Seq));
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(3ul, reader.Current.SeatEvent.Seq);
        buffer.Enqueue(Event(A, HarnessEventKind.Telemetry));
        pending = reader.MoveNextAsync().AsTask();
        Assert.False(pending.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await reader.DisposeAsync();
        Assert.Equal(0, time.ActiveTimers);
        Assert.Equal(5, buffer.BufferedCount);
        buffer.ApplyReplay([new() { SeatId = A, NextSeq = 2 }]);
        await using var reconnected = buffer.ReadAllAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await reconnected.MoveNextAsync());
        Assert.Equal(2ul, reconnected.Current.SeatEvent.Seq);
        Assert.Equal(0, time.ActiveTimers);
    }

    [Fact]
    public async Task HeldTelemetryWakesForNewOtherSeatAndDispose()
    {
        var time = new ManualTimeProvider();
        using var buffer = Buffer(timeProvider: time);
        buffer.Enqueue(Event(A, HarnessEventKind.Telemetry));
        buffer.Enqueue(Event(A, HarnessEventKind.Telemetry));
        buffer.ApplyReplay([new() { SeatId = A, NextSeq = 1 }, new() { SeatId = B, NextSeq = 1 }]);
        await using var reader = buffer.ReadAllAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await reader.MoveNextAsync());
        var pending = reader.MoveNextAsync().AsTask();
        Assert.False(pending.IsCompleted);
        buffer.Enqueue(Event(B));
        Assert.True(await pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(B, reader.Current.SeatEvent.SeatId);
        pending = reader.MoveNextAsync().AsTask();
        buffer.Dispose();
        Assert.False(await pending.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(0, time.ActiveTimers);
    }

    [Fact]
    public void PendingOverflowBeyondUlongIsEmittedWithoutLoss()
    {
        using var buffer = Buffer(new() { MaxEventsPerSeat = 2, MaxEvents = 2 });
        for (var i = 0; i < 2; i++)
        {
            var gap = Event(A);
            gap.Gap = new() { Reason = GapReason.BufferOverflow, DroppedEvents = ulong.MaxValue };
            buffer.Enqueue(gap);
        }
        buffer.Enqueue(Event(A));
        buffer.Enqueue(Event(A));
        Ack(buffer, A, 4);
        AssertOverflow(Assert.Single(buffer.Snapshot(A)), ulong.MaxValue);
        var first = buffer.Snapshot(A)[0].SeatEvent.Seq;
        Ack(buffer, A, first);
        AssertOverflow(Assert.Single(buffer.Snapshot(A)), ulong.MaxValue);
        Ack(buffer, A, buffer.Snapshot(A)[0].SeatEvent.Seq);
        Assert.Empty(buffer.Snapshot(A));
    }

    [Fact]
    public void GlobalEvictionRetainsEverySeatsPendingGapUntilCapacityReturns()
    {
        using var buffer = Buffer(new() { MaxEventsPerSeat = 2, MaxEvents = 2 });
        var seats = Enumerable.Range(1, 6).Select(i => new Guid(i, 0, 0, new byte[8]).ToString("D")).ToArray();
        foreach (var seat in seats)
        {
            buffer.RegisterSeat(new() { SeatId = seat });
            buffer.Enqueue(Event(seat));
            Assert.InRange(buffer.BufferedCount, 1, 2);
            Assert.True(buffer.BufferedBytes <= 67108864);
        }
        var gaps = new Dictionary<string, ulong>();
        for (var pass = 0; pass < 10 && buffer.BufferedCount > 0; pass++)
        {
            foreach (var inventory in buffer.Inventory())
            {
                var events = buffer.Snapshot(inventory.SeatId);
                if (events.Count == 0) continue;
                foreach (var item in events.Where(e => e.SeatEvent.Gap?.Reason == GapReason.BufferOverflow))
                    gaps[inventory.SeatId] = gaps.GetValueOrDefault(inventory.SeatId) + item.SeatEvent.Gap.DroppedEvents;
                Ack(buffer, inventory.SeatId, events[^1].SeatEvent.Seq);
                Assert.InRange(buffer.BufferedCount, 0, 2);
            }
        }
        Assert.Equal(0, buffer.BufferedCount);
        foreach (var seat in seats[..4]) Assert.Equal(1ul, gaps[seat]);
        Assert.Equal(4, gaps.Count);
    }

    [Fact]
    public void ByteOverflowCoalescesBeforeDroppingGlobalOldest()
    {
        using var buffer = Buffer(new() { MaxEvents = 20, MaxBytes = 1024 });
        buffer.Enqueue(Event(A, raw: new byte[400]));
        buffer.Enqueue(Event(B, HarnessEventKind.Telemetry, new byte[400]));
        buffer.Enqueue(Event(B, HarnessEventKind.Telemetry, new byte[400]));
        Assert.Equal(1ul, buffer.Snapshot(A)[0].SeatEvent.Seq);
        Assert.Equal(HarnessEventKind.Telemetry, buffer.Snapshot(B)[0].SeatEvent.Harness.Kind);
        AssertOverflow(buffer.Snapshot(B)[1], 1);
        buffer.Enqueue(Event(A, raw: new byte[800]));
        Assert.True(buffer.BufferedBytes <= 1024);
        Assert.DoesNotContain(buffer.Snapshot(A), e => e.SeatEvent.Seq == 1);
        Assert.Equal(buffer.Snapshot(A).Concat(buffer.Snapshot(B)).Sum(e => (long)e.CalculateSize()), buffer.BufferedBytes);
    }

    private static void AssertOverflow(ConnectRequest request, ulong dropped)
    {
        Assert.Equal(GapReason.BufferOverflow, request.SeatEvent.Gap.Reason);
        Assert.Equal(dropped, request.SeatEvent.Gap.DroppedEvents);
    }

    private static void Ack(NodeEventBuffer buffer, string seat, ulong through) =>
        buffer.Acknowledge(new() { SeatId = seat, ThroughSeq = through });

    private static NodeEventBuffer Buffer(NodeEventBufferOptions? options = null, TimeProvider? timeProvider = null)
    {
        var result = new NodeEventBuffer(options, timeProvider ?? new ManualTimeProvider());
        result.RegisterSeat(new() { SeatId = A });
        result.RegisterSeat(new() { SeatId = B });
        return result;
    }

    private static SeatEvent Event(string seat, HarnessEventKind kind = HarnessEventKind.Other, byte[]? raw = null) => new()
    {
        SeatId = seat, ObservedAt = Timestamp.FromDateTimeOffset(Now),
        Harness = new() { Kind = kind, Raw = ByteString.CopyFrom(raw ?? []) }
    };

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly object _sync = new();
        private readonly List<ManualTimer> _timers = [];
        private long _timestamp;

        public int ActiveTimers { get { lock (_sync) return _timers.Count; } }

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() { lock (_sync) return _timestamp; }
        public override DateTimeOffset GetUtcNow() => Now.AddTicks(GetTimestamp());

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

        private void Add(ManualTimer timer)
        {
            lock (_sync)
                if (!_timers.Contains(timer)) _timers.Add(timer);
        }

        private void Remove(ManualTimer timer)
        {
            lock (_sync) _timers.Remove(timer);
        }

        private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
        {
            private long? _dueAt;
            private TimeSpan _period = Timeout.InfiniteTimeSpan;
            private bool _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (dueTime < TimeSpan.Zero && dueTime != Timeout.InfiniteTimeSpan)
                    throw new ArgumentOutOfRangeException(nameof(dueTime));
                if (period < TimeSpan.Zero && period != Timeout.InfiniteTimeSpan)
                    throw new ArgumentOutOfRangeException(nameof(period));
                lock (owner._sync)
                {
                    if (_disposed) return false;
                    _period = period;
                    _dueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner._timestamp + dueTime.Ticks;
                    owner.Add(this);
                    return true;
                }
            }

            public void Dispose()
            {
                lock (owner._sync)
                {
                    _disposed = true;
                    _dueAt = null;
                    owner.Remove(this);
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
