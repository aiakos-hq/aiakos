using Akka.Actor;
using Akka.Event;

namespace Aiakos.Orchestrator.Seats;

public sealed class SeatRegion : ReceiveActor
{
    private const string SeatNotFound = "SEAT_NOT_FOUND";
    private const string HumanSeat = "HUMAN_SEAT";
    private const string SeatRetired = "SEAT_RETIRED";
    private const string SeatNodeMismatch = "SEAT_NODE_MISMATCH";
    private const string SeatInputNotSupported = "SEAT_INPUT_NOT_SUPPORTED";
    private const string SeatActorUnavailable = "SEAT_ACTOR_UNAVAILABLE";

    private readonly ISeatActorReader _reader;
    private readonly ISeatActorWriter _writer;
    private readonly IReadOnlyList<IHarnessStateProfile> _profiles;
    private readonly TimeProvider _timeProvider;
    private readonly CancellationTokenSource _stoppingToken;
    private readonly Dictionary<Guid, ChildEntry> _childrenBySeat = [];
    private readonly Dictionary<IActorRef, ChildEntry> _childrenByActor = [];
    private readonly Dictionary<Guid, Action<SeatActorReloaded>> _observers = [];
    private readonly HashSet<IActorRef> _expectedStops = [];
    private readonly HashSet<SeatKey> _startupPending = [];
    private readonly List<TaskCompletionSource> _startWaiters = [];
    private readonly List<TaskCompletionSource> _stopWaiters = [];
    private bool _starting;
    private bool _started;
    private bool _stopping;

    public SeatRegion(ISeatActorReader reader, ISeatActorWriter writer,
        IReadOnlyList<IHarnessStateProfile> profiles, TimeProvider timeProvider)
        : this(reader, writer, profiles, timeProvider, CancellationToken.None)
    {
    }

    internal SeatRegion(ISeatActorReader reader, ISeatActorWriter writer,
        IReadOnlyList<IHarnessStateProfile> profiles, TimeProvider timeProvider, CancellationToken stoppingToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _reader = reader;
        _writer = writer;
        _profiles = profiles;
        _timeProvider = timeProvider;
        _stoppingToken = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        ReceiveAsync<StartSeatRegion>(StartAsync);
        Receive<StopSeatRegion>(Stop);
        ReceiveAsync<SeatEnvelope>(message => RouteAsync(message, Sender));
        Receive<SeatChildLoaded>(ChildLoaded);
        Receive<SeatChildLoadFailed>(ChildLoadFailed);
        Receive<AddSeatActorObserver>(message => _observers[message.Id] = message.Observer);
        Receive<RemoveSeatActorObserver>(message => _observers.Remove(message.Id));
        Receive<SeatRequestSettled>(RequestSettled);
        Receive<SeatRegionBarrier>(message => message.Completion.TrySetResult());
        Receive<GetSeatActorRef>(request => request.Completion.TrySetResult(
            _childrenBySeat.TryGetValue(request.SeatId, out var child) ? child.Actor : null));
        Receive<Terminated>(ChildTerminated);
    }

    protected override SupervisorStrategy SupervisorStrategy() =>
        new OneForOneStrategy(10, TimeSpan.FromMinutes(1), static _ => Directive.Restart);

    protected override void PostStop()
    {
        _stoppingToken.Cancel();
        foreach (var child in _childrenByActor.Keys.ToArray())
        {
            FailPending(_childrenByActor[child]);
            Context.Stop(child);
        }
        _stoppingToken.Dispose();
        base.PostStop();
    }

    private async Task StartAsync(StartSeatRegion request)
    {
        if (_started)
        {
            request.Completion.TrySetResult();
            return;
        }

        _startWaiters.Add(request.Completion);
        if (_starting)
            return;

        _starting = true;
        try
        {
            var startupSeats = await _reader.GetStartupSeatsAsync(_stoppingToken.Token).ConfigureAwait(false);
            var seen = new HashSet<SeatKey>();
            foreach (var key in startupSeats)
            {
                if (!SeatActor.IsValidKey(key) || !seen.Add(key))
                    continue;

                var result = await LoadEligibleSnapshotAsync(key).ConfigureAwait(false);
                if (result.Error is not null)
                {
                    if (result.Error == SeatActorUnavailable && !_stopping)
                        RecordActorStopped(key);
                    continue;
                }

                SpawnChild(key);
                _startupPending.Add(key);
            }
        }
        catch (OperationCanceledException) when (_stoppingToken.IsCancellationRequested)
        {
            FailStartWaiters();
            return;
        }
        catch (Exception)
        {
            foreach (var waiter in _startWaiters)
                waiter.TrySetException(new InvalidOperationException(SeatActorUnavailable));
            _startWaiters.Clear();
            _starting = false;
            return;
        }

        CompleteStartupIfReady();
    }

    private async Task RouteAsync(SeatEnvelope envelope, IActorRef sender)
    {
        if (_stopping)
        {
            FailRequest(envelope.Message, SeatActorUnavailable);
            return;
        }

        var key = new SeatKey(envelope.TenantId, envelope.SeatId);
        var validationError = ValidateRequest(key, envelope.Message);
        if (validationError is not null)
        {
            FailRequest(envelope.Message, validationError);
            return;
        }

        if (_childrenBySeat.TryGetValue(key.SeatId, out var existing))
        {
            if (existing.Key != key || _expectedStops.Contains(existing.Actor))
            {
                FailRequest(envelope.Message, SeatNotFound);
                return;
            }

            Forward(existing, envelope.Message, sender);
            return;
        }

        var eligibility = await LoadEligibleSnapshotAsync(key).ConfigureAwait(false);
        if (eligibility.Error is not null)
        {
            FailRequest(envelope.Message, eligibility.Error);
            if (eligibility.Error == SeatActorUnavailable && !_stopping)
                RecordActorStopped(key);
            return;
        }

        var child = SpawnChild(key);
        Forward(child, envelope.Message, sender);
    }

    private async Task<(SeatActorSnapshot? Snapshot, string? Error)> LoadEligibleSnapshotAsync(SeatKey key)
    {
        SeatActorSnapshot? snapshot;
        try
        {
            snapshot = await _reader.LoadAsync(key, _stoppingToken.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stoppingToken.IsCancellationRequested)
        {
            return (null, SeatActorUnavailable);
        }
        catch (Exception)
        {
            return (null, SeatActorUnavailable);
        }

        if (snapshot is null || snapshot.Key != key || snapshot.State is null)
            return (null, SeatNotFound);
        if (snapshot.Kind == "human")
            return (null, HumanSeat);
        if (snapshot.Retired)
            return (null, SeatRetired);
        if (snapshot.Kind != "agent")
            return (null, SeatNotFound);
        return (snapshot, null);
    }

    private ChildEntry SpawnChild(SeatKey key)
    {
        if (_childrenBySeat.TryGetValue(key.SeatId, out var existing))
        {
            if (existing.Key != key)
                throw new InvalidOperationException(SeatNotFound);
            return existing;
        }

        var child = Context.ActorOf(Props.Create(() =>
            new SeatActor(key, _reader, _writer, _profiles, _timeProvider)), key.SeatId.ToString("D"));
        var entry = new ChildEntry(key, child);
        _childrenBySeat.Add(key.SeatId, entry);
        _childrenByActor.Add(child, entry);
        Context.Watch(child);
        return entry;
    }

    private void Forward(ChildEntry child, object message, IActorRef sender)
    {
        var task = CompletionTask(message);
        if (task is not null)
        {
            child.Pending[task] = () => FailRequest(message, SeatActorUnavailable);
            _ = task.ContinueWith(static (completed, state) =>
            {
                var notice = (RequestFinishedNotice)state!;
                notice.Region.Tell(new SeatRequestSettled(notice.Child, completed));
            }, new RequestFinishedNotice(Self, child.Actor), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }

        child.Actor.Tell(message, sender);
    }

    private void ChildLoaded(SeatChildLoaded notice)
    {
        if (!_childrenByActor.TryGetValue(Sender, out var child) || child.Key != notice.Key)
        {
            notice.Ready.TrySetException(new InvalidOperationException(SeatActorUnavailable));
            return;
        }

        if (child.InitialLoadComplete)
        {
            var reloaded = new SeatActorReloaded(notice.Key.TenantId, notice.Key.SeatId, notice.Version);
            foreach (var observer in _observers.Values.ToArray())
            {
                try
                {
                    observer(reloaded);
                }
                catch (Exception)
                {
                    Logging.GetLogger(Context).Log(Akka.Event.LogLevel.ErrorLevel, null,
                        $"Seat actor observer failed: tenant {notice.Key.TenantId:D}, seat {notice.Key.SeatId:D}");
                }
            }
        }
        else
        {
            child.InitialLoadComplete = true;
            _startupPending.Remove(notice.Key);
        }

        notice.Ready.TrySetResult();
        CompleteStartupIfReady();
    }

    private void ChildLoadFailed(SeatChildLoadFailed notice)
    {
        if (!_childrenByActor.TryGetValue(Sender, out var child) || child.Key != notice.Key)
            return;

        _startupPending.Remove(notice.Key);
        _expectedStops.Add(child.Actor);
        FailPending(child);
        _childrenByActor.Remove(child.Actor);
        _childrenBySeat.Remove(child.Key.SeatId);
        Context.Stop(child.Actor);
        RecordActorStopped(notice.Key);
        CompleteStartupIfReady();
    }

    private void ChildTerminated(Terminated terminated)
    {
        if (!_childrenByActor.Remove(terminated.ActorRef, out var child))
        {
            _expectedStops.Remove(terminated.ActorRef);
            return;
        }

        _childrenBySeat.Remove(child.Key.SeatId);
        _startupPending.Remove(child.Key);
        var expected = _expectedStops.Remove(terminated.ActorRef) || _stopping;
        if (!expected)
        {
            FailPending(child);
            RecordActorStopped(child.Key);
        }
        else
        {
            FailPending(child);
        }

        CompleteStartupIfReady();
        CompleteStopIfReady();
    }

    private void RequestSettled(SeatRequestSettled notice)
    {
        if (_childrenByActor.TryGetValue(notice.Child, out var child))
            child.Pending.Remove(notice.Completion);
    }

    private void Stop(StopSeatRegion request)
    {
        _stopWaiters.Add(request.Completion);
        if (_stopping)
        {
            CompleteStopIfReady();
            return;
        }

        _stopping = true;
        _stoppingToken.Cancel();
        FailStartWaiters();
        foreach (var entry in _childrenByActor.Values.ToArray())
        {
            _expectedStops.Add(entry.Actor);
            FailPending(entry);
            Context.Stop(entry.Actor);
        }
        CompleteStopIfReady();
    }

    private void RecordActorStopped(SeatKey key)
    {
        var logger = Logging.GetLogger(Context);
        logger.Log(Akka.Event.LogLevel.ErrorLevel, null, $"Seat actor stopped: tenant {key.TenantId:D}, seat {key.SeatId:D}");
        _ = RecordActorStoppedAsync(key, logger);
    }

    private async Task RecordActorStoppedAsync(SeatKey key, Akka.Event.ILoggingAdapter logger)
    {
        try
        {
            await _writer.RecordActorStoppedAsync(key, _timeProvider.GetUtcNow(), _stoppingToken.Token)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            logger.Log(Akka.Event.LogLevel.ErrorLevel, null,
                $"Seat actor finding write failed: tenant {key.TenantId:D}, seat {key.SeatId:D}");
        }
    }

    private void CompleteStartupIfReady()
    {
        if (!_starting || _startupPending.Count != 0)
            return;

        _starting = false;
        _started = true;
        foreach (var waiter in _startWaiters)
            waiter.TrySetResult();
        _startWaiters.Clear();
    }

    private void CompleteStopIfReady()
    {
        if (!_stopping || _childrenByActor.Count != 0)
            return;
        foreach (var waiter in _stopWaiters)
            waiter.TrySetResult();
        _stopWaiters.Clear();
    }

    private void FailStartWaiters()
    {
        foreach (var waiter in _startWaiters)
            waiter.TrySetException(new InvalidOperationException(SeatActorUnavailable));
        _startWaiters.Clear();
        _starting = false;
    }

    private static string? ValidateRequest(SeatKey key, object message)
    {
        if (!SeatActor.IsValidKey(key))
            return message is SeatEventRequest ? "INVALID_SEAT_EVENT" : SeatNotFound;
        if (message is SeatInputRequest input)
            return SeatActor.IsSupportedInput(input.Input) ? null : SeatInputNotSupported;
        if (message is not SeatEventRequest events || events.NodeInstanceId == Guid.Empty ||
            string.IsNullOrEmpty(events.NodeName) || events.Events is null ||
            events.Events.Any(static value => value is null) ||
            events.Events.Any(value => !SeatActor.IsValidEvent(value, key.SeatId)))
            return message is SeatEventRequest ? "INVALID_SEAT_EVENT" : "SEAT_INPUT_NOT_SUPPORTED";
        return null;
    }

    private static Task? CompletionTask(object message) => message switch
    {
        SeatEventRequest request => request.Completion.Task,
        SeatInputRequest request => request.Completion.Task,
        _ => null
    };

    private static void FailRequest(object message, string error)
    {
        var exception = new InvalidOperationException(error);
        switch (message)
        {
            case SeatEventRequest request:
                request.Completion.TrySetException(exception);
                break;
            case SeatInputRequest request:
                request.Completion.TrySetException(exception);
                break;
        }
    }

    private static void FailPending(ChildEntry child)
    {
        foreach (var fail in child.Pending.Values.ToArray())
            fail();
        child.Pending.Clear();
    }

    private sealed class ChildEntry(SeatKey key, IActorRef actor)
    {
        internal SeatKey Key { get; } = key;
        internal IActorRef Actor { get; } = actor;
        internal bool InitialLoadComplete { get; set; }
        internal Dictionary<Task, Action> Pending { get; } = [];
    }

    private sealed record RequestFinishedNotice(IActorRef Region, IActorRef Child);
}

internal sealed record StartSeatRegion(TaskCompletionSource Completion);
internal sealed record StopSeatRegion(TaskCompletionSource Completion);
internal sealed record AddSeatActorObserver(Guid Id, Action<SeatActorReloaded> Observer);
internal sealed record RemoveSeatActorObserver(Guid Id);
internal sealed record SeatRequestSettled(IActorRef Child, Task Completion);
internal sealed record GetSeatActorRef(Guid SeatId, TaskCompletionSource<IActorRef?> Completion);
internal sealed record SeatRegionBarrier(TaskCompletionSource Completion);
