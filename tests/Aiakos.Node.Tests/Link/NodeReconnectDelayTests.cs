using Aiakos.Node.Link;
using Grpc.Core;

namespace Aiakos.Node.Tests.Link;

public sealed class NodeReconnectDelayTests
{
    [Fact]
    public void UsesFullJitterAndDoublesTheStoredCeiling()
    {
        var delay = new NodeReconnectDelay(static () => 0.5);

        Assert.Equal(TimeSpan.FromMilliseconds(250), delay.NextDelay(StatusCode.Unavailable));
        Assert.Equal(TimeSpan.FromMilliseconds(500), delay.NextDelay(StatusCode.Unavailable));
        Assert.Equal(TimeSpan.FromMilliseconds(1000), delay.NextDelay(StatusCode.Unavailable));
        Assert.Equal(TimeSpan.FromMilliseconds(2000), delay.NextDelay(StatusCode.Unavailable));
        Assert.Equal(TimeSpan.FromMilliseconds(4000), delay.NextDelay(StatusCode.Unavailable));
    }

    [Fact]
    public void CapsNormalAndAuthenticationFailuresWithoutResettingTheCeiling()
    {
        var delay = new NodeReconnectDelay(static () => 0.5);
        TimeSpan result = default;
        for (var attempt = 0; attempt < 10; attempt++)
            result = delay.NextDelay(StatusCode.Unavailable);

        Assert.Equal(TimeSpan.FromSeconds(15), result);
        Assert.Equal(TimeSpan.FromSeconds(150), delay.NextDelay(StatusCode.Unauthenticated));

        var auth = new NodeReconnectDelay(static () => 0.5);
        for (var attempt = 0; attempt < 11; attempt++)
            result = auth.NextDelay(StatusCode.Unauthenticated);
        Assert.Equal(TimeSpan.FromSeconds(150), result);
    }

    [Fact]
    public void BoundsLongFailureRunsAndResetRestoresTheInitialCeiling()
    {
        var delay = new NodeReconnectDelay(static () => 0.5);
        for (var attempt = 0; attempt < 1000; attempt++)
            Assert.InRange(delay.NextDelay(StatusCode.Unavailable), TimeSpan.Zero, TimeSpan.FromSeconds(15));

        delay.Reset();

        Assert.Equal(TimeSpan.FromMilliseconds(250), delay.NextDelay(StatusCode.Unavailable));
        Assert.Equal(TimeSpan.Zero, new NodeReconnectDelay(static () => 0).NextDelay(StatusCode.Unavailable));
    }
}
