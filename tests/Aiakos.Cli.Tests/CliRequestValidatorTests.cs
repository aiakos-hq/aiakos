using Aiakos.Cli;

namespace Aiakos.Cli.Tests;

public sealed class CliRequestValidatorTests
{
    [Theory]
    [MemberData(nameof(InvalidInvocations))]
    public void ValidateRejectsInvalidCrossFieldCombinations(string[] args)
    {
        var request = ParseRequest(args);

        Assert.Equal("Invalid command line.", CliRequestValidator.Validate(request));
    }

    public static IEnumerable<object[]> InvalidInvocations =>
    [
        [new[] { "up", "--fresh" }],
        [new[] { "up", "--note", "reason" }],
        [new[] { "down" }],
        [new[] { "down", "seat", "--all" }],
        [new[] { "down", "--all", "--rig", "demo" }],
        [new[] { "send", "seat" }],
        [new[] { "send", "seat", "body", "--file", "body.txt" }],
        [new[] { "send", "seat", "body", "--wait", "sometimes" }],
        [new[] { "ps", "seat", "--rig", "demo" }],
        [new[] { "instance", "init", "--instance", "dev" }],
        [new[] { "instance", "start", "--instance", "dev" }],
        [new[] { "instance", "stop", "--instance", "dev" }],
        [new[] { "instance", "run", "--instance", "dev" }],
        [new[] { "attach", "seat", "--json" }]
    ];

    [Theory]
    [MemberData(nameof(ValidInvocations))]
    public void ValidateAcceptsLegalCrossFieldCombinations(string[] args)
    {
        var request = ParseRequest(args);

        Assert.Null(CliRequestValidator.Validate(request));
    }

    public static IEnumerable<object[]> ValidInvocations =>
    [
        [new[] { "up" }],
        [new[] { "up", "--fresh", "--seat", "impl" }],
        [new[] { "up", "--fresh", "--note", "reason", "--seat", "impl" }],
        [new[] { "up", "--dry-run", "--no-wait" }],
        [new[] { "down", "seat" }],
        [new[] { "down", "--rig", "demo" }],
        [new[] { "down", "--all" }],
        [new[] { "send", "seat", "body" }],
        [new[] { "send", "seat", "-" }],
        [new[] { "send", "seat", "--file", "body.txt", "--wait", "turn" }],
        [new[] { "send", "seat", "body", "--wait", "none" }],
        [new[] { "capture", "seat", "--lines", "10000" }],
        [new[] { "ps", "--rig", "demo" }],
        [new[] { "instance", "init", "--instance", "release" }],
        [new[] { "instance", "status", "--instance", "dev" }],
        [new[] { "ps", "--instance", "dev" }],
        [new[] { "attach", "seat" }]
    ];

    private static CliRequest ParseRequest(string[] args)
    {
        var result = CliCommandLine.Parse(args, null);
        Assert.Null(result.Error);
        return Assert.IsType<CliRequest>(result.Request);
    }
}
