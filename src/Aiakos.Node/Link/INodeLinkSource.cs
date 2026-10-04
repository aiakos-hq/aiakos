using Aiakos.Contracts.Node.V1;

namespace Aiakos.Node.Link;

public interface INodeLinkSource
{
    Hello CreateHello(string nodeInstanceId);
    Heartbeat CreateHeartbeat();
    Task WelcomeAsync(Welcome welcome, CancellationToken ct);
    Task ReceiveAsync(ConnectResponse response, CancellationToken ct);
}
