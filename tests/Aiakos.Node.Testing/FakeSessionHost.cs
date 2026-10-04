using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Aiakos.Core;
using Aiakos.Node.Sessions;

namespace Aiakos.Node.Testing;

public sealed class FakeSessionHost : ISessionHost
{
    private static readonly IReadOnlyList<string> Capabilities =
        Array.AsReadOnly(new[] { "session-host.tmux" });
    private static readonly TimeSpan MaxSubmitDelay = TimeSpan.FromMilliseconds(uint.MaxValue - 1d);
    private readonly object _sync = new();
    private readonly TimeProvider _time;
    private readonly Dictionary<string, FakeSession> _sessionsById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FakeSession> _sessionsBySeat = new(StringComparer.Ordinal);
    private readonly List<SessionHostEvent> _eventHistory = [];
    private readonly List<Channel<SessionHostEvent>> _watchers = [];
    private readonly ConditionalWeakTable<SessionSpec, LaunchOptions> _launchOptions = new();
    private string? _unavailableReason;
    private int _nextId;

    public FakeSessionHost(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        Files = new FakeLaunchFiles();
    }

    public FakeLaunchFiles Files { get; }

    public bool StatusUnresponsive { get; set; }

    public bool IgnoresGracefulStop { get; set; }

    public SessionHostInfo Info
    {
        get
        {
            lock (_sync)
            {
                return _unavailableReason is null
                    ? new SessionHostInfo("fake", null, SessionHostAvailability.Available, null, Capabilities)
                    : new SessionHostInfo("fake", null, SessionHostAvailability.Unavailable, _unavailableReason,
                        Array.Empty<string>());
            }
        }
    }

    public void MakeUnavailable(string reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        lock (_sync)
        {
            _unavailableReason = reason;
            var unavailable = new SessionHostException(new SessionHostError(SessionHostErrorCode.Unavailable,
                reason, false));
            foreach (var watcher in _watchers)
            {
                watcher.Writer.TryComplete(unavailable);
            }
        }
    }

    public byte[] Received(SessionHandle session)
    {
        EnsureAvailable();
        lock (_sync)
        {
            var state = Find(session) ?? throw NotFound();
            return state.ReceivedBytes.ToArray();
        }
    }

    public void Print(SessionHandle session, IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        EnsureAvailable();
        lock (_sync)
        {
            var state = Find(session) ?? throw NotFound();
            state.ScreenLines.AddRange(lines);
        }
    }

    public void Exit(SessionHandle session, int? exitCode, int? signal)
    {
        EnsureAvailable();
        lock (_sync)
        {
            var state = Find(session) ?? throw NotFound();
            if (state.State != FakeSessionState.Alive)
            {
                return;
            }

            state.State = FakeSessionState.Exited;
            state.ExitCode = exitCode;
            state.Signal = signal;
            PublishPaneExited(state);
        }
    }

    public void Vanish(SessionHandle session)
    {
        EnsureAvailable();
        lock (_sync)
        {
            var state = Find(session) ?? throw NotFound();
            if (state.VanishPublished || state.StopRequested)
            {
                return;
            }

            state.State = FakeSessionState.Missing;
            RemoveFromSeatIndex(state);
            state.VanishPublished = true;
            Publish(new SessionHostEvent.SessionVanished(state.Handle, _time.GetUtcNow()));
        }
    }

    internal void RegisterIgnoresGracefulStop(SessionSpec spec)
    {
        _launchOptions.Add(spec, new LaunchOptions(true));
    }

    public Task<SessionHandle> StartAsync(SessionSpec spec, CancellationToken ct)
    {
        EnsureAvailable();
        ct.ThrowIfCancellationRequested();
        var error = LaunchValidator.Validate(spec, Files);
        if (error is not null)
        {
            throw new SessionHostException(error);
        }

        lock (_sync)
        {
            EnsureAvailable();
            if (_sessionsBySeat.TryGetValue(spec.SeatId, out var existing))
            {
                if (existing.State == FakeSessionState.Alive)
                {
                    throw new SessionHostException(new SessionHostError(SessionHostErrorCode.AlreadyRunning,
                        "A live session already exists for SeatId.", false,
                        new Dictionary<string, string> { ["launch_id"] = existing.Handle.LaunchId }));
                }

                if (existing.State == FakeSessionState.Exited)
                {
                    PublishPaneExited(existing);
                }

                _sessionsBySeat.Remove(spec.SeatId);
            }

            var id = ++_nextId;
            var handle = new SessionHandle(spec.SeatId, spec.LaunchId, TmuxNames.SessionName(spec.SeatAddress),
                $"${id}", $"%{id}", 1000 + id, (ulong)id, false);
            var ignoresGracefulStop = IgnoresGracefulStop ||
                                      _launchOptions.TryGetValue(spec, out var options) && options.IgnoreStop;
            var labels = new SessionLabels(1, "fake", spec.SeatId, spec.SeatAddress,
                spec.LaunchId, spec.Harness);
            var session = new FakeSession(handle, labels, spec.Size, ignoresGracefulStop);
            _sessionsBySeat.Add(spec.SeatId, session);
            _sessionsById.Add(handle.SessionId, session);
            return Task.FromResult(handle);
        }
    }

    public Task<PaneStatus> GetStatusAsync(SessionHandle session, CancellationToken ct)
    {
        EnsureAvailable();
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            var state = Find(session) ?? throw NotFound();
            if (StatusUnresponsive)
            {
                return Task.FromResult(new PaneStatus(PaneState.Unknown, null, null, "Status is unresponsive."));
            }

            return Task.FromResult(state?.State switch
            {
                FakeSessionState.Alive => new PaneStatus(PaneState.Alive, null, null, null),
                FakeSessionState.Exited => new PaneStatus(PaneState.Exited, state.ExitCode, state.Signal, null),
                _ => new PaneStatus(PaneState.Missing, null, null, null)
            });
        }
    }

    public Task<StopReport> StopAsync(SessionHandle session, StopRequest request, CancellationToken ct)
    {
        EnsureAvailable();
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        lock (_sync)
        {
            var state = Find(session) ?? throw NotFound();
            if (state.State != FakeSessionState.Alive)
            {
                state.State = FakeSessionState.Missing;
                state.StopRequested = true;
                RemoveFromSeatIndex(state);

                return Task.FromResult(new StopReport(StopOutcome.NotRunning, state.ExitCode, state.Signal,
                    0, Array.Empty<int>(), null));
            }

            state.State = FakeSessionState.Missing;
            state.StopRequested = true;
            state.DeliveryCancellation?.Cancel();
            RemoveFromSeatIndex(state);
            var outcome = IgnoresGracefulStop || state.IgnoreGracefulStop
                ? StopOutcome.Killed
                : StopOutcome.Stopped;
            return Task.FromResult(new StopReport(outcome, null, null, 0, Array.Empty<int>(), null));
        }
    }

    public Task<IReadOnlyList<SessionListing>> ListAsync(CancellationToken ct)
    {
        EnsureAvailable();
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            IReadOnlyList<SessionListing> listings = _sessionsById.Values
                .Where(static session => session.State != FakeSessionState.Missing)
                .Select(static session => new SessionListing(
                    session.Handle.SessionName,
                    session.Handle.SessionId,
                    session.Handle.PaneId,
                    session.Handle.PanePid,
                    session.State == FakeSessionState.Exited,
                    session.ExitCode,
                    session.Signal,
                    session.Labels,
                    ListingClass.Managed,
                    null))
                .ToArray();
            return Task.FromResult(listings);
        }
    }

    public async Task<DeliveryReport> DeliverAsync(SessionHandle session, DeliveryRequest request, CancellationToken ct)
    {
        EnsureAvailable();
        ArgumentNullException.ThrowIfNull(request);

        lock (_sync)
        {
            var current = Find(session) ?? throw NotFound();
            if (current.State != FakeSessionState.Alive)
                throw NotFound();
        }

        var lead = InputValidator.CheckLead(request.Lead);
        var body = InputValidator.CheckBody(request.Body);
        if (lead.Problem != InputProblem.None)
        {
            throw InputRejected(lead.Reason ?? "Lead is not allowed.");
        }

        if (body.Problem == InputProblem.NotAllowed)
        {
            throw InputRejected(body.Reason ?? "Body is not allowed.");
        }

        if (body.Problem == InputProblem.TooLarge)
        {
            throw new SessionHostException(new SessionHostError(SessionHostErrorCode.PayloadTooLarge,
                body.Reason ?? "Body exceeds the allowed size.", false));
        }

        if (request.Lead.Length == 0 && body.Normalized.Length == 0)
        {
            throw InputRejected("Lead and body cannot both be empty.");
        }

        if (request.SubmitDelay > MaxSubmitDelay)
        {
            throw new SessionHostException(new SessionHostError(SessionHostErrorCode.InvalidArgument,
                "SubmitDelay exceeds the supported delay range.", false));
        }

        FakeSession state;
        lock (_sync)
        {
            state = Find(session) ?? throw NotFound();
            if (state.State != FakeSessionState.Alive)
            {
                throw NotFound();
            }

            if (!state.DeliveryGate.Wait(0, CancellationToken.None))
            {
                throw new SessionHostException(new SessionHostError(SessionHostErrorCode.Busy,
                    "A delivery is already in progress for this session.", true));
            }

            state.DeliveryCancellation?.Dispose();
            state.DeliveryCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        }

        var deliveryToken = state.DeliveryCancellation.Token;
        var deliveryId = Guid.NewGuid().ToString("N");
        var submittedAt = _time.GetUtcNow();
        var stage = DeliveryStage.BufferLoaded;
        var confirmation = new Confirmation(ConfirmationOutcome.Unconfirmed, null);
        var resubmits = 0;
        var deliveryContext = new FakeDeliveryContext(this, state, deliveryId, submittedAt,
            () => resubmits++, deliveryToken);
        try
        {
            if (IsCancelled(state, deliveryToken))
            {
                return Report();
            }

            if (!Append(state, Encoding.UTF8.GetBytes(request.Lead)))
                return Report();
            stage = DeliveryStage.LeadTyped;
            if (IsCancelled(state, deliveryToken))
            {
                return Report();
            }

            if (body.Normalized.Length > 0)
            {
                var bodyBytes = Encoding.UTF8.GetBytes(body.Normalized);
                var paste = new byte[bodyBytes.Length + 12];
                new byte[] { 0x1b, (byte)'[', (byte)'2', (byte)'0', (byte)'0', (byte)'~' }.CopyTo(paste, 0);
                bodyBytes.CopyTo(paste, 6);
                new byte[] { 0x1b, (byte)'[', (byte)'2', (byte)'0', (byte)'1', (byte)'~' }
                    .CopyTo(paste, 6 + bodyBytes.Length);
                if (!Append(state, paste))
                    return Report();
                stage = DeliveryStage.BodyPasted;
            }

            if (IsCancelled(state, deliveryToken))
            {
                return Report();
            }

            if (request.SubmitDelay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(request.SubmitDelay, _time, deliveryToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (deliveryToken.IsCancellationRequested)
                {
                    return Report();
                }
            }

            if (IsCancelled(state, deliveryToken))
            {
                return Report();
            }

            if (request.Submit == SubmitKey.CtrlM)
            {
                if (!Append(state, [0x0d]))
                    return Report();
                stage = DeliveryStage.Submitted;
            }

            if (IsCancelled(state, deliveryToken))
            {
                return Report();
            }

            if (request.Confirmer is null)
            {
                confirmation = new Confirmation(ConfirmationOutcome.NotRequested, null);
            }
            else
            {
                try
                {
                    confirmation = await request.Confirmer.ConfirmAsync(deliveryContext, deliveryToken)
                        .WaitAsync(deliveryToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (deliveryToken.IsCancellationRequested)
                {
                    return Report();
                }
            }
            return Report();
        }
        finally
        {
            lock (_sync)
            {
                state.DeliveryCancellation?.Dispose();
                state.DeliveryCancellation = null;
                state.DeliveryGate.Release();
            }
        }

        DeliveryReport Report() => new(deliveryId, stage, confirmation, resubmits, body.Bytes,
            body.LineEndingsNormalized, null);
    }

    public Task SendKeysAsync(SessionHandle session, IReadOnlyList<NamedKey> keys, CancellationToken ct)
    {
        EnsureAvailable();
        ArgumentNullException.ThrowIfNull(keys);
        lock (_sync)
        {
            var state = Find(session) ?? throw NotFound();
            if (state.State != FakeSessionState.Alive)
                throw NotFound();
            ct.ThrowIfCancellationRequested();
            if (keys.Count > 32 || keys.Any(static key => !Enum.IsDefined(key)))
                throw InvalidArgument("Keys must contain at most 32 defined key values.");
            if (!state.DeliveryGate.Wait(0, CancellationToken.None))
                throw Busy();
            try
            {
                foreach (var key in keys)
                    state.ReceivedBytes.AddRange(KeyBytes(key));
            }
            finally
            {
                state.DeliveryGate.Release();
            }
        }

        return Task.CompletedTask;
    }

    public Task<PaneSnapshot> CaptureAsync(SessionHandle session, CaptureRequest request, CancellationToken ct)
    {
        EnsureAvailable();
        lock (_sync)
        {
            var state = Find(session) ?? throw NotFound();
            ArgumentNullException.ThrowIfNull(request);
            ct.ThrowIfCancellationRequested();
            if (state.State == FakeSessionState.Missing)
                throw NotFound();
            return Task.FromResult(Capture(state, request));
        }
    }

    public Task<SessionHandle> AdoptAsync(SessionListing listing, CancellationToken ct)
    {
        EnsureAvailable();
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(listing);
        lock (_sync)
        {
            if (!_sessionsById.TryGetValue(listing.SessionId, out var session) ||
                session.State == FakeSessionState.Missing)
            {
                throw NotFound();
            }

            return Task.FromResult(session.Handle);
        }
    }

    public IReadOnlyList<string> GetAttachCommand(SessionHandle session, bool readOnlyMode = true)
    {
        EnsureAvailable();
        ArgumentNullException.ThrowIfNull(session);
        lock (_sync)
        {
            _ = Find(session) ?? throw NotFound();
        }
        return readOnlyMode
            ? ["fake-attach", "-r", $"={session.SessionName}"]
            : ["fake-attach", $"={session.SessionName}"];
    }

    public async IAsyncEnumerable<SessionHostEvent> WatchAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        EnsureAvailable();
        var channel = Channel.CreateUnbounded<SessionHostEvent>();
        lock (_sync)
        {
            EnsureAvailable();
            foreach (var hostEvent in _eventHistory)
            {
                channel.Writer.TryWrite(hostEvent);
            }

            _watchers.Add(channel);
        }

        try
        {
            await foreach (var hostEvent in channel.Reader.ReadAllAsync(ct))
            {
                EnsureAvailable();
                yield return hostEvent;
            }
        }
        finally
        {
            lock (_sync)
            {
                _watchers.Remove(channel);
                channel.Writer.TryComplete();
            }
        }
    }

    private void EnsureAvailable()
    {
        var reason = Info;
        if (reason.Availability == SessionHostAvailability.Unavailable)
        {
            throw new SessionHostException(new SessionHostError(SessionHostErrorCode.Unavailable,
                reason.Reason ?? "Session host is unavailable.", false));
        }
    }

    private FakeSession? Find(SessionHandle session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!_sessionsById.TryGetValue(session.SessionId, out var state) ||
            state.Handle.LaunchId != session.LaunchId || state.Handle.PaneId != session.PaneId ||
            state.Handle.SeatId != session.SeatId)
            return null;
        if (_sessionsBySeat.TryGetValue(state.Handle.SeatId, out var current) && !ReferenceEquals(current, state))
            return null;
        return state;
    }

    private void RemoveFromSeatIndex(FakeSession state)
    {
        if (_sessionsBySeat.TryGetValue(state.Handle.SeatId, out var current) && ReferenceEquals(current, state))
        {
            _sessionsBySeat.Remove(state.Handle.SeatId);
        }
    }

    private void PublishPaneExited(FakeSession state)
    {
        if (state.ExitPublished)
        {
            return;
        }

        state.ExitPublished = true;
        Publish(new SessionHostEvent.PaneExited(state.Handle, _time.GetUtcNow(), state.ExitCode, state.Signal));
    }

    private void Publish(SessionHostEvent hostEvent)
    {
        _eventHistory.Add(hostEvent);
        foreach (var watcher in _watchers)
        {
            watcher.Writer.TryWrite(hostEvent);
        }
    }

    private static SessionHostException NotFound() =>
        new(new SessionHostError(SessionHostErrorCode.NotFound, "Session was not found.", false));

    private static SessionHostException InputRejected(string message) =>
        new(new SessionHostError(SessionHostErrorCode.InputNotAllowed, message, false));

    private static SessionHostException InvalidArgument(string message) =>
        new(new SessionHostError(SessionHostErrorCode.InvalidArgument, message, false));

    private static SessionHostException Busy() =>
        new(new SessionHostError(SessionHostErrorCode.Busy,
            "A delivery is already in progress for this session.", true));

    private static byte[] KeyBytes(NamedKey key) => key switch
    {
        NamedKey.Enter => [0x0d],
        NamedKey.Escape => [0x1b],
        NamedKey.Tab => [0x09],
        NamedKey.Up => [0x1b, (byte)'[', (byte)'A'],
        NamedKey.Down => [0x1b, (byte)'[', (byte)'B'],
        NamedKey.Right => [0x1b, (byte)'[', (byte)'C'],
        NamedKey.Left => [0x1b, (byte)'[', (byte)'D'],
        NamedKey.CtrlC => [0x03],
        NamedKey.CtrlD => [0x04],
        _ => throw InvalidArgument("Key value is not defined.")
    };

    private bool IsCancelled(FakeSession state, CancellationToken token)
    {
        lock (_sync)
        {
            return token.IsCancellationRequested || state.State != FakeSessionState.Alive;
        }
    }

    private bool Append(FakeSession state, byte[] bytes)
    {
        lock (_sync)
        {
            if (state.State != FakeSessionState.Alive)
                return false;
            state.ReceivedBytes.AddRange(bytes);
            return true;
        }
    }

    private Task<PaneSnapshot> CaptureDuringDelivery(FakeSession state, CaptureRequest request, CancellationToken ct)
    {
        EnsureAvailable();
        ArgumentNullException.ThrowIfNull(request);
        lock (_sync)
        {
            if (Find(state.Handle) is null || state.State == FakeSessionState.Missing)
                throw NotFound();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Capture(state, request));
        }
    }

    private PaneSnapshot Capture(FakeSession state, CaptureRequest request)
    {
        if (request.HistoryLines is < 0 or > 10_000 || request.MaxBytes is < 1 or > 1_048_576)
            throw InvalidArgument("CaptureRequest limits are outside the supported range.");

        var lines = state.ScreenLines.TakeLast(state.Size.Rows + request.HistoryLines)
            .Select(static line => line.TrimEnd())
            .ToList();
        while (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);

        var truncated = false;
        var visibleLineCount = Math.Min(state.Size.Rows, lines.Count);
        while (Utf8Bytes(lines) > request.MaxBytes && lines.Count > visibleLineCount)
        {
            lines.RemoveAt(0);
            truncated = true;
        }

        return new PaneSnapshot(string.Join('\n', lines), truncated, lines.Count, _time.GetUtcNow(),
            state.Size, null, state.State == FakeSessionState.Exited);
    }

    private static int Utf8Bytes(IReadOnlyList<string> lines) =>
        Encoding.UTF8.GetByteCount(string.Join('\n', lines));

    private Task ResubmitAsync(FakeSession state, Action incrementCount, Func<bool> isAlreadyResubmitted,
        Action markResubmitted, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (Find(state.Handle) is null || state.State != FakeSessionState.Alive)
                throw NotFound();
            if (isAlreadyResubmitted())
                throw new InvalidOperationException("Only one resubmit is allowed for a delivery.");
            markResubmitted();
            state.ReceivedBytes.Add(0x0d);
            incrementCount();
        }
        return Task.CompletedTask;
    }

    private sealed class FakeDeliveryContext : DeliveryContext
    {
        private readonly FakeSessionHost _host;
        private readonly FakeSession _session;
        private readonly CancellationToken _deliveryToken;
        private readonly Action _incrementCount;
        private bool _resubmitted;

        public FakeDeliveryContext(FakeSessionHost host, FakeSession session, string deliveryId,
            DateTimeOffset submittedAt, Action incrementCount, CancellationToken deliveryToken)
        {
            _host = host;
            _session = session;
            DeliveryId = deliveryId;
            SubmittedAt = submittedAt;
            _deliveryToken = deliveryToken;
            _incrementCount = incrementCount;
        }

        public override string DeliveryId { get; }
        public override DateTimeOffset SubmittedAt { get; }

        public override Task<PaneSnapshot> CaptureAsync(CaptureRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _deliveryToken.ThrowIfCancellationRequested();
            return _host.CaptureDuringDelivery(_session, request, ct);
        }

        public override Task ResubmitAsync(string reason, CancellationToken ct) =>
            _host.ResubmitAsync(_session, _incrementCount, () => _resubmitted, () => _resubmitted = true, ct);
    }

    private sealed record LaunchOptions(bool IgnoreStop);

    private sealed class FakeSession(SessionHandle handle, SessionLabels labels, TerminalSize size,
        bool ignoreGracefulStop)
    {
        public SessionHandle Handle { get; } = handle;
        public SessionLabels Labels { get; } = labels;
        public TerminalSize Size { get; } = size;
        public bool IgnoreGracefulStop { get; } = ignoreGracefulStop;
        public FakeSessionState State { get; set; } = FakeSessionState.Alive;
        public List<byte> ReceivedBytes { get; } = [];
        public List<string> ScreenLines { get; } = [];
        public int? ExitCode { get; set; }
        public int? Signal { get; set; }
        public bool ExitPublished { get; set; }
        public bool VanishPublished { get; set; }
        public bool StopRequested { get; set; }
        public SemaphoreSlim DeliveryGate { get; } = new(1, 1);
        public CancellationTokenSource? DeliveryCancellation { get; set; }
    }

    private enum FakeSessionState { Alive, Exited, Missing }
}
