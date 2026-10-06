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
}
