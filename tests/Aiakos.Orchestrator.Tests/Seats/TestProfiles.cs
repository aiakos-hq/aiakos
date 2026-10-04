using Aiakos.Contracts.Node.V1;
using Aiakos.Orchestrator.Seats;

namespace Aiakos.Orchestrator.Tests.Seats;

internal class ClaudeLike : IHarnessStateProfile
{
    public string Harness => "claude-code";
    public string NewNativeSessionId() => "new-session";
    public bool IsValidNativeSessionId(string id) => !string.IsNullOrWhiteSpace(id);
    public bool IsReadiness(HarnessEventKind kind, IReadOnlyDictionary<string, string> attributes) =>
        kind == HarnessEventKind.SessionStarted && attributes.TryGetValue("source", out var source) &&
        source is "startup" or "resume" or "fork" or "clear";
    public bool IsConversationEvidence(HarnessEventKind kind, IReadOnlyDictionary<string, string> attributes) =>
        kind is HarnessEventKind.PromptSubmitted or HarnessEventKind.TurnEnded or HarnessEventKind.TurnFailed;
    public virtual bool IsSessionRotation(HarnessEventKind kind, IReadOnlyDictionary<string, string> attributes) =>
        kind == HarnessEventKind.SessionStarted && attributes.TryGetValue("source", out var source) && source == "clear" &&
        attributes.ContainsKey("previous_session_id");
    public bool FreshRelaunchReusesSessionId => true;
    public bool EmitsInputResolved => false;
    public TimeSpan ReadyTimeout => TimeSpan.FromSeconds(15);
    public TimeSpan ConfirmTimeout => TimeSpan.FromSeconds(5);
    public TimeSpan QuietTimeout => TimeSpan.FromMinutes(10);
}

internal sealed class OpenCodeLike : IHarnessStateProfile
{
    public string Harness => "opencode";
    public string NewNativeSessionId() => "new-session";
    public bool IsValidNativeSessionId(string id) => !string.IsNullOrWhiteSpace(id);
    public bool IsReadiness(HarnessEventKind kind, IReadOnlyDictionary<string, string> attributes) => kind == HarnessEventKind.SessionStarted;
    public bool IsConversationEvidence(HarnessEventKind kind, IReadOnlyDictionary<string, string> attributes) => kind == HarnessEventKind.SessionStarted;
    public bool IsSessionRotation(HarnessEventKind kind, IReadOnlyDictionary<string, string> attributes) => false;
    public bool FreshRelaunchReusesSessionId => false;
    public bool EmitsInputResolved => true;
    public TimeSpan ReadyTimeout => TimeSpan.FromSeconds(15);
    public TimeSpan ConfirmTimeout => TimeSpan.FromSeconds(5);
    public TimeSpan QuietTimeout => TimeSpan.FromMinutes(10);
}
