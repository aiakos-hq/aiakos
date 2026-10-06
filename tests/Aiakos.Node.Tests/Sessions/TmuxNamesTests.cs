using Aiakos.Core;

namespace Aiakos.Node.Tests.Sessions;

public sealed class TmuxNamesTests
{
    private static readonly string[] AttachCommand = ["tmux", "-L", "aiakos-test", "attach-session", "-r", "-t", "demo_impl"];
    [Fact]
    public void FormsASessionNameFromAValidAddress()
    {
        Assert.Equal("aiakos-dev_impl", TmuxNames.SessionName("impl@aiakos-dev"));
    }

    [Fact]
    public void BuildsPrivateSocketConfigAndAttachCommands()
    {
        Assert.Equal("aiakos-test", TmuxNames.SocketName("test"));
        Assert.Equal(Path.Combine("/tmp/node-fixture", "tmux", "tmux.conf"),
            TmuxNames.ConfigPath("/tmp/node-fixture"));
        Assert.Equal(AttachCommand, TmuxNames.AttachCommand("test", "impl@demo"));
        Assert.Throws<ArgumentException>(() => TmuxNames.SocketName("Bad"));
        Assert.Throws<ArgumentException>(() => TmuxNames.ConfigPath("relative"));
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
