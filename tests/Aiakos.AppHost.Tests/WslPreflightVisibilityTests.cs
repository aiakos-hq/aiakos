using Aiakos.Hosting.Wsl;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Aiakos.AppHost.Tests;

/// <summary>
/// Rule 3 (spec 0001 R26): a failed WSL preflight shows up as a resource that failed to start, and
/// the process is never launched. Runs the real orchestration (DCP) with a fake WSL runner; no WSL,
/// Docker or wsl.exe is needed, so it runs on Linux CI too.
/// </summary>
public sealed class WslPreflightVisibilityTests
{
    [Fact]
    public async Task AFailedPreflightMarksTheResourceFailedToStart()
    {
        var ct = TestContext.Current.CancellationToken;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));

        // The testing builder needs the AppHost assembly (it supplies DCP's location), and the public
        // API offers that only through the AppHost's entry point. Its resources are removed so that
        // nothing but the probe starts (no Docker, no dotnet publish, no WSL).
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Aiakos_AppHost>(timeout.Token);
        foreach (var resource in builder.Resources.ToList())
        {
            builder.Resources.Remove(resource);
        }

        var probe = builder.AddWslExecutable("wsl-probe", "Ubuntu", "/bin/true");
        builder.Services.AddSingleton<IWslProcessRunner>(new NatRunner());

        await using var app = await builder.BuildAsync(timeout.Token);
        var notifications = app.Services.GetRequiredService<ResourceNotificationService>();
        var logs = app.Services.GetRequiredService<ResourceLoggerService>();
        await app.StartAsync(timeout.Token);

        var failed = await notifications.WaitForResourceAsync(
            "wsl-probe", e => e.Snapshot.State?.Text == KnownResourceStates.FailedToStart, timeout.Token);

        Assert.Equal(KnownResourceStates.FailedToStart, failed.Snapshot.State?.Text);

        // The actionable message is in the log the dashboard shows for that resource instance.
        using var logTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        logTimeout.CancelAfter(TimeSpan.FromSeconds(30));
        var line = await WaitForPreflightLogAsync(logs, failed.ResourceId, logTimeout.Token);
        Assert.Contains("networkingMode=mirrored", line, StringComparison.Ordinal);
    }

    private static async Task<string> WaitForPreflightLogAsync(
        ResourceLoggerService logs, string resourceName, CancellationToken cancellationToken)
    {
        await foreach (var batch in logs.WatchAsync(resourceName).WithCancellation(cancellationToken))
        {
            foreach (var line in batch)
            {
                if (line.Content.Contains("WSL preflight failed", StringComparison.Ordinal))
                {
                    return line.Content;
                }
            }
        }

        throw new InvalidOperationException("The preflight failure was not logged on the resource.");
    }

    private sealed class NatRunner : IWslProcessRunner
    {
        public Task<WslProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
            Task.FromResult(new WslProcessResult(0, "nat\n", string.Empty));
    }
}
