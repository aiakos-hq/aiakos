using Aiakos.Contracts.Node.V1;

namespace Aiakos.Node.Harnesses.ClaudeCode;

public sealed record ClaudeNormalizationResult(HarnessEvent Event, ClaudeNormalizationState State)
{
    public override string ToString() => "ClaudeNormalizationResult";
}
