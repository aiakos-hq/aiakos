using System.Text;
using Aiakos.Core;
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
    public async Task DeliversLeadThenOneBracketedPasteThenSubmit()
    {
        await using var rig = await CreateRigAsync();
        var bodies = new[] { "one line", "quotes ' \" ` $HOME\tΕλλάδα ✓\n\nlast" };
        for (var index = 0; index < bodies.Length; index++)
        {
            var session = await rig.Host.StartAsync(rig.NewSpec($"impl-{index}"), CancellationToken.None);
            var body = bodies[index];
            var lead = "run command";
            await rig.Host.DeliverAsync(session, new DeliveryRequest(lead, body), CancellationToken.None);

            var expected = Encoding.UTF8.GetBytes(lead + "\u001b[200~" + body + "\u001b[201~\r");
            Assert.Equal(expected, await rig.ReceivedAsync(session));
        }
    }

    [Fact]
    public async Task KeepsALeadEndingInSemicolon()
    {
        await using var rig = await CreateRigAsync();
        var session = await rig.Host.StartAsync(rig.NewSpec(), CancellationToken.None);

        await rig.Host.DeliverAsync(session, new DeliveryRequest("run;", ""), CancellationToken.None);

        Assert.Equal(Encoding.UTF8.GetBytes("run;\r"), await rig.ReceivedAsync(session));
    }

    [Fact]
    public async Task NormalizesCrLfInTheBody()
    {
        await using var rig = await CreateRigAsync();
        var session = await rig.Host.StartAsync(rig.NewSpec(), CancellationToken.None);

        var report = await rig.Host.DeliverAsync(session, new DeliveryRequest("run", "a\r\nb"), CancellationToken.None);

        Assert.True(report.LineEndingsNormalized);
        Assert.Equal(Encoding.UTF8.GetBytes("run\u001b[200~a\nb\u001b[201~\r"),
            await rig.ReceivedAsync(session));
    }

    [Fact]
    public async Task RejectsEscapeInTheBody()
    {
        await using var rig = await CreateRigAsync();
        var session = await rig.Host.StartAsync(rig.NewSpec(), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<SessionHostException>(() =>
            rig.Host.DeliverAsync(session, new DeliveryRequest("run", "\u001b"), CancellationToken.None));

        Assert.Equal(SessionHostErrorCode.InputNotAllowed, exception.Code);
        Assert.Empty(await rig.ReceivedAsync(session));
    }

    [Fact]
    public async Task RejectsANewlineInTheLead()
    {
        await using var rig = await CreateRigAsync();
        var session = await rig.Host.StartAsync(rig.NewSpec(), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<SessionHostException>(() =>
            rig.Host.DeliverAsync(session, new DeliveryRequest("run\nnext", "body"), CancellationToken.None));

        Assert.Equal(SessionHostErrorCode.InputNotAllowed, exception.Code);
        Assert.Empty(await rig.ReceivedAsync(session));
    }

    [Fact]
    public async Task RejectsABodyOverOneMebibyte()
    {
        await using var rig = await CreateRigAsync();
        var session = await rig.Host.StartAsync(rig.NewSpec(), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<SessionHostException>(() => rig.Host.DeliverAsync(session,
            new DeliveryRequest("run", new string('x', InputValidator.MaxBodyBytes + 1)), CancellationToken.None));

        Assert.Equal(SessionHostErrorCode.PayloadTooLarge, exception.Code);
        Assert.Empty(await rig.ReceivedAsync(session));
    }

    [Fact]
    public async Task ReportsNotRequestedWithoutAConfirmer()
    {
        await using var rig = await CreateRigAsync();
        var session = await rig.Host.StartAsync(rig.NewSpec(), CancellationToken.None);

        var report = await rig.Host.DeliverAsync(session, new DeliveryRequest("run", "body"), CancellationToken.None);

        Assert.Equal(DeliveryStage.Submitted, report.Stage);
        Assert.Equal(ConfirmationOutcome.NotRequested, report.Confirmation.Outcome);
        Assert.Equal(0, report.Resubmits);
    }

    [Fact]
    public async Task ReportsTheConfirmersTurnId()
    {
        await using var rig = await CreateRigAsync();
        var session = await rig.Host.StartAsync(rig.NewSpec(), CancellationToken.None);

        var report = await rig.Host.DeliverAsync(session,
            new DeliveryRequest("run", "body", Confirmer: new FixedConfirmer(new Confirmation(ConfirmationOutcome.Confirmed, "turn-17"))),
            CancellationToken.None);

        Assert.Equal(ConfirmationOutcome.Confirmed, report.Confirmation.Outcome);
        Assert.Equal("turn-17", report.Confirmation.TurnId);
    }

    [Fact]
    public async Task RefusesASecondDeliveryWhileOneIsUnconfirmed()
    {
        await using var rig = await CreateRigAsync();
        var session = await rig.Host.StartAsync(rig.NewSpec(), CancellationToken.None);
        var confirmer = new BlockingConfirmer();
        var first = rig.Host.DeliverAsync(session,
            new DeliveryRequest("first", "body", Confirmer: confirmer), CancellationToken.None);
        await confirmer.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var exception = await Assert.ThrowsAsync<SessionHostException>(() => rig.Host.DeliverAsync(session,
            new DeliveryRequest("second", "body"), CancellationToken.None));

        Assert.Equal(SessionHostErrorCode.Busy, exception.Code);
        Assert.Equal(Encoding.UTF8.GetBytes("first\u001b[200~body\u001b[201~\r"),
            await rig.ReceivedAsync(session));
        confirmer.Complete.TrySetResult(new Confirmation(ConfirmationOutcome.Unconfirmed, null));
        await first;
    }

    [Fact]
    public async Task ReportsTheBufferLoadedStageForAnAlreadyCancelledRequest()
    {
        await using var rig = await CreateRigAsync();
        var session = await rig.Host.StartAsync(rig.NewSpec(), CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var report = await rig.Host.DeliverAsync(session, new DeliveryRequest("l", "b"), cancellation.Token);

        Assert.Equal(DeliveryStage.BufferLoaded, report.Stage);
        Assert.Empty(await rig.ReceivedAsync(session));
    }

    [Fact]
    public async Task RejectsAnUnsupportedSubmitDelayBeforeSendingInput()
    {
        await using var rig = await CreateRigAsync();
        var session = await rig.Host.StartAsync(rig.NewSpec(), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<SessionHostException>(() => rig.Host.DeliverAsync(session,
            new DeliveryRequest("l", "b", SubmitDelay: TimeSpan.MaxValue), CancellationToken.None));

        Assert.Equal(SessionHostErrorCode.InvalidArgument, exception.Code);
        Assert.Empty(await rig.ReceivedAsync(session));
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
        Assert.Equal(ReadOnlyAttachCommand, rig.Host.GetAttachCommand(session, readOnlyMode: true));
        Assert.Equal(WritableAttachCommand, rig.Host.GetAttachCommand(session, readOnlyMode: false));
        Assert.Empty(await rig.ReceivedAsync(session));
    }

    [Fact]
    public async Task AllowsOneResubmit()
    {
        await using var rig = await CreateRigAsync();
        var session = await rig.Host.StartAsync(rig.NewSpec(), CancellationToken.None);
        var confirmer = new ResubmittingConfirmer();

        var report = await rig.Host.DeliverAsync(session,
            new DeliveryRequest("run", "", Confirmer: confirmer), CancellationToken.None);

        Assert.Equal(1, report.Resubmits);
        Assert.Equal(Encoding.UTF8.GetBytes("run\r\r"), await rig.ReceivedAsync(session));
        Assert.IsType<InvalidOperationException>(confirmer.SecondCallException);
    }

    [Fact]
    public async Task CaptureWorksWhileADeliveryWaits()
    {
        await using var rig = await CreateRigAsync();
        var session = await rig.Host.StartAsync(rig.NewSpec(), CancellationToken.None);
        await rig.PrintAsync(session, ["capture during delivery"]);
        var confirmer = new CapturingConfirmer();
        var delivery = rig.Host.DeliverAsync(session,
            new DeliveryRequest("run", "body", Confirmer: confirmer), CancellationToken.None);

        var snapshot = await confirmer.Captured.Task.WaitAsync(TimeSpan.FromSeconds(1));
        confirmer.Complete.TrySetResult(new Confirmation(ConfirmationOutcome.Confirmed, "turn"));
        await delivery;

        Assert.Contains("capture during delivery", snapshot.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendsNamedKeys()
    {
        await using var rig = await CreateRigAsync();
        var session = await rig.Host.StartAsync(rig.NewSpec(), CancellationToken.None);

        await rig.Host.SendKeysAsync(session, [NamedKey.Enter, NamedKey.Up, NamedKey.CtrlC],
            CancellationToken.None);

        Assert.Equal(new byte[] { 0x0d, 0x1b, (byte)'[', (byte)'A', 0x03 },
            await rig.ReceivedAsync(session));
    }

    [Fact]
    public async Task RejectsMoreThanThirtyTwoKeys()
    {
        await using var rig = await CreateRigAsync();
        var session = await rig.Host.StartAsync(rig.NewSpec(), CancellationToken.None);

        var exception = await Assert.ThrowsAsync<SessionHostException>(() =>
            rig.Host.SendKeysAsync(session, Enumerable.Repeat(NamedKey.Enter, 33).ToArray(), CancellationToken.None));

        Assert.Equal(SessionHostErrorCode.InvalidArgument, exception.Code);
        Assert.Empty(await rig.ReceivedAsync(session));
    }

    [Fact]
    public async Task CaptureDropsOldestLinesFirst()
    {
        await using var rig = await CreateRigAsync();
        var session = await rig.Host.StartAsync(rig.NewSpec(), CancellationToken.None);
        var lines = Enumerable.Range(0, 500)
            .Select(index => $"line-{index:D3}-{new string('x', 20)}")
            .ToArray();
        await rig.PrintAsync(session, lines);

        var snapshot = await rig.Host.CaptureAsync(session, new CaptureRequest(HistoryLines: 500, MaxBytes: 4096),
            CancellationToken.None);

        Assert.True(snapshot.Truncated);
        Assert.DoesNotContain(lines[0], snapshot.Text, StringComparison.Ordinal);
        for (var index = lines.Length - TerminalSize.Default.Rows; index < lines.Length; index++)
            Assert.Contains(lines[index], snapshot.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CaptureWorksOnADeadPane()
    {
        await using var rig = await CreateRigAsync();
        var session = await rig.Host.StartAsync(rig.NewSpec(), CancellationToken.None);
        await rig.PrintAsync(session, ["last screen"]);
        await rig.ExitPaneAsync(session, 9);

        var snapshot = await rig.Host.CaptureAsync(session, new CaptureRequest(), CancellationToken.None);

        Assert.True(snapshot.PaneDead);
        Assert.Contains("last screen", snapshot.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StopCancelsADeliveryInFlight()
    {
        await using var rig = await CreateRigAsync();
        var session = await rig.Host.StartAsync(rig.NewSpec(), CancellationToken.None);
        var confirmer = new CancellationConfirmer();
        var delivery = rig.Host.DeliverAsync(session,
            new DeliveryRequest("run", "body", Confirmer: confirmer), CancellationToken.None);
        await confirmer.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        var stop = await rig.Host.StopAsync(session, new StopRequest(TimeSpan.Zero), CancellationToken.None);
        var report = await delivery.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(StopOutcome.Stopped, stop.Outcome);
        Assert.Equal(DeliveryStage.Submitted, report.Stage);
    }

    [Fact]
    public async Task AStaleHandleIsNotFound()
    {
        await using var rig = await CreateRigAsync();
        var host = rig.Host;
        var old = await host.StartAsync(rig.NewSpec(), CancellationToken.None);
        await host.StopAsync(old, new StopRequest(TimeSpan.Zero), CancellationToken.None);
        var current = await host.StartAsync(rig.NewSpec(), CancellationToken.None);

        await AssertNotFound(() => host.DeliverAsync(old, new DeliveryRequest("run", "body"), CancellationToken.None));
        await AssertNotFound(() => host.SendKeysAsync(old, [NamedKey.Enter], CancellationToken.None));
        await AssertNotFound(() => host.CaptureAsync(old, new CaptureRequest(), CancellationToken.None));
        await AssertNotFound(() => host.StopAsync(old, new StopRequest(TimeSpan.Zero), CancellationToken.None));
        Assert.Empty(await rig.ReceivedAsync(current));
    }

    private static async Task AssertNotFound(Func<Task> operation)
    {
        var exception = await Assert.ThrowsAsync<SessionHostException>(operation);
        Assert.Equal(SessionHostErrorCode.NotFound, exception.Code);
    }

    private sealed class ResubmittingConfirmer : IDeliveryConfirmer
    {
        public Exception? SecondCallException { get; private set; }

        public async Task<Confirmation> ConfirmAsync(DeliveryContext context, CancellationToken ct)
        {
            await context.ResubmitAsync("retry", ct);
            try
            {
                await context.ResubmitAsync("second retry", ct);
            }
            catch (Exception exception)
            {
                SecondCallException = exception;
            }

            return new Confirmation(ConfirmationOutcome.Unconfirmed, null);
        }
    }

    private sealed class CapturingConfirmer : IDeliveryConfirmer
    {
        public TaskCompletionSource<PaneSnapshot> Captured { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<Confirmation> Complete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<Confirmation> ConfirmAsync(DeliveryContext context, CancellationToken ct)
        {
            Captured.TrySetResult(await context.CaptureAsync(new CaptureRequest(), ct));
            return await Complete.Task;
        }
    }

    private sealed class CancellationConfirmer : IDeliveryConfirmer
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Confirmation> ConfirmAsync(DeliveryContext context, CancellationToken ct)
        {
            Started.TrySetResult();
            return Task.Delay(Timeout.InfiniteTimeSpan, ct)
                .ContinueWith(_ => new Confirmation(ConfirmationOutcome.Unconfirmed, null),
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    private sealed class FixedConfirmer(Confirmation confirmation) : IDeliveryConfirmer
    {
        public Task<Confirmation> ConfirmAsync(DeliveryContext context, CancellationToken ct) =>
            Task.FromResult(confirmation);
    }

    private sealed class BlockingConfirmer : IDeliveryConfirmer
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<Confirmation> Complete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Confirmation> ConfirmAsync(DeliveryContext context, CancellationToken ct)
        {
            Started.TrySetResult();
            return Complete.Task;
        }
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
