using Aiakos.Contracts.Node;
using Aiakos.Node.Link;
using Aiakos.Contracts.Node.V1;
using Microsoft.Extensions.DependencyInjection;

namespace Aiakos.Node.Tests.Link;

public sealed class NodeLinkSourceTests
{
    [Fact]
    public async Task DefaultSourceBuildsBootstrapMessagesAndCompletesCallbacks()
    {
        using var home = new TempDirectory();
        using var logs = new CapturingLoggerProvider();
        using var host = NodeProgram.BuildHost([], NodeHost.With(NodeHost.ValidSettings(home.Path), logs));
        var source = host.Services.GetRequiredService<INodeLinkSource>();

        var hello = source.CreateHello("instance-123");
        var heartbeat = source.CreateHeartbeat();

        Assert.Equal(NodeProtocol.Current, hello.Protocol);
        Assert.Equal("test-node", hello.NodeName);
        Assert.Equal("instance-123", hello.NodeInstanceId);
        Assert.Equal(typeof(NodeProgram).Assembly.GetName().Version?.ToString(), hello.AgentVersion);
        Assert.NotNull(hello.Platform);
        Assert.False(string.IsNullOrWhiteSpace(hello.Platform.Os));
        Assert.False(string.IsNullOrWhiteSpace(hello.Platform.Arch));
        Assert.Empty(hello.Capabilities);
        Assert.Empty(hello.Seats);
        Assert.Equal((uint)4194304, hello.Limits.MaxMessageBytes);
        Assert.Equal((uint)64, hello.Limits.MaxInflightCommands);
        Assert.Equal((uint)10000, hello.Limits.EventBufferCapacity);
        Assert.Equal((uint)0, heartbeat.BufferedEvents);
        Assert.Equal((uint)0, heartbeat.InflightCommands);

        await source.WelcomeAsync(new Welcome(), TestContext.Current.CancellationToken);
        await source.ReceiveAsync(new ConnectResponse(), TestContext.Current.CancellationToken);
    }
}
