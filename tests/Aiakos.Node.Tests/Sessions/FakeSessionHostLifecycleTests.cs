using Aiakos.Node.Sessions;
using Aiakos.Node.Testing;

namespace Aiakos.Node.Tests.Sessions;

public sealed class FakeSessionHostLifecycleTests
{
    [Fact]
    public async Task StopReadsTheHostGracefulStopFlagAtStopTime()
    {
        await using var rig = new FakeSessionHostRig();
        var host = (FakeSessionHost)rig.Host;
        var cancellationToken = TestContext.Current.CancellationToken;
        var session = await host.StartAsync(rig.NewSpec(), cancellationToken);
        host.IgnoresGracefulStop = true;

        var report = await host.StopAsync(session, new StopRequest(TimeSpan.Zero), cancellationToken);

        Assert.Equal(StopOutcome.Killed, report.Outcome);
    }

    [Fact]
    public async Task VanishPublishesOneSessionVanishedEvent()
    {
        await using var rig = new FakeSessionHostRig();
        var host = (FakeSessionHost)rig.Host;
        var cancellationToken = TestContext.Current.CancellationToken;
        var session = await host.StartAsync(rig.NewSpec(), cancellationToken);
        await using var events = host.WatchAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);

        host.Vanish(session);

        Assert.True(await events.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1), cancellationToken));
        Assert.IsType<SessionHostEvent.SessionVanished>(events.Current);
        host.Vanish(session);
    }
}
