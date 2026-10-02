using Aiakos.Contracts.Node;

namespace Aiakos.Contracts.Tests;

[Trait("Category", "Contract")]
public sealed class CapabilityAndKeyTests
{
    [Fact]
    public void ExposesStableCapabilitiesAndKeys()
    {
        Assert.Equal("harness.claude-code", NodeCapabilities.HarnessClaudeCode);
        Assert.Equal("command.send-keys", NodeCapabilities.CommandSendKeys);
        Assert.Equal("launch.fork", NodeCapabilities.LaunchFork);
        Assert.Equal("session-host.tmux", NodeCapabilities.SessionHostTmux);
        Assert.Equal(NodeCapabilities.HarnessClaudeCode, NodeCapabilities.ForHarness("claude-code"));
        Assert.Equal(["Enter", "Escape", "Tab", "Up", "Down", "Left", "Right", "C-c", "C-d"], SendKeyNames.All);
        Assert.True(SendKeyNames.IsAllowed("C-c"));
        Assert.False(SendKeyNames.IsAllowed("c-c"));
    }
}
