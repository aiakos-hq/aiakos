using Aiakos.Core;

namespace Aiakos.Node.Tests.Sessions;

public sealed class TmuxNamesTests
{
    [Fact]
    public void FormsASessionNameFromAValidAddress()
    {
        Assert.Equal("aiakos-dev_impl", TmuxNames.SessionName("impl@aiakos-dev"));
    }

    [Theory]
    [InlineData("impl")]
    [InlineData("Impl@demo")]
    [InlineData("impl@d")]
    [InlineData("impl@demo@x")]
    [InlineData("im_pl@demo")]
    public void RejectsInvalidAddresses(string address)
    {
        Assert.False(TmuxNames.TryParseAddress(address, out _, out _));
        Assert.Throws<ArgumentException>(() => TmuxNames.SessionName(address));
    }
}
