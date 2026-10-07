using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text;
using Aiakos.Node.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aiakos.Node.Tests.Sessions;

public sealed class TmuxInitializationRealTests
{
    private const string TmuxPath = "/usr/bin/tmux";
    private const string TmuxOptIn = "AIAKOS_TEST_TMUX";
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    [Trait("Category", "RealTmux")]
    public async Task ColdInitializationAllowsDetachedSessionCreation()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Real tmux regression tests require Linux.");
        Assert.SkipUnless(Environment.GetEnvironmentVariable(TmuxOptIn) == "1",
            "Set AIAKOS_TEST_TMUX=1 to run the real tmux regression tests.");
        await using var fixture = await RealTmuxFixture.CreateAsync();

        var client = fixture.CreateClient();
        await client.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.True(client.Info.Availability == SessionHostAvailability.Available,
            $"Initialization failed: {client.Info.Reason}");

        var created = await client.RunAsync(["new-session", "-d", "-s", fixture.ColdSession,
            "-x", "80", "-y", "24", "/bin/sleep", "30"], null,
            TestContext.Current.CancellationToken);
        Assert.Equal(0, created.ExitCode);
        Assert.Equal("largest", await fixture.ShowWindowSizeAsync());
    }

    [Fact]
    [Trait("Category", "RealTmux")]
    public async Task WarmInitializationPreservesServerAndPaneAndAllowsAnotherDetachedSession()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Real tmux regression tests require Linux.");
        Assert.SkipUnless(Environment.GetEnvironmentVariable(TmuxOptIn) == "1",
            "Set AIAKOS_TEST_TMUX=1 to run the real tmux regression tests.");
        await using var fixture = await RealTmuxFixture.CreateAsync();

        var seeded = await fixture.RunRawAsync(["-u", "-f", fixture.EmptyConfig, "-L", fixture.Socket,
            "new-session", "-d", "-s", fixture.WarmSession, "-x", "80", "-y", "24", "/bin/sleep", "30"]);
        Assert.True(seeded.ExitCode == 0,
            $"Private tmux seed failed: {Encoding.UTF8.GetString(seeded.Stderr)}");
        var before = await fixture.GetSessionStateAsync(fixture.WarmSession);

        var client = fixture.CreateClient();
        await client.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.Equal(SessionHostAvailability.Available, client.Info.Availability);
        Assert.Equal(before, await fixture.GetSessionStateAsync(fixture.WarmSession));
        Assert.Equal("largest", await fixture.ShowWindowSizeAsync());

        var created = await client.RunAsync(["new-session", "-d", "-s", fixture.SecondSession,
            "-x", "80", "-y", "24", "/bin/sleep", "30"], null,
            TestContext.Current.CancellationToken);
        Assert.Equal(0, created.ExitCode);
        Assert.Equal(before, await fixture.GetSessionStateAsync(fixture.WarmSession));
        Assert.False(Process.GetProcessById(before.PanePid).HasExited);
    }

    private sealed class RealTmuxFixture : IAsyncDisposable
    {
        private readonly ProcessRunner _runner = new();
        private readonly ServiceProvider _services;
        private readonly string _root;
        private readonly IReadOnlyDictionary<string, string> _environment;

        private RealTmuxFixture(string root)
        {
            _root = root;
            Home = Path.Combine(root, "home");
            Directory.CreateDirectory(Home);
            var socketDirectory = Path.Combine(root, "sockets");
            Directory.CreateDirectory(socketDirectory);
            Instance = "r" + Guid.NewGuid().ToString("N")[..8];
            Socket = "aiakos-" + Instance;
            EmptyConfig = Path.Combine(root, "empty.conf");
            File.WriteAllBytes(EmptyConfig, []);
            ColdSession = "c" + Guid.NewGuid().ToString("N")[..8];
            WarmSession = "w" + Guid.NewGuid().ToString("N")[..8];
            SecondSession = "s" + Guid.NewGuid().ToString("N")[..8];
            _environment = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["HOME"] = Home,
                ["USER"] = Environment.UserName,
                ["LOGNAME"] = Environment.UserName,
                ["SHELL"] = "/bin/sh",
                ["PATH"] = "/usr/bin:/bin",
                ["TMUX_TMPDIR"] = socketDirectory,
                ["LANG"] = "C.UTF-8"
            };
            _services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        }

        public string Home { get; }
        public string Instance { get; }
        public string Socket { get; }
        public string EmptyConfig { get; }
        public string ColdSession { get; }
        public string WarmSession { get; }
        public string SecondSession { get; }

        public static async Task<RealTmuxFixture> CreateAsync()
        {
            var version = await ProcessRunnerForVersion();
            Assert.StartsWith("tmux ", version, StringComparison.Ordinal);
            var versionText = version.AsSpan("tmux ".Length).Trim();
            var parts = versionText.ToString().Split('.', 3);
            Assert.True(parts.Length >= 2 && int.TryParse(parts[0], NumberStyles.None,
                CultureInfo.InvariantCulture, out var major) && int.TryParse(parts[1], NumberStyles.None,
                CultureInfo.InvariantCulture, out var minor) && (major > 3 || major == 3 && minor >= 4),
                "Real tmux regression tests require tmux >= 3.4.");
            return new RealTmuxFixture(Path.Combine(Path.GetTempPath(), "art" + Guid.NewGuid().ToString("N")[..8]));
        }

        public TmuxClient CreateClient() => new(new TmuxHostOptions
        {
            Instance = Instance,
            Home = Home,
            TmuxPath = TmuxPath,
            InvocationTimeout = CommandTimeout
        }, _runner, _environment, NullLogger<TmuxClient>.Instance,
            _services.GetRequiredService<IMeterFactory>());

        public async Task<string> ShowWindowSizeAsync()
        {
            var result = await RunRawAsync(["-u", "-f", Path.Combine(Home, "tmux", "tmux.conf"), "-L", Socket,
                "show-options", "-g", "window-size"]);
            Assert.Equal(0, result.ExitCode);
            var output = Encoding.UTF8.GetString(result.Stdout).Trim();
            const string prefix = "window-size ";
            Assert.StartsWith(prefix, output, StringComparison.Ordinal);
            return output[prefix.Length..];
        }

        public async Task<SessionState> GetSessionStateAsync(string session)
        {
            var result = await RunRawAsync(["-u", "-f", Path.Combine(Home, "tmux", "tmux.conf"), "-L", Socket,
                "display-message", "-p", "-t", session, "#{pid}|#{session_id}|#{pane_pid}"]);
            Assert.Equal(0, result.ExitCode);
            var fields = Encoding.UTF8.GetString(result.Stdout).Trim().Split('|');
            Assert.Equal(3, fields.Length);
            return new SessionState(int.Parse(fields[0], CultureInfo.InvariantCulture), fields[1],
                int.Parse(fields[2], CultureInfo.InvariantCulture));
        }

        public Task<ProcessResult> RunRawAsync(IReadOnlyList<string> arguments) => _runner.RunAsync(
            new ProcessRequest(TmuxPath, arguments, _environment, null, CommandTimeout),
            TestContext.Current.CancellationToken);

        public async ValueTask DisposeAsync()
        {
            try
            {
                await RunRawAsync(["-u", "-f", File.Exists(Path.Combine(Home, "tmux", "tmux.conf"))
                    ? Path.Combine(Home, "tmux", "tmux.conf") : EmptyConfig, "-L", Socket, "kill-server"]);
            }
            finally
            {
                _runner.Dispose();
                _services.Dispose();
                Directory.Delete(_root, recursive: true);
            }
        }

        private static async Task<string> ProcessRunnerForVersion()
        {
            using var runner = new ProcessRunner();
            var result = await runner.RunAsync(new ProcessRequest(TmuxPath, ["-V"],
                new Dictionary<string, string> { ["PATH"] = "/usr/bin:/bin", ["HOME"] = Path.GetTempPath(),
                    ["LANG"] = "C.UTF-8" }, null, CommandTimeout), TestContext.Current.CancellationToken);
            Assert.Equal(0, result.ExitCode);
            return Encoding.UTF8.GetString(result.Stdout).Trim();
        }
    }

    private sealed record SessionState(int ServerPid, string SessionId, int PanePid);
}
