using Aiakos.Orchestrator.Link;

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
    [InlineData("http://user:pw@127.0.0.1:5180", false)]
    [InlineData("http://example.test@127.0.0.1:5180", false)]
    [InlineData("http://127.0.0.1#@example.test", false)]
    [InlineData("http://127.0.0.1:5180/", false)]
    [InlineData("http://127.0.0.1:5180/path", false)]
    [InlineData("http://127.0.0.1:5180?x=1", false)]
    [InlineData("http://127.0.0.1:5180#x", false)]
    [InlineData(" http://127.0.0.1:5180", false)]
    [InlineData("http://127.0.0.1:5180\n", false)]
    [InlineData("http://[::1%1]:5180", false)]
    [InlineData("http://127.1:5180", false)]
    [InlineData("http://2130706433:5180", false)]
    [InlineData("http://0x7f.0.0.1:5180", false)]
    [InlineData("http://127.0.0.1:0", false)]
    [InlineData("http://127.00.0.1:5180", false)]
    [InlineData("http://127.0.0.1:05180", false)]
    [InlineData("http://127.0.0.1:65536", false)]
    [InlineData("http://127.0.0.1:-1", false)]
    [InlineData("http://127.0.0.1:%31", false)]
    [InlineData("http://127.0.0.1:5180 ", false)]
    [InlineData("http://127.999999999999999999999999.0.1", false)]
    [InlineData("http://127.0.0.1:999999999999999999999999", false)]
    [InlineData("http://127.0.0.1", true)]
    [InlineData("HTTP://LOCALHOST:5180", true)]
    [InlineData("not an endpoint", false)]
    public void IsAllowedOnlyForCanonicalLiteralLoopbackHttpAddresses(string address, bool expected)
    {
        Assert.Equal(expected, NodeLinkEndpointPolicy.IsAllowed(address));
    }

    [Fact]
    public void IsAllowedReturnsFalseForNull()
    {
        Assert.False(NodeLinkEndpointPolicy.IsAllowed(null!));
    }

    [Fact]
    public void ValidateRejectsNullAddressWithTheFixedError()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => NodeLinkEndpointPolicy.Validate([null!]));

        Assert.Equal("NodeLink requires an explicit loopback HTTP endpoint.", exception.Message);
    }

    [Theory]
    [InlineData("http://user:pw@127.0.0.1:5180")]
    [InlineData("http://example.test@127.0.0.1:5180")]
    [InlineData("http://127.0.0.1#@example.test")]
    [InlineData("http://127.0.0.1:5180/")]
    [InlineData("http://127.0.0.1:5180/path")]
    [InlineData("http://127.0.0.1:5180?x=1")]
    [InlineData("http://127.0.0.1:5180#x")]
    [InlineData(" http://127.0.0.1:5180")]
    [InlineData("http://127.0.0.1:5180\n")]
    [InlineData("http://[::1%1]:5180")]
    [InlineData("http://127.1:5180")]
    [InlineData("http://2130706433:5180")]
    [InlineData("http://0x7f.0.0.1:5180")]
    [InlineData("http://127.0.0.1:0")]
    [InlineData("http://127.00.0.1:5180")]
    [InlineData("http://127.0.0.1:05180")]
    [InlineData("http://127.999999999999999999999999.0.1")]
    [InlineData("http://127.0.0.1:999999999999999999999999")]
    public void ValidateRejectsEveryNoncanonicalFormWithTheFixedError(string address)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => NodeLinkEndpointPolicy.Validate([address]));

        Assert.Equal("NodeLink requires an explicit loopback HTTP endpoint.", exception.Message);
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
}
