using Aiakos.Orchestrator.Link;
using System.Net;

namespace Aiakos.Orchestrator.Tests.Link;

public sealed class NodeLinkEndpointPolicyTests
{
    [Theory]
    [InlineData("http://127.0.0.1:5180", true)]
    [InlineData("http://127.2.3.4:5180", true)]
    [InlineData("http://localhost:5180", true)]
    [InlineData("http://[::1]:5180", true)]
    [InlineData("http://0.0.0.0:5180", false)]
    [InlineData("http://example.test:5180", false)]
    [InlineData("https://127.0.0.1:5180", false)]
    [InlineData("not an endpoint", false)]
    public void IsAllowedOnlyForExplicitLoopbackHttpAddresses(string address, bool expected)
    {
        Assert.Equal(expected, NodeLinkEndpointPolicy.IsAllowed(address));
    }

    [Fact]
    public void ValidateRejectsNonLoopbackAndEmptyAddressListsWithTheFixedError()
    {
        var nonLoopback = Assert.Throws<InvalidOperationException>(() =>
            NodeLinkEndpointPolicy.Validate(["http://127.0.0.1:5180", "http://0.0.0.0:5180"]));
        var empty = Assert.Throws<InvalidOperationException>(() => NodeLinkEndpointPolicy.Validate([]));

        Assert.Equal("NodeLink requires an explicit loopback HTTP endpoint.", nonLoopback.Message);
        Assert.Equal("NodeLink requires an explicit loopback HTTP endpoint.", empty.Message);
    }

    [Fact]
    public void ConfiguredAndResolvedEndpointChecksHonorGrpcPortAndRejectIndeterminateEndpoints()
    {
        NodeLinkEndpointPolicy.ValidateConfigured(
            ["http://0.0.0.0:5180", "http://127.0.0.1:5181"], grpcPort: 5181);
        NodeLinkEndpointPolicy.ValidateResolved(new IPEndPoint(IPAddress.Any, 5180), grpcPort: 5181);

        var noEligibleEndpoint = Assert.Throws<InvalidOperationException>(() =>
            NodeLinkEndpointPolicy.ValidateConfigured(["http://127.0.0.1:5180"], grpcPort: 5181));
        var nonLoopback = Assert.Throws<InvalidOperationException>(() =>
            NodeLinkEndpointPolicy.ValidateResolved(new IPEndPoint(IPAddress.Any, 5181), grpcPort: 5181));
        var indeterminate = Assert.Throws<InvalidOperationException>(() =>
            NodeLinkEndpointPolicy.ValidateResolved(null, grpcPort: 5181));

        Assert.Equal("NodeLink requires an explicit loopback HTTP endpoint.", noEligibleEndpoint.Message);
        Assert.Equal("NodeLink requires an explicit loopback HTTP endpoint.", nonLoopback.Message);
        Assert.Equal("NodeLink requires an explicit loopback HTTP endpoint.", indeterminate.Message);
    }
}
