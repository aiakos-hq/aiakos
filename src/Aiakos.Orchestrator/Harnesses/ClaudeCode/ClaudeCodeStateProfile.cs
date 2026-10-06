using Aiakos.Contracts.Node.V1;
using Aiakos.Orchestrator.Seats;

namespace Aiakos.Orchestrator.Harnesses.ClaudeCode;

public sealed class ClaudeCodeStateProfile : IHarnessStateProfile
{
    private const string PreviousSessionId = "previous_session_id";
    private const string Source = "source";

    public string Harness => "claude-code";

    public string NewNativeSessionId() => Guid.NewGuid().ToString("D");

    public bool IsValidNativeSessionId(string id)
    {
        if (id is null || id.Length != 36 || id[8] != '-' || id[13] != '-' || id[18] != '-' || id[23] != '-')
            return false;

        for (var index = 0; index < id.Length; index++)
        {
            if (index is 8 or 13 or 18 or 23)
                continue;

            var character = id[index];
            if (index == 14)
            {
                if (character != '4')
                    return false;
            }
            else if (index == 19)
            {
                if (character is not ('8' or '9' or 'a' or 'b'))
                    return false;
            }
            else if (!IsLowercaseHex(character))
            {
                return false;
            }
        }

        return true;
    }

    public bool IsReadiness(HarnessEventKind kind, IReadOnlyDictionary<string, string> attributes) =>
        kind == HarnessEventKind.SessionStarted && TryGetSource(attributes, out var source) && IsReadinessSource(source);

    public bool IsConversationEvidence(HarnessEventKind kind, IReadOnlyDictionary<string, string> attributes) =>
        kind is HarnessEventKind.PromptSubmitted or HarnessEventKind.TurnEnded or HarnessEventKind.TurnFailed;

    public bool IsSessionRotation(HarnessEventKind kind, IReadOnlyDictionary<string, string> attributes) =>
        kind == HarnessEventKind.SessionStarted && TryGetSource(attributes, out var source) && source == "clear" &&
        TryGetExact(attributes, PreviousSessionId, out var previousSessionId) && IsValidNativeSessionId(previousSessionId);

    public bool FreshRelaunchReusesSessionId => true;
    public bool EmitsInputResolved => false;
    public TimeSpan ReadyTimeout => TimeSpan.FromSeconds(15);
    public TimeSpan ConfirmTimeout => TimeSpan.FromSeconds(5);
    public TimeSpan QuietTimeout => TimeSpan.FromMinutes(10);

    private static bool TryGetSource(IReadOnlyDictionary<string, string> attributes, out string source)
    {
        if (TryGetExact(attributes, Source, out var value))
        {
            source = value;
            return true;
        }

        source = string.Empty;
        return false;
    }

    private static bool TryGetExact(IReadOnlyDictionary<string, string> attributes, string key, out string value)
    {
        if (attributes is not null)
        {
            foreach (var pair in attributes)
            {
                if (string.Equals(pair.Key, key, StringComparison.Ordinal))
                {
                    value = pair.Value;
                    return true;
                }
            }
        }

        value = string.Empty;
        return false;
    }

    private static bool IsReadinessSource(string source) =>
        source is "startup" or "resume" or "fork" or "clear";

    private static bool IsLowercaseHex(char character) =>
        character is >= '0' and <= '9' or >= 'a' and <= 'f';
}
