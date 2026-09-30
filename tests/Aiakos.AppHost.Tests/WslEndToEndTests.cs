using Aiakos.Hosting.Wsl;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Aiakos.AppHost.Tests;

/// <summary>
/// Opt-in end-to-end test (spec 0001 Test plan): starts the real dev AppHost on Windows with WSL,
/// Docker and mirrored networking. It runs only when <c>AIAKOS_E2E_WSL=1</c> and the dev stack is
/// not already running; otherwise it is reported as skipped, so <c>dotnet test</c> works on Linux CI.
/// </summary>
public sealed class WslEndToEndTests
{
    private const string OptInVariable = "AIAKOS_E2E_WSL";
    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task TheDevStackStartsTheNodeConnectsAndNothingIsLeftAfterStop()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(OptInVariable) == "1",
            $"Opt-in WSL end-to-end test: set {OptInVariable}=1 (Windows + WSL + Docker, dev stack not running).");

        var ct = TestContext.Current.CancellationToken;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(StartTimeout);

        var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Aiakos_AppHost>(timeout.Token);
        var distro = appHost.Configuration["Aiakos:Wsl:Distro"]!;
        var home = appHost.Configuration["Aiakos:Wsl:Home"]!;
        var nodePattern = NodeDeployment.InstalledNodePath(home);

        await using (var app = await appHost.BuildAsync(timeout.Token))
        {
            var notifications = app.Services.GetRequiredService<ResourceNotificationService>();
            var logs = app.Services.GetRequiredService<ResourceLoggerService>();

            // Subscribe before starting so no log line is missed.
            var connected = WaitForLogAsync(logs, "node-wsl", IsConnectedLine, timeout.Token);

            await app.StartAsync(timeout.Token);

            await notifications.WaitForResourceAsync("node-publish", KnownResourceStates.Finished, timeout.Token);
            await notifications.WaitForResourceAsync("node-install", KnownResourceStates.Finished, timeout.Token);
            await notifications.WaitForResourceHealthyAsync("orchestrator", timeout.Token);
            await notifications.WaitForResourceAsync("node-wsl", KnownResourceStates.Running, timeout.Token);

            using (var http = app.CreateHttpClient("orchestrator", "http"))
            {
                using var health = await http.GetAsync(new Uri("/health", UriKind.Relative), timeout.Token);
                Assert.True(health.IsSuccessStatusCode, $"/health returned {(int)health.StatusCode}");
            }

            var line = await connected;
            Assert.Contains("127.0.0.1", line, StringComparison.Ordinal);

            using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            stop.CancelAfter(StopTimeout);
            await app.StopAsync(stop.Token);
        }

        // Every stop path must take the node down (spike 0003 §4, AC11). Match the dev instance's
        // path only, so a released node running on the same machine does not count.
        var runner = new WslProcessRunner();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        WslProcessResult pgrep;
        do
        {
            pgrep = await runner.RunAsync("wsl.exe", ["-d", distro, "--exec", "pgrep", "-f", "--", nodePattern], ct);
            if (pgrep.ExitCode == 1 && string.IsNullOrWhiteSpace(pgrep.StandardOutput))
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), ct);
        }
        while (DateTime.UtcNow < deadline);

        Assert.True(
            string.IsNullOrWhiteSpace(pgrep.StandardOutput),
            $"aiakos-node still running in WSL after stop: pids {pgrep.StandardOutput.Trim()}");
    }

    private static bool IsConnectedLine(string content) =>
        content.Contains("connected", StringComparison.OrdinalIgnoreCase)
        && !content.Contains("disconnected", StringComparison.OrdinalIgnoreCase);

    private static async Task<string> WaitForLogAsync(
        ResourceLoggerService logs, string resourceName, Func<string, bool> match, CancellationToken cancellationToken)
    {
        await foreach (var batch in logs.WatchAsync(resourceName).WithCancellation(cancellationToken))
        {
            foreach (var line in batch)
            {
                if (match(line.Content))
                {
                    return line.Content;
                }
            }
        }

        throw new InvalidOperationException($"The log stream of '{resourceName}' ended without a matching line.");
    }
}
