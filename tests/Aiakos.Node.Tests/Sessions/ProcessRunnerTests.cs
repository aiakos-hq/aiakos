using System.Diagnostics;
using Aiakos.Node.Sessions;
using Aiakos.Node.Testing;

namespace Aiakos.Node.Tests.Sessions;

public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task FakeProcessRunnerRecordsIndependentRequestSnapshotsAndReturnsQueuedResults()
    {
        var runner = new FakeProcessRunner();
        var arguments = new List<string> { "first" };
        var environment = new Dictionary<string, string> { ["MODE"] = "before" };
        var stdin = new byte[] { 1, 2, 3 };
        var request = new ProcessRequest("fixture", arguments, environment, stdin, TimeSpan.FromSeconds(1));
        var expected = new ProcessResult(ProcessOutcome.Exited, 0, [4], [], false, false);
        runner.Enqueue(expected);

        var result = await runner.RunAsync(request, TestContext.Current.CancellationToken);
        arguments[0] = "changed";
        environment["MODE"] = "changed";
        stdin[0] = 9;

        Assert.Same(expected, result);
        var recorded = Assert.Single(runner.Requests);
        Assert.Equal("first", Assert.Single(recorded.Arguments));
        Assert.Equal("before", recorded.Environment["MODE"]);
        Assert.Equal(new byte[] { 1, 2, 3 }, recorded.Stdin!.Value.ToArray());
        Assert.False(recorded.RetainStdoutTail);

        runner.Enqueue(expected);
        await runner.RunAsync(request with { RetainStdoutTail = true }, TestContext.Current.CancellationToken);
        Assert.True(runner.Requests[^1].RetainStdoutTail);
    }

    [Fact]
    public async Task FakeProcessRunnerChecksCancellationBeforeRecordingOrDequeuing()
    {
        var runner = new FakeProcessRunner();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        runner.Enqueue(new ProcessResult(ProcessOutcome.Exited, 0, [], [], false, false));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runner.RunAsync(new ProcessRequest("fixture", [], new Dictionary<string, string>(), null,
                TimeSpan.FromSeconds(1)), cancellation.Token));

        Assert.Empty(runner.Requests);
        await runner.RunAsync(new ProcessRequest("fixture", [], new Dictionary<string, string>(), null,
            TimeSpan.FromSeconds(1)), TestContext.Current.CancellationToken);
        Assert.Null(Assert.Single(runner.Requests).Stdin);
        var empty = await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(
            new ProcessRequest("fixture", [], new Dictionary<string, string>(), null, TimeSpan.FromSeconds(1)),
            TestContext.Current.CancellationToken));
        Assert.Equal("No scripted process result.", empty.Message);
    }

    [Fact]
    public async Task ProcessRunnerUsesExplicitArgumentsEnvironmentAndBoundedIndependentStreams()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The harmless process fixtures use standard Linux utilities.");
        Assert.SkipUnless(File.Exists("/usr/bin/env") && File.Exists("/usr/bin/tee") && File.Exists("/usr/bin/printf"),
            "Required standard process fixtures are unavailable.");
        using var runner = new ProcessRunner();
        var cancellationToken = TestContext.Current.CancellationToken;

        var arguments = await runner.RunAsync(Request("/usr/bin/printf", ["%s", "one argument with spaces"], null), cancellationToken);
        var environment = await runner.RunAsync(Request("/usr/bin/env", [], null,
            environment: new Dictionary<string, string> { ["ONLY_VALUE"] = "explicit" }), cancellationToken);
        var stdinBytes = new byte[] { 0, 1, 127, 128, 254, 255 };
        var echoedStdin = await runner.RunAsync(Request("/usr/bin/cat", [], stdinBytes), cancellationToken);
        var exactlyAtLimit = await runner.RunAsync(Request("/usr/bin/tee", ["/dev/stderr"],
            new byte[1048576]), cancellationToken);
        var overLimit = await runner.RunAsync(Request("/usr/bin/tee", ["/dev/stderr"],
            new byte[1048577]), cancellationToken);

        Assert.Equal(ProcessOutcome.Exited, arguments.Outcome);
        Assert.Equal("one argument with spaces", System.Text.Encoding.UTF8.GetString(arguments.Stdout));
        Assert.Equal("ONLY_VALUE=explicit\n", System.Text.Encoding.UTF8.GetString(environment.Stdout));
        Assert.Empty(environment.Stderr);
        Assert.Equal(stdinBytes, echoedStdin.Stdout);
        Assert.Equal(1048576, exactlyAtLimit.Stdout.Length);
        Assert.Equal(1048576, exactlyAtLimit.Stderr.Length);
        Assert.False(exactlyAtLimit.StdoutTruncated);
        Assert.False(exactlyAtLimit.StderrTruncated);
        Assert.Equal(1048576, overLimit.Stdout.Length);
        Assert.Equal(1048576, overLimit.Stderr.Length);
        Assert.True(overLimit.StdoutTruncated);
        Assert.True(overLimit.StderrTruncated);
    }

    [Fact]
    public async Task ProcessRunnerCanRetainTheTailAfterDrainingMoreThanTwoMiB()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The Python process fixture is Linux-specific.");
        Assert.SkipUnless(File.Exists("/usr/bin/python3"), "The Python process fixture is unavailable.");
        using var runner = new ProcessRunner();
        const string script = "import os; os.write(1, b'A' * (2 * 1048576) + b'END')";

        var first = await runner.RunAsync(Request("/usr/bin/python3", ["-c", script], null),
            TestContext.Current.CancellationToken);
        var tail = await runner.RunAsync(Request("/usr/bin/python3", ["-c", script], null, retainStdoutTail: true),
            TestContext.Current.CancellationToken);

        Assert.Equal(1048576, first.Stdout.Length);
        Assert.Equal((byte)'A', first.Stdout[^1]);
        Assert.True(first.StdoutTruncated);
        Assert.Equal(1048576, tail.Stdout.Length);
        Assert.EndsWith("END", System.Text.Encoding.ASCII.GetString(tail.Stdout));
        Assert.True(tail.StdoutTruncated);
    }

    [Fact]
    public async Task ProcessRunnerTailRetentionPreservesExactMultibyteBytes()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The Python process fixture is Linux-specific.");
        Assert.SkipUnless(File.Exists("/usr/bin/python3"), "The Python process fixture is unavailable.");
        using var runner = new ProcessRunner();
        const string script = "import os; os.write(1, b'A' * (1048576 + 100) + '😀'.encode())";

        var tail = await runner.RunAsync(Request("/usr/bin/python3", ["-c", script], null, retainStdoutTail: true),
            TestContext.Current.CancellationToken);

        Assert.Equal([.. Enumerable.Repeat((byte)'A', 1048572), 0xf0, 0x9f, 0x98, 0x80], tail.Stdout);
        Assert.True(tail.StdoutTruncated);
    }

    [Fact]
    public async Task ProcessRunnerClosesNullStdinAndReportsUnstartableExecutableWithoutDetails()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The harmless process fixtures use standard Linux utilities.");
        Assert.SkipUnless(File.Exists("/usr/bin/cat"), "The cat process fixture is unavailable.");
        using var runner = new ProcessRunner();

        var eof = await runner.RunAsync(Request("/usr/bin/cat", [], null), TestContext.Current.CancellationToken);
        var missing = await runner.RunAsync(Request(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), [], null),
            TestContext.Current.CancellationToken);

        Assert.Equal(ProcessOutcome.Exited, eof.Outcome);
        Assert.Equal(0, eof.ExitCode);
        Assert.Empty(eof.Stdout);
        Assert.Equal(ProcessOutcome.StartFailed, missing.Outcome);
        Assert.Null(missing.ExitCode);
        Assert.Empty(missing.Stdout);
        Assert.Empty(missing.Stderr);
    }

    [Fact]
    public async Task ProcessRunnerTimeoutUsesInjectedClockAndReleasesItsPermit()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The Python process fixture is Linux-specific.");
        Assert.SkipUnless(File.Exists("/usr/bin/python3") && File.Exists("/usr/bin/true"),
            "Required process fixtures are unavailable.");
        var time = new ManualTimeProvider();
        using var runner = new ProcessRunner(1, time);
        var marker = Path.Combine(Path.GetTempPath(), $"aiakos-process-timeout-{Guid.NewGuid():N}");
        try
        {
            var script = $"import os,time\nos.write(1,b'A'*(2*1048576)+b'END')\nopen('{marker}','w').close()\ntime.sleep(30)\n";
            var invocation = runner.RunAsync(Request("/usr/bin/python3", ["-c", script], new byte[16777216],
                timeout: TimeSpan.FromSeconds(10), retainStdoutTail: true), TestContext.Current.CancellationToken);
            await WaitForFileAsync(marker, TestContext.Current.CancellationToken);
            await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);

            time.Advance(TimeSpan.FromSeconds(10));
            var timedOut = await invocation.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var next = await runner.RunAsync(Request("/usr/bin/true", [], null), TestContext.Current.CancellationToken);

            Assert.Equal(ProcessOutcome.TimedOut, timedOut.Outcome);
            Assert.Null(timedOut.ExitCode);
            Assert.EndsWith("END", System.Text.Encoding.ASCII.GetString(timedOut.Stdout));
            Assert.Equal(ProcessOutcome.Exited, next.Outcome);
        }
        finally
        {
            await WaitForFileAsync(marker, CancellationToken.None);
            File.Delete(marker);
        }
    }

    [Fact]
    public async Task ProcessRunnerBoundsActiveClientsAndCancelsQueuedAndActiveCalls()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The harmless process fixtures use standard Linux utilities.");
        Assert.SkipUnless(File.Exists("/usr/bin/sleep") && File.Exists("/usr/bin/touch"),
            "Required standard process fixtures are unavailable.");
        using var runner = new ProcessRunner();
        using var queuedCancellation = new CancellationTokenSource();
        var activeTokens = Enumerable.Range(0, 8).Select(_ => new CancellationTokenSource()).ToArray();
        var active = activeTokens.Select(source => runner.RunAsync(Request("/usr/bin/sleep", ["30"], null,
            timeout: TimeSpan.FromSeconds(40)), source.Token)).ToArray();
        var marker = Path.Combine(Path.GetTempPath(), $"aiakos-process-{Guid.NewGuid():N}");
        try
        {
            var queued = runner.RunAsync(Request("/usr/bin/touch", [marker], null), queuedCancellation.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(150), TestContext.Current.CancellationToken);
            Assert.False(File.Exists(marker));
            queuedCancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);

            foreach (var source in activeTokens) source.Cancel();
            foreach (var task in active)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);

            var afterCancellation = await runner.RunAsync(Request("/usr/bin/touch", [marker], null),
                TestContext.Current.CancellationToken);
            Assert.Equal(ProcessOutcome.Exited, afterCancellation.Outcome);
            Assert.True(File.Exists(marker));
        }
        finally
        {
            queuedCancellation.Cancel();
            foreach (var source in activeTokens)
            {
                source.Cancel();
                source.Dispose();
            }
            File.Delete(marker);
        }
    }

    [Fact]
    public async Task ProcessRunnerClosesPipesWhenAChildRetainsTheOutputHandles()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The inherited-pipe fixture is Linux-specific.");
        Assert.SkipUnless(File.Exists("/usr/bin/python3"),
            "Required standard process fixtures are unavailable.");
        using var runner = new ProcessRunner();
        var marker = Path.Combine(Path.GetTempPath(), $"aiakos-process-child-{Guid.NewGuid():N}");
        try
        {
            var script = $"import os,time\npid=os.fork()\nif pid:\n    os._exit(0)\ntime.sleep(0.5)\nopen('{marker}','w').write('ok')\nos.write(1,b'child')\nos._exit(0)\n";
            var result = await runner.RunAsync(Request("/usr/bin/python3", ["-c", script], null,
                timeout: TimeSpan.FromMilliseconds(100)), TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

            Assert.Equal(ProcessOutcome.TimedOut, result.Outcome);
            await WaitForFileAsync(marker, TestContext.Current.CancellationToken);
            Assert.True(File.Exists(marker));
        }
        finally
        {
            await WaitForFileAsync(marker, CancellationToken.None);
            File.Delete(marker);
        }
    }

    [Fact]
    public async Task ProcessRunnerTreatsEarlyStdinClosureAsAnExitedChild()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The shell process fixture is Linux-specific.");
        Assert.SkipUnless(File.Exists("/bin/sh"), "The shell process fixture is unavailable.");
        using var runner = new ProcessRunner();

        var result = await runner.RunAsync(Request("/bin/sh", ["-c", "exec 0<&-; printf partial; exit 3"],
            new byte[8 * 1024 * 1024]), TestContext.Current.CancellationToken);

        Assert.Equal(ProcessOutcome.Exited, result.Outcome);
        Assert.Equal(3, result.ExitCode);
        Assert.Equal("partial", System.Text.Encoding.UTF8.GetString(result.Stdout));
        Assert.Empty(result.Stderr);
    }

    [Fact]
    public async Task ProcessRunnerPropagatesCallerCancellationWhenGrandchildRetainsOutputPipes()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The shell process fixture is Linux-specific.");
        Assert.SkipUnless(File.Exists("/bin/sh") && File.Exists("/usr/bin/sleep"),
            "Required standard process fixtures are unavailable.");
        using var runner = new ProcessRunner();
        using var cancellation = new CancellationTokenSource();

        var invocation = runner.RunAsync(Request("/bin/sh", ["-c", "(sleep 5) & printf partial; exit 0"], null,
            timeout: TimeSpan.FromSeconds(10)), cancellation.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(700), TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invocation.WaitAsync(
            TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ProcessRunnerRejectsInvalidConcurrencyAndTimeoutsBeforeLaunching()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessRunner(0));
        using var runner = new ProcessRunner();
        var request = Request("/definitely/not/launched", [], null, timeout: TimeSpan.Zero);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            runner.RunAsync(request, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            runner.RunAsync(request with { Timeout = TimeSpan.FromMilliseconds(4294967295d) },
                TestContext.Current.CancellationToken));
    }

    private static async Task WaitForFileAsync(string path, CancellationToken ct)
    {
        var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 3;
        while (!File.Exists(path) && Stopwatch.GetTimestamp() < deadline)
            await Task.Delay(TimeSpan.FromMilliseconds(20), ct);
    }

    private static ProcessRequest Request(string fileName, IReadOnlyList<string> arguments,
        ReadOnlyMemory<byte>? stdin, TimeSpan? timeout = null,
        IReadOnlyDictionary<string, string>? environment = null, bool retainStdoutTail = false) =>
        new(fileName, arguments, environment ?? new Dictionary<string, string>(), stdin,
            timeout ?? TimeSpan.FromSeconds(5), retainStdoutTail);

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly object _sync = new();
        private readonly List<ManualTimer> _timers = [];
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() { lock (_sync) return _timestamp; }
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(GetTimestamp());

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            return timer;
        }

        public void Advance(TimeSpan amount)
        {
            List<(TimerCallback Callback, object? State)> callbacks = [];
            lock (_sync)
            {
                _timestamp += amount.Ticks;
                foreach (var timer in _timers.ToArray())
                    if (timer.TakeIfDue(_timestamp) is { } callback) callbacks.Add(callback);
            }
            foreach (var (callback, state) in callbacks) callback(state);
        }

        private void Add(ManualTimer timer)
        {
            lock (_sync)
                if (!_timers.Contains(timer)) _timers.Add(timer);
        }

        private void Remove(ManualTimer timer)
        {
            lock (_sync) _timers.Remove(timer);
        }

        private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
        {
            private long? _dueAt;
            private TimeSpan _period = Timeout.InfiniteTimeSpan;
            private bool _disposed;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (dueTime < TimeSpan.Zero && dueTime != Timeout.InfiniteTimeSpan)
                    throw new ArgumentOutOfRangeException(nameof(dueTime));
                if (period < TimeSpan.Zero && period != Timeout.InfiniteTimeSpan)
                    throw new ArgumentOutOfRangeException(nameof(period));
                lock (owner._sync)
                {
                    if (_disposed) return false;
                    _period = period;
                    _dueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner._timestamp + dueTime.Ticks;
                    owner.Add(this);
                    return true;
                }
            }

            public void Dispose()
            {
                lock (owner._sync)
                {
                    _disposed = true;
                    _dueAt = null;
                    owner.Remove(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public (TimerCallback Callback, object? State)? TakeIfDue(long now)
            {
                if (_disposed || _dueAt is not { } dueAt || now < dueAt) return null;
                _dueAt = _period == Timeout.InfiniteTimeSpan ? null : now + _period.Ticks;
                return (callback, state);
            }
        }
    }
}
