using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Aiakos.ServiceDefaults;

/// <summary>Provider shutdown timeout and lifecycle admission deadline (spec 0001 R37).</summary>
public sealed class TelemetryShutdownOptions
{
    /// <summary>Configuration section.</summary>
    public const string Section = "Aiakos:Telemetry";

    /// <summary>Maximum time spent flushing and shutting down each telemetry provider.</summary>
    public TimeSpan ShutdownFlushTimeout { get; set; } = TimeSpan.FromSeconds(2);
}

/// <summary>
/// Flushes and shuts down telemetry concurrently after the host stops. Admission is bounded;
/// entered provider calls finish before DI disposal, even when they exceed that bound.
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

    public async Task StoppedAsync(CancellationToken cancellationToken)
    {
        var timeoutMs = (int)Math.Clamp(options.Value.ShutdownFlushTimeout.TotalMilliseconds, 0, int.MaxValue);
        var admissionTimeout = TimeSpan.FromMilliseconds(timeoutMs + 500L);
        var startedAt = timeProvider.GetTimestamp();
        var gate = new object();
        var closed = false;
        var workers = new List<ShutdownWork>(3);

        // Closing admission completes only workers that have not entered. Entered workers
        // retain ownership until their provider call returns, even beyond the deadline.
        void CloseAdmission()
        {
            lock (gate)
            {
                closed = true;
                foreach (var worker in workers)
                {
                    if (!worker.Entered)
                        worker.Completion.TrySetResult();
                }
            }
        }

        void StartShutdown(Action shutdown)
        {
            var worker = new ShutdownWork();
            lock (gate)
            {
                workers.Add(worker);
                if (closed)
                {
                    worker.Completion.TrySetResult();
                    return;
                }
            }

            try
            {
                _ = Task.Factory.StartNew(() =>
                {
                    lock (gate)
                    {
                        // Check elapsed time as well as the timer: a delayed callback must
                        // never let a queued worker gain admission after the deadline.
                        if (timeProvider.GetElapsedTime(startedAt) >= admissionTimeout)
                            CloseAdmission();
                        if (closed)
                            return;
                        worker.Entered = true;
                    }

                    try
                    {
                        shutdown();
                        worker.Completion.TrySetResult();
                    }
                    catch (Exception exception)
                    {
                        worker.Completion.TrySetException(exception);
                    }
                }, CancellationToken.None, TaskCreationOptions.DenyChildAttach, taskScheduler);
            }
            catch (Exception exception)
            {
                worker.Completion.TrySetException(exception);
            }
        }

        using var timer = timeProvider.CreateTimer(
            _ => CloseAdmission(), null, admissionTimeout, Timeout.InfiniteTimeSpan);
        using var cancellation = cancellationToken.Register(CloseAdmission);
        if (tracer is not null)
            StartShutdown(() => tracer.Shutdown(timeoutMs));
        if (meter is not null)
            StartShutdown(() => meter.Shutdown(timeoutMs));
        if (logger is not null)
            StartShutdown(() => logger.Shutdown(timeoutMs));

        try
        {
            // WhenAll observes faults only after every entered call finishes. Queued
            // delegates may run later, but closed admission keeps them off the providers.
            await Task.WhenAll(workers.Select(static worker => worker.Completion.Task)).ConfigureAwait(false);
        }
        finally
        {
            CloseAdmission();
        }
    }

    private sealed class ShutdownWork
    {
        public bool Entered { get; set; }
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
