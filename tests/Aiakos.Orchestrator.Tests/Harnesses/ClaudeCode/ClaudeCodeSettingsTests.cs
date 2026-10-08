using System.Text;
using System.Text.Json;
using Aiakos.Orchestrator.Harnesses.ClaudeCode;
using Aiakos.Spec;
using Xunit;

namespace Aiakos.Orchestrator.Tests.Harnesses.ClaudeCode;

public sealed class ClaudeCodeSettingsTests
{
    private static readonly string[] ExpectedTopLevelFields = ["disableAllHooks", "permissions", "hooks", "statusLine"];

    [Fact]
    public void SubscriptionSettingsMatchOrderedGoldenBytesAndAppendOneDenyRule()
    {
        var seat = Seat();
        var settings = new ClaudeCodeSettings();

        var actual = settings.Build(seat);

        Assert.Equal(Golden(), actual);
        Assert.False(actual.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
        Assert.Equal((byte)'\n', actual[^1]);
        using var document = JsonDocument.Parse(actual);
        Assert.Equal(ExpectedTopLevelFields,
            document.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.DoesNotContain("apiKeyHelper", Encoding.UTF8.GetString(actual), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("subscription")]
    [InlineData("api-key")]
    public void SettingsUseOnlyLfLineBreaksForEachAuthMode(string auth)
    {
        var seat = Seat(auth: auth, secrets: [new ResolvedSeatSecret("anthropic_api_key", "/keys/anthropic.key", "file")]);

        var bytes = new ClaudeCodeSettings().Build(seat);

        Assert.Contains((byte)'\n', bytes);
        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.Equal((byte)'\n', bytes[^1]);
        Assert.NotEqual((byte)'\n', bytes[^2]);
    }

    [Fact]
    public void PreservesPermissionOrderDuplicatesAndUnicodeWithoutMutation()
    {
        var allow = new List<string> { "cafe\u0301", "odd-\uFFFE-\U0001F680" };
        var ask = new List<string> { "ask-two", "ask-one" };
        var deny = new List<string> { "first", "Bash(tmux:*)", "Bash(tmux:*)", "last" };
        var seat = Seat(allow: allow, ask: ask, deny: deny);
        var beforeAllow = allow.ToArray();
        var beforeAsk = ask.ToArray();
        var beforeDeny = deny.ToArray();

        var bytes = new ClaudeCodeSettings().Build(seat);
        using var document = JsonDocument.Parse(bytes);
        var permissions = document.RootElement.GetProperty("permissions");
        Assert.Equal(beforeAllow, permissions.GetProperty("allow").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(beforeAsk, permissions.GetProperty("ask").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(beforeDeny, permissions.GetProperty("deny").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(beforeAllow, allow);
        Assert.Equal(beforeAsk, ask);
        Assert.Equal(beforeDeny, deny);
        Assert.Contains("\\uFFFE", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        Assert.Contains("\\uD83D\\uDE80", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("default")]
    [InlineData("acceptEdits")]
    [InlineData("plan")]
    [InlineData("auto")]
    public void AcceptsEachSpecifiedPermissionMode(string permissionMode)
    {
        using var document = JsonDocument.Parse(new ClaudeCodeSettings().Build(Seat(permissionMode: permissionMode)));

        Assert.Equal(permissionMode, document.RootElement.GetProperty("permissions").GetProperty("defaultMode").GetString());
    }

    [Theory]
    [InlineData("/keys/anthropic.key", "cat '/keys/anthropic.key'", "  \"apiKeyHelper\": \"cat \\u0027/keys/anthropic.key\\u0027\"")]
    [InlineData("~/.config/aiakos/secrets/anthropic_api_key", "cat \"$HOME/.config/aiakos/secrets/anthropic_api_key\"", "  \"apiKeyHelper\": \"cat \\u0022$HOME/.config/aiakos/secrets/anthropic_api_key\\u0022\"")]
    public void ApiKeyPathsBecomeFixedHelpersAndByteExactFinalProperty(string file, string helper, string finalLine)
    {
        var seat = Seat(auth: "api-key", secrets: [new ResolvedSeatSecret("anthropic_api_key", file, "file")]);

        var bytes = new ClaudeCodeSettings().Build(seat);

        Assert.Equal(Golden(finalLine), bytes);
        using var document = JsonDocument.Parse(bytes);
        Assert.Equal(helper, document.RootElement.GetProperty("apiKeyHelper").GetString());
        Assert.Equal("apiKeyHelper", document.RootElement.EnumerateObject().Last().Name);
        Assert.DoesNotContain("api-key-value", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("~/")]
    [InlineData("~user/key")]
    [InlineData("relative/key")]
    [InlineData("/keys//key")]
    [InlineData("/keys/./key")]
    [InlineData("/keys/../key")]
    [InlineData("/keys/key/")]
    [InlineData("/keys/has space")]
    [InlineData("/keys/é")]
    [InlineData("/keys/'key")]
    [InlineData("/keys/$key")]
    [InlineData("/keys/`key")]
    [InlineData("/keys/\\key")]
    [InlineData("/keys/key\n")]
    public void RejectsUnsafeApiKeySourceWithFixedError(string file)
    {
        var seat = Seat(auth: "api-key", secrets: [new ResolvedSeatSecret("anthropic_api_key", file, "file")]);

        var exception = Assert.Throws<InvalidOperationException>(() => new ClaudeCodeSettings().Build(seat));

        Assert.Equal("INVALID_LAUNCH", exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void RejectsMissingOrDuplicateApiKeyReferenceAndInvalidSettingsStrings()
    {
        AssertInvalid(Seat(auth: "api-key"));
        AssertInvalid(Seat(auth: "api-key", secrets:
            [new ResolvedSeatSecret("Anthropic_api_key", "/keys/key", "file")]));
        AssertInvalid(Seat(auth: "api-key", secrets:
        [
            new ResolvedSeatSecret("anthropic_api_key", "/keys/first", "file"),
            new ResolvedSeatSecret("anthropic_api_key", "/keys/second", "file")
        ]));
        AssertInvalid(Seat(permissionMode: "invalid"));
        AssertInvalid(Seat(auth: "other"));
        AssertInvalid(Seat(harness: "opencode"));
        AssertInvalid(Seat(allow: ["line\nbreak"]));
        AssertInvalid(Seat(deny: ["bad\u0000rule"]));
        AssertInvalid(Seat(ask: ["bad\uD800surrogate"]));
        var exception = Assert.Throws<ArgumentNullException>(() => new ClaudeCodeSettings().Build(null!));
        Assert.Equal("seat", exception.ParamName);
    }

    private static ResolvedSeatParameters Seat(string harness = "claude-code", string auth = "subscription",
        string permissionMode = "default", IReadOnlyList<string>? allow = null, IReadOnlyList<string>? ask = null,
        IReadOnlyList<string>? deny = null, IReadOnlyList<ResolvedSeatSecret>? secrets = null) => new(
        "impl", "aiakos-dev", "node", harness, null, auth, "none", "/srv/seats/impl", "/srv/seats/impl/repos/app",
        "/srv/seats/impl/projection", [],
        new ResolvedHarnessSettings(permissionMode, new ResolvedPermissions(
            allow ?? ["Bash(dotnet build:*)"], ask ?? [], deny ?? ["Bash(git push --force:*)"])), secrets ?? []);

    private static void AssertInvalid(ResolvedSeatParameters seat)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new ClaudeCodeSettings().Build(seat));
        Assert.Equal("INVALID_LAUNCH", exception.Message);
        Assert.Null(exception.InnerException);
    }

    private static byte[] Golden(string? finalApiKeyHelperLine = null)
    {
        var json = GoldenPrefix + (finalApiKeyHelperLine is null ? "\n" : ",\n");
        if (finalApiKeyHelperLine is not null)
            json += finalApiKeyHelperLine + "\n";
        json += "}\n";
        return Encoding.UTF8.GetBytes(json);
    }

    private const string GoldenPrefix = """
{
  "disableAllHooks": false,
  "permissions": {
    "defaultMode": "default",
    "allow": [
      "Bash(dotnet build:*)"
    ],
    "ask": [],
    "deny": [
      "Bash(git push --force:*)",
      "Bash(tmux:*)"
    ]
  },
  "hooks": {
    "SessionStart": [
      {
        "hooks": [
          {
            "type": "command",
            "command": "${AIAKOS_SEAT_HOME}/aiakos/bin/aiakos-hook-relay hook SessionStart",
            "timeout": 5
          }
        ]
      }
    ],
    "UserPromptSubmit": [
      {
        "hooks": [
          {
            "type": "command",
            "command": "${AIAKOS_SEAT_HOME}/aiakos/bin/aiakos-hook-relay hook UserPromptSubmit",
            "timeout": 5
          }
        ]
      }
    ],
    "PreToolUse": [
      {
        "matcher": "*",
        "hooks": [
          {
            "type": "command",
            "command": "${AIAKOS_SEAT_HOME}/aiakos/bin/aiakos-hook-relay hook PreToolUse",
            "timeout": 5
          }
        ]
      }
    ],
    "PermissionRequest": [
      {
        "matcher": "*",
        "hooks": [
          {
            "type": "command",
            "command": "${AIAKOS_SEAT_HOME}/aiakos/bin/aiakos-hook-relay hook PermissionRequest",
            "timeout": 5
          }
        ]
      }
    ],
    "PostToolUse": [
      {
        "matcher": "*",
        "hooks": [
          {
            "type": "command",
            "command": "${AIAKOS_SEAT_HOME}/aiakos/bin/aiakos-hook-relay hook PostToolUse",
            "timeout": 5
          }
        ]
      }
    ],
    "PostToolUseFailure": [
      {
        "matcher": "*",
        "hooks": [
          {
            "type": "command",
            "command": "${AIAKOS_SEAT_HOME}/aiakos/bin/aiakos-hook-relay hook PostToolUseFailure",
            "timeout": 5
          }
        ]
      }
    ],
    "Notification": [
      {
        "hooks": [
          {
            "type": "command",
            "command": "${AIAKOS_SEAT_HOME}/aiakos/bin/aiakos-hook-relay hook Notification",
            "timeout": 5
          }
        ]
      }
    ],
    "Stop": [
      {
        "hooks": [
          {
            "type": "command",
            "command": "${AIAKOS_SEAT_HOME}/aiakos/bin/aiakos-hook-relay hook Stop",
            "timeout": 5
          }
        ]
      }
    ],
    "StopFailure": [
      {
        "hooks": [
          {
            "type": "command",
            "command": "${AIAKOS_SEAT_HOME}/aiakos/bin/aiakos-hook-relay hook StopFailure",
            "timeout": 5
          }
        ]
      }
    ],
    "PreCompact": [
      {
        "hooks": [
          {
            "type": "command",
            "command": "${AIAKOS_SEAT_HOME}/aiakos/bin/aiakos-hook-relay hook PreCompact",
            "timeout": 5
          }
        ]
      }
    ],
    "SessionEnd": [
      {
        "hooks": [
          {
            "type": "command",
            "command": "${AIAKOS_SEAT_HOME}/aiakos/bin/aiakos-hook-relay hook SessionEnd",
            "timeout": 5
          }
        ]
      }
    ]
  },
  "statusLine": {
    "type": "command",
    "command": "${AIAKOS_SEAT_HOME}/aiakos/bin/aiakos-hook-relay status statusLine"
  }
""";
}
