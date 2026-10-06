using Aiakos.Core;
using Aiakos.Orchestrator.Harnesses.ClaudeCode;

namespace Aiakos.Orchestrator.Tests.Harnesses.ClaudeCode;

public sealed class ClaudeCodeDeliveryTests
{
    private static readonly Guid CommandId = Guid.Parse("12345678-1234-4123-8123-123456789abc");

    [Fact]
    public void NormalizesBodyAndBuildsExactLeadWithConfirmation()
    {
        var delivery = new ClaudeCodeDelivery(new ClaudeCodeStateProfile());

        var result = delivery.BuildDelivery("operator", "first\r\nsecond\rthird\tline", CommandId);

        Assert.Equal("[aiakos from operator #12345678] ", result.Lead);
        Assert.EndsWith("] ", result.Lead, StringComparison.Ordinal);
        Assert.Equal("first\nsecond\nthird\tline", result.Body);
        Assert.True(result.ExpectConfirmation);
        Assert.Equal(TimeSpan.FromSeconds(5), result.ConfirmTimeout);
    }

    [Fact]
    public void AllowsEmptyBodyAndUtf8LimitButRejectsBodyAboveLimit()
    {
        var delivery = new ClaudeCodeDelivery(new ClaudeCodeStateProfile());
        Assert.Equal(string.Empty, delivery.BuildDelivery("operator", string.Empty, CommandId).Body);
        Assert.Equal(InputValidator.MaxBodyBytes,
            delivery.BuildDelivery("operator", new string('a', InputValidator.MaxBodyBytes), CommandId).Body.Length);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            delivery.BuildDelivery("operator", new string('a', InputValidator.MaxBodyBytes + 1), CommandId));
        Assert.Equal("INPUT_TOO_LARGE", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad\nname")]
    [InlineData("bad\tname")]
    [InlineData("bad\0name")]
    [InlineData("bad\u0085name")]
    public void RejectsInvalidSenderWithoutEchoingIt(string sender) =>
        AssertDeliveryFailure(sender, "body", CommandId, "INVALID_DELIVERY");

    [Fact]
    public void RejectsUnpairedSurrogatesInSenderAndBody()
    {
        AssertDeliveryFailure("bad\ud800name", "body", CommandId, "INVALID_DELIVERY");
        AssertDeliveryFailure("operator", "bad\ud800body", CommandId, "INVALID_INPUT");
    }

    [Fact]
    public void RejectsEmptyCommandIdAndOversizedFinalLead()
    {
        AssertDeliveryFailure("operator", "body", Guid.Empty, "INVALID_DELIVERY");
        AssertDeliveryFailure(new string('x', 1100), "body", CommandId, "INVALID_DELIVERY");
    }

    [Theory]
    [InlineData("body\0text", "INVALID_INPUT")]
    [InlineData("body\u0085text", "INVALID_INPUT")]
    public void RejectsInvalidBodiesWithFixedError(string body, string expected) =>
        AssertDeliveryFailure("operator", body, CommandId, expected);

    [Fact]
    public void RejectsNullArgumentsByParameterName()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new ClaudeCodeDelivery(null!));
        Assert.Equal("profile", exception.ParamName);

        var delivery = new ClaudeCodeDelivery(new ClaudeCodeStateProfile());
        Assert.Equal("authenticatedSender", Assert.Throws<ArgumentNullException>(() => delivery.BuildDelivery(null!, "", CommandId)).ParamName);
        Assert.Equal("body", Assert.Throws<ArgumentNullException>(() => delivery.BuildDelivery("operator", null!, CommandId)).ParamName);
    }

    [Fact]
    public void PreservesNoncharactersAndNonBmpTextInSenderAndBody()
    {
        var delivery = new ClaudeCodeDelivery(new ClaudeCodeStateProfile());
        const string sender = "op\uffff\U0001f680";
        const string body = "text\ufffe\U0001f680";

        var result = delivery.BuildDelivery(sender, body, CommandId);

        Assert.Equal($"[aiakos from {sender} #12345678] ", result.Lead);
        Assert.Equal(body, result.Body);
    }

    [Fact]
    public void BodyValidationPrecedesSlashClassification()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ClaudeCodeDelivery(new ClaudeCodeStateProfile()).BuildDelivery("operator", "/clear\0", CommandId));

        Assert.Equal("INVALID_INPUT", exception.Message);
    }

    [Fact]
    public void AcceptsOnlyTrimmedCompactSlashCommandAndRetainsOriginalNormalizedBody()
    {
        var delivery = new ClaudeCodeDelivery(new ClaudeCodeStateProfile());

        var result = delivery.BuildDelivery("operator", " \t/compact\r\n", CommandId);

        Assert.Equal(string.Empty, result.Lead);
        Assert.Equal(" \t/compact\n", result.Body);
        Assert.False(result.ExpectConfirmation);
        Assert.Equal(TimeSpan.FromSeconds(5), result.ConfirmTimeout);
    }

    [Theory]
    [InlineData("/clear")]
    [InlineData("/exit")]
    [InlineData("/resume")]
    [InlineData("/login")]
    [InlineData("/compact argument")]
    [InlineData("/compact\n/compact")]
    [InlineData(" \t/clear\n")]
    public void RejectsEveryOtherSlashCommand(string body)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ClaudeCodeDelivery(new ClaudeCodeStateProfile()).BuildDelivery("operator", body, CommandId));

        Assert.Equal("SLASH_COMMAND_NOT_ALLOWED", exception.Message);
    }

    [Fact]
    public void SlashClassificationTrimsOnlyAsciiSpaceTabAndLineFeed()
    {
        var delivery = new ClaudeCodeDelivery(new ClaudeCodeStateProfile());
        var body = "\u2003/compact";

        var result = delivery.BuildDelivery("operator", body, CommandId);

        Assert.Equal("[aiakos from operator #12345678] ", result.Lead);
        Assert.Equal(body, result.Body);
        Assert.True(result.ExpectConfirmation);

        var prose = delivery.BuildDelivery("operator", "ordinary /clear text", CommandId);
        Assert.Equal("ordinary /clear text", prose.Body);
        Assert.True(prose.ExpectConfirmation);
    }

    private static void AssertDeliveryFailure(string sender, string body, Guid commandId, string expected)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ClaudeCodeDelivery(new ClaudeCodeStateProfile()).BuildDelivery(sender, body, commandId));

        Assert.Equal(expected, exception.Message);
    }
}
