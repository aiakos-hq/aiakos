using Akka.Actor;

using Microsoft.Extensions.Hosting;

namespace Aiakos.Orchestrator.Seats;

internal sealed class SeatRegionHost(
    ActorSystem actorSystem,
    ISeatActorReader reader,
    ISeatActorWriter writer,
    IEnumerable<IHarnessStateProfile> profiles,
    TimeProvider timeProvider) : IHostedService, IDisposable
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(30);
    private readonly CancellationTokenSource _stopping = new();
    private IActorRef? _region;

    internal IActorRef Region => _region ?? throw new InvalidOperationException("SEAT_ACTOR_UNAVAILABLE");

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var region = actorSystem.ActorOf(Props.Create(() => new SeatRegion(reader, writer,
            profiles.ToArray(), timeProvider, _stopping.Token)), "seats");
        _region = region;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        region.Tell(new StartSeatRegion(ready));
        await ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var region = _region;
        if (region is null)
            return;

        _stopping.Cancel();
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        region.Tell(new StopSeatRegion(stopped));
        try
        {
            await stopped.Task.WaitAsync(StopTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            actorSystem.Stop(region);
            throw;
        }
        finally
        {
            _stopping.Dispose();
        }

        actorSystem.Stop(region);
    }

    public void Dispose() => _stopping.Dispose();
}
