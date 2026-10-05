using Aiakos.Contracts.Node;
using Aiakos.Contracts.Node.V1;
using Aiakos.Node.Link;

using Grpc.Core;
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

/// <summary>Keeps an authenticated Connect stream to the orchestrator, retrying failures.</summary>
public sealed partial class OrchestratorConnection(
    IOptions<NodeOptions> options,
    NodeReconnectDelay reconnectDelay,
    INodeLinkSource source,
    NodeTelemetry telemetry,
    ILogger<OrchestratorConnection> logger,
    TimeProvider? timeProvider = null) : BackgroundService
{
    public static readonly TimeSpan KeepAlivePingDelay = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan KeepAlivePingTimeout = TimeSpan.FromSeconds(10);

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _writer = new(1, 1);
    private readonly string _nodeInstanceId = Guid.NewGuid().ToString();
    private int _state = (int)ConnectionState.Connecting;

    public ConnectionState State => (ConnectionState)Volatile.Read(ref _state);

    public event EventHandler<ConnectionState>? StateChanged;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var url = options.Value.OrchestratorUri;
        var urlText = options.Value.OrchestratorUrl!.TrimEnd('/');
        var connectedBefore = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            var statusCode = StatusCode.Unavailable;
            try
            {
                SetState(ConnectionState.Connecting);
                LogConnecting(logger, urlText);
                using var channel = CreateChannel(url);
                var client = new NodeLinkService.NodeLinkServiceClient(channel);
                var headers = new Metadata { { "authorization", $"Bearer {options.Value.NodeToken}" } };
                using var call = client.Connect(headers: headers, cancellationToken: CancellationToken.None);
                using var streamCancellation = new CancellationTokenSource();
                var attemptToken = streamCancellation.Token;
                var first = new ConnectRequest { Hello = source.CreateHello(_nodeInstanceId) };
                await WriteAsync(call, first, attemptToken).ConfigureAwait(false);

                var firstResponse = await ReadResponseOrStopAsync(call, stoppingToken, attemptToken).ConfigureAwait(false);
                if (firstResponse is null)
                {
                    await ShutdownStreamAsync(call, sendGoodbye: false).ConfigureAwait(false);
                    streamCancellation.Cancel();
                    break;
                }

                if (firstResponse.BodyCase != ConnectResponse.BodyOneofCase.Welcome || !ValidWelcome(firstResponse.Welcome))
                {
                    statusCode = StatusCode.FailedPrecondition;
                    throw new RpcException(new Status(statusCode, "Invalid Connect welcome."));
                }

                await source.WelcomeAsync(firstResponse.Welcome, stoppingToken).ConfigureAwait(false);
                reconnectDelay.Reset();
                if (connectedBefore)
                {
                    telemetry.Reconnects.Add(1);
                }

                connectedBefore = true;
                SetState(ConnectionState.Connected);
                LogConnected(logger, urlText);

                using var heartbeatCancellation = CancellationTokenSource.CreateLinkedTokenSource(attemptToken);
                var heartbeats = SendHeartbeatsAsync(call, firstResponse.Welcome.HeartbeatInterval.ToTimeSpan(), heartbeatCancellation.Token);
                try
                {
                    await ReceiveUntilEndAsync(call, stoppingToken, attemptToken).ConfigureAwait(false);
                }
                finally
                {
                    heartbeatCancellation.Cancel();
                    try { await heartbeats.ConfigureAwait(false); }
                    catch (OperationCanceledException) { }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (RpcException ex)
            {
                statusCode = ex.StatusCode;
            }
            catch (Exception) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception)
            {
                statusCode = StatusCode.Unavailable;
            }

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            var delay = reconnectDelay.NextDelay(statusCode);
            SetState(ConnectionState.Unavailable);
            if (statusCode is StatusCode.Unauthenticated or StatusCode.PermissionDenied or StatusCode.FailedPrecondition)
                LogAuthenticationFailure(logger, statusCode, delay.TotalSeconds);
            else
                LogUnavailable(logger, statusCode, delay.TotalSeconds);

            try { await Task.Delay(delay, _timeProvider, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
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
            MaxReceiveMessageSize = 4194304,
            MaxSendMessageSize = 4194304,
        });
    }

    private static bool ValidWelcome(Welcome welcome) =>
        welcome.Protocol is { Major: 1 } protocol && protocol.Minor <= NodeProtocol.Current.Minor
        && !string.IsNullOrEmpty(welcome.NodeId)
        && welcome.HeartbeatInterval is not null && welcome.HeartbeatInterval.ToTimeSpan() > TimeSpan.Zero
        && welcome.LivenessTimeout is not null && welcome.LivenessTimeout.ToTimeSpan() > TimeSpan.Zero;

    private async Task SendHeartbeatsAsync(AsyncDuplexStreamingCall<ConnectRequest, ConnectResponse> call, TimeSpan interval, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(interval, _timeProvider);
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
        {
            var heartbeat = source.CreateHeartbeat();
            heartbeat.SentAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset(_timeProvider.GetUtcNow());
            await WriteAsync(call, new ConnectRequest { Heartbeat = heartbeat }, ct).ConfigureAwait(false);
        }
    }

    private async Task ReceiveUntilEndAsync(AsyncDuplexStreamingCall<ConnectRequest, ConnectResponse> call, CancellationToken stoppingToken, CancellationToken streamToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var response = await ReadResponseOrStopAsync(call, stoppingToken, streamToken).ConfigureAwait(false);
            if (response is null)
            {
                await ShutdownStreamAsync(call, sendGoodbye: true).ConfigureAwait(false);
                return;
            }

            if (response.BodyCase == ConnectResponse.BodyOneofCase.Welcome || response.BodyCase == ConnectResponse.BodyOneofCase.None)
                throw new RpcException(new Status(StatusCode.FailedPrecondition, "Invalid Connect response."));

            await source.ReceiveAsync(response, stoppingToken).ConfigureAwait(false);
            if (response.BodyCase == ConnectResponse.BodyOneofCase.Goodbye)
                return;
        }
    }

    private static async Task<ConnectResponse?> ReadResponseOrStopAsync(AsyncDuplexStreamingCall<ConnectRequest, ConnectResponse> call, CancellationToken stoppingToken, CancellationToken streamToken)
    {
        var read = call.ResponseStream.MoveNext(streamToken);
        var stop = Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        if (await Task.WhenAny(read, stop).ConfigureAwait(false) == stop)
            return null;
        return await read.ConfigureAwait(false) ? call.ResponseStream.Current : throw new RpcException(new Status(StatusCode.Unavailable, "Connect stream ended."));
    }

    private async Task WriteAsync(AsyncDuplexStreamingCall<ConnectRequest, ConnectResponse> call, ConnectRequest request, CancellationToken ct)
    {
        await _writer.WaitAsync(ct).ConfigureAwait(false);
            try { await call.RequestStream.WriteAsync(request, ct).ConfigureAwait(false); }
        finally { _writer.Release(); }
    }

    private async Task ShutdownStreamAsync(AsyncDuplexStreamingCall<ConnectRequest, ConnectResponse> call, bool sendGoodbye)
    {
        try
        {
            await _writer.WaitAsync(TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
            try
            {
                if (sendGoodbye)
                {
                    await call.RequestStream.WriteAsync(new ConnectRequest
                    {
                        Goodbye = new Goodbye { Reason = GoodbyeReason.Shutdown, Message = "Node shutting down." },
                    }).ConfigureAwait(false);
                }
                await call.RequestStream.CompleteAsync().ConfigureAwait(false);
            }
            finally { _writer.Release(); }
        }
        catch (Exception) { }
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
    [LoggerMessage(Level = LogLevel.Warning, Message = "Orchestrator connection: unavailable ({StatusCode}); next retry in {DelaySeconds:0.0} s")]
    private static partial void LogUnavailable(ILogger logger, StatusCode statusCode, double delaySeconds);
    [LoggerMessage(Level = LogLevel.Error, Message = "Orchestrator connection: authentication or protocol failure ({StatusCode}); next retry in {DelaySeconds:0.0} s")]
    private static partial void LogAuthenticationFailure(ILogger logger, StatusCode statusCode, double delaySeconds);
}
