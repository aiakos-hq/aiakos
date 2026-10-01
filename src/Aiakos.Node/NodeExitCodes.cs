using Microsoft.Extensions.Options;

namespace Aiakos.Node;

/// <summary>Process exit codes of <c>aiakos-node</c> (spec 0001, Design → Node).</summary>
public static class NodeExitCodes
{
    /// <summary>Graceful stop (SIGHUP, SIGTERM, SIGINT).</summary>
    public const int Graceful = 0;

    /// <summary>Anything unexpected.</summary>
    public const int Unexpected = 1;

    /// <summary>A required setting is missing or invalid (R39).</summary>
    public const int InvalidConfiguration = 2;

    /// <summary>Another node holds the instance lock (R38).</summary>
    public const int LockHeld = 3;

    /// <summary>Maps an exception that escaped startup or the host to an exit code.</summary>
    public static int FromException(Exception exception) => exception switch
    {
        OptionsValidationException => InvalidConfiguration,
        AggregateException aggregate when aggregate.InnerExceptions.Count > 0
            && aggregate.InnerExceptions.All(static e => e is OptionsValidationException) => InvalidConfiguration,
        _ => Unexpected,
    };
}
