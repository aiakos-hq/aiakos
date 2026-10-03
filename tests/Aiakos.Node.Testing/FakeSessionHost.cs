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
            return Find(session)?.ReceivedBytes.ToArray() ?? Array.Empty<byte>();
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
            var state = Find(session);
            if (state is null || state.State != FakeSessionState.Alive)
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
            var state = Find(session);
            if (state is null || state.VanishPublished || state.StopRequested)
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
            var session = new FakeSession(handle, ignoresGracefulStop);
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
            if (StatusUnresponsive)
            {
                return Task.FromResult(new PaneStatus(PaneState.Unknown, null, null, "Status is unresponsive."));
            }

            var state = Find(session);
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
            var state = Find(session);
            if (state is null || state.State != FakeSessionState.Alive)
            {
                if (state is not null)
                {
                    state.State = FakeSessionState.Missing;
                    state.StopRequested = true;
                    RemoveFromSeatIndex(state);
                }

                return Task.FromResult(new StopReport(StopOutcome.NotRunning, state?.ExitCode, state?.Signal,
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
        return Task.FromResult<IReadOnlyList<SessionListing>>(Array.Empty<SessionListing>());
    }

    public async Task<DeliveryReport> DeliverAsync(SessionHandle session, DeliveryRequest request, CancellationToken ct)
    {
        EnsureAvailable();
        ArgumentNullException.ThrowIfNull(request);

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

        FakeSession state;
        lock (_sync)
        {
            state = Find(session) ?? throw NotFound();
            if (state.State != FakeSessionState.Alive)
            {
                throw NotFound();
            }

            if (!state.DeliveryGate.Wait(0, ct))
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
        try
        {
            if (IsCancelled(state, deliveryToken))
            {
                return Report();
            }

            Append(state, Encoding.UTF8.GetBytes(request.Lead));
            stage = DeliveryStage.LeadTyped;
            if (IsCancelled(state, deliveryToken))
            {
                return Report();
            }

            if (body.Normalized.Length > 0)
            {
                Append(state, [0x1b, (byte)'[', (byte)'2', (byte)'0', (byte)'0', (byte)'~']);
                Append(state, Encoding.UTF8.GetBytes(body.Normalized));
                Append(state, [0x1b, (byte)'[', (byte)'2', (byte)'0', (byte)'1', (byte)'~']);
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
                Append(state, [0x0d]);
                stage = DeliveryStage.Submitted;
            }

            if (IsCancelled(state, deliveryToken))
            {
                return Report();
            }

            confirmation = request.Confirmer is null
                ? new Confirmation(ConfirmationOutcome.NotRequested, null)
                : await request.Confirmer.ConfirmAsync(
                    new FakeDeliveryContext(deliveryId, submittedAt), deliveryToken).ConfigureAwait(false);
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

        DeliveryReport Report() => new(deliveryId, stage, confirmation, 0, body.Bytes,
            body.LineEndingsNormalized, null);
    }

    public Task SendKeysAsync(SessionHandle session, IReadOnlyList<NamedKey> keys, CancellationToken ct)
    {
        EnsureAvailable();
        throw new NotSupportedException("Key delivery is not part of this fake lifecycle story.");
    }

    public Task<PaneSnapshot> CaptureAsync(SessionHandle session, CaptureRequest request, CancellationToken ct)
    {
        EnsureAvailable();
        throw new NotSupportedException("Capture is not part of this fake lifecycle story.");
    }

    public Task<SessionHandle> AdoptAsync(SessionListing listing, CancellationToken ct)
    {
        EnsureAvailable();
        throw new NotSupportedException("Adoption is not part of this fake lifecycle story.");
    }

    public IReadOnlyList<string> GetAttachCommand(SessionHandle session, bool readOnlyMode = true)
    {
        EnsureAvailable();
        throw new NotSupportedException("Attach commands are not part of this fake lifecycle story.");
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

    private FakeSession? Find(SessionHandle session) =>
        _sessionsById.TryGetValue(session.SessionId, out var state) ? state : null;

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

    private bool IsCancelled(FakeSession state, CancellationToken token)
    {
        lock (_sync)
        {
            return token.IsCancellationRequested || state.State != FakeSessionState.Alive;
        }
    }

    private void Append(FakeSession state, byte[] bytes)
    {
        lock (_sync)
        {
            if (state.State == FakeSessionState.Alive)
            {
                state.ReceivedBytes.AddRange(bytes);
            }
        }
    }

    private sealed class FakeDeliveryContext(string deliveryId, DateTimeOffset submittedAt) : DeliveryContext
    {
        public override string DeliveryId { get; } = deliveryId;
        public override DateTimeOffset SubmittedAt { get; } = submittedAt;

        public override Task<PaneSnapshot> CaptureAsync(CaptureRequest request, CancellationToken ct) =>
            throw new NotSupportedException("Capture is not part of this delivery story.");

        public override Task ResubmitAsync(string reason, CancellationToken ct) =>
            throw new NotSupportedException("Resubmit is not part of this delivery story.");
    }

    private sealed record LaunchOptions(bool IgnoreStop);

    private sealed class FakeSession(SessionHandle handle, bool ignoreGracefulStop)
    {
        public SessionHandle Handle { get; } = handle;
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
