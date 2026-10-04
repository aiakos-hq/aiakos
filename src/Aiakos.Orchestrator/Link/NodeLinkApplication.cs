using Aiakos.Contracts.Node.V1;

namespace Aiakos.Orchestrator.Link;

public sealed class NodeLinkOptions
{
    public TimeSpan HelloTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan LivenessTimeout { get; set; } = TimeSpan.FromSeconds(15);
}

public enum NodeLinkState { Connected, Unknown, Disconnected }

public interface INodeLinkApplication
{
    Task<IReadOnlyList<ReplayFrom>> GetReplayAsync(NodeIdentity identity, Hello hello, CancellationToken ct);
    Task ReceiveAsync(NodeIdentity identity, string nodeInstanceId, ConnectRequest request, CancellationToken ct);
    Task StateChangedAsync(NodeIdentity identity, NodeLinkState state, CancellationToken ct);
}

internal sealed class EmptyNodeLinkApplication : INodeLinkApplication
{
    public Task<IReadOnlyList<ReplayFrom>> GetReplayAsync(NodeIdentity identity, Hello hello, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ReplayFrom>>(Array.Empty<ReplayFrom>());

    public Task ReceiveAsync(NodeIdentity identity, string nodeInstanceId, ConnectRequest request, CancellationToken ct) => Task.CompletedTask;

    public Task StateChangedAsync(NodeIdentity identity, NodeLinkState state, CancellationToken ct) => Task.CompletedTask;
}
