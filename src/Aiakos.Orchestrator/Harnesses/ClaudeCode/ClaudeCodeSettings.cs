using System.Text.Encodings.Web;
using System.Text.Json;
using Aiakos.Spec;

namespace Aiakos.Orchestrator.Harnesses.ClaudeCode;

public sealed class ClaudeCodeSettings
{
    private const string TmuxDenyRule = "Bash(tmux:*)";
    private const string ApiKeyName = "anthropic_api_key";
    private const string InvalidLaunch = "INVALID_LAUNCH";
    private static readonly string Relay = "${AIAKOS_SEAT_HOME}/aiakos/bin/aiakos-hook-relay";
    private static readonly string[] HookNames =
    [
        "SessionStart",
        "UserPromptSubmit",
        "PreToolUse",
        "PermissionRequest",
        "PostToolUse",
        "PostToolUseFailure",
        "Notification",
        "Stop",
        "StopFailure",
        "PreCompact",
        "SessionEnd"
    ];

    public ClaudeCodeSettings() { }

    public byte[] Build(ResolvedSeatParameters seat)
    {
        ArgumentNullException.ThrowIfNull(seat);
        if (seat.Harness != "claude-code" || seat.Auth is not ("subscription" or "api-key") ||
            seat.HarnessSettings.PermissionMode is not ("default" or "acceptEdits" or "plan" or "auto"))
            throw InvalidLaunchException();

        var allow = ValidatePermissionList(seat.HarnessSettings.Permissions.Allow);
        var ask = ValidatePermissionList(seat.HarnessSettings.Permissions.Ask);
        var deny = ValidatePermissionList(seat.HarnessSettings.Permissions.Deny);
        if (!deny.Contains(TmuxDenyRule, StringComparer.Ordinal))
            deny.Add(TmuxDenyRule);

        var apiKeyHelper = seat.Auth == "api-key" ? GetApiKeyHelper(seat.Secrets) : null;
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true,
            NewLine = "\n",
            IndentSize = 2,
            IndentCharacter = ' ',
            Encoder = JavaScriptEncoder.Default
        }))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("disableAllHooks", false);
            writer.WritePropertyName("permissions");
            writer.WriteStartObject();
            writer.WriteString("defaultMode", seat.HarnessSettings.PermissionMode);
            WriteStrings(writer, "allow", allow);
            WriteStrings(writer, "ask", ask);
            WriteStrings(writer, "deny", deny);
            writer.WriteEndObject();

            writer.WritePropertyName("hooks");
            writer.WriteStartObject();
            foreach (var hookName in HookNames)
                WriteHook(writer, hookName, HasMatcher(hookName));
            writer.WriteEndObject();

            writer.WritePropertyName("statusLine");
            writer.WriteStartObject();
            writer.WriteString("type", "command");
            writer.WriteString("command", $"{Relay} status statusLine");
            writer.WriteEndObject();

            if (apiKeyHelper is not null)
                writer.WriteString("apiKeyHelper", apiKeyHelper);

            writer.WriteEndObject();
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    private static List<string> ValidatePermissionList(IReadOnlyList<string> values)
    {
        var copy = new List<string>(values.Count);
        foreach (var value in values)
        {
            if (!IsSafeSettingsString(value))
                throw InvalidLaunchException();
            copy.Add(value);
        }

        return copy;
    }

    private static string GetApiKeyHelper(IReadOnlyList<ResolvedSeatSecret> secrets)
    {
        var references = secrets.Where(secret => string.Equals(secret.Name, ApiKeyName, StringComparison.Ordinal)).ToArray();
        if (references.Length != 1 || !TryGetSafeApiKeyPath(references[0].File, out var helper))
            throw InvalidLaunchException();

        return helper;
    }

    private static bool TryGetSafeApiKeyPath(string? file, out string helper)
    {
        helper = string.Empty;
        if (string.IsNullOrEmpty(file))
            return false;

        var homeRelative = file.StartsWith("~/", StringComparison.Ordinal);
        string path;
        if (homeRelative)
        {
            path = file[2..];
        }
        else if (file[0] == '/')
        {
            path = file[1..];
        }
        else
        {
            return false;
        }

        if (path.Length == 0 || path.Any(character => !IsSafePathCharacter(character)))
            return false;

        var segments = path.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
            return false;

        helper = homeRelative ? $"cat \"$HOME/{path}\"" : $"cat '{file}'";
        return true;
    }

    private static bool IsSafePathCharacter(char character) =>
        character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '/' or '-';

    private static bool IsSafeSettingsString(string? value)
    {
        if (value is null)
            return false;

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsHighSurrogate(character))
            {
                if (index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
                {
                    index++;
                    continue;
                }

                return false;
            }

            if (char.IsLowSurrogate(character) || char.IsControl(character))
                return false;
        }

        return true;
    }

    private static void WriteStrings(Utf8JsonWriter writer, string name, IReadOnlyList<string> values)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        foreach (var value in values)
            writer.WriteStringValue(value);
        writer.WriteEndArray();
    }

    private static void WriteHook(Utf8JsonWriter writer, string name, bool matcher)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        writer.WriteStartObject();
        if (matcher)
            writer.WriteString("matcher", "*");
        writer.WritePropertyName("hooks");
        writer.WriteStartArray();
        writer.WriteStartObject();
        writer.WriteString("type", "command");
        writer.WriteString("command", $"{Relay} hook {name}");
        writer.WriteNumber("timeout", 5);
        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndArray();
    }

    private static bool HasMatcher(string name) =>
        name is "PreToolUse" or "PermissionRequest" or "PostToolUse" or "PostToolUseFailure";

    private static InvalidOperationException InvalidLaunchException() => new(InvalidLaunch);
}
