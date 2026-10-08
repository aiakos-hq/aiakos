using System.Globalization;
using System.Text;
using System.Text.Json;
using Aiakos.Contracts.Node;
using Aiakos.Contracts.Node.V1;
using Google.Protobuf;

namespace Aiakos.Node.Harnesses.ClaudeCode;

public static class ClaudeHookNormalizer
{
    private const int MaxDepth = 64;

    public static ClaudeNormalizationResult Normalize(ClaudeNormalizationState state,
        string nativeName, ReadOnlyMemory<byte> body, string? deliveryId = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(nativeName);

        JsonDocument? document = null;
        JsonElement root = default;
        bool valid = body.Length > 0 && TryParse(body, out document, out root);
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        string sessionId = valid ? String(root, "session_id") ?? string.Empty : string.Empty;
        HarnessEventKind kind = HarnessEventKind.Other;

        if (valid)
        {
            switch (nativeName)
            {
                case "SessionStart":
                    string? source = String(root, "source");
                    if (source is "startup" or "resume" or "fork")
                    {
                        kind = HarnessEventKind.SessionStarted;
                        Add(attributes, "source", source);
                        Add(attributes, "model", String(root, "model"));
                        Add(attributes, "session_title", String(root, "session_title"));
                        if (source == "resume")
                        {
                            AddNumber(attributes, "context_tokens", root, "context_tokens");
                            AddNumber(attributes, "seconds_since_last_response", root, "seconds_since_last_response");
                        }
                    }
                    break;
                case "PreCompact":
                    kind = HarnessEventKind.CompactionStarted;
                    Add(attributes, "trigger", String(root, "trigger"));
                    break;
                case "SessionEnd":
                    string? reason = String(root, "reason");
                    if (reason is not null && reason != "clear")
                    {
                        kind = HarnessEventKind.SessionEnded;
                        Add(attributes, "reason", reason);
                    }
                    break;
                case "UserPromptSubmit":
                    kind = HarnessEventKind.PromptSubmitted;
                    Add(attributes, "turn_id", String(root, "prompt_id"));
                    Add(attributes, "permission_mode", String(root, "permission_mode"));
                    Add(attributes, "delivery_id", string.IsNullOrEmpty(deliveryId) ? null : deliveryId);
                    break;
                case "PreToolUse":
                    kind = HarnessEventKind.ToolStarted;
                    Add(attributes, "tool_name", String(root, "tool_name"));
                    Add(attributes, "tool_use_id", String(root, "tool_use_id"));
                    Add(attributes, "turn_id", String(root, "prompt_id"));
                    break;
                case "PostToolUse":
                case "PostToolUseFailure":
                    kind = HarnessEventKind.ToolFinished;
                    Add(attributes, "tool_name", String(root, "tool_name"));
                    Add(attributes, "tool_use_id", String(root, "tool_use_id"));
                    AddNumber(attributes, "duration_ms", root, "duration_ms");
                    if (nativeName == "PostToolUseFailure") attributes.Add("failed", "true");
                    break;
                case "Stop":
                    kind = HarnessEventKind.TurnEnded;
                    Add(attributes, "turn_id", String(root, "prompt_id"));
                    AddBoolean(attributes, "stop_hook_active", root, "stop_hook_active");
                    break;
                case "StopFailure":
                    kind = HarnessEventKind.TurnFailed;
                    Add(attributes, "turn_id", String(root, "prompt_id"));
                    Add(attributes, "error", String(root, "last_assistant_message"));
                    break;
                case "Notification":
                    Add(attributes, "notification_type", String(root, "notification_type"));
                    break;
            }
        }

        document?.Dispose();
        var resultEvent = new HarnessEvent
        {
            Harness = "claude-code",
            NativeName = nativeName,
            Kind = kind,
            NativeSessionId = sessionId,
            Raw = ByteString.CopyFrom(body.Span),
            RawContentType = "application/json",
            Origin = EventOrigin.Live
        };
        foreach ((string key, string value) in attributes) resultEvent.Attributes.Add(key, value);
        HarnessEventLimits.Apply(resultEvent);
        return new ClaudeNormalizationResult(resultEvent, state);
    }

    private static bool TryParse(ReadOnlyMemory<byte> body, out JsonDocument? document, out JsonElement root)
    {
        document = null;
        root = default;
        try
        {
            document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = MaxDepth });
            root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !ValidStrings(root))
            {
                document.Dispose();
                document = null;
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            document?.Dispose();
            document = null;
            return false;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            document?.Dispose();
            document = null;
            return false;
        }
    }

    private static bool ValidStrings(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (!ValidUtf16(property.Name) || !ValidStrings(property.Value)) return false;
                }
                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray()) if (!ValidStrings(item)) return false;
                break;
            case JsonValueKind.String:
                return ValidUtf16(element.GetString() ?? string.Empty);
        }
        return true;
    }

    private static bool ValidUtf16(string value)
    {
        try { _ = new UTF8Encoding(false, true).GetByteCount(value); return true; }
        catch (EncoderFallbackException) { return false; }
    }

    private static string? String(JsonElement root, string name)
    {
        if (root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            return value.GetString();
        return null;
    }

    private static void Add(Dictionary<string, string> values, string name, string? value)
    {
        if (value is not null) values[name] = value;
    }

    private static void AddNumber(Dictionary<string, string> values, string name, JsonElement root, string property)
    {
        if (root.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.Number)
            values[name] = value.GetRawText();
    }

    private static void AddBoolean(Dictionary<string, string> values, string name, JsonElement root, string property)
    {
        if (root.TryGetProperty(property, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            values[name] = value.GetBoolean().ToString().ToLower(CultureInfo.InvariantCulture);
    }
}
