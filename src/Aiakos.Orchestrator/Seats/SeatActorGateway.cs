using Akka.Actor;

using Aiakos.Contracts.Node.V1;

namespace Aiakos.Orchestrator.Seats;

public sealed class SeatActorGateway : ISeatEventCommitter, ISeatInputCommitter, ISeatActorLifecycle
{
    private const string InvalidSeatEvent = "INVALID_SEAT_EVENT";
    private const string SeatNotFound = "SEAT_NOT_FOUND";
    private const string SeatInputNotSupported = "SEAT_INPUT_NOT_SUPPORTED";
    private const string SeatActorUnavailable = "SEAT_ACTOR_UNAVAILABLE";
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(30);

    private readonly IActorRef _region;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _requestTimeout;

    public SeatActorGateway(IActorRef region) : this(region, TimeProvider.System, DefaultRequestTimeout)
    {
    }

    internal SeatActorGateway(IActorRef region, TimeProvider timeProvider, TimeSpan requestTimeout)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(requestTimeout, TimeSpan.Zero);
        _region = region;
        _timeProvider = timeProvider;
        _requestTimeout = requestTimeout;
    }

    public Task<SeatEventsCommitted> CommitAsync(Guid tenantId, Guid seatId, string nodeName,
        Guid nodeInstanceId, IReadOnlyList<SeatEvent> events, string? traceParent, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return Task.FromCanceled<SeatEventsCommitted>(ct);

        if (tenantId == Guid.Empty || seatId == Guid.Empty || nodeInstanceId == Guid.Empty ||
            string.IsNullOrEmpty(nodeName) || events is null || events.Any(static item => item is null) ||
            events.Any(item => !SeatActor.IsValidEvent(item, seatId)))
        {
            return Task.FromException<SeatEventsCommitted>(new InvalidOperationException(InvalidSeatEvent));
        }

        var clonedEvents = events.Select(static item => item.Clone()).ToArray();
        if (ct.IsCancellationRequested)
            return Task.FromCanceled<SeatEventsCommitted>(ct);

        var completion = new TaskCompletionSource<SeatEventsCommitted>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new SeatEventRequest(nodeName, nodeInstanceId, clonedEvents, traceParent, completion);
        _region.Tell(new SeatEnvelope(tenantId, seatId, request));
        return WaitForCompletionAsync(completion.Task, ct);
    }

    public Task<SeatInputCommitted> ApplyAsync(Guid tenantId, Guid seatId, SeatInput input, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            return Task.FromCanceled<SeatInputCommitted>(ct);
        if (tenantId == Guid.Empty || seatId == Guid.Empty)
            return Task.FromException<SeatInputCommitted>(new InvalidOperationException(SeatNotFound));
        if (!SeatActor.IsSupportedInput(input))
            return Task.FromException<SeatInputCommitted>(new InvalidOperationException(SeatInputNotSupported));
        if (ct.IsCancellationRequested)
            return Task.FromCanceled<SeatInputCommitted>(ct);

        var completion = new TaskCompletionSource<SeatInputCommitted>(TaskCreationOptions.RunContinuationsAsynchronously);
        _region.Tell(new SeatEnvelope(tenantId, seatId, new SeatInputRequest(input, completion)));
        return WaitForCompletionAsync(completion.Task, ct);
    }

    public IDisposable Subscribe(Action<SeatActorReloaded> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        var id = Guid.NewGuid();
        _region.Tell(new AddSeatActorObserver(id, observer));
        return new ObserverRegistration(_region, id);
    }

    private async Task<T> WaitForCompletionAsync<T>(Task<T> completion, CancellationToken ct)
    {
        try
        {
            return await completion.WaitAsync(_requestTimeout, _timeProvider, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            ObserveLateFault(completion);
            throw new InvalidOperationException(SeatActorUnavailable);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            ObserveLateFault(completion);
            throw;
        }
    }

    private static void ObserveLateFault(Task completion) => _ = completion.ContinueWith(
        static task => _ = task.Exception, CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
        TaskScheduler.Default);

    private sealed class ObserverRegistration(IActorRef region, Guid id) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                region.Tell(new RemoveSeatActorObserver(id));
        }
    }
}
