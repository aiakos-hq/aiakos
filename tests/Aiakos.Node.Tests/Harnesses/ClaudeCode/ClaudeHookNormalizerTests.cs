using System.Text;
using Aiakos.Contracts.Node.V1;
using Aiakos.Node.Harnesses.ClaudeCode;
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
