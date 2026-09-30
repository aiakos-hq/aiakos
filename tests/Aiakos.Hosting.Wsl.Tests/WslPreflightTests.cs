using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Eventing;
using Microsoft.Extensions.DependencyInjection;

namespace Aiakos.Hosting.Wsl.Tests;

public sealed class WslPreflightTests
{
    private const string Wslinfo = "-d Ubuntu --exec wslinfo --networking-mode";
    private const string List = "-l -q";

    private static WslPreflight Create(FakeWslProcessRunner runner, string? wslConfig = null) =>
        new(runner, new FakeWslConfigFile(wslConfig));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static void AssertNamesTheRemedy(string message)
    {
        Assert.Contains(@"%USERPROFILE%\.wslconfig", message, StringComparison.Ordinal);
        Assert.Contains("networkingMode=mirrored", message, StringComparison.Ordinal);
        Assert.Contains("wsl --shutdown", message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("mirrored\n")]
    [InlineData("mirrored\r\n")]
    [InlineData("  Mirrored  ")]
    public async Task MirroredPasses(string output)
    {
        var runner = new FakeWslProcessRunner().On(Wslinfo, 0, output);

        var result = await Create(runner).CheckAsync("Ubuntu", Ct);

        Assert.True(result.Succeeded);
        Assert.Equal("mirrored", result.NetworkingMode);
        Assert.Equal([Wslinfo], runner.Calls);
    }

    [Fact]
    public async Task NatFailsWithTheRemedy()
    {
        var runner = new FakeWslProcessRunner().On(Wslinfo, 0, "nat\n");

        var result = await Create(runner).CheckAsync("Ubuntu", Ct);

        Assert.False(result.Succeeded);
        Assert.Equal("nat", result.NetworkingMode);
        Assert.Contains("'nat'", result.Message, StringComparison.Ordinal);
        AssertNamesTheRemedy(result.Message);
    }

    [Fact]
    public async Task EmptyOutputIsUnknownAndFails()
    {
        var runner = new FakeWslProcessRunner().On(Wslinfo, 0, "  \n");

        var result = await Create(runner, "[wsl2]\nnetworkingMode=mirrored\n").CheckAsync("Ubuntu", Ct);

        Assert.False(result.Succeeded);
        Assert.Equal("unknown", result.NetworkingMode);
        AssertNamesTheRemedy(result.Message);
    }

    [Fact]
    public async Task NonZeroExitForAMissingDistroReportsNotFound()
    {
        var runner = new FakeWslProcessRunner()
            .On("-d Nope --exec wslinfo --networking-mode", -1, stdout: "There is no distribution with the supplied name.")
            .On(List, 0, "Ubuntu\r\ndocker-desktop\r\n");

        var result = await Create(runner).CheckAsync("Nope", Ct);

        Assert.False(result.Succeeded);
        Assert.Equal("WSL distro 'Nope' not found (wsl -l -v).", result.Message);
    }

    [Fact]
    public async Task NonZeroExitWithNoDistrosInstalledReportsNotFound()
    {
        var runner = new FakeWslProcessRunner()
            .On(Wslinfo, -1)
            .On(List, -1, "Windows Subsystem for Linux has no installed distributions.");

        var result = await Create(runner).CheckAsync("Ubuntu", Ct);

        Assert.Equal("WSL distro 'Ubuntu' not found (wsl -l -v).", result.Message);
    }

    [Fact]
    public async Task WslinfoUnavailableFallsBackToWslconfigMirrored()
    {
        var runner = new FakeWslProcessRunner()
            .On(Wslinfo, 1, stderr: "execvpe(wslinfo) failed: No such file or directory")
            .On(List, 0, "Ubuntu\n");

        var result = await Create(runner, "# mine\n[wsl2]\nmemory=8GB\nnetworkingMode = Mirrored ; recommended\n")
            .CheckAsync("Ubuntu", Ct);

        Assert.True(result.Succeeded);
        Assert.Contains(".wslconfig", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WslinfoUnavailableWithWslconfigNatFails()
    {
        var runner = new FakeWslProcessRunner().On(Wslinfo, 1).On(List, 0, "Ubuntu\n");

        var result = await Create(runner, "[wsl2]\nnetworkingMode=NAT\n").CheckAsync("Ubuntu", Ct);

        Assert.False(result.Succeeded);
        Assert.Equal("nat", result.NetworkingMode);
        AssertNamesTheRemedy(result.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("[experimental]\nnetworkingMode=mirrored\n")]
    public async Task WslinfoUnavailableWithoutAWslconfigSettingIsUnknown(string? wslConfig)
    {
        var runner = new FakeWslProcessRunner()
            .On(Wslinfo, 1, stderr: "execvpe(wslinfo) failed: No such file or directory")
            .On(List, 0, "Ubuntu\n");

        var result = await Create(runner, wslConfig).CheckAsync("Ubuntu", Ct);

        Assert.False(result.Succeeded);
        Assert.Equal("unknown", result.NetworkingMode);
        Assert.Contains("execvpe(wslinfo) failed", result.Message, StringComparison.Ordinal);
        AssertNamesTheRemedy(result.Message);
    }

    [Fact]
    public async Task WslExeMissingIsUnknownAndFails()
    {
        var runner = new FakeWslProcessRunner().Throws(Wslinfo, new WslProcessStartException("Could not start 'wsl.exe'"));

        var result = await Create(runner).CheckAsync("Ubuntu", Ct);

        Assert.False(result.Succeeded);
        Assert.Equal("unknown", result.NetworkingMode);
        Assert.Contains("WSL is not available", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASuccessIsCachedPerDistro()
    {
        var runner = new FakeWslProcessRunner()
            .On(Wslinfo, 0, "mirrored")
            .On("-d Debian --exec wslinfo --networking-mode", 0, "mirrored");
        var preflight = Create(runner);

        await preflight.EnsureAsync("Ubuntu", Ct);
        await preflight.EnsureAsync("ubuntu", Ct);
        await preflight.EnsureAsync("Debian", Ct);

        Assert.Equal([Wslinfo, "-d Debian --exec wslinfo --networking-mode"], runner.Calls);
    }

    [Fact]
    public async Task AFailureIsNotCachedSoARestartChecksAgain()
    {
        var runner = new FakeWslProcessRunner().On(Wslinfo, 0, "nat");
        var preflight = Create(runner);

        var first = await Assert.ThrowsAsync<WslPreflightException>(() => preflight.EnsureAsync("Ubuntu", Ct));
        runner.On(Wslinfo, 0, "mirrored");
        var second = await preflight.EnsureAsync("Ubuntu", Ct);

        AssertNamesTheRemedy(first.Message);
        Assert.True(second.Succeeded);
        Assert.Equal(2, runner.Calls.Count);
    }

    [Fact]
    public async Task BeforeResourceStartedEventRunsThePreflightAndFailsTheResource()
    {
        var builder = TestApp.CreateBuilder();
        var runner = new FakeWslProcessRunner().On(Wslinfo, 0, "nat");
        var node = builder.AddWslExecutable("node", "Ubuntu", "/x");
        builder.Services.AddSingleton<IWslProcessRunner>(runner);
        builder.Services.AddSingleton<IWslConfigFile>(new FakeWslConfigFile(null));
        using var app = builder.Build();
        var eventing = app.Services.GetRequiredService<IDistributedApplicationEventing>();

        var ex = await Assert.ThrowsAsync<WslPreflightException>(() =>
            eventing.PublishAsync(new BeforeResourceStartedEvent(node.Resource, app.Services), Ct));

        AssertNamesTheRemedy(ex.Message);
        Assert.Equal([Wslinfo], runner.Calls);
    }

    [Fact]
    public async Task BeforeResourceStartedEventPassesWhenMirrored()
    {
        var builder = TestApp.CreateBuilder();
        var runner = new FakeWslProcessRunner().On(Wslinfo, 0, "mirrored\n");
        var install = builder.AddWslExecutable("install", "Ubuntu", "/bin/true");
        var node = builder.AddWslExecutable("node", "Ubuntu", "/x");
        builder.Services.AddSingleton<IWslProcessRunner>(runner);
        using var app = builder.Build();
        var eventing = app.Services.GetRequiredService<IDistributedApplicationEventing>();

        await eventing.PublishAsync(new BeforeResourceStartedEvent(install.Resource, app.Services), Ct);
        await eventing.PublishAsync(new BeforeResourceStartedEvent(node.Resource, app.Services), Ct);

        Assert.Equal([Wslinfo], runner.Calls);
    }
}
