using Akka.Actor;

using Aiakos.Contracts.Node.V1;

namespace Aiakos.Orchestrator.Link;

public sealed class NodeProxyActor : ReceiveActor
{
    private readonly NodeIdentity _identity;
    private readonly string _nodeInstanceId;
    private readonly INodeLinkApplication _application;
    private readonly CancellationToken _sessionToken;

    public NodeProxyActor(NodeIdentity identity, string nodeInstanceId, INodeLinkApplication application)
        : this(identity, nodeInstanceId, application, CancellationToken.None)
    {
    }

    public NodeProxyActor(NodeIdentity identity, string nodeInstanceId, INodeLinkApplication application,
        CancellationToken sessionToken)
    {
        _identity = identity;
        _nodeInstanceId = nodeInstanceId;
        _application = application;
        _sessionToken = sessionToken;
        ReceiveAsync<ReceiveNodeMessage>(ReceiveAsync);
    }

    private async Task ReceiveAsync(ReceiveNodeMessage message)
    {
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(message.CancellationToken, _sessionToken);
            await _application.ReceiveAsync(_identity, _nodeInstanceId, message.Request, linked.Token)
                .ConfigureAwait(false);
            message.Completion.TrySetResult();
        }
        catch (Exception exception)
        {
            message.Completion.TrySetException(exception);
        }
    }
}

internal sealed record ReceiveNodeMessage(
    ConnectRequest Request,
    TaskCompletionSource Completion,
    CancellationToken CancellationToken);
