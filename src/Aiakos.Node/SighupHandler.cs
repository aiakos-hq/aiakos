using System.Runtime.InteropServices;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aiakos.Node;

/// <summary>
/// Treats SIGHUP as a graceful stop (spec 0001 R36). WSL delivers SIGHUP, never SIGTERM, when the
/// AppHost stops the node, and .NET's default SIGHUP handling skips host shutdown and the telemetry
/// flush (spike 0003 §4). SIGTERM and SIGINT keep the Generic Host's default handling.
/// </summary>
public sealed partial class SighupHandler(IHostApplicationLifetime lifetime, ILogger<SighupHandler> logger)
    : IHostedService, IDisposable
{
    private PosixSignalRegistration? _registration;
    private int _received;

    /// <summary>Whether a SIGHUP started the stop.</summary>
    public bool Received => Volatile.Read(ref _received) == 1;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _registration ??= PosixSignalRegistration.Create(PosixSignal.SIGHUP, Handle);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>The registered handler: cancels the default (abrupt) handling and stops the host.</summary>
    public void Handle(PosixSignalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Cancel = true;
        if (Interlocked.Exchange(ref _received, 1) == 0)
        {
            LogStopping(logger);
        }

        lifetime.StopApplication();
    }

    public void Dispose()
    {
        _registration?.Dispose();
        _registration = null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Received SIGHUP; stopping gracefully")]
    private static partial void LogStopping(ILogger logger);
}
