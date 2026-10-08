using Aiakos.Contracts.Node.V1;
using Aiakos.Orchestrator.Harnesses.ClaudeCode;
using Xunit;

namespace Aiakos.Orchestrator.Tests.Harnesses.ClaudeCode;

public sealed class ClaudeCodeRelayTests
{
    [Fact]
    public void EmbeddedRelayHasStableSeatFileContractAndFreshSnapshots()
    {
        var first = ClaudeCodeRelay.BuildFile();
        var second = ClaudeCodeRelay.BuildFile();
        Assert.Equal(FileRoot.SeatHome, first.Root);
        Assert.Equal("aiakos/bin/aiakos-hook-relay", first.Path);
        Assert.Equal(493U, first.Mode);
        Assert.False(first.Expand);
        Assert.Equal(first.Content.ToByteArray(), second.Content.ToByteArray());
        Assert.Equal((byte)10, first.Content.ToByteArray()[^1]);
        Assert.DoesNotContain(new byte[] { 13 }, first.Content.ToByteArray());
        Assert.NotSame(first, second);
        first.Path = "changed";
        first.Content = Google.Protobuf.ByteString.Empty;
        Assert.Equal("aiakos/bin/aiakos-hook-relay", second.Path);
        Assert.NotEmpty(second.Content);
    }
}
