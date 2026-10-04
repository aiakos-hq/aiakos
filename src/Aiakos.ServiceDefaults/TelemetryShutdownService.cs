using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Aiakos.ServiceDefaults;

/// <summary>Bound for the final telemetry flush (spec 0001 R37).</summary>
public sealed class TelemetryShutdownOptions
{
    /// <summary>Configuration section.</summary>
    public const string Section = "Aiakos:Telemetry";

    /// <summary>Maximum time spent flushing and shutting down each telemetry provider.</summary>
    public TimeSpan ShutdownFlushTimeout { get; set; } = TimeSpan.FromSeconds(2);
}

/// <summary>
/// Flushes and shuts down the telemetry providers once the host has stopped, with a bounded
/// timeout, so a stop while the collector is gone does not wait for the exporter's default
/// timeout (spike 0003 §4).
/// </summary>
internal sealed class TelemetryShutdownService : IHostedLifecycleService
{
    private readonly TimeProvider timeProvider;
    private readonly TaskScheduler taskScheduler;
    private readonly IOptions<TelemetryShutdownOptions> options;

    // Resolved when the service is created, so a host that failed to start (and whose container
    // is already being disposed) can still be stopped without touching the service provider.
    private readonly TracerProvider? tracer;
    private readonly MeterProvider? meter;
    private readonly LoggerProvider? logger;

    public TelemetryShutdownService(IServiceProvider services, IOptions<TelemetryShutdownOptions> options)
        : this(services, options, TimeProvider.System, TaskScheduler.Default)
    {
    }

    internal TelemetryShutdownService(
        IServiceProvider services,
        IOptions<TelemetryShutdownOptions> options,
        TimeProvider timeProvider,
        TaskScheduler taskScheduler)
    {
        this.options = options;
        this.timeProvider = timeProvider;
        this.taskScheduler = taskScheduler;
        tracer = services.GetService<TracerProvider>();
        meter = services.GetService<MeterProvider>();
        logger = services.GetService<LoggerProvider>();
    }

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken)
    {
        var timeoutMs = (int)Math.Clamp(options.Value.ShutdownFlushTimeout.TotalMilliseconds, 0, int.MaxValue);

        // Shutdown flushes pending data first. Each provider gets the bound and they run in parallel,
        // so the total stays near one bound.
        var flushes = new List<Task>(3);
        if (tracer is not null)
        {
            flushes.Add(Task.Factory.StartNew(
                () => tracer.Shutdown(timeoutMs),
                CancellationToken.None,
                TaskCreationOptions.DenyChildAttach,
                taskScheduler));
        }

        if (meter is not null)
        {
            flushes.Add(Task.Factory.StartNew(
                () => meter.Shutdown(timeoutMs),
                CancellationToken.None,
                TaskCreationOptions.DenyChildAttach,
                taskScheduler));
        }

        if (logger is not null)
        {
            flushes.Add(Task.Factory.StartNew(
                () => logger.Shutdown(timeoutMs),
                CancellationToken.None,
                TaskCreationOptions.DenyChildAttach,
                taskScheduler));
        }

        return Task.WhenAll(flushes).WaitAsync(
                TimeSpan.FromMilliseconds(timeoutMs + 500L),
                timeProvider,
                CancellationToken.None)
            .ContinueWith(static _ => { }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
}
