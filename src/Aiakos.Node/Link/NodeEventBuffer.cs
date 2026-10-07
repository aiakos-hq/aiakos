using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
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
    private readonly NodeEventBufferOptions _options;
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
        _options = options is null ? new NodeEventBufferOptions() : new NodeEventBufferOptions
        {
            MaxEventsPerSeat = options.MaxEventsPerSeat, MaxEvents = options.MaxEvents, MaxBytes = options.MaxBytes
        };
        if (_options.MaxEventsPerSeat < 2 || _options.MaxEvents < 2 || _options.MaxBytes < 1024)
            throw new ArgumentException("Invalid event buffer options.");
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
        NormalizeMessage(copy);
        if (copy.Harness is { } harness)
        {
            foreach (var pair in harness.Attributes)
                if (Encoding.UTF8.GetByteCount(pair.Value) > 1024)
                    throw InvalidEvent();
            if (harness.Raw.Length > 262144)
            {
                var original = harness.Raw.Length;
                harness.Raw = ByteString.CopyFrom(harness.Raw.Span[..262144]);
                harness.RawTruncated = true;
                harness.RawSize = (uint)original;
            }
        }

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
                MaterializeGaps();
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
            MaterializeGaps();
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
            {
                _readerActive = true;
                foreach (var state in _seats.Values)
                    state.LastTelemetrySent = null;
            }
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

                var telemetryDelay = TelemetryDelay();
                if (telemetryDelay is { } delay)
                {
                    using var wake = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    var timer = Task.Delay(delay, _timeProvider, wake.Token);
                    try
                    {
                        await Task.WhenAny(timer, changed).WaitAsync(ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        await wake.CancelAsync().ConfigureAwait(false);
                    }
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
        if (size > 4194304)
            throw InvalidEvent();
        var newByteCount = checked(_bufferedBytes + size);
        var ordinal = checked(_nextOrdinal + 1);

        state.LastSequence = sequence;
        state.Events.Add(new BufferedEvent(sequence, ordinal, size, request));
        _nextOrdinal = ordinal;
        _bufferedBytes = newByteCount;
        _bufferedCount++;
        EnforceBounds();
        SignalChanged();
    }

    private void EnforceBounds()
    {
        var violatingSeats = _seats.Values
            .Where(state => state.Events.Count > _options.MaxEventsPerSeat)
            .OrderBy(state => state.Inventory.SeatId, StringComparer.Ordinal);
        foreach (var state in violatingSeats)
        {
            Coalesce(state);
            while (state.Events.Count > _options.MaxEventsPerSeat)
                Drop(state, state.Events[0]);
        }

        if (_bufferedCount > _options.MaxEvents || _bufferedBytes > _options.MaxBytes)
            foreach (var state in _seats.Values)
                Coalesce(state);

        while (_bufferedCount > _options.MaxEvents || _bufferedBytes > _options.MaxBytes)
        {
            var oldestSeat = _seats.Values.Where(state => state.Events.Count > 0)
                .MinBy(state => state.Events[0].Ordinal)!;
            Drop(oldestSeat, oldestSeat.Events[0]);
        }
        MaterializeGaps();
    }

    private void Coalesce(SeatState state)
    {
        var telemetry = state.Events
            .Where(item => item.Request.SeatEvent.Harness?.Kind == HarnessEventKind.Telemetry)
            .ToArray();
        for (var index = 0; index < telemetry.Length - 1; index++)
            Drop(state, telemetry[index]);
    }

    private void Drop(SeatState state, BufferedEvent item)
    {
        state.Events.Remove(item);
        _bufferedCount--;
        _bufferedBytes -= item.Size;
        var represented = item.Request.SeatEvent.Gap?.Reason == GapReason.BufferOverflow
            ? item.Request.SeatEvent.Gap.DroppedEvents : 1UL;
        state.PendingOverflow += represented;
    }

    private void MaterializeGaps()
    {
        foreach (var state in _seats.Values.OrderBy(state => state.Inventory.SeatId, StringComparer.Ordinal))
        {
            if (state.PendingOverflow.IsZero || state.LastSequence >= MaximumSequence ||
                state.Events.Count >= _options.MaxEventsPerSeat || _bufferedCount >= _options.MaxEvents)
                continue;

            var emitted = (ulong)BigInteger.Min(state.PendingOverflow, ulong.MaxValue);
            var sequence = state.LastSequence + 1;
            var gap = new SeatEvent
            {
                SeatId = state.Inventory.SeatId,
                Seq = sequence,
                ObservedAt = Timestamp.FromDateTimeOffset(_timeProvider.GetUtcNow()),
                Gap = new ObservationGap { Reason = GapReason.BufferOverflow, DroppedEvents = emitted },
            };
            var request = new ConnectRequest { SeatEvent = gap };
            SetTrace(request);
            var size = request.CalculateSize();
            if (size > _options.MaxBytes - _bufferedBytes)
                continue;

            var ordinal = checked(_nextOrdinal + 1);
            state.Events.Add(new BufferedEvent(sequence, ordinal, size, request));
            state.LastSequence = sequence;
            state.PendingOverflow -= emitted;
            _nextOrdinal = ordinal;
            _bufferedCount++;
            _bufferedBytes += size;
        }
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
            if (candidate?.Request.SeatEvent.Harness?.Kind == HarnessEventKind.Telemetry &&
                state.LastTelemetrySent.HasValue && _timeProvider.GetElapsedTime(state.LastTelemetrySent.Value) < TimeSpan.FromSeconds(1))
                candidate = null;
            if (candidate is not null && (selected is null || candidate.Ordinal < selected.Ordinal))
                selected = candidate;
        }

        if (selected is null)
            return null;
        selected.SentGeneration = _generation;
        if (selected.Request.SeatEvent.Harness?.Kind == HarnessEventKind.Telemetry)
            _seats[selected.Request.SeatEvent.SeatId].LastTelemetrySent = _timeProvider.GetTimestamp();
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

    private TimeSpan? TelemetryDelay()
    {
        lock (_sync)
        {
            TimeSpan? shortest = null;
            var now = _timeProvider.GetTimestamp();
            foreach (var state in _seats.Values)
            {
                if (!state.Eligible || state.LastTelemetrySent is not { } sent)
                    continue;
                var head = state.Events.FirstOrDefault(item => item.SentGeneration != _generation);
                if (head?.Request.SeatEvent.Harness?.Kind != HarnessEventKind.Telemetry)
                    continue;
                var remaining = TimeSpan.FromSeconds(1) - _timeProvider.GetElapsedTime(sent, now);
                if (remaining <= TimeSpan.Zero)
                    return TimeSpan.Zero;
                if (shortest is null || remaining < shortest)
                    shortest = remaining;
            }
            return shortest;
        }
    }

    private static void NormalizeMessage(IMessage message)
    {
        foreach (var field in message.Descriptor.Fields.InFieldNumberOrder())
        {
            if (field.HasPresence && !field.Accessor.HasValue(message))
                continue;
            var value = field.Accessor.GetValue(message);
            if (field.IsMap)
            {
                if (value is System.Collections.IDictionary dictionary)
                {
                    var entries = dictionary.Keys.Cast<object>().Select(key => (key, dictionary[key]!)).ToArray();
                    dictionary.Clear();
                    foreach (var (key, item) in entries)
                    {
                        var normalizedKey = key is string text ? NormalizeText(text) : key;
                        var normalizedValue = item is string valueText ? NormalizeText(valueText) : item;
                        dictionary[normalizedKey] = normalizedValue;
                    }
                }
            }
            else if (field.FieldType == Google.Protobuf.Reflection.FieldType.String)
                field.Accessor.SetValue(message, NormalizeText((string)value));
            else if (value is IMessage nested) NormalizeMessage(nested);
            else if (value is System.Collections.IEnumerable items && value is not ByteString)
                foreach (var item in items) if (item is IMessage child) NormalizeMessage(child);
        }
    }

    private static string NormalizeText(string value) => new UTF8Encoding(false, false).GetString(Encoding.UTF8.GetBytes(value));

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
        public BigInteger PendingOverflow { get; set; }
        public long? LastTelemetrySent { get; set; }
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
