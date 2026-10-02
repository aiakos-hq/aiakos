namespace Aiakos.Contracts.Node;

public static class NodeCapabilities
{
    public const string HarnessClaudeCode = "harness.claude-code";
    public const string CommandSendKeys = "command.send-keys";
    public const string LaunchFork = "launch.fork";
    public const string SessionHostTmux = "session-host.tmux";

    public static string ForHarness(string harness) => "harness." + harness;
}
