using Aiakos.Orchestrator.Link;
using Aiakos.Orchestrator.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Aiakos.Contracts.Node.V1;

namespace Aiakos.Orchestrator.Tests.Link;

public sealed class NodeLinkApplicationTests(DatabaseFixture db) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task ProductionRegistrationUsesTheEmptyReplaceableApplicationAndDefaultOptions()
    {
        await using var factory = new OrchestratorFactory(db.ConnectionString);
        var application = factory.Services.GetRequiredService<INodeLinkApplication>();
        var options = factory.Services.GetRequiredService<IOptions<NodeLinkOptions>>().Value;
        var identity = new NodeIdentity("node", Guid.NewGuid(), "friendly");
        var hello = new Hello();
        var request = new ConnectRequest();
        var cancellationToken = TestContext.Current.CancellationToken;

        Assert.Empty(await application.GetReplayAsync(identity, hello, cancellationToken));
        await application.ReceiveAsync(identity, "instance", request, cancellationToken);
        await application.StateChangedAsync(identity, NodeLinkState.Connected, cancellationToken);
        Assert.Equal(TimeSpan.FromSeconds(10), options.HelloTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), options.HeartbeatInterval);
        Assert.Equal(TimeSpan.FromSeconds(15), options.LivenessTimeout);
    }
}
