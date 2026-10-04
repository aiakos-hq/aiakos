using Akka.Actor;

using Aiakos.Contracts.Node.V1;

namespace Aiakos.Orchestrator.Link;

public sealed class NodeLinkRegistry(ActorSystem actorSystem, INodeLinkApplication application)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, IActorRef> _actors = new(StringComparer.Ordinal);

    public IActorRef Register(NodeIdentity identity, string nodeInstanceId)
    {
        lock (_gate)
        {
            var actor = actorSystem.ActorOf(
                Props.Create(() => new NodeProxyActor(identity, nodeInstanceId, application)),
                $"node-link-{Guid.NewGuid():N}");
            if (_actors.TryGetValue(identity.NodeId, out var previous))
                actorSystem.Stop(previous);
            _actors[identity.NodeId] = actor;
            return actor;
        }
    }

    public async Task ReceiveAsync(IActorRef actor, ConnectRequest request, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        actor.Tell(new ReceiveNodeMessage(request, completion, cancellationToken));
        await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Remove(NodeIdentity identity, IActorRef actor)
    {
        lock (_gate)
        {
            if (_actors.TryGetValue(identity.NodeId, out var current) && ReferenceEquals(current, actor))
                _actors.Remove(identity.NodeId);
            actorSystem.Stop(actor);
        }
    }
}
