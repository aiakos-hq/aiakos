using System.Diagnostics.Metrics;
using System.Text;
using Aiakos.Node.Sessions;
using Aiakos.Node.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aiakos.Node.Tests.Sessions;

public sealed class TmuxSessionInputTests
{
    [Fact]
    public async Task CaptureSanitizesBytesAndUsesPaneOnlyCaptureArguments()
    {
        var bytes = Encoding.UTF8.GetBytes("one  \n\u001btwo\t \n").Concat(new byte[] { 0xff, (byte)'\n', (byte)'\n' }).ToArray();
        await using var fixture = await Fixture.CreateAsync(bytes, "123\tlaunch\t80\t24\t80\t23\t0\n");

        var pane = await fixture.Input.CaptureAsync(Handle(), new CaptureRequest(), CancellationToken.None);

        Assert.Equal("one\ntwo\n�", pane.Text);
        Assert.Equal(3, pane.Lines);
        Assert.Equal(new TerminalSize(80, 24), pane.Size);
        Assert.Equal((80, 23), pane.Cursor);
        Assert.False(pane.PaneDead);
        Assert.Equal("capture-pane", fixture.Runner.Requests[^1].Arguments[5]);
        Assert.Contains("-p", fixture.Runner.Requests[^1].Arguments);
        Assert.DoesNotContain("-e", fixture.Runner.Requests[^1].Arguments);
        Assert.DoesNotContain("-J", fixture.Runner.Requests[^1].Arguments);
    }

    [Theory]
    [InlineData(-1, 100)]
    [InlineData(10001, 100)]
    [InlineData(0, 0)]
    [InlineData(0, 1048577)]
    public async Task CaptureRejectsInvalidBoundsBeforeTmux(int historyLines, int maxBytes)
    {
        await using var fixture = await Fixture.CreateAsync([]);

        var exception = await Assert.ThrowsAsync<SessionHostException>(() => fixture.Input.CaptureAsync(
            Handle(), new CaptureRequest(historyLines, maxBytes), CancellationToken.None));

        Assert.Equal(SessionHostErrorCode.InvalidArgument, exception.Code);
        Assert.Equal("Invalid capture request.", exception.Message);
        Assert.Equal(2, fixture.Runner.Requests.Count);
    }

    [Fact]
    public async Task CaptureKeepsVisibleScreenWhenHistoryCapIsExceeded()
    {
        await using var fixture = await Fixture.CreateAsync(Encoding.UTF8.GetBytes("old\nA\nB"),
            "123\tlaunch\t80\t2\t2\t1\t0\n");

        var pane = await fixture.Input.CaptureAsync(Handle(), new CaptureRequest(10, 4), CancellationToken.None);

        Assert.Equal("A\nB", pane.Text);
        Assert.Equal(2, pane.Lines);
        Assert.True(pane.Truncated);
        Assert.Equal(["-S", "-10"], fixture.Runner.Requests[^1].Arguments.TakeLast(2));
    }

    [Fact]
    public async Task CapturePreservesOversizeVisibleScreenAndMarksTruncated()
    {
        await using var fixture = await Fixture.CreateAsync(Encoding.UTF8.GetBytes("abc\ndef"),
            "123\tlaunch\t80\t2\t0\t1\t0\n");

        var pane = await fixture.Input.CaptureAsync(Handle(), new CaptureRequest(0, 1), CancellationToken.None);

        Assert.Equal("abc\ndef", pane.Text);
        Assert.True(pane.Truncated);
    }

    [Fact]
    public async Task VisibleBlankRowsProtectOnlyTheCurrentScreenWhenCappingHistory()
    {
        await using var fixture = await Fixture.CreateAsync(Encoding.UTF8.GetBytes("old-history\nvisible\n\n\n"),
            "123\tlaunch\t80\t3\t0\t2\t0\n");

        var pane = await fixture.Input.CaptureAsync(Handle(), new CaptureRequest(10, 7), CancellationToken.None);

        Assert.Equal("visible", pane.Text);
        Assert.Equal(1, pane.Lines);
        Assert.True(pane.Truncated);
    }

    [Fact]
    public async Task CaptureDropsFirstPartialLineFromTailAndAllowsDeadReadonlyPane()
    {
        await using var fixture = await Fixture.CreateAsync(Encoding.UTF8.GetBytes("partial\nkept\n"),
            "123\tlaunch\t80\t24\t0\t0\t1\n", truncated: true, processes: []);

        var pane = await fixture.Input.CaptureAsync(Handle(readOnly: true), new CaptureRequest(), CancellationToken.None);

        Assert.Equal("kept", pane.Text);
        Assert.True(pane.Truncated);
        Assert.True(pane.PaneDead);
        Assert.Equal(0, fixture.Snapshot.ReadCalls);
    }

    [Fact]
    public async Task MutableHandleVerificationRejectsMismatchedIdentityWithoutMutation()
    {
        await using var fixture = await Fixture.CreateAsync([], "999\tlaunch\t0\t0\t0\t0\t0\n");

        var exception = await Assert.ThrowsAsync<SessionHostException>(() =>
            fixture.Input.VerifyMutableHandleAsync(Handle(), CancellationToken.None));

        Assert.Equal(SessionHostErrorCode.NotFound, exception.Code);
        Assert.Equal("Session was not found.", exception.Message);
        Assert.Equal("display-message", fixture.Runner.Requests[^1].Arguments[5]);
        Assert.Equal(3, fixture.Runner.Requests.Count);
    }

    [Theory]
    [InlineData("999\tlaunch\t80\t24\t0\t0\t0\n")]
    [InlineData("123\tother\t80\t24\t0\t0\t0\n")]
    [InlineData("123\tlaunch\t80\t24\t81\t0\t0\n")]
    [InlineData("123\tlaunch\t80\t24\t0\t24\t0\n")]
    [InlineData("123\tlaunch\t0\t24\t0\t0\t0\n")]
    public async Task CaptureRejectsMalformedMetadataAndIdentity(string metadata)
    {
        await using var fixture = await Fixture.CreateAsync([], metadata);

        var exception = await Assert.ThrowsAsync<SessionHostException>(() => fixture.Input.CaptureAsync(
            Handle(), new CaptureRequest(), CancellationToken.None));

        Assert.Equal(SessionHostErrorCode.NotFound, exception.Code);
        Assert.Equal(3, fixture.Runner.Requests.Count);
    }

    [Fact]
    public async Task CaptureRejectsReusedPaneProcessStartTime()
    {
        await using var fixture = await Fixture.CreateAsync([], "123\tlaunch\t80\t24\t0\t0\t0\n",
            processes: [new ProcessInfo(123, 1, 123, 457, [])]);

        var exception = await Assert.ThrowsAsync<SessionHostException>(() => fixture.Input.CaptureAsync(
            Handle(), new CaptureRequest(), CancellationToken.None));

        Assert.Equal(SessionHostErrorCode.NotFound, exception.Code);
        Assert.Equal(3, fixture.Runner.Requests.Count);
    }

    [Fact]
    public async Task DeliveryReverifiesBeforeEachPaneMutationAndPreservesLeadArgumentBoundary()
    {
        await using var fixture = await Fixture.CreateAsync([]);
        for (var index = 0; index < 4; index++)
        {
            fixture.Runner.Enqueue(Result(stdout: "123\tlaunch\t0\n"));
            fixture.Runner.Enqueue(Result());
        }

        var report = await fixture.Input.DeliverAsync(Handle(), new DeliveryRequest("run;", "body"),
            CancellationToken.None);

        Assert.Equal(DeliveryStage.Submitted, report.Stage);
        var commands = fixture.Runner.Requests.Skip(2).Select(request => request.Arguments[5]).ToArray();
        Assert.Equal(["display-message", "load-buffer", "display-message", "send-keys", "display-message",
            "paste-buffer", "display-message", "send-keys"], commands);
        var lead = fixture.Runner.Requests.First(request => request.Arguments.Contains("-l"));
        Assert.Equal("run\\;", lead.Arguments[^1]);
        Assert.Equal(Encoding.UTF8.GetBytes("body"), fixture.Runner.Requests.Skip(2)
            .Single(request => request.Arguments[5] == "load-buffer").Stdin!.Value.ToArray());
    }

    [Fact]
    public async Task SendKeysVerifiesHandleBeforeMutationAndRejectsReadonlyBeforeTmux()
    {
        await using var fixture = await Fixture.CreateAsync([]);
        fixture.Runner.Enqueue(Result(stdout: "123\tlaunch\t0\n"));
        fixture.Runner.Enqueue(Result());

        await fixture.Input.SendKeysAsync(Handle(), [NamedKey.Enter, NamedKey.CtrlC], CancellationToken.None);

        Assert.Equal("display-message", fixture.Runner.Requests[^2].Arguments[5]);
        Assert.Equal("send-keys", fixture.Runner.Requests[^1].Arguments[5]);
        Assert.Equal(["Enter", "C-c"], fixture.Runner.Requests[^1].Arguments.TakeLast(2));
        var count = fixture.Runner.Requests.Count;
        await Assert.ThrowsAsync<SessionHostException>(() => fixture.Input.SendKeysAsync(
            Handle(readOnly: true), [NamedKey.Enter], CancellationToken.None));
        Assert.Equal(count, fixture.Runner.Requests.Count);
    }

    [Fact]
    public async Task ResubmitVerifiesTheHandleAndRecordsTheAcceptedReason()
    {
        await using var fixture = await Fixture.CreateAsync([]);
        for (var index = 0; index < 4; index++)
        {
            fixture.Runner.Enqueue(Result(stdout: "123\tlaunch\t0\n"));
            fixture.Runner.Enqueue(Result());
        }

        var report = await fixture.Input.DeliverAsync(Handle(), new DeliveryRequest("run", "",
            Confirmer: new ResubmitConfirmer()), CancellationToken.None);

        Assert.Equal(1, report.Resubmits);
        Assert.Equal("retry reason", report.ResubmitReason);
        var commands = fixture.Runner.Requests.Skip(2).Select(request => request.Arguments[5]).ToArray();
        Assert.Equal(["display-message", "load-buffer", "display-message", "send-keys",
            "display-message", "send-keys", "display-message", "send-keys"], commands);
    }

    [Fact]
    public async Task CaptureRemainsAvailableWhileDeliveryConfirmerHoldsTheGate()
    {
        await using var fixture = await Fixture.CreateAsync([]);
        EnqueueDeliveryResults(fixture.Runner);
        var confirmer = new BlockingConfirmer();
        var delivery = fixture.Input.DeliverAsync(Handle(), new DeliveryRequest("run", "body", Confirmer: confirmer),
            CancellationToken.None);
        await confirmer.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        fixture.Runner.Enqueue(Result(stdout: "123\tlaunch\t80\t24\t0\t0\t0\n"));
        fixture.Runner.Enqueue(new ProcessResult(ProcessOutcome.Exited, 0, Encoding.UTF8.GetBytes("still here"), [], false, false));

        var pane = await fixture.Input.CaptureAsync(Handle(), new CaptureRequest(), CancellationToken.None);
        confirmer.Complete.TrySetResult(new Confirmation(ConfirmationOutcome.Confirmed, "turn"));
        await delivery;

        Assert.Equal("still here", pane.Text);
    }

    [Fact]
    public async Task CancelDeliveryCancelsOnlyTheActiveDeliveryAndReturnsItsLastStage()
    {
        await using var fixture = await Fixture.CreateAsync([]);
        EnqueueDeliveryResults(fixture.Runner);
        var confirmer = new BlockingConfirmer();
        var delivery = fixture.Input.DeliverAsync(Handle(), new DeliveryRequest("run", "body", Confirmer: confirmer),
            CancellationToken.None);
        await confirmer.Entered.Task.WaitAsync(TestContext.Current.CancellationToken);

        fixture.Input.CancelDelivery(Handle());
        var report = await delivery.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStage.Submitted, report.Stage);
    }

    [Fact]
    public async Task PreCancelledDeliveryStillVerifiesAndLoadsBeforeReturningCancellation()
    {
        await using var fixture = await Fixture.CreateAsync([]);
        fixture.Runner.Enqueue(Result(stdout: "123\tlaunch\t0\n"));
        fixture.Runner.Enqueue(Result());
        fixture.Runner.Enqueue(Result());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var report = await fixture.Input.DeliverAsync(Handle(), new DeliveryRequest("run", "body"),
            cancellation.Token);

        Assert.Equal(DeliveryStage.BufferLoaded, report.Stage);
        Assert.Equal(SessionHostErrorCode.TmuxFailed, report.Error?.Code);
        Assert.Equal("Delivery was cancelled.", report.Error?.Message);
        Assert.Equal(["display-message", "load-buffer", "delete-buffer"],
            fixture.Runner.Requests.Skip(2).Select(request => request.Arguments[5]));
    }

    [Fact]
    public async Task InitialIdentityFailureWithCancelledCallerDoesNotLoadAndReleasesGate()
    {
        await using var fixture = await Fixture.CreateAsync([]);
        fixture.Runner.Enqueue(Result(stdout: "999\tlaunch\t0\n"));
        fixture.Runner.Enqueue(Result(stdout: "123\tlaunch\t0\n"));
        fixture.Runner.Enqueue(Result());
        fixture.Runner.Enqueue(Result(stdout: "123\tlaunch\t0\n"));
        fixture.Runner.Enqueue(Result());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Assert.ThrowsAsync<SessionHostException>(() => fixture.Input.DeliverAsync(
            Handle(), new DeliveryRequest("run", "body"), cancellation.Token));
        Assert.Equal(SessionHostErrorCode.NotFound, exception.Code);

        var report = await fixture.Input.DeliverAsync(Handle(), new DeliveryRequest("run", "body"),
            cancellation.Token);
        Assert.Equal(DeliveryStage.BufferLoaded, report.Stage);
        Assert.Equal(2, fixture.Runner.Requests.Skip(2).Count(request => request.Arguments[5] == "display-message"));
    }

    [Fact]
    public async Task FailedLeadReturnsLastSuccessfulStageAndCleansBuffer()
    {
        await using var fixture = await Fixture.CreateAsync([]);
        fixture.Runner.Enqueue(Result(stdout: "123\tlaunch\t0\n"));
        fixture.Runner.Enqueue(Result());
        fixture.Runner.Enqueue(Result(stdout: "123\tlaunch\t0\n"));
        fixture.Runner.Enqueue(Result(exitCode: 1, stderr: "tmux failure"));
        fixture.Runner.Enqueue(Result());

        var report = await fixture.Input.DeliverAsync(Handle(), new DeliveryRequest("run", "body"),
            CancellationToken.None);

        Assert.Equal(DeliveryStage.BufferLoaded, report.Stage);
        Assert.Equal(SessionHostErrorCode.TmuxFailed, report.Error?.Code);
        Assert.False(report.Error?.Retryable);
        Assert.Equal("delete-buffer", fixture.Runner.Requests[^1].Arguments[5]);
        Assert.DoesNotContain(fixture.Runner.Requests.Skip(2), request => request.Arguments.Contains("C-m"));
    }

    private static SessionHandle Handle(bool readOnly = false) =>
        new("seat", "launch", "session", "$1", "%123", 123, 456, readOnly);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _home;
        private readonly ServiceProvider _services;

        private Fixture(string home, ServiceProvider services, FakeProcessRunner runner,
            TmuxSessionInput input, FakeSnapshot snapshot)
        {
            _home = home;
            _services = services;
            Runner = runner;
            Input = input;
            Snapshot = snapshot;
        }

        public FakeProcessRunner Runner { get; }
        public TmuxSessionInput Input { get; }
        public FakeSnapshot Snapshot { get; }

        public static async Task<Fixture> CreateAsync(byte[] capture, string? metadata = null,
            bool truncated = false, IReadOnlyList<ProcessInfo>? processes = null)
        {
            Assert.SkipUnless(OperatingSystem.IsLinux(), "Tmux initialization is Linux-only.");
            var home = Path.Combine(Path.GetTempPath(), $"aiakos-tmux-input-{Guid.NewGuid():N}");
            Directory.CreateDirectory(home);
            var runner = new FakeProcessRunner();
            runner.Enqueue(Result(stdout: "tmux 3.4\n"));
            runner.Enqueue(Result(exitCode: 1, stderr: "no server running"));
            if (metadata is not null)
            {
                runner.Enqueue(Result(stdout: metadata));
                runner.Enqueue(new ProcessResult(ProcessOutcome.Exited, 0, capture, [], truncated, false));
            }
            var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
            var client = new TmuxClient(new TmuxHostOptions { Instance = "test", Home = home,
                    TmuxPath = "/usr/bin/tmux" }, runner, new Dictionary<string, string>(),
                NullLogger<TmuxClient>.Instance, services.GetRequiredService<IMeterFactory>());
            await client.InitializeAsync(TestContext.Current.CancellationToken);
            var snapshot = new FakeSnapshot(processes ?? [new ProcessInfo(123, 1, 123, 456, [])]);
            var input = new TmuxSessionInput(client, snapshot, NullLogger<TmuxSessionInput>.Instance,
                services.GetRequiredService<IMeterFactory>());
            return new Fixture(home, services, runner, input, snapshot);
        }

        public ValueTask DisposeAsync()
        {
            _services.Dispose();
            if (Directory.Exists(_home)) Directory.Delete(_home, recursive: true);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeSnapshot(IReadOnlyList<ProcessInfo> processes) : IProcessSnapshot
    {
        public int ReadCalls { get; private set; }

        public Task<IReadOnlyList<ProcessInfo>> ReadAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ReadCalls++;
            return Task.FromResult(processes);
        }
    }

    private sealed class ResubmitConfirmer : IDeliveryConfirmer
    {
        public async Task<Confirmation> ConfirmAsync(DeliveryContext context, CancellationToken ct)
        {
            await context.ResubmitAsync("retry reason", ct);
            return new Confirmation(ConfirmationOutcome.Unconfirmed, null);
        }
    }

    private static void EnqueueDeliveryResults(FakeProcessRunner runner)
    {
        for (var index = 0; index < 4; index++)
        {
            runner.Enqueue(Result(stdout: "123\tlaunch\t0\n"));
            runner.Enqueue(Result());
        }
    }

    private sealed class BlockingConfirmer : IDeliveryConfirmer
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<Confirmation> Complete { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<Confirmation> ConfirmAsync(DeliveryContext context, CancellationToken ct)
        {
            Entered.TrySetResult();
            return await Complete.Task.WaitAsync(ct);
        }
    }

    private static ProcessResult Result(int exitCode = 0, string? stderr = null, string? stdout = null) =>
        new(ProcessOutcome.Exited, exitCode, Encoding.UTF8.GetBytes(stdout ?? ""),
            Encoding.UTF8.GetBytes(stderr ?? ""), false, false);
}
