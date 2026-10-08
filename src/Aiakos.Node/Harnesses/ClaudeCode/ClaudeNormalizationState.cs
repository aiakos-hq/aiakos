namespace Aiakos.Node.Harnesses.ClaudeCode;

public sealed class ClaudeNormalizationState
{
    public ClaudeNormalizationState(string currentSessionId)
    {
        ArgumentNullException.ThrowIfNull(currentSessionId);
        CurrentSessionId = currentSessionId;
    }

    public string CurrentSessionId { get; }

    public override string ToString() => "ClaudeNormalizationState";
}
