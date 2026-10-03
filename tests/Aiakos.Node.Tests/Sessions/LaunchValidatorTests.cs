using Aiakos.Node.Sessions;

namespace Aiakos.Node.Tests.Sessions;

public sealed class LaunchValidatorTests
{
    private static readonly TestLaunchFiles Files = new();

    [Fact]
    public void AcceptsAValidLaunchSpec()
    {
        var error = LaunchValidator.Validate(ValidSpec(), Files);

        Assert.Null(error);
    }

    [Theory]
    [InlineData("impl")]
    [InlineData("Impl@demo")]
    [InlineData("im_pl@demo")]
    [InlineData("impl@d")]
    [InlineData("impl@demo@other")]
    public void RejectsInvalidSeatAddresses(string address)
    {
        var error = Validate(ValidSpec(seatAddress: address));

        AssertInvalidArgument(error);
        Assert.Contains("SeatAddress", error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAnEmptySeatIdBeforeLaterFailures()
    {
        var error = Validate(ValidSpec(seatId: "", argv: []));

        AssertInvalidArgument(error);
        Assert.Contains("SeatId", error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsEmptyLaunchIdAndHarness()
    {
        AssertInvalidArgument(Validate(ValidSpec(launchId: "")));
        AssertInvalidArgument(Validate(ValidSpec(harness: "")));
    }

    [Theory]
    [InlineData("home/seat")]
    [InlineData("")]
    public void RejectsNonAbsoluteSeatHome(string seatHome)
    {
        var error = Validate(ValidSpec(seatHome: seatHome));

        AssertInvalidArgument(error);
        Assert.Contains("SeatHome", error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAnEmptyArgv()
    {
        AssertInvalidArgument(Validate(ValidSpec(argv: [])));
    }

    [Theory]
    [InlineData("bin/app")]
    [InlineData("/not-installed")]
    public void RejectsANonAbsoluteOrMissingExecutable(string executable)
    {
        var error = Validate(ValidSpec(argv: [executable]));

        AssertInvalidArgument(error);
        Assert.DoesNotContain(executable, error!.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("bad\0arg")]
    [InlineData("shell;")]
    public void RejectsInvalidArgumentElements(string argument)
    {
        AssertInvalidArgument(Validate(ValidSpec(argv: ["/bin/app", argument])));
    }

    [Fact]
    public void RejectsAWorkingDirectoryThatIsNotAbsoluteOrMissing()
    {
        AssertInvalidArgument(Validate(ValidSpec(workingDirectory: "work")));
        AssertInvalidArgument(Validate(ValidSpec(workingDirectory: "/missing")));
    }

    [Theory]
    [InlineData("lowercase", "value")]
    [InlineData("9BAD", "value")]
    public void RejectsEnvironmentNamesOutsideTheUppercaseRule(string name, string value)
    {
        AssertInvalidArgument(Validate(WithEnvironment(name, value)));
    }

    [Theory]
    [InlineData("VALUE\0NUL")]
    [InlineData("VALUE\nLF")]
    [InlineData("VALUE\rCR")]
    public void RejectsEnvironmentValuesWithNulOrLineEndings(string value)
    {
        AssertInvalidArgument(Validate(WithEnvironment("SAFE_NAME", value)));
    }

    [Theory]
    [InlineData("TMUX")]
    [InlineData("TMUX_PANE")]
    [InlineData("AIAKOS_NODE_ID")]
    [InlineData("AIAKOS_NODE_TOKEN")]
    public void RejectsReservedEnvironmentNames(string name)
    {
        AssertInvalidArgument(Validate(WithEnvironment(name, "/home/seat/value")));
    }

    [Theory]
    [InlineData("AIAKOS_SEAT_TOKEN")]
    [InlineData("DATABASE_PASSWORD")]
    [InlineData("MY_API_KEY")]
    [InlineData("APP_SECRET")]
    public void RejectsSecretEnvironmentValuesWithoutTheFileException(string name)
    {
        const string sentinel = "private-secret-value";
        var error = Validate(WithEnvironment(name, sentinel));

        AssertInvalidArgument(error);
        Assert.DoesNotContain(sentinel, error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptsASeatTokenFileBelowSeatHome()
    {
        var spec = WithEnvironment("AIAKOS_SEAT_TOKEN_FILE", "/home/seat/aiakos/seat-token.header");

        Assert.Null(Validate(spec));
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("/home/seat/../outside")]
    [InlineData("relative/token")]
    public void RejectsASecretFileOutsideSeatHome(string value)
    {
        AssertInvalidArgument(Validate(WithEnvironment("AIAKOS_SEAT_TOKEN_FILE", value)));
    }

    [Theory]
    [InlineData(79, 45)]
    [InlineData(501, 45)]
    [InlineData(160, 23)]
    [InlineData(160, 201)]
    public void RejectsTerminalSizesOutsideTheSupportedRange(int columns, int rows)
    {
        AssertInvalidArgument(Validate(ValidSpec(size: new TerminalSize(columns, rows))));
    }

    [Theory]
    [InlineData(80, 24)]
    [InlineData(500, 200)]
    public void AcceptsTerminalSizeBoundaries(int columns, int rows)
    {
        Assert.Null(Validate(ValidSpec(size: new TerminalSize(columns, rows))));
    }

    [Theory]
    [InlineData(12278, true)]
    [InlineData(12279, false)]
    public void EnforcesThePackedArgumentSizeLimit(int secondArgumentLength, bool valid)
    {
        var spec = ValidSpec(argv: ["/bin/app", new string('x', secondArgumentLength)]);

        var error = Validate(spec);

        if (valid)
        {
            Assert.Null(error);
        }
        else
        {
            Assert.Equal(SessionHostErrorCode.PayloadTooLarge, error?.Code);
        }
    }

    [Fact]
    public void EnforcesTheAttributeSizeLimit()
    {
        var accepted = ValidSpec(attributes: new Dictionary<string, string> { ["k"] = new string('v', 4095) });
        var rejected = ValidSpec(attributes: new Dictionary<string, string> { ["k"] = new string('v', 4096) });

        Assert.Null(Validate(accepted));
        Assert.Equal(SessionHostErrorCode.PayloadTooLarge, Validate(rejected)?.Code);
    }

    private static SessionHostError? Validate(SessionSpec spec) => LaunchValidator.Validate(spec, Files);

    private static SessionSpec ValidSpec(
        string seatId = "seat-id",
        string seatAddress = "impl@demo",
        string launchId = "launch-id",
        string harness = "runner",
        IReadOnlyList<string>? argv = null,
        string workingDirectory = "/work",
        IReadOnlyDictionary<string, string>? environment = null,
        TerminalSize? size = null,
        string seatHome = "/home/seat",
        IReadOnlyDictionary<string, string>? attributes = null) =>
        new(seatId, seatAddress, launchId, harness, argv ?? ["/bin/app"], workingDirectory, seatHome,
            environment ?? new Dictionary<string, string>(), size ?? TerminalSize.Default, null,
            attributes ?? new Dictionary<string, string>());

    private static SessionSpec WithEnvironment(string name, string value) =>
        ValidSpec(environment: new Dictionary<string, string> { [name] = value });

    private static void AssertInvalidArgument(SessionHostError? error) =>
        Assert.Equal(SessionHostErrorCode.InvalidArgument, error?.Code);

    private sealed class TestLaunchFiles : ILaunchFiles
    {
        public bool ExecutableExists(string path) => path == "/bin/app";

        public bool DirectoryExists(string path) => path == "/work";
    }
}
