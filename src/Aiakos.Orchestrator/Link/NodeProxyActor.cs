using Akka.Actor;

using Aiakos.Contracts.Node.V1;

namespace Aiakos.Orchestrator.Link;

public sealed class NodeProxyActor : ReceiveActor
{
    private readonly NodeIdentity _identity;
    private readonly string _nodeInstanceId;
    private readonly INodeLinkApplication _application;

    public NodeProxyActor(NodeIdentity identity, string nodeInstanceId, INodeLinkApplication application)
    {
        _identity = identity;
        _nodeInstanceId = nodeInstanceId;
        _application = application;
        ReceiveAsync<ReceiveNodeMessage>(ReceiveAsync);
    }

    private async Task ReceiveAsync(ReceiveNodeMessage message)
    {
        try
        {
            await _application.ReceiveAsync(_identity, _nodeInstanceId, message.Request, message.CancellationToken)
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
