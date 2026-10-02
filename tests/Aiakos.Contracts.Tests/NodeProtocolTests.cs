using Aiakos.Contracts.Node;
using Aiakos.Contracts.Node.V1;

namespace Aiakos.Contracts.Tests;

[Trait("Category", "Contract")]
public sealed class NodeProtocolTests
{
    [Theory]
    [InlineData(1u, 0u, 1u, 0u, 1u, 0u)]
    [InlineData(1u, 3u, 1u, 1u, 1u, 1u)]
    [InlineData(1u, 0u, 1u, 2u, 1u, 0u)]
    public void NegotiatesTheSharedVersion(uint nodeMajor, uint nodeMinor, uint orchestratorMajor, uint orchestratorMinor, uint major, uint minor)
    {
        ProtocolVersion? result = NodeProtocol.Negotiate(new() { Major = nodeMajor, Minor = nodeMinor }, new() { Major = orchestratorMajor, Minor = orchestratorMinor });
        Assert.Equal(new ProtocolVersion { Major = major, Minor = minor }, result);
    }

    [Fact]
    public void RejectsMissingOrMismatchedMajorVersions()
    {
        Assert.Null(NodeProtocol.Negotiate(null, NodeProtocol.Current));
        Assert.Null(NodeProtocol.Negotiate(new() { Major = 2 }, NodeProtocol.Current));
    }
}
