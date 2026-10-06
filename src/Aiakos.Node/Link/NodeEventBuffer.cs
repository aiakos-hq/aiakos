using System.Diagnostics;
using System.Runtime.CompilerServices;
using Aiakos.Contracts.Node.V1;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aiakos.Node.Link;

public sealed class NodeEventBufferOptions
{
    public int MaxEventsPerSeat { get; set; } = 2000;
    public int MaxEvents { get; set; } = 10000;
    public long MaxBytes { get; set; } = 67108864;
}

public sealed class NodeEventBuffer : IDisposable
{
    private const ulong MaximumSequence = (ulong)long.MaxValue - 1;
    private readonly object _sync = new();
    private readonly Dictionary<string, SeatState> _seats = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private readonly ulong _initialSequenceForTests;
    private TaskCompletionSource _changed = NewSignal();
    private long _generation;
    private long _nextOrdinal;
    private long _bufferedBytes;
    private int _bufferedCount;
    private bool _readerActive;
    private bool _disposed;

    public NodeEventBuffer(NodeEventBufferOptions? options = null, TimeProvider? timeProvider = null)
        : this(options, timeProvider, 0)
    {
    }

    internal NodeEventBuffer(NodeEventBufferOptions? options, TimeProvider? timeProvider, ulong initialSequence)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(initialSequence, MaximumSequence);
        _initialSequenceForTests = initialSequence;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public int BufferedCount
    {
        get { lock (_sync) return _bufferedCount; }
    }

    public long BufferedBytes
    {
        get { lock (_sync) return _bufferedBytes; }
    }

    public void RegisterSeat(SeatInventory inventory, bool recovered = false)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        var copy = inventory.Clone();
        if (!TryCanonicalId(copy.SeatId, out var seatId))
            throw new ArgumentException("Invalid seat inventory.");

        lock (_sync)
        {
            ThrowIfDisposed();
            var hasExisting = _seats.TryGetValue(seatId, out var state);
            var previousSequence = hasExisting ? state!.LastSequence : _initialSequenceForTests;
            if (recovered && !(hasExisting && state!.RecoveryGapQueued) && previousSequence >= MaximumSequence)
                throw new InvalidOperationException("Event sequence exhausted.");

            copy.SeatId = seatId;
            if (!hasExisting)
            {
                state = new SeatState(copy, _initialSequenceForTests);
                _seats.Add(seatId, state);
            }
            else
            {
                state!.Inventory = copy;
            }

            if (recovered && !state!.RecoveryGapQueued)
            {
                state.RecoveryGapQueued = true;
                EnqueueLocked(new SeatEvent
                {
                    SeatId = seatId,
                    ObservedAt = Timestamp.FromDateTimeOffset(_timeProvider.GetUtcNow()),
                    Gap = new ObservationGap
                    {
                        Reason = GapReason.NodeRestarted,
                        DroppedEvents = 0,
                    },
                }, state);
            }
        }
    }

    public void Enqueue(SeatEvent value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var copy = value.Clone();
        if (!IsValidEvent(copy) || !TryCanonicalId(copy.SeatId, out var seatId))
            throw InvalidEvent();

        lock (_sync)
        {
            ThrowIfDisposed();
            if (!_seats.TryGetValue(seatId, out var state))
                throw InvalidEvent();
            if (state.LastSequence >= MaximumSequence)
                throw new InvalidOperationException("Event sequence exhausted.");

            EnqueueLocked(copy, state);
        }
    }

    public IReadOnlyList<SeatInventory> Inventory()
    {
        lock (_sync)
        {
            var result = _seats.Values
                .OrderBy(static seat => seat.Inventory.SeatId, StringComparer.Ordinal)
                .Select(CreateInventorySnapshot)
                .ToArray();
            return Array.AsReadOnly(result);
        }
    }

    public IReadOnlyList<ConnectRequest> Snapshot(string seatId)
    {
        if (!TryCanonicalId(seatId, out var canonical))
            return Array.Empty<ConnectRequest>();
        lock (_sync)
        {
            if (!_seats.TryGetValue(canonical, out var state))
                return Array.Empty<ConnectRequest>();
            return Array.AsReadOnly(state.Events.Select(static item => item.Request.Clone()).ToArray());
        }
    }

    public void Acknowledge(EventAck ack)
    {
        ArgumentNullException.ThrowIfNull(ack);
        ack = ack.Clone();
        lock (_sync)
        {
            ThrowIfDisposed();
            if (!TryCanonicalId(ack.SeatId, out var seatId) ||
                !_seats.TryGetValue(seatId, out var state) ||
                ack.ThroughSeq == 0 || ack.ThroughSeq > state.LastSequence)
                throw new ArgumentException("Invalid event acknowledgement.");

            if (RemoveEvents(state, item => item.Sequence <= ack.ThroughSeq) > 0)
            {
                SignalChanged();
            }
        }
    }

    public void ApplyReplay(IReadOnlyList<ReplayFrom> replay)
    {
        ArgumentNullException.ThrowIfNull(replay);
        var replaySnapshot = replay.Select(static checkpoint => checkpoint?.Clone()).ToArray();
        lock (_sync)
        {
            ThrowIfDisposed();
            var checkpoints = new List<(SeatState State, ulong NextSequence)>(replaySnapshot.Length);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var checkpoint in replaySnapshot)
            {
                if (checkpoint is null || !TryCanonicalId(checkpoint.SeatId, out var seatId) ||
                    !seen.Add(seatId) || !_seats.TryGetValue(seatId, out var state) ||
                    checkpoint.NextSeq == 0 || checkpoint.NextSeq > state.LastSequence + 1)
                    throw new ArgumentException("Invalid event replay checkpoint.");
                checkpoints.Add((state, checkpoint.NextSeq));
            }

            AdvanceGeneration();
            foreach (var state in _seats.Values)
                state.Eligible = false;
            foreach (var (state, nextSequence) in checkpoints)
            {
                RemoveEvents(state, item => item.Sequence < nextSequence);
                state.Eligible = true;
            }
            SignalChanged();
        }
    }

    public async IAsyncEnumerable<ConnectRequest> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        var alreadyDisposed = false;
        lock (_sync)
        {
            if (_disposed)
                alreadyDisposed = true;
            else if (_readerActive)
                throw new InvalidOperationException("An event buffer reader is already active.");
            else
                _readerActive = true;
        }
        if (alreadyDisposed)
            yield break;

        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                ConnectRequest? next;
                Task changed;
                bool disposed;
                lock (_sync)
                {
                    disposed = _disposed;
                    ct.ThrowIfCancellationRequested();
                    next = TakeNextEvent();
                    changed = _changed.Task;
                }

                if (disposed)
                    yield break;

                if (next is not null)
                {
                    yield return next;
                    continue;
                }

                await changed.WaitAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            lock (_sync)
                _readerActive = false;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            SignalChanged();
        }
    }

    private void EnqueueLocked(SeatEvent value, SeatState state)
    {
        if (state.LastSequence >= MaximumSequence)
            throw new InvalidOperationException("Event sequence exhausted.");
        var sequence = state.LastSequence + 1;
        var eventCopy = value;
        eventCopy.SeatId = state.Inventory.SeatId;
        eventCopy.Seq = sequence;
        var request = new ConnectRequest { SeatEvent = eventCopy };
        SetTrace(request);
        var size = request.CalculateSize();
        var newByteCount = checked(_bufferedBytes + size);
        var ordinal = checked(_nextOrdinal + 1);

        state.LastSequence = sequence;
        state.Events.Add(new BufferedEvent(sequence, ordinal, size, request));
        _nextOrdinal = ordinal;
        _bufferedBytes = newByteCount;
        _bufferedCount++;
        SignalChanged();
    }

    private ConnectRequest? TakeNextEvent()
    {
        if (_disposed)
            return null;
        BufferedEvent? selected = null;
        foreach (var state in _seats.Values)
        {
            if (!state.Eligible)
                continue;
            var candidate = state.Events.FirstOrDefault(item => item.SentGeneration != _generation);
            if (candidate is not null && (selected is null || candidate.Ordinal < selected.Ordinal))
                selected = candidate;
        }

        if (selected is null)
            return null;
        selected.SentGeneration = _generation;
        return selected.Request.Clone();
    }

    private SeatInventory CreateInventorySnapshot(SeatState state)
    {
        var inventory = state.Inventory.Clone();
        inventory.FirstBufferedSeq = state.Events.Count == 0 ? 0 : state.Events[0].Sequence;
        inventory.LastSeq = state.LastSequence;
        return inventory;
    }

    private int RemoveEvents(SeatState state, Predicate<BufferedEvent> shouldRemove)
    {
        var removedCount = 0;
        for (var index = state.Events.Count - 1; index >= 0; index--)
        {
            var item = state.Events[index];
            if (!shouldRemove(item))
                continue;
            state.Events.RemoveAt(index);
            _bufferedCount--;
            _bufferedBytes -= item.Size;
            removedCount++;
        }
        return removedCount;
    }

    private void AdvanceGeneration()
    {
        if (_generation == long.MaxValue)
        {
            foreach (var state in _seats.Values)
                foreach (var item in state.Events)
                    item.SentGeneration = 0;
            _generation = 1;
            return;
        }
        _generation++;
    }

    private void SignalChanged()
    {
        var previous = _changed;
        _changed = NewSignal();
        previous.TrySetResult();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void SetTrace(ConnectRequest request)
    {
        var current = Activity.Current;
        request.Trace = current?.IdFormat == ActivityIdFormat.W3C
            ? new TraceContext
            {
                Traceparent = current.Id ?? string.Empty,
                Tracestate = current.TraceStateString ?? string.Empty,
            }
            : new TraceContext();
    }

    private static bool IsValidEvent(SeatEvent value) =>
        TryCanonicalId(value.SeatId, out _) &&
        value.ObservedAt is not null && IsValidTimestamp(value.ObservedAt) &&
        value.SourceSeq <= long.MaxValue &&
        (value.LaunchId.Length == 0 || TryCanonicalId(value.LaunchId, out _));

    private static bool IsValidTimestamp(Timestamp timestamp) =>
        timestamp.Seconds is >= -62135596800 and <= 253402300799 &&
        timestamp.Nanos is >= 0 and < 1000000000;

    private static bool TryCanonicalId(string? value, out string canonical)
    {
        canonical = string.Empty;
        if (!Guid.TryParse(value, out var parsed) || parsed == Guid.Empty)
            return false;
        canonical = parsed.ToString("D");
        return true;
    }

    private static ArgumentException InvalidEvent() => new("Invalid seat event.");

    private sealed class SeatState(SeatInventory inventory, ulong lastSequence)
    {
        public SeatInventory Inventory { get; set; } = inventory;
        public ulong LastSequence { get; set; } = lastSequence;
        public List<BufferedEvent> Events { get; } = [];
        public bool Eligible { get; set; }
        public bool RecoveryGapQueued { get; set; }
    }

    private sealed class BufferedEvent(ulong sequence, long ordinal, int size, ConnectRequest request)
    {
        public ulong Sequence { get; } = sequence;
        public long Ordinal { get; } = ordinal;
        public int Size { get; } = size;
        public ConnectRequest Request { get; } = request;
        public long SentGeneration { get; set; }
    }
}
