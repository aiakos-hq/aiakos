using Microsoft.Extensions.Options;

namespace Aiakos.Orchestrator.Link;

public sealed class NodeLinkOptionsValidator : IValidateOptions<NodeLinkOptions>
{
    private static readonly TimeSpan Minimum = TimeSpan.FromMilliseconds(1);
    private static readonly TimeSpan Maximum = TimeSpan.FromTicks(42_949_672_940_000);

    public ValidateOptionsResult Validate(string? name, NodeLinkOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();
        var helloValid = IsValid(options.HelloTimeout);
        var heartbeatValid = IsValid(options.HeartbeatInterval);
        var livenessValid = IsValid(options.LivenessTimeout);

        if (!helloValid)
            failures.Add("NodeLink HelloTimeout must be between 1 and 4294967294 milliseconds.");
        if (!heartbeatValid)
            failures.Add("NodeLink HeartbeatInterval must be between 1 and 4294967294 milliseconds.");
        if (!livenessValid)
            failures.Add("NodeLink LivenessTimeout must be between 1 and 4294967294 milliseconds.");
        if (heartbeatValid && livenessValid && options.HeartbeatInterval >= options.LivenessTimeout)
            failures.Add("NodeLink HeartbeatInterval must be less than LivenessTimeout.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsValid(TimeSpan value) => value >= Minimum && value <= Maximum;
}
