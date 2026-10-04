namespace Aiakos.Node.Tests;

public sealed class NodeOptionsTests
{
    [Theory]
    [InlineData(NodeOptions.OrchestratorUrlVariable)]
    [InlineData(NodeOptions.HomeVariable)]
    [InlineData(NodeOptions.NodeIdVariable)]
    [InlineData(NodeOptions.NodeTokenVariable)]
    public async Task MissingRequiredVariableExits2AndNamesIt(string variable)
    {
        using var home = new TempDirectory();
        using var logs = new CapturingLoggerProvider();
        var settings = NodeHost.ValidSettings(home.Path);
        settings[variable] = string.Empty;

        var code = await NodeProgram.RunAsync([], NodeHost.With(settings, logs), TestContext.Current.CancellationToken);

        Assert.Equal(NodeExitCodes.InvalidConfiguration, code);
        Assert.Contains(variable, logs.Text, StringComparison.Ordinal);
        Assert.False(Directory.Exists(home.Path), "Nothing is created before the settings are valid.");
    }

    [Theory]
    [InlineData("127.0.0.1:5180")]
    [InlineData("ftp://127.0.0.1:5180")]
    [InlineData("not a uri")]
    public async Task InvalidOrchestratorUrlExits2AndNamesIt(string url)
    {
        using var home = new TempDirectory();
        using var logs = new CapturingLoggerProvider();
        var settings = NodeHost.ValidSettings(home.Path);
        settings[NodeOptions.OrchestratorUrlVariable] = url;

        var code = await NodeProgram.RunAsync([], NodeHost.With(settings, logs), TestContext.Current.CancellationToken);

        Assert.Equal(NodeExitCodes.InvalidConfiguration, code);
        Assert.Contains(NodeOptions.OrchestratorUrlVariable, logs.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void NodeTokenIsRequired()
    {
        var options = new NodeOptions { OrchestratorUrl = "http://127.0.0.1:5180", Home = ".aiakos-dev", NodeId = "wsl-local" };

        var result = new NodeOptionsValidator().Validate(null, options);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Failures);
        Assert.Equal("AIAKOS_NODE_TOKEN is required.", Assert.Single(result.Failures));
    }

    [Theory]
    [InlineData("https://orchestrator.example:443")]
    [InlineData("http://127.0.0.1:5180")]
    public void AbsoluteHttpUrlsAreValid(string url)
    {
        var options = new NodeOptions { OrchestratorUrl = url, Home = "x", NodeId = "n", NodeToken = "token" };

        Assert.True(new NodeOptionsValidator().Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData("~/x")]
    [InlineData("x")]
    public void RelativeHomeResolvesUnderTheUserHome(string home)
    {
        var userHome = Path.Combine(Path.GetTempPath(), "user");

        Assert.Equal(Path.Combine(userHome, "x"), NodeOptions.ResolveHome(home, userHome));
    }

    [Fact]
    public void TildeAloneIsTheUserHome()
    {
        var userHome = Path.Combine(Path.GetTempPath(), "user");

        Assert.Equal(Path.GetFullPath(userHome), NodeOptions.ResolveHome("~", userHome));
    }

    [Fact]
    public void AbsoluteHomeIsKept()
    {
        var absolute = Path.Combine(Path.GetTempPath(), "abs", "node");

        Assert.Equal(absolute, NodeOptions.ResolveHome(absolute, Path.Combine(Path.GetTempPath(), "user")));
    }

    [Fact]
    public void RelativeHomeWithoutAUserHomeIsUnresolved()
    {
        Assert.Null(NodeOptions.ResolveHome("x", null));
        Assert.Null(NodeOptions.ResolveHome("~/x", ""));
    }

    [Fact]
    public void ValidationExceptionsMapToExit2AndOthersToExit1()
    {
        var validation = new Microsoft.Extensions.Options.OptionsValidationException("n", typeof(NodeOptions), ["f"]);

        Assert.Equal(NodeExitCodes.InvalidConfiguration, NodeExitCodes.FromException(validation));
        Assert.Equal(NodeExitCodes.InvalidConfiguration, NodeExitCodes.FromException(new AggregateException(validation)));
        Assert.Equal(NodeExitCodes.Unexpected, NodeExitCodes.FromException(new InvalidOperationException()));
        Assert.Equal(NodeExitCodes.Unexpected, NodeExitCodes.FromException(new AggregateException(validation, new IOException())));
    }
}
