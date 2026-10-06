using System.Collections.ObjectModel;
using System.Diagnostics.Metrics;
using System.Text;
using Aiakos.Node.Sessions;
using Aiakos.Node.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aiakos.Node.Tests.Sessions;

public sealed class TmuxClientTests
{
    private static readonly string[] EscapedArguments = ["-u", "-f", "/private/config", "-L", "aiakos-test",
        "display-message", "plain", "end\\;", "literal\\\\;", "\\;"];
    private static readonly string[] AllowedCapabilities = ["session-host.tmux"];
    private static readonly string[] VersionArguments = ["-V"];
    private static readonly string[] ConvergenceOperations = ["show-environment", "set-environment", "set-environment", "set-environment",
        "set-environment", "set-option", "set-option", "set-option", "set-option", "set-option", "set-option"];
    [Theory]
    [InlineData("tmux 3.4\n", true, 3, 4, "3.4")]
    [InlineData("tmux 3.3a\r\n", true, 3, 3, "3.3a")]
    [InlineData("tmux 4.0", true, 4, 0, "4.0")]
    [InlineData("tmux next-3.7", false, 0, 0, null)]
    [InlineData("tmux 3.4 extra", false, 0, 0, null)]
    [InlineData("tmux 3.4\n\n", false, 0, 0, null)]
    public void ParsesOnlyTmuxVersionOutput(string text, bool expected, int major, int minor, string? version)
    {
        Assert.Equal(expected, TmuxVersion.TryParse(text, out var actualMajor, out var actualMinor, out var actualVersion));
        Assert.Equal(major, actualMajor);
        Assert.Equal(minor, actualMinor);
        Assert.Equal(version, actualVersion);
    }

    [Fact]
    public void EscapesTrailingSemicolonInEveryDataArgumentOnly()
    {
        var args = TmuxClient.BuildCommandArguments("/private/config", "aiakos-test",
            ["display-message", "plain", "end;", "literal\\;", ";"]);
        Assert.Equal(EscapedArguments, args);
    }

    [Theory]
    [InlineData(ProcessOutcome.TimedOut, null, "", SessionHostErrorCode.TmuxTimeout, "tmux invocation timed out.", true)]
    [InlineData(ProcessOutcome.StartFailed, null, "", SessionHostErrorCode.Unavailable, "tmux executable could not be started; requires tmux >= 3.4.", false)]
    [InlineData(ProcessOutcome.Exited, 1, "no server running", SessionHostErrorCode.TmuxFailed, "tmux server is not running.", true)]
    [InlineData(ProcessOutcome.Exited, 1, "can't find pane: %1", SessionHostErrorCode.NotFound, "Session was not found.", false)]
    [InlineData(ProcessOutcome.Exited, 1, "duplicate session: x", SessionHostErrorCode.AlreadyRunning, "A live session already exists for SeatId.", false)]
    [InlineData(ProcessOutcome.Exited, 1, "Permission denied", SessionHostErrorCode.TmuxFailed, "tmux invocation failed.", true)]
    public void ClassifiesTmuxFailures(ProcessOutcome outcome, int? exitCode, string stderr,
        SessionHostErrorCode code, string message, bool retryable)
    {
        var error = TmuxClient.GetError(Result(outcome, exitCode, stderr));
        Assert.NotNull(error);
        Assert.Equal((code, message, retryable), (error.Code, error.Message, error.Retryable));
        if (stderr is "Permission denied") Assert.Equal(stderr, error.Metadata!["stderr"]);
        else Assert.Null(error.Metadata);
    }

    [Fact]
    public void ErrorMetadataIsBoundedAndSuccessIgnoresStderr()
    {
        var longError = TmuxClient.GetError(Result(ProcessOutcome.Exited, 2, new string('x', 2048)))!;
        Assert.Equal(1024, Encoding.UTF8.GetByteCount(longError.Metadata!["stderr"]));
        Assert.Null(TmuxClient.GetError(Result(ProcessOutcome.Exited, 0, "diagnostic")));
        Assert.False(TmuxClient.IsNoServer(Result(ProcessOutcome.Exited, 1, "error connecting to x: Permission denied")));
        Assert.True(TmuxClient.IsNoServer(Result(ProcessOutcome.Exited, 1, "error connecting to x: (Connection refused)")));
    }

    [Fact]
    public async Task InitializationUsesPrivateConfigSanitizedEnvironmentAndConvergesServer()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Tmux initialization is Linux-only.");
        var home = Path.Combine(Path.GetTempPath(), $"aiakos-tmux-{Guid.NewGuid():N}");
        Directory.CreateDirectory(home);
        var source = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PATH"] = "/usr/bin:/bin", ["HOME"] = home, ["LANG"] = "en_US.UTF-8",
            ["LC_ALL"] = "", ["TMUX"] = "sentinel-secret", ["AIAKOS_NODE_TOKEN"] = "sentinel-token",
            ["OTEL_EXPORTER_OTLP_HEADERS"] = "sentinel-diagnostic", ["DOTNET_ENVIRONMENT"] = "sentinel-dotnet"
        };
        var runner = new FakeProcessRunner();
        runner.Enqueue(Result(ProcessOutcome.Exited, 0, stdout: "tmux 3.6\n"));
        runner.Enqueue(Result(ProcessOutcome.Exited, 0, stdout: "HOME=/tmp\nLC_ALL=C\nAIAKOS_NODE_TOKEN=x\nOTEL_X=y\nDOTNET_X=z\n"));
        runner.Enqueue(Result(ProcessOutcome.Exited, 0)); // unset LC_ALL
        runner.Enqueue(Result(ProcessOutcome.Exited, 0)); // unset AIAKOS_NODE_TOKEN
        runner.Enqueue(Result(ProcessOutcome.Exited, 0)); // unset OTEL_X
        runner.Enqueue(Result(ProcessOutcome.Exited, 0)); // unset DOTNET_X
        for (var i = 0; i < 6; i++) runner.Enqueue(Result(ProcessOutcome.Exited, 0));
        using var provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var client = new TmuxClient(new TmuxHostOptions { Instance = "test", Home = home,
            TmuxPath = "/usr/bin/tmux" }, runner, source, NullLogger<TmuxClient>.Instance,
            provider.GetRequiredService<IMeterFactory>());

        var first = client.InitializeAsync(TestContext.Current.CancellationToken);
        source["AIAKOS_NODE_TOKEN"] = "mutated";
        await Task.WhenAll(first, client.InitializeAsync(TestContext.Current.CancellationToken));

        Assert.Equal(SessionHostAvailability.Available, client.Info.Availability);
        Assert.Equal("3.6", client.Info.Version);
        Assert.Equal(AllowedCapabilities, client.Info.Capabilities);
        Assert.Equal(12, runner.Requests.Count);
        var versionRequest = runner.Requests[0];
        Assert.Equal(VersionArguments, versionRequest.Arguments);
        Assert.Equal("/usr/bin/tmux", versionRequest.FileName);
        Assert.Equal("en_US.UTF-8", versionRequest.Environment["LANG"]);
        Assert.DoesNotContain("TMUX", versionRequest.Environment.Keys);
        Assert.DoesNotContain("AIAKOS_NODE_TOKEN", versionRequest.Environment.Keys);
        Assert.DoesNotContain("OTEL_EXPORTER_OTLP_HEADERS", versionRequest.Environment.Keys);
        Assert.DoesNotContain("DOTNET_ENVIRONMENT", versionRequest.Environment.Keys);
        Assert.Equal("show-environment", runner.Requests[1].Arguments[5]);
        Assert.Equal("-f", runner.Requests[1].Arguments[1]);
        Assert.Equal("/usr/bin/tmux", runner.Requests[1].FileName);
        Assert.Contains("# Generated by aiakos-node. Do not edit; rewritten at every node start.\n",
            File.ReadAllText(Path.Combine(home, "tmux", "tmux.conf")));
        Assert.Equal(ConvergenceOperations,
            runner.Requests.Skip(1).Select(request => request.Arguments[5]));
        Assert.All(runner.Requests.Skip(2).Take(4), request => Assert.Equal("-u", request.Arguments[7]));
        Assert.Equal("remain-on-exit", runner.Requests[6].Arguments[7]);
        Assert.Equal("automatic-rename", runner.Requests[^1].Arguments[7]);
        Directory.Delete(home, recursive: true);
    }

    [Fact]
    public async Task NoServerInitializationStopsAfterEnvironmentProbeAndNeverStartsServer()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Tmux initialization is Linux-only.");
        var home = Path.Combine(Path.GetTempPath(), $"aiakos-tmux-{Guid.NewGuid():N}");
        var runner = new FakeProcessRunner();
        runner.Enqueue(Result(ProcessOutcome.Exited, 0, stdout: "tmux 3.4\n"));
        runner.Enqueue(Result(ProcessOutcome.Exited, 1, stderr: "no server running"));
        using var provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var client = new TmuxClient(new TmuxHostOptions { Instance = "test", Home = home,
            TmuxPath = "/usr/bin/tmux" }, runner, new Dictionary<string, string>(),
            NullLogger<TmuxClient>.Instance, provider.GetRequiredService<IMeterFactory>());

        await client.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(SessionHostAvailability.Available, client.Info.Availability);
        Assert.Equal(2, runner.Requests.Count);
        Assert.Equal(VersionArguments, runner.Requests[0].Arguments);
        Assert.DoesNotContain(runner.Requests.SelectMany(request => request.Arguments), arg => arg == "new-session" || arg == "kill-server");
        Assert.True(Directory.Exists(Path.Combine(home, "tmux")));
        Directory.Delete(home, recursive: true);
    }

    [Fact]
    public async Task RunRequiresAvailableClientEscapesDataAndAllowsStdinOnlyForLoadBuffer()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Tmux initialization is Linux-only.");
        var home = Path.Combine(Path.GetTempPath(), $"aiakos-tmux-{Guid.NewGuid():N}");
        Directory.CreateDirectory(home);
        var runner = new FakeProcessRunner();
        runner.Enqueue(Result(ProcessOutcome.Exited, 0, stdout: "tmux 3.4"));
        runner.Enqueue(Result(ProcessOutcome.Exited, 1, stderr: "no server running"));
        runner.Enqueue(Result(ProcessOutcome.Exited, 0));
        using var provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var client = new TmuxClient(new TmuxHostOptions { Instance = "test", Home = home,
            TmuxPath = "/usr/bin/tmux" }, runner, new Dictionary<string, string>(),
            NullLogger<TmuxClient>.Instance, provider.GetRequiredService<IMeterFactory>());
        await client.InitializeAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<ArgumentException>(() => client.RunAsync(["send-keys", "value"], new byte[] { 1 },
            TestContext.Current.CancellationToken));
        await client.RunAsync(["load-buffer", "-"], new byte[] { 1, 2, 3 }, TestContext.Current.CancellationToken);
        var request = runner.Requests[^1];
        Assert.Equal(new[] { "-u", "-f", Path.Combine(home, "tmux", "tmux.conf"), "-L", "aiakos-test",
            "load-buffer", "-" }, request.Arguments);
        Assert.Equal(new byte[] { 1, 2, 3 }, request.Stdin!.Value.ToArray());
        Assert.Equal(TimeSpan.FromTicks(TimeSpan.FromSeconds(5).Ticks +
            (long)(TimeSpan.TicksPerSecond * 2d * 3 / 1048576d)), request.Timeout);
        Assert.DoesNotContain(runner.Requests.SelectMany(item => item.Arguments), arg => arg == "kill-server");
        Directory.Delete(home, recursive: true);
    }

    private static ProcessResult Result(ProcessOutcome outcome, int? exitCode = null, string? stderr = null,
        string? stdout = null) => new(outcome, exitCode, Encoding.UTF8.GetBytes(stdout ?? string.Empty),
        Encoding.UTF8.GetBytes(stderr ?? string.Empty), false, false);
}
