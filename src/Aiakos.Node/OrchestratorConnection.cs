using System.Diagnostics;

using Aiakos.Contracts.Node.V1;
using Aiakos.Node.Link;

using Grpc.Core;
using Grpc.Net.Client;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aiakos.Node;

public enum ConnectionState
{
    Connecting,
    Connected,
    Unavailable,
}

/// <summary>Keeps an authenticated bidirectional node link open and retries it after failures.</summary>
public sealed partial class OrchestratorConnection(
    IOptions<NodeOptions> options,
    NodeReconnectDelay reconnectDelay,
    INodeLinkSource source,
    NodeTelemetry telemetry,
    ILogger<OrchestratorConnection> logger) : BackgroundService
{
    public static readonly TimeSpan KeepAlivePingDelay = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan KeepAlivePingTimeout = TimeSpan.FromSeconds(10);

    private int _state = (int)ConnectionState.Connecting;

    public ConnectionState State => (ConnectionState)Volatile.Read(ref _state);

    public event EventHandler<ConnectionState>? StateChanged;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var url = options.Value.OrchestratorUri;
        var host = url.HostNameType == UriHostNameType.IPv6 ? $"[{url.Host.Trim('[', ']')}]" : url.Host;
        var urlText = $"{url.Scheme}://{host}:{url.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        var connectedBefore = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            var status = StatusCode.Unavailable;
            try
            {
                SetState(ConnectionState.Connecting);
                LogConnecting(logger, urlText);

                using var channel = CreateChannel(url);
                var client = new NodeLinkService.NodeLinkServiceClient(channel);
                var headers = new Metadata { { "authorization", $"Bearer {options.Value.NodeToken}" } };
                using var call = client.Connect(headers, cancellationToken: stoppingToken);
                var hello = source.CreateHello(Guid.NewGuid().ToString("D"));
                await call.RequestStream.WriteAsync(new ConnectRequest { Hello = hello }, stoppingToken).ConfigureAwait(false);

                if (!await call.ResponseStream.MoveNext(stoppingToken).ConfigureAwait(false) ||
                    call.ResponseStream.Current.BodyCase != ConnectResponse.BodyOneofCase.Welcome)
                    throw new RpcException(new Status(StatusCode.FailedPrecondition, "Welcome must be the first server message."));

                var welcome = call.ResponseStream.Current.Welcome;
                await source.WelcomeAsync(welcome, stoppingToken).ConfigureAwait(false);
                reconnectDelay.Reset();
                if (connectedBefore)
                    telemetry.Reconnects.Add(1);
                connectedBefore = true;
                SetState(ConnectionState.Connected);
                LogConnected(logger, urlText);

                status = await ReadLinkAsync(call, welcome.HeartbeatInterval.ToTimeSpan(), stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (RpcException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (RpcException exception)
            {
                status = exception.StatusCode;
            }
            catch (HttpRequestException exception)
            {
                status = StatusCode.Unavailable;
                LogConnectionFailure(logger, exception.GetType().Name);
            }
            catch (IOException exception)
            {
                status = StatusCode.Unavailable;
                LogConnectionFailure(logger, exception.GetType().Name);
            }
            catch (InvalidOperationException exception)
            {
                status = StatusCode.Unknown;
                LogConnectionFailure(logger, exception.GetType().Name);
            }
            catch (TimeoutException exception)
            {
                status = StatusCode.DeadlineExceeded;
                LogConnectionFailure(logger, exception.GetType().Name);
            }
            catch (ArgumentException exception)
            {
                status = StatusCode.Unknown;
                LogConnectionFailure(logger, exception.GetType().Name);
            }

            if (stoppingToken.IsCancellationRequested)
                break;

            var delay = reconnectDelay.NextDelay(status);
            SetState(ConnectionState.Unavailable);
            LogUnavailable(logger, urlText, status, delay.TotalSeconds);
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

    private async Task<StatusCode> ReadLinkAsync(
        AsyncDuplexStreamingCall<ConnectRequest, ConnectResponse> call,
        TimeSpan heartbeatInterval,
        CancellationToken cancellationToken)
    {
        using var activity = NodeTelemetry.ActivitySource.StartActivity("node.connect", ActivityKind.Internal);
        activity?.SetTag("aiakos.node.id", options.Value.NodeId);
        activity?.SetTag("server.address", options.Value.OrchestratorUri.Host);
        activity?.SetTag("server.port", options.Value.OrchestratorUri.Port);

        if (heartbeatInterval <= TimeSpan.Zero)
            heartbeatInterval = TimeSpan.FromSeconds(5);
        using var timer = new PeriodicTimer(heartbeatInterval);
        Task<bool>? readTask = null;
        Task<bool>? heartbeatTask = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            readTask ??= call.ResponseStream.MoveNext(cancellationToken);
            heartbeatTask ??= timer.WaitForNextTickAsync(cancellationToken).AsTask();
            var completed = await Task.WhenAny(readTask, heartbeatTask).ConfigureAwait(false);
            if (completed == readTask)
            {
                if (!await readTask.ConfigureAwait(false))
                {
                    activity?.SetStatus(ActivityStatusCode.Error);
                    return StatusCode.Unavailable;
                }

                await source.ReceiveAsync(call.ResponseStream.Current, cancellationToken).ConfigureAwait(false);
                readTask = null;
                continue;
            }

            if (!await heartbeatTask.ConfigureAwait(false))
                return StatusCode.Unavailable;
            await call.RequestStream.WriteAsync(new ConnectRequest { Heartbeat = source.CreateHeartbeat() }, cancellationToken)
                .ConfigureAwait(false);
            heartbeatTask = null;
        }

        return StatusCode.Cancelled;
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Orchestrator connection: attempt failed ({FailureType})")]
    private static partial void LogConnectionFailure(ILogger logger, string failureType);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Orchestrator connection: unavailable at {Url} (status {StatusCode}); next retry in {DelaySeconds:0.0} s")]
    private static partial void LogUnavailable(ILogger logger, string url, StatusCode statusCode, double delaySeconds);
}
