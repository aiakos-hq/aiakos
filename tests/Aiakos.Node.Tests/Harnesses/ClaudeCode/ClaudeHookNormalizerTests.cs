using System.Text;
using System.Text.Json;
using Aiakos.Contracts.Node.V1;
using Aiakos.Node.Harnesses.ClaudeCode;
using Google.Protobuf;
using Xunit;

namespace Aiakos.Node.Tests.Harnesses.ClaudeCode;

public sealed class ClaudeHookNormalizerTests
{
    private const string Base = "{\"session_id\":\"session-a\",\"prompt_id\":\"turn-a\"}";

    [Fact]
    public void MapsOwnedSessionHooksAndPreservesSessionSnapshot()
    {
        var state = new ClaudeNormalizationState("session-a");
        AssertEvent("SessionStart", "{\"session_id\":\"session-a\",\"source\":\"startup\",\"model\":\"haiku\",\"session_title\":\"impl\"}",
            HarnessEventKind.SessionStarted, state, [("source", "startup"), ("model", "haiku"), ("session_title", "impl")]);
        AssertEvent("SessionStart", "{\"source\":\"resume\",\"context_tokens\":42,\"seconds_since_last_response\":2}",
            HarnessEventKind.SessionStarted, state, [("source", "resume"), ("context_tokens", "42"), ("seconds_since_last_response", "2")]);
        AssertEvent("SessionStart", "{\"source\":\"fork\"}", HarnessEventKind.SessionStarted, state, [("source", "fork")]);
        AssertEvent("SessionStart", "{\"source\":\"clear\"}", HarnessEventKind.Other, state);
        AssertEvent("PreCompact", "{\"trigger\":\"manual\"}", HarnessEventKind.CompactionStarted, state, [("trigger", "manual")]);
        AssertEventWithSession("SessionEnd", "{\"session_id\":\"old\",\"reason\":\"other\"}", HarnessEventKind.SessionEnded, state, [("reason", "other")], "old");
    }

    [Fact]
    public void MapsPromptToolAndTurnHooksWithoutCopyingUntrustedText()
    {
        var state = new ClaudeNormalizationState("session-a");
        AssertEvent("UserPromptSubmit", "{\"prompt_id\":\"turn-a\",\"permission_mode\":\"default\",\"prompt\":\"secret\",\"delivery_id\":\"bad\"}",
            HarnessEventKind.PromptSubmitted, state, ("turn_id", "turn-a"), ("permission_mode", "default"));
        var withDelivery = ClaudeHookNormalizer.Normalize(state, "UserPromptSubmit", Encoding.UTF8.GetBytes(Base), "delivery-a");
        Assert.Equal("delivery-a", withDelivery.Event.Attributes["delivery_id"]);
        AssertEvent("PreToolUse", "{\"tool_name\":\"Bash\",\"tool_use_id\":\"tool-a\",\"prompt_id\":\"turn-a\",\"tool_input\":{\"x\":\"secret\"}}",
            HarnessEventKind.ToolStarted, state, ("tool_name", "Bash"), ("tool_use_id", "tool-a"), ("turn_id", "turn-a"));
        AssertEvent("PostToolUse", "{\"tool_name\":\"Bash\",\"tool_use_id\":\"tool-a\",\"duration_ms\":331,\"tool_response\":\"secret\"}",
            HarnessEventKind.ToolFinished, state, ("tool_name", "Bash"), ("tool_use_id", "tool-a"), ("duration_ms", "331"));
        AssertEvent("PostToolUseFailure", "{\"tool_name\":\"Bash\",\"tool_use_id\":\"tool-a\",\"duration_ms\":331}",
            HarnessEventKind.ToolFinished, state, ("tool_name", "Bash"), ("tool_use_id", "tool-a"), ("duration_ms", "331"), ("failed", "true"));
        AssertEvent("Stop", "{\"prompt_id\":\"turn-a\",\"stop_hook_active\":false}",
            HarnessEventKind.TurnEnded, state, ("turn_id", "turn-a"), ("stop_hook_active", "false"));
        AssertEvent("StopFailure", "{\"prompt_id\":\"turn-a\",\"last_assistant_message\":\"Invalid API key\"}",
            HarnessEventKind.TurnFailed, state, ("turn_id", "turn-a"), ("error", "Invalid API key"));
    }

    [Fact]
    public void InvalidAndUnknownPayloadsAreOtherAndDoNotChangeState()
    {
        var state = new ClaudeNormalizationState("session-a");
        Assert.Equal("ClaudeNormalizationState", state.ToString());
        Assert.Equal("ClaudeNormalizationResult", ClaudeHookNormalizer.Normalize(state, "Mystery", Encoding.UTF8.GetBytes(Base)).ToString());
        foreach (byte[] body in new[] { Array.Empty<byte>(), Encoding.UTF8.GetBytes("{"), Encoding.UTF8.GetBytes("[]"), Encoding.UTF8.GetBytes("null"), Encoding.UTF8.GetBytes("{\"x\":\"\\ud800\"}"), new byte[] { (byte)'{', (byte)'"', (byte)'x', (byte)'"', (byte)':', (byte)'"', 0xff, (byte)'"', (byte)'}' } })
        {
            HarnessEvent item = ClaudeHookNormalizer.Normalize(state, "Mystery", body).Event;
            Assert.Equal(HarnessEventKind.Other, item.Kind);
            Assert.Equal(string.Empty, item.NativeSessionId);
            Assert.Empty(item.Attributes);
            Assert.Equal(body, item.Raw.ToByteArray());
        }
        var validUnknown = ClaudeHookNormalizer.Normalize(state, "Mystery", Encoding.UTF8.GetBytes(Base)).Event;
        Assert.Equal("session-a", validUnknown.NativeSessionId);
        Assert.Empty(validUnknown.Attributes);
        Assert.Throws<ArgumentNullException>(() => ClaudeHookNormalizer.Normalize(null!, "x", ReadOnlyMemory<byte>.Empty));
        Assert.Throws<ArgumentNullException>(() => ClaudeHookNormalizer.Normalize(state, null!, ReadOnlyMemory<byte>.Empty));
        Assert.Throws<ArgumentNullException>(() => new ClaudeNormalizationState(null!));
    }

    [Fact]
    public void NotificationsAndRawLimitsAreAppliedAfterExtraction()
    {
        var state = new ClaudeNormalizationState("session-a");
        AssertEvent("Notification", "{\"notification_type\":\"idle_prompt\"}", HarnessEventKind.Other, state, ("notification_type", "idle_prompt"));
        string large = "{\"tool_name\":\"Bash\",\"tool_use_id\":\"tool-a\",\"duration_ms\":331,\"tool_response\":\"" + new string('x', 307100) + "\"}";
        HarnessEvent largeEvent = ClaudeHookNormalizer.Normalize(state, "PostToolUse", Encoding.UTF8.GetBytes(large)).Event;
        Assert.Equal(HarnessEventKind.ToolFinished, largeEvent.Kind);
        Assert.Equal((uint)Encoding.UTF8.GetByteCount(large), largeEvent.RawSize);
        Assert.Equal(262144, largeEvent.Raw.Length);
        Assert.True(largeEvent.RawTruncated);
        Assert.Equal("331", largeEvent.Attributes["duration_ms"]);
        Assert.False(ClaudeHookNormalizer.Normalize(state, "Mystery", new byte[262144]).Event.RawTruncated);
        Assert.True(ClaudeHookNormalizer.Normalize(state, "Mystery", new byte[262145]).Event.RawTruncated);
    }

    [Fact]
    public void FixtureLoaderReadsBytesAndTextFromSolutionTree()
    {
        Assert.Equal("{}\n", ClaudeFixtureLoader.ReadText("loader.json"));
        Assert.Equal(new byte[] { (byte)'{', (byte)'}', (byte)'\n' }, ClaudeFixtureLoader.ReadBytes("loader.json"));
    }

    [Theory]
    [InlineData("e5")]
    [InlineData("e6")]
    [InlineData("e7")]
    public void StatusLineFixturesMatchCompleteTelemetryAndUsage(string fixture)
    {
        byte[] payload = ClaudeFixtureLoader.ReadBytes($"statusLine-{fixture}.json");
        using JsonDocument expected = JsonDocument.Parse(ClaudeFixtureLoader.ReadText($"statusLine-{fixture}.expected.json"));
        var state = new ClaudeNormalizationState("session-a");
        ClaudeNormalizationResult first = ClaudeHookNormalizer.Normalize(state, "statusLine", payload);
        ClaudeNormalizationResult second = ClaudeHookNormalizer.Normalize(state, "statusLine", payload);

        Assert.Equal(HarnessEventKind.Telemetry, first.Event.Kind);
        Assert.Equal(expected.RootElement.GetProperty("kind").GetString(), first.Event.Kind.ToString());
        Assert.Equal(payload, first.Event.Raw.ToByteArray());
        Assert.Equal("session-a", first.Event.NativeSessionId);
        Assert.Equal(expected.RootElement.GetProperty("attributes").EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetString()!), first.Event.Attributes);
        Assert.Equal(first.Event.ToByteArray(), second.Event.ToByteArray());
        Assert.Same(state, first.State);
        Assert.Same(state, second.State);

        if (expected.RootElement.TryGetProperty("usage", out JsonElement expectedUsage))
        {
            Assert.NotNull(first.Event.Usage);
            AssertUsageField(expectedUsage, "context_used_percent", first.Event.Usage.HasContextUsedPercent, first.Event.Usage.ContextUsedPercent);
            AssertUsageField(expectedUsage, "context_window_tokens", first.Event.Usage.HasContextWindowTokens, first.Event.Usage.ContextWindowTokens);
            AssertUsageField(expectedUsage, "context_used_tokens", first.Event.Usage.HasContextUsedTokens, first.Event.Usage.ContextUsedTokens);
            AssertUsageField(expectedUsage, "cost_usd", first.Event.Usage.HasCostUsd, first.Event.Usage.CostUsd);
            Assert.Equal(expectedUsage.TryGetProperty("model_id", out JsonElement modelId) ? modelId.GetString() : string.Empty,
                first.Event.Usage.ModelId);
        }
        else Assert.Null(first.Event.Usage);
    }

    [Fact]
    public void StatusLineOmitsInvalidOptionalIntegersAndEmptyUsage()
    {
        var state = new ClaudeNormalizationState("session-a");
        string[] emptyCases =
        [
            "{\"context_window\":null,\"cost\":{\"total_cost_usd\":null},\"model\":{\"id\":\"\"}}",
            "{\"context_window\":{\"used_percentage\":4294967296,\"context_window_size\":-1,\"current_usage\":null}}",
            "{\"context_window\":{\"used_percentage\":16.0,\"context_window_size\":1e2,\"current_usage\":{\"input_tokens\":1,\"cache_creation_input_tokens\":2,\"cache_read_input_tokens\":18446744073709551615}}}"
        ];
        foreach (string json in emptyCases)
        {
            HarnessEvent item = ClaudeHookNormalizer.Normalize(state, "statusLine", Encoding.UTF8.GetBytes(json)).Event;
            Assert.Equal(HarnessEventKind.Telemetry, item.Kind);
            Assert.Null(item.Usage);
        }

        HarnessEvent independentlyValid = ClaudeHookNormalizer.Normalize(state, "statusLine",
            Encoding.UTF8.GetBytes("{\"context_window\":{\"used_percentage\":null,\"context_window_size\":12,\"current_usage\":null},\"rate_limits\":{\"five_hour\":{\"used_percentage\":null,\"resets_at\":12.5}}}")).Event;
        Assert.NotNull(independentlyValid.Usage);
        Assert.False(independentlyValid.Usage.HasContextUsedPercent);
        Assert.False(independentlyValid.Usage.HasContextUsedTokens);
        Assert.True(independentlyValid.Usage.HasContextWindowTokens);
        Assert.Equal(12UL, independentlyValid.Usage.ContextWindowTokens);
        Assert.Equal("12.5", independentlyValid.Attributes["rate_limit.five_hour.resets_at"]);
        Assert.False(independentlyValid.Attributes.ContainsKey("rate_limit.five_hour.used_percent"));
    }

    private static void AssertUsageField(JsonElement expected, string name, bool hasField, object actual)
    {
        if (!expected.TryGetProperty(name, out JsonElement value))
        {
            Assert.False(hasField);
            return;
        }
        Assert.True(hasField);
        switch (value.ValueKind)
        {
            case JsonValueKind.Number when name == "cost_usd":
                Assert.Equal(value.GetDouble(), (double)actual);
                break;
            case JsonValueKind.Number when name is "context_window_tokens" or "context_used_tokens":
                Assert.Equal(value.GetUInt64(), (ulong)actual);
                break;
            default:
                Assert.Equal(value.GetUInt32(), (uint)actual);
                break;
        }
    }

    private static void AssertEvent(string nativeName, string json, HarnessEventKind kind,
        ClaudeNormalizationState state, params (string Key, string Value)[] attributes)
        => AssertEventCore(nativeName, json, kind, state, attributes, expectedSessionId: null);

    private static void AssertEventWithSession(string nativeName, string json, HarnessEventKind kind,
        ClaudeNormalizationState state, (string Key, string Value)[] attributes, string expectedSessionId)
        => AssertEventCore(nativeName, json, kind, state, attributes, expectedSessionId);

    private static void AssertEventCore(string nativeName, string json, HarnessEventKind kind,
        ClaudeNormalizationState state, (string Key, string Value)[] attributes, string? expectedSessionId)
    {
        var normalized = ClaudeHookNormalizer.Normalize(state, nativeName, Encoding.UTF8.GetBytes(json));
        Assert.Equal(kind, normalized.Event.Kind);
        Assert.Equal(nativeName, normalized.Event.NativeName);
        Assert.Equal(expectedSessionId ?? (json.Contains("session_id") ? "session-a" : string.Empty), normalized.Event.NativeSessionId);
        Assert.Equal("claude-code", normalized.Event.Harness);
        Assert.Equal("application/json", normalized.Event.RawContentType);
        Assert.Equal(EventOrigin.Live, normalized.Event.Origin);
        Assert.Equal(Encoding.UTF8.GetBytes(json), normalized.Event.Raw.ToByteArray());
        Assert.Equal(attributes.ToDictionary(pair => pair.Key, pair => pair.Value), normalized.Event.Attributes);
        Assert.Same(state, normalized.State);
        Assert.Equal("session-a", state.CurrentSessionId);
    }
}
