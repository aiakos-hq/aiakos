using Aiakos.Cli;

namespace Aiakos.Cli.Tests;

public sealed class CliCommandLineTests
{
    private static readonly string[] RepeatedSeats = ["alpha", "beta"];
    private static readonly string[] SingleSeat = ["one"];
    [Fact]
    public void ParsesCommandPathDefaultsGlobalOptionsAndOrderedRepeatedSeats()
    {
        var result = CliCommandLine.Parse(
            ["--json", "up", "--seat", "alpha", "--seat", "beta", "--dry-run", "--no-wait"],
            "staging");

        Assert.Null(result.Error);
        Assert.Null(result.Help);
        Assert.False(result.Version);
        Assert.Equal("up", result.Request!.Command);
        Assert.Equal("staging", result.Request.Instance);
        Assert.True(result.Request.Json);
        Assert.Empty(result.Request.Arguments);
        Assert.Equal(RepeatedSeats, result.Request.Options["seat"]);
        Assert.Empty(result.Request.Options["no-wait"]);
        Assert.Empty(result.Request.Options["dry-run"]);
    }

    [Fact]
    public void ReturnsGeneratedHelpWithExitCodesAndCommandPurposes()
    {
        var root = CliCommandLine.Parse(["--help"], null);
        var nested = CliCommandLine.Parse(["instance", "init", "--help"], null);

        Assert.NotNull(root.Help);
        Assert.Null(root.Request);
        Assert.Contains("instance init", root.Help);
        Assert.Contains("Aiakos: a file-defined control plane for teams of AI coding agents.", root.Help);
        Assert.Contains("Exit codes: 0 done; 1 unsuccessful outcome; 2 invalid input; 3 instance unavailable; 4 incompatible version; 5 authentication failed.", root.Help);
        Assert.Contains("Initialize instance configuration.", nested.Help);
        Assert.Contains("--database-url-file", nested.Help);
    }

    [Fact]
    public void HelpAndVersionBypassInvalidInstanceButNotBadGrammar()
    {
        Assert.NotNull(CliCommandLine.Parse(["--help"], "BAD").Help);
        Assert.True(CliCommandLine.Parse(["--version"], "BAD").Version);
        Assert.NotNull(CliCommandLine.Parse(["--instance", "BAD", "--help"], null).Help);
        Assert.True(CliCommandLine.Parse(["--instance", "BAD", "--version"], null).Version);
        Assert.Equal("Invalid command line.", CliCommandLine.Parse(["bogus", "--help"], null).Error);
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("--version")]
    public void InformationOptionsBypassOnlyInstanceValidation(string option)
    {
        var result = CliCommandLine.Parse(["--instance", "BAD", "--json", "-v", option], "ALSO_BAD");
        Assert.Null(result.Error);
        Assert.Null(result.Request);
        Assert.Equal(option == "--version", result.Version);
        Assert.Equal(option != "--version", result.Help is not null);
        Assert.Equal("Invalid command line.", CliCommandLine.Parse(["--instance", option], null).Error);
        Assert.Equal("Invalid command line.", CliCommandLine.Parse(["--instance", "BAD", "--unknown", option], null).Error);
        Assert.Equal("Invalid command line.", CliCommandLine.Parse(["--instance", "BAD", "unexpected", option], null).Error);
    }

    [Fact]
    public void VersionIsRootOnlyAndCannotBypassUnknownOrExtraTokens()
    {
        foreach (var args in new string[][]
        {
            ["up", "--version"], ["instance", "--version"],
            ["--version", "up"], ["--version", "--unknown"],
            ["--version", "unexpected"], ["--version", "--version"],
        })
        {
            var result = CliCommandLine.Parse(args, "BAD");
            Assert.Equal("Invalid command line.", result.Error);
            Assert.False(result.Version);
            Assert.Null(result.Help);
            Assert.Null(result.Request);
        }
    }

    [Theory]
    [MemberData(nameof(InvalidCommands))]
    public void RejectsUnrecognizedDuplicateAndOutOfRangeTokens(string[] args)
    {
        var result = CliCommandLine.Parse(args, null);

        Assert.Null(result.Request);
        Assert.Equal("Invalid command line.", result.Error);
        Assert.Null(result.Help);
        Assert.False(result.Version);
    }

    public static IEnumerable<object[]> InvalidCommands =>
    [
        [new[] { "up", "--dry-run", "--wat" }],
        [new[] { "send", "seat", "hello", "--timeout", "0" }],
        [new[] { "capture", "seat", "--lines", "10001" }],
        [new[] { "instance", "init", "--port-base", "51536" }],
        [new[] { "down", "--all", "--all" }],
        [new[] { "down", "--wat" }]
    ];

    [Theory]
    [MemberData(nameof(AmendedInvalidCommands))]
    public void AmendedGrammarRejectsMalformedInformationAndOptionValues(string[] args)
    {
        var result = CliCommandLine.Parse(args, "BAD");
        Assert.Equal("Invalid command line.", result.Error);
        Assert.Null(result.Request);
        Assert.Null(result.Help);
        Assert.False(result.Version);
    }

    public static IEnumerable<object[]> AmendedInvalidCommands =>
    [
        [new[] { "--json" }], [new[] { "--instance", "--help", "--help" }],
        [new[] { "-h", "--help" }], [new[] { "-v", "--verbose", "--version" }],
        [new[] { "up", "--dry" }], [new[] { "up", "--version" }],
        [new[] { "capture", "--version" }], [new[] { "up", "--env", "--dry-run" }],
        [new[] { "up", "--seat", "--fresh" }], [new[] { "up", "--seat" }],
        [new[] { "up", "--seat", "one", "--seat", "--help" }],
        [new[] { "down", "--unknown", "--help" }],
        [new[] { "capture", "--lines", "0", "--help" }],
        [new[] { "capture" }], [new[] { "send", "" }],
        [new[] { "send", "seat", "" }], [new[] { "up", "--env", "" }],
        [new[] { "instance", "--json", "--json" }],
        [new[] { "send", "seat", "--fake", "--", "--fake" }],
        [new[] { "up", "--note=-bad", "--help" }],
        [new[] { "--instance=one", "--instance=two", "--version" }],
    ];

    [Theory]
    [InlineData("capture")]
    [InlineData("send")]
    [InlineData("attach")]
    public void HelpDoesNotRequirePositionals(string command)
    {
        Assert.NotNull(CliCommandLine.Parse([command, "--help"], "BAD").Help);
        Assert.NotNull(CliCommandLine.Parse([command, "-h"], "BAD").Help);
    }

    [Fact]
    public void GroupHelpAndLiteralResponseFileLikeBodiesNeedNoLookup()
    {
        Assert.NotNull(CliCommandLine.Parse(["instance", "--json"], "BAD").Help);
        var args = new[] { "send", "seat", "@never-open-this-file" };
        var before = args.ToArray();
        var request = CliCommandLine.Parse(args, null).Request;
        Assert.NotNull(request);
        Assert.Equal("@never-open-this-file", request.Arguments[1]);
        Assert.Equal(before, args);
        Assert.Equal("-", CliCommandLine.Parse(["send", "seat", "-"], null).Request!.Arguments[1]);
    }

    [Theory]
    [InlineData("release\n")]
    [InlineData("release\0")]
    [InlineData("BAD")]
    public void InstanceSlugConsumesTheEntireValueAndErrorsDoNotEchoIt(string instance)
    {
        var explicitResult = CliCommandLine.Parse(["ps", "--instance", instance], null);
        var environmentResult = CliCommandLine.Parse(["ps"], instance);
        Assert.Equal("Invalid command line.", explicitResult.Error);
        Assert.Equal("Invalid command line.", environmentResult.Error);
        Assert.Null(explicitResult.Request);
        Assert.Null(environmentResult.Request);
        Assert.Equal("release", CliCommandLine.Parse(["ps", "--instance", "release"], instance).Request!.Instance);
    }

    [Theory]
    [InlineData("instance init", "distro", "Ubuntu")]
    [InlineData("instance init", "node-id", "wsl-local")]
    [InlineData("instance start", "timeout", "90")]
    [InlineData("instance stop", null, null)]
    [InlineData("instance status", null, null)]
    [InlineData("instance run", null, null)]
    [InlineData("up", null, null)]
    [InlineData("down", null, null)]
    [InlineData("send seat", "wait", "delivery")]
    [InlineData("send seat", "timeout", "1800")]
    [InlineData("capture seat", "lines", "200")]
    [InlineData("ps", null, null)]
    [InlineData("attach seat", null, null)]
    public void CommandGrammarAndDefaultsRemainAvailable(string input, string? option, string? expected)
    {
        var tokens = input.Split(' ');
        var result = CliCommandLine.Parse(tokens, null);
        Assert.Null(result.Error);
        Assert.NotNull(result.Request);
        Assert.Equal("release", result.Request.Instance);
        var path = input.EndsWith(" seat", StringComparison.Ordinal) ? tokens[0] : input;
        Assert.Equal(path, result.Request.Command);
        if (option is not null)
            Assert.Equal(expected, Assert.Single(result.Request.Options[option]));
        else
            Assert.Empty(result.Request.Options);
        var help = CliCommandLine.Parse(tokens.Append("--help").ToArray(), "BAD");
        Assert.NotNull(help.Help);
        Assert.Null(help.Request);
        Assert.EndsWith("\n", help.Help);
        Assert.DoesNotContain("\r", help.Help);
        Assert.Contains("Exit codes: 0 done; 1 unsuccessful outcome; 2 invalid input; 3 instance unavailable; 4 incompatible version; 5 authentication failed.", help.Help);
    }

    [Fact]
    public void SnapshotsAreImmutableAndIndependentBetweenParseCalls()
    {
        var first = CliCommandLine.Parse(["up", "--seat", "one"], null).Request!;
        var second = CliCommandLine.Parse(["up"], null).Request!;

        Assert.Equal(SingleSeat, first.Options["seat"]);
        Assert.DoesNotContain("seat", second.Options.Keys);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)first.Options["seat"]).Add("two"));
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, IReadOnlyList<string>>)first.Options).Add("x", []));
    }

    [Fact]
    public void SendHasDeliveryWaitDefaultAndEndOfOptionsKeepsOptionLikeText()
    {
        var send = CliCommandLine.Parse(["send", "impl", "message"], null).Request!;

        Assert.Equal("delivery", Assert.Single(send.Options["wait"]));
        Assert.Equal("1800", Assert.Single(send.Options["timeout"]));

        var literal = CliCommandLine.Parse(["send", "impl", "--", "--version"], null);

        Assert.Null(literal.Error);
        Assert.False(literal.Version);
        Assert.Equal(2, literal.Request!.Arguments.Count);
        Assert.Equal("impl", literal.Request.Arguments[0]);
        Assert.Equal("--version", literal.Request.Arguments[1]);
    }
}
