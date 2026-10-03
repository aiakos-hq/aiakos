using Aiakos.Node.Sessions;
using Aiakos.Node.Testing;

namespace Aiakos.Node.Tests.Sessions;

public sealed class FakeUnavailableTests
{
    [Fact]
    public async Task ReportsUnavailableAndRejectsEveryOperation()
    {
        await using var rig = new FakeSessionHostRig();
        var host = (FakeSessionHost)rig.Host;
        var spec = rig.NewSpec();
        var handle = await host.StartAsync(spec, CancellationToken.None);
        var listing = new SessionListing(handle.SessionName, handle.SessionId, handle.PaneId, handle.PanePid,
            false, null, null, null, ListingClass.Managed, null);
        host.StatusUnresponsive = true;
        var unknown = await host.GetStatusAsync(handle, CancellationToken.None);
        Assert.Equal(PaneState.Unknown, unknown.State);
        Assert.Contains("unresponsive", unknown.Reason, StringComparison.Ordinal);

        host.MakeUnavailable("offline");

        Assert.Equal("fake", host.Info.Kind);
        Assert.Equal(SessionHostAvailability.Unavailable, host.Info.Availability);
        Assert.Equal("offline", host.Info.Reason);
        Assert.Empty(host.Info.Capabilities);
        await AssertUnavailable(() => host.StartAsync(spec, CancellationToken.None));
        await AssertUnavailable(() => host.DeliverAsync(handle, new DeliveryRequest("", ""), CancellationToken.None));
        await AssertUnavailable(() => host.SendKeysAsync(handle, [], CancellationToken.None));
        await AssertUnavailable(() => host.CaptureAsync(handle, new CaptureRequest(), CancellationToken.None));
        await AssertUnavailable(() => host.GetStatusAsync(handle, CancellationToken.None));
        await AssertUnavailable(() => host.StopAsync(handle, new StopRequest(TimeSpan.Zero), CancellationToken.None));
        await AssertUnavailable(() => host.ListAsync(CancellationToken.None));
        await AssertUnavailable(() => host.AdoptAsync(listing, CancellationToken.None));
        Assert.Throws<SessionHostException>(() => host.GetAttachCommand(handle));
        await AssertUnavailable(() => Task.Run(() => host.Received(handle)));
        await AssertUnavailable(() => Task.Run(() => host.Print(handle, [])));
        await AssertUnavailable(() => Task.Run(() => host.Exit(handle, 1, null)));
        await AssertUnavailable(() => Task.Run(() => host.Vanish(handle)));
        await AssertUnavailable(async () =>
        {
            await using var events = host.WatchAsync(CancellationToken.None).GetAsyncEnumerator();
            await events.MoveNextAsync();
        });
    }

    private static async Task AssertUnavailable(Func<Task> operation)
    {
        var exception = await Assert.ThrowsAsync<SessionHostException>(operation);
        Assert.Equal(SessionHostErrorCode.Unavailable, exception.Code);
    }
}
