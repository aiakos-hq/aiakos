using System.Diagnostics;

using Grpc.Core;
using Grpc.Health.V1;
using Grpc.Net.Client;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aiakos.Node;

/// <summary>State of the node's connection to the orchestrator.</summary>
public enum ConnectionState
{
    Connecting,
    Connected,
    Unavailable,
}

/// <summary>
/// Keeps a connection to the orchestrator (spec 0001 R34, R35): calls
/// <c>grpc.health.v1.Health/Check</c>, then holds a <c>Health/Watch</c> stream until it ends or
/// fails, then waits a backoff delay and tries again. It never gives up because the orchestrator is
/// unavailable. Spec 0002 replaces the two calls with the <c>Connect</c> stream.
/// </summary>
public sealed partial class OrchestratorConnection(
    IOptions<NodeOptions> options,
    Backoff backoff,
    NodeTelemetry telemetry,
    ILogger<OrchestratorConnection> logger) : BackgroundService
{
    /// <summary>HTTP/2 keep-alive ping interval, so a silently dead connection is noticed on the long stream.</summary>
    public static readonly TimeSpan KeepAlivePingDelay = TimeSpan.FromSeconds(30);

    /// <summary>How long a keep-alive ping may go unanswered before the connection is dropped.</summary>
    public static readonly TimeSpan KeepAlivePingTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Deadline of the unary <c>Check</c> call.</summary>
    public static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(10);

    private int _state = (int)ConnectionState.Connecting;

    /// <summary>Current state.</summary>
    public ConnectionState State => (ConnectionState)Volatile.Read(ref _state);

    /// <summary>Raised on every state change (also for repeated <c>connecting</c> attempts).</summary>
    public event EventHandler<ConnectionState>? StateChanged;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var url = options.Value.OrchestratorUri;
        var urlText = options.Value.OrchestratorUrl!.TrimEnd('/');
        var connectedBefore = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            string reason;
            try
            {
                SetState(ConnectionState.Connecting);
                LogConnecting(logger, urlText);

                // A fresh channel per attempt: the client's subchannel layer otherwise applies its own
                // reconnect backoff (up to minutes) underneath ours, and a call can wait on it.
                using var channel = CreateChannel(url);
                var client = new Health.HealthClient(channel);

                var status = await CheckAsync(client, stoppingToken).ConfigureAwait(false);
                if (status != HealthCheckResponse.Types.ServingStatus.Serving)
                {
                    reason = $"health status is {status}";
                }
                else
                {
                    backoff.Reset();
                    if (connectedBefore)
                    {
                        telemetry.Reconnects.Add(1);
                    }

                    connectedBefore = true;
                    SetState(ConnectionState.Connected);
                    LogConnected(logger, urlText);

                    reason = await WatchAsync(client, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (Exception) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (RpcException ex)
            {
                reason = $"{ex.StatusCode}: {ex.Status.Detail}";
            }
#pragma warning disable CA1031 // The loop must survive any failure to reach the orchestrator (R35).
            catch (Exception ex)
#pragma warning restore CA1031
            {
                reason = $"{ex.GetType().Name}: {ex.Message}";
            }

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            var delay = backoff.NextDelay();
            SetState(ConnectionState.Unavailable);
            LogUnavailable(logger, urlText, reason, delay.TotalSeconds);

            try
            {
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        telemetry.Connected = false;
    }

    /// <summary>
    /// Creates the h2c channel with HTTP/2 keep-alive pings (spec 0001, Design → Node).
    /// </summary>
    public static GrpcChannel CreateChannel(Uri address)
    {
        var handler = new SocketsHttpHandler
        {
            KeepAlivePingDelay = KeepAlivePingDelay,
            KeepAlivePingTimeout = KeepAlivePingTimeout,
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.WithActiveRequests,
            EnableMultipleHttp2Connections = true,
            ConnectTimeout = TimeSpan.FromSeconds(5),
        };

        return GrpcChannel.ForAddress(address, new GrpcChannelOptions
        {
            HttpHandler = handler,
            DisposeHttpClient = true,
        });
    }

    private async Task<HealthCheckResponse.Types.ServingStatus> CheckAsync(Health.HealthClient client, CancellationToken cancellationToken)
    {
        using var activity = NodeTelemetry.ActivitySource.StartActivity("node.connect", ActivityKind.Internal);
        activity?.SetTag("aiakos.node.id", options.Value.NodeId);
        activity?.SetTag("server.address", options.Value.OrchestratorUri.Host);
        activity?.SetTag("server.port", options.Value.OrchestratorUri.Port);

        try
        {
            var response = await client.CheckAsync(
                new HealthCheckRequest(),
                deadline: DateTime.UtcNow.Add(CheckTimeout),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var serving = response.Status == HealthCheckResponse.Types.ServingStatus.Serving;
            activity?.SetTag("grpc.health.status", response.Status.ToString());
            activity?.SetStatus(serving ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
            return response.Status;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }

    private static async Task<string> WatchAsync(Health.HealthClient client, CancellationToken cancellationToken)
    {
        using var call = client.Watch(new HealthCheckRequest(), cancellationToken: cancellationToken);
        await foreach (var update in call.ResponseStream.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (update.Status != HealthCheckResponse.Types.ServingStatus.Serving)
            {
                return $"health status changed to {update.Status}";
            }
        }

        return "watch stream ended";
    }

    private void SetState(ConnectionState state)
    {
        Volatile.Write(ref _state, (int)state);
        telemetry.Connected = state == ConnectionState.Connected;
        StateChanged?.Invoke(this, state);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Orchestrator connection: connecting to {Url}")]
    private static partial void LogConnecting(ILogger logger, string url);

    [LoggerMessage(Level = LogLevel.Information, Message = "Orchestrator connection: connected to {Url}")]
    private static partial void LogConnected(ILogger logger, string url);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Orchestrator connection: unavailable at {Url} ({Reason}); next retry in {DelaySeconds:0.0} s")]
    private static partial void LogUnavailable(ILogger logger, string url, string reason, double delaySeconds);
}
