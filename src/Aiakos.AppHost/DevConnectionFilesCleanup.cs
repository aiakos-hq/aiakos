using Microsoft.Extensions.Hosting;

namespace Aiakos.AppHost;

/// <summary>Deletes this AppHost's <c>connection.json</c> when it stops (spec 0007 R20).</summary>
internal sealed class DevConnectionFilesCleanup(DevConnectionFiles files) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
    {
        files.DeleteConnection(Environment.ProcessId);
        return Task.CompletedTask;
    }
}
