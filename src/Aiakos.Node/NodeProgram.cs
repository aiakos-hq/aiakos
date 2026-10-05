using Aiakos.ServiceDefaults;
using Aiakos.Node.Link;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aiakos.Node;

/// <summary>
/// Composition and startup of the node (spec 0001, Design → Node): validate the settings (exit 2),
/// take the instance lock (exit 3), run the host until a graceful stop (exit 0); anything else
/// exits 1.
/// </summary>
public static partial class NodeProgram
{
    /// <summary>Shutdown budget of the host (spec 0001, Design → Node).</summary>
    public static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// OTLP exporter timeout used when the environment does not set one: the collector is on
    /// loopback, and a stop while it is gone should not wait for the 10 s default (R37).
    /// </summary>
    public const string DefaultOtlpTimeoutMilliseconds = "2000";

    private const string OtlpTimeoutVariable = "OTEL_EXPORTER_OTLP_TIMEOUT";

    /// <summary>Sets process-wide environment defaults; call before building the host.</summary>
    public static void ApplyEnvironmentDefaults()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(OtlpTimeoutVariable)))
        {
            Environment.SetEnvironmentVariable(OtlpTimeoutVariable, DefaultOtlpTimeoutMilliseconds);
        }
    }

    /// <summary>Builds the host without starting it.</summary>
    /// <param name="args">Command-line arguments.</param>
    /// <param name="configure">Test hook, applied last (e.g. in-memory settings, log capture, a short backoff).</param>
    public static IHost BuildHost(string[] args, Action<HostApplicationBuilder>? configure = null)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.AddAiakosServiceDefaults();

        builder.Services.Configure<HostOptions>(static host => host.ShutdownTimeout = ShutdownTimeout);

        builder.Services.AddOptions<NodeOptions>()
            .Bind(builder.Configuration)
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<NodeOptions>, NodeOptionsValidator>();

        builder.Services.AddSingleton<NodeTelemetry>();
        builder.Services.TryAddSingleton<TimeProvider>(static _ => TimeProvider.System);
        builder.Services.AddSingleton<NodeReconnectDelay>();
        builder.Services.TryAddSingleton<INodeLinkSource, DefaultNodeLinkSource>();

        builder.Services.AddSingleton<SighupHandler>();
        builder.Services.AddHostedService(static sp => sp.GetRequiredService<SighupHandler>());
        builder.Services.AddSingleton<OrchestratorConnection>();
        builder.Services.AddHostedService(static sp => sp.GetRequiredService<OrchestratorConnection>());

        configure?.Invoke(builder);
        return builder.Build();
    }

    /// <summary>Runs the node and returns its exit code (see <see cref="NodeExitCodes"/>).</summary>
    public static async Task<int> RunAsync(
        string[] args,
        Action<HostApplicationBuilder>? configure = null,
        CancellationToken cancellationToken = default)
    {
        IHost host;
        try
        {
            host = BuildHost(args, configure);
        }
#pragma warning disable CA1031 // Top-level boundary: every failure becomes an exit code.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            await Console.Error.WriteLineAsync($"aiakos-node: failed to build the host: {ex}").ConfigureAwait(false);
            return NodeExitCodes.FromException(ex);
        }

        using (host)
        {
            var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Aiakos.Node");
            try
            {
                return await RunHostAsync(host, logger, cancellationToken).ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Top-level boundary: every failure becomes an exit code.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                var code = NodeExitCodes.FromException(ex);
                if (code == NodeExitCodes.InvalidConfiguration)
                {
                    LogInvalidConfiguration(logger, ex.Message);
                }
                else
                {
                    LogUnexpected(logger, ex);
                }

                return code;
            }
        }
    }

    private static async Task<int> RunHostAsync(IHost host, ILogger logger, CancellationToken cancellationToken)
    {
        NodeOptions options;
        try
        {
            options = host.Services.GetRequiredService<IOptions<NodeOptions>>().Value;
        }
        catch (OptionsValidationException ex)
        {
            foreach (var failure in ex.Failures)
            {
                LogInvalidConfiguration(logger, failure);
            }

            return NodeExitCodes.InvalidConfiguration;
        }

        var home = NodeOptions.ResolveHome(options.Home!, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        if (home is null)
        {
            var failure = $"{NodeOptions.HomeVariable} '{options.Home}' is relative, but the user home ($HOME) is unknown.";
            LogInvalidConfiguration(logger, failure);
            return NodeExitCodes.InvalidConfiguration;
        }

        if (!InstanceLock.TryAcquire(home, out var instanceLock, out var holderPid))
        {
            var lockPath = Path.Combine(home, InstanceLock.FileName);
            var holder = holderPid?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown";
            LogLockHeld(logger, lockPath, holder);
            return NodeExitCodes.LockHeld;
        }

        using (instanceLock)
        {
            var pid = Environment.ProcessId;
            LogStarting(logger, options.NodeId!, home, instanceLock.FilePath, pid);

            await host.StartAsync(cancellationToken).ConfigureAwait(false);
            await host.WaitForShutdownAsync(cancellationToken).ConfigureAwait(false);

            var connection = host.Services.GetRequiredService<OrchestratorConnection>();
            if (connection.ExecuteTask is { IsFaulted: true } faulted)
            {
                LogUnexpected(logger, faulted.Exception);
                return NodeExitCodes.Unexpected;
            }

            var reason = host.Services.GetRequiredService<SighupHandler>().Received ? "SIGHUP" : "host stop";
            LogStopped(logger, reason);
            return NodeExitCodes.Graceful;
        }
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "Invalid configuration: {Failure}")]
    private static partial void LogInvalidConfiguration(ILogger logger, string failure);

    [LoggerMessage(Level = LogLevel.Critical,
        Message = "Another node holds the instance lock {LockPath} (pid {HolderPid}); exiting. The holder is not touched.")]
    private static partial void LogLockHeld(ILogger logger, string lockPath, string holderPid);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "aiakos-node {NodeId} starting: home {Home}, lock {LockPath}, pid {Pid}")]
    private static partial void LogStarting(ILogger logger, string nodeId, string home, string lockPath, int pid);

    [LoggerMessage(Level = LogLevel.Information, Message = "aiakos-node stopped gracefully ({Reason})")]
    private static partial void LogStopped(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Critical, Message = "aiakos-node failed")]
    private static partial void LogUnexpected(ILogger logger, Exception? exception);
}
