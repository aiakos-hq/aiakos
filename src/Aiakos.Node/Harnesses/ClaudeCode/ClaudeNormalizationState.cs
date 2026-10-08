namespace Aiakos.Node.Harnesses.ClaudeCode;

public sealed class ClaudeNormalizationState
{
    public ClaudeNormalizationState(string currentSessionId)
        : this(currentSessionId, [], false)
    {
    }

    internal ClaudeNormalizationState(string currentSessionId, ClaudeOpenTool[] openTools, bool permissionEchoSeen)
    {
        ArgumentNullException.ThrowIfNull(currentSessionId);
        CurrentSessionId = currentSessionId;
        _openTools = (ClaudeOpenTool[])openTools.Clone();
        PermissionEchoSeen = permissionEchoSeen;
    }

    public string CurrentSessionId { get; }

    private readonly ClaudeOpenTool[] _openTools;

    internal ClaudeOpenTool[] OpenTools => (ClaudeOpenTool[])_openTools.Clone();

    internal bool PermissionEchoSeen { get; }

    internal ClaudeNormalizationState With(ClaudeOpenTool[]? openTools = null, bool? permissionEchoSeen = null)
    {
        ClaudeOpenTool[] nextTools = openTools ?? _openTools;
        bool nextEchoSeen = permissionEchoSeen ?? PermissionEchoSeen;
        if (nextEchoSeen == PermissionEchoSeen && nextTools.SequenceEqual(_openTools)) return this;
        return new ClaudeNormalizationState(CurrentSessionId, nextTools, nextEchoSeen);
    }

    public override string ToString() => "ClaudeNormalizationState";
}

internal sealed record ClaudeOpenTool(string Id, string Name, string CanonicalInput);
