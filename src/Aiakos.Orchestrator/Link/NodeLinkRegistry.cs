using System.Diagnostics;
using Akka.Actor;

using Aiakos.Contracts.Node.V1;

namespace Aiakos.Orchestrator.Link;

public sealed class NodeLinkRegistry(ActorSystem actorSystem, INodeLinkApplication application)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, NodeLinkSession> _sessions = new(StringComparer.Ordinal);

    internal NodeLinkSession Register(NodeIdentity identity, string nodeInstanceId)
    {
        lock (_gate)
        {
            var session = new NodeLinkSession();
            session.Actor = actorSystem.ActorOf(
                Props.Create(() => new NodeProxyActor(identity, nodeInstanceId, application, session.CallbackToken)),
                $"node-link-{Guid.NewGuid():N}");
            if (_sessions.TryGetValue(identity.NodeId, out var previous))
                previous.Supersede(actorSystem);
            _sessions[identity.NodeId] = session;
            return session;
        }
    }

    internal static async Task ReceiveAsync(NodeLinkSession session, ConnectRequest request, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.CallbackToken);
        session.Actor.Tell(new ReceiveNodeMessage(request, completion, Activity.Current, linked.Token));
        await completion.Task.WaitAsync(linked.Token).ConfigureAwait(false);
    }

    internal void Remove(NodeIdentity identity, NodeLinkSession session)
    {
        lock (_gate)
        {
            if (_sessions.TryGetValue(identity.NodeId, out var current) && ReferenceEquals(current, session))
                _sessions.Remove(identity.NodeId);
            actorSystem.Stop(session.Actor);
            session.Dispose();
        }
    }
}

internal sealed class NodeLinkSession : IDisposable
{
    private readonly CancellationTokenSource _callbackCancellation = new();
    private readonly TaskCompletionSource _superseded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _ended;

    public IActorRef Actor { get; set; } = ActorRefs.Nobody;
    public CancellationToken CallbackToken => _callbackCancellation.Token;
    public Task Superseded => _superseded.Task;

    public void Supersede(ActorSystem actorSystem)
    {
        if (Interlocked.Exchange(ref _ended, 1) != 0)
            return;
        _superseded.TrySetResult();
        _callbackCancellation.Cancel();
        actorSystem.Stop(Actor);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _ended, 1) == 0)
            _callbackCancellation.Cancel();
        _callbackCancellation.Dispose();
    }
}
