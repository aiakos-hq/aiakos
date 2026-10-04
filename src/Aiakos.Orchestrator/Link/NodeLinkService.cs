using Aiakos.Contracts.Node;
using Aiakos.Contracts.Node.V1;

using Grpc.Core;

using Microsoft.Extensions.Options;

namespace Aiakos.Orchestrator.Link;

public sealed class NodeLinkService(
    NodeTokenRegistry tokens,
    IOptions<NodeLinkOptions> options,
    INodeLinkApplication application,
    NodeLinkRegistry registry,
    TimeProvider timeProvider) : Aiakos.Contracts.Node.V1.NodeLinkService.NodeLinkServiceBase
{
    public override async Task Connect(
        IAsyncStreamReader<ConnectRequest> requestStream,
        IServerStreamWriter<ConnectResponse> responseStream,
        ServerCallContext context)
    {
        var authorization = context.RequestHeaders
            .Where(static entry => string.Equals(entry.Key, "authorization", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var identity = authorization.Length == 1 ? tokens.Authenticate(authorization[0].Value) : null;
        if (identity is null)
            throw Failure(StatusCode.Unauthenticated, "Node authentication failed.");

        var hello = await ReadHelloAsync(requestStream, context, options.Value.HelloTimeout).ConfigureAwait(false);
        if (!string.Equals(hello.NodeName, identity.NodeName, StringComparison.Ordinal))
            throw Failure(StatusCode.PermissionDenied, "Node name does not match registration.");

        var protocol = NodeProtocol.Negotiate(hello.Protocol, NodeProtocol.Current);
        if (protocol is null)
            throw Failure(StatusCode.FailedPrecondition, "Unsupported node protocol.");

        if (!Guid.TryParse(hello.NodeInstanceId, out var instanceId) || instanceId == Guid.Empty)
            throw Failure(StatusCode.FailedPrecondition, "Invalid node instance ID.");

        IReadOnlyList<ReplayFrom> replay;
        try
        {
            replay = await application.GetReplayAsync(identity, hello, context.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (context.CancellationToken.IsCancellationRequested)
        {
            throw Failure(StatusCode.Cancelled, string.Empty);
        }
        catch (Exception)
        {
            throw Failure(StatusCode.Internal, "Node link processing failed.");
        }

        context.CancellationToken.ThrowIfCancellationRequested();
        var proxy = registry.Register(identity, hello.NodeInstanceId);
        var connected = false;
        var finalState = NodeLinkState.Unknown;
        try
        {
            var linkOptions = options.Value;
            var welcome = new Welcome
            {
                Protocol = protocol,
                NodeId = identity.NodeId,
                ConnectionId = Guid.NewGuid().ToString("D"),
                HeartbeatInterval = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(linkOptions.HeartbeatInterval),
                LivenessTimeout = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(linkOptions.LivenessTimeout),
                ServerTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(timeProvider.GetUtcNow().UtcDateTime),
                Limits = new Limits
                {
                    MaxMessageBytes = 4194304,
                    MaxInflightCommands = 64,
                    EventBufferCapacity = 10000,
                },
            };
            welcome.Replay.AddRange(replay);
            await responseStream.WriteAsync(new ConnectResponse { Welcome = welcome }).ConfigureAwait(false);
            await application.StateChangedAsync(identity, NodeLinkState.Connected, context.CancellationToken)
                .ConfigureAwait(false);
            connected = true;

            while (await requestStream.MoveNext(context.CancellationToken).ConfigureAwait(false))
            {
                var request = requestStream.Current;
                if (request.BodyCase == ConnectRequest.BodyOneofCase.Goodbye)
                    break;

                await registry.ReceiveAsync(proxy, request, context.CancellationToken).ConfigureAwait(false);
            }
            finalState = NodeLinkState.Disconnected;
        }
        catch (Exception) when (context.CancellationToken.IsCancellationRequested)
        {
            throw Failure(StatusCode.Cancelled, string.Empty);
        }
        catch (Exception)
        {
            throw Failure(StatusCode.Internal, "Node link processing failed.");
        }
        finally
        {
            try
            {
                if (connected)
                    await application.StateChangedAsync(identity, finalState, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Callback failures must not expose peer-controlled values in server logs.
            }
            finally
            {
                registry.Remove(identity, proxy);
            }
        }
    }

    private static async Task<Hello> ReadHelloAsync(
        IAsyncStreamReader<ConnectRequest> requestStream,
        ServerCallContext context,
        TimeSpan timeout)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        timeoutSource.CancelAfter(timeout);
        bool hasFirstMessage;
        try
        {
            hasFirstMessage = await requestStream.MoveNext(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!context.CancellationToken.IsCancellationRequested)
        {
            throw Failure(StatusCode.FailedPrecondition, "Hello must be the first node message.");
        }
        catch (RpcException exception) when (timeoutSource.IsCancellationRequested &&
            !context.CancellationToken.IsCancellationRequested && exception.StatusCode == StatusCode.Cancelled)
        {
            throw Failure(StatusCode.FailedPrecondition, "Hello must be the first node message.");
        }
        catch (Exception) when (context.CancellationToken.IsCancellationRequested)
        {
            throw Failure(StatusCode.Cancelled, string.Empty);
        }
        catch (Exception)
        {
            throw Failure(StatusCode.Unavailable, "Node link unavailable.");
        }

        if (!hasFirstMessage || requestStream.Current.BodyCase != ConnectRequest.BodyOneofCase.Hello)
            throw Failure(StatusCode.FailedPrecondition, "Hello must be the first node message.");
        return requestStream.Current.Hello;
    }

    private static RpcException Failure(StatusCode code, string detail) => new(new Status(code, detail));
}
