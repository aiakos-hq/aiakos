using System.Runtime.InteropServices;

using Aiakos.Contracts.Node;
using Aiakos.Contracts.Node.V1;

using Microsoft.Extensions.Options;

namespace Aiakos.Node.Link;

internal sealed class DefaultNodeLinkSource(IOptions<NodeOptions> options) : INodeLinkSource
{
    public Hello CreateHello(string nodeInstanceId)
    {
        var platform = new NodePlatform
        {
            Os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows"
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos"
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux"
                : "unknown",
            Arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
            Kernel = Environment.OSVersion.VersionString,
            Hostname = Environment.MachineName,
        };

        return new Hello
        {
            Protocol = NodeProtocol.Current,
            NodeName = options.Value.NodeId!,
            NodeInstanceId = nodeInstanceId,
            AgentVersion = typeof(NodeProgram).Assembly.GetName().Version?.ToString() ?? string.Empty,
            Platform = platform,
            Limits = new Limits
            {
                MaxMessageBytes = 4194304,
                MaxInflightCommands = 64,
                EventBufferCapacity = 10000,
            },
        };
    }

    public Heartbeat CreateHeartbeat() => new()
    {
        BufferedEvents = 0,
        InflightCommands = 0,
    };

    public Task WelcomeAsync(Welcome welcome, CancellationToken ct) => Task.CompletedTask;

    public Task ReceiveAsync(ConnectResponse response, CancellationToken ct) => Task.CompletedTask;
}
