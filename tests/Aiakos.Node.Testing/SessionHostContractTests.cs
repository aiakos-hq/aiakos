using Aiakos.Node.Sessions;

namespace Aiakos.Node.Testing;

public abstract class SessionHostContractTests
{
    private static readonly string[] ReadOnlyAttachCommand = ["fake-attach", "-r", "=demo_impl"];
    private static readonly string[] WritableAttachCommand = ["fake-attach", "=demo_impl"];

    protected abstract Task<ISessionHostRig> CreateRigAsync();

    [Fact]
    public async Task StartReturnsAHandleNamedAfterTheSeat()
    {
        await using var rig = await CreateRigAsync();
        var session = await rig.Host.StartAsync(rig.NewSpec(), CancellationToken.None);

        Assert.Equal("demo_impl", session.SessionName);
        Assert.Equal(PaneState.Alive, (await rig.Host.GetStatusAsync(session, CancellationToken.None)).State);
        Assert.Empty(await rig.ReceivedAsync(session));
    }

    [Fact]
    public async Task StartRejectsAMissingWorkingDirectory()
    {
        await using var rig = await CreateRigAsync();
        var spec = rig.NewSpec() with { WorkingDirectory = rig.MissingDirectory };

        var exception = await Assert.ThrowsAsync<SessionHostException>(
            () => rig.Host.StartAsync(spec, CancellationToken.None));

        Assert.Equal(SessionHostErrorCode.InvalidArgument, exception.Code);
        Assert.Empty(await rig.Host.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task StartRejectsASecondLaunchOfALiveSeat()
    {
        await using var rig = await CreateRigAsync();
        var first = await rig.Host.StartAsync(rig.NewSpec(), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<SessionHostException>(
            () => rig.Host.StartAsync(rig.NewSpec(), CancellationToken.None));

        Assert.Equal(SessionHostErrorCode.AlreadyRunning, exception.Code);
        Assert.Equal(first.LaunchId, exception.Error.Metadata!["launch_id"]);
        Assert.Equal(PaneState.Alive, (await rig.Host.GetStatusAsync(first, CancellationToken.None)).State);
    }

    [Fact]
    public async Task StartReplacesADeadPane()
    {
        await using var rig = await CreateRigAsync();
        var first = await rig.Host.StartAsync(rig.NewSpec(), CancellationToken.None);
        using var watcherCancellation = new CancellationTokenSource();
        await using var events = rig.Host.WatchAsync(watcherCancellation.Token).GetAsyncEnumerator();
        await rig.ExitPaneAsync(first, 7);

        var moved = await events.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
        var paneExited = Assert.IsType<SessionHostEvent.PaneExited>(events.Current);
        var replacement = await rig.Host.StartAsync(rig.NewSpec(), CancellationToken.None);

        Assert.True(moved);
        Assert.Equal(7, paneExited.ExitCode);
        Assert.NotEqual(first.SessionId, replacement.SessionId);
        await AssertNoEventAsync(events, watcherCancellation);
    }

    [Fact]
    public async Task ReportsExitCodeOnce()
    {
        await using var rig = await CreateRigAsync();
        var session = await rig.Host.StartAsync(rig.NewSpec(), CancellationToken.None);
        using var watcherCancellation = new CancellationTokenSource();
        await using var events = rig.Host.WatchAsync(watcherCancellation.Token).GetAsyncEnumerator();
        await rig.ExitPaneAsync(session, 7);
        await rig.ExitPaneAsync(session, 7);

        var status = await rig.Host.GetStatusAsync(session, CancellationToken.None);
        Assert.Equal(PaneState.Exited, status.State);
        Assert.Equal(7, status.ExitCode);
        Assert.Null(status.Signal);
        Assert.True(await events.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(7, Assert.IsType<SessionHostEvent.PaneExited>(events.Current).ExitCode);
        await AssertNoEventAsync(events, watcherCancellation);
    }

    [Fact]
    public async Task StopReportsStopped()
    {
        await using var rig = await CreateRigAsync();
        var session = await rig.Host.StartAsync(rig.NewSpec(), CancellationToken.None);
        using var watcherCancellation = new CancellationTokenSource();
        await using var events = rig.Host.WatchAsync(watcherCancellation.Token).GetAsyncEnumerator();

        var report = await rig.Host.StopAsync(session, new StopRequest(TimeSpan.Zero), CancellationToken.None);

        Assert.Equal(StopOutcome.Stopped, report.Outcome);
        Assert.Equal(PaneState.Missing, (await rig.Host.GetStatusAsync(session, CancellationToken.None)).State);
        await AssertNoEventAsync(events, watcherCancellation);
    }

    [Fact]
    public async Task StopReportsKilledWhenGracefulStopIsIgnored()
    {
        await using var rig = await CreateRigAsync();
        var session = await rig.Host.StartAsync(rig.NewSpec(ignoresGracefulStop: true), CancellationToken.None);
        using var watcherCancellation = new CancellationTokenSource();
        await using var events = rig.Host.WatchAsync(watcherCancellation.Token).GetAsyncEnumerator();

        var report = await rig.Host.StopAsync(session, new StopRequest(TimeSpan.Zero), CancellationToken.None);

        Assert.Equal(StopOutcome.Killed, report.Outcome);
        Assert.Equal(PaneState.Missing, (await rig.Host.GetStatusAsync(session, CancellationToken.None)).State);
        await AssertNoEventAsync(events, watcherCancellation);
    }

    [Fact]
    public async Task StopOfADeadPaneReportsNotRunning()
    {
        await using var rig = await CreateRigAsync();
        var session = await rig.Host.StartAsync(rig.NewSpec(), CancellationToken.None);
        using var watcherCancellation = new CancellationTokenSource();
        await using var events = rig.Host.WatchAsync(watcherCancellation.Token).GetAsyncEnumerator();
        await rig.ExitPaneAsync(session, 7);
        Assert.True(await events.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1)));

        var report = await rig.Host.StopAsync(session, new StopRequest(TimeSpan.Zero), CancellationToken.None);

        Assert.Equal(StopOutcome.NotRunning, report.Outcome);
        Assert.Equal(7, report.ExitCode);
        Assert.Equal(PaneState.Missing, (await rig.Host.GetStatusAsync(session, CancellationToken.None)).State);
        await AssertNoEventAsync(events, watcherCancellation);
    }

    [Fact]
    public async Task ListsStartedSessionAsManaged()
    {
        await using var rig = await CreateRigAsync();
        var spec = rig.NewSpec();
        var session = await rig.Host.StartAsync(spec, CancellationToken.None);

        var listing = Assert.Single(await rig.Host.ListAsync(CancellationToken.None));

        Assert.Equal(ListingClass.Managed, listing.Class);
        Assert.Equal("demo_impl", listing.SessionName);
        Assert.Equal(session.SessionId, listing.SessionId);
        Assert.Equal(session.PaneId, listing.PaneId);
        Assert.Equal(session.PanePid, listing.PanePid);
        Assert.NotNull(listing.Labels);
        Assert.Equal(spec.SeatId, listing.Labels.SeatId);
        Assert.Equal(spec.SeatAddress, listing.Labels.SeatAddress);
        Assert.Equal(spec.LaunchId, listing.Labels.LaunchId);
        Assert.Equal(spec.Harness, listing.Labels.Harness);
        Assert.Null(listing.Registry);
    }

    [Fact]
    public async Task AdoptsManagedListingIdempotently()
    {
        await using var rig = await CreateRigAsync();
        var session = await rig.Host.StartAsync(rig.NewSpec(), CancellationToken.None);
        var listing = Assert.Single(await rig.Host.ListAsync(CancellationToken.None));

        var first = await rig.Host.AdoptAsync(listing, CancellationToken.None);
        var second = await rig.Host.AdoptAsync(listing, CancellationToken.None);

        Assert.Equal(session, first);
        Assert.Equal(session, second);
        Assert.False(first.ReadOnly);
        Assert.Single(await rig.Host.ListAsync(CancellationToken.None));
        Assert.Empty(await rig.ReceivedAsync(session));
    }

    [Fact]
    public async Task ReturnsExactAttachCommand()
    {
        await using var rig = await CreateRigAsync();
        var session = await rig.Host.StartAsync(rig.NewSpec(), CancellationToken.None);

        Assert.Equal(ReadOnlyAttachCommand, rig.Host.GetAttachCommand(session));
        Assert.Equal(ReadOnlyAttachCommand,
            rig.Host.GetAttachCommand(session, readOnlyMode: true));
        Assert.Equal(WritableAttachCommand,
            rig.Host.GetAttachCommand(session, readOnlyMode: false));
        Assert.Empty(await rig.ReceivedAsync(session));
    }

    private static async Task AssertNoEventAsync(IAsyncEnumerator<SessionHostEvent> events,
        CancellationTokenSource watcherCancellation)
    {
        var moveNext = events.MoveNextAsync().AsTask();
        var completed = await Task.WhenAny(moveNext, Task.Delay(TimeSpan.FromMilliseconds(100)));
        if (completed != moveNext)
        {
            watcherCancellation.Cancel();
        }

        var hasNext = false;
        try
        {
            hasNext = await moveNext;
        }
        catch (OperationCanceledException)
        {
            // Cancellation confirms the event stream stayed quiet for the observation interval.
        }

        Assert.False(hasNext);
    }
}
