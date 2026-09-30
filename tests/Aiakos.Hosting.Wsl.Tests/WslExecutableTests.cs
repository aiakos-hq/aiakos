using Aspire.Hosting;

namespace Aiakos.Hosting.Wsl.Tests;

public sealed class WslExecutableTests
{
    [Fact]
    public async Task AddWslExecutableRunsWslExeWithDistroCdHomeAndExec()
    {
        var builder = TestApp.CreateBuilder();

        var node = builder.AddWslExecutable("node", "Ubuntu", "/bin/sh", "-c", "echo \"$1\"", "sh", @"C:\a b\c");

        Assert.Equal("wsl.exe", node.Resource.Command);
        Assert.Equal("Ubuntu", node.Resource.Distro);
        Assert.Equal("/bin/sh", node.Resource.LinuxPath);
        var config = await TestApp.ResolveAsync(builder, node.Resource);
        Assert.Equal(
            ["-d", "Ubuntu", "--cd", "~", "--exec", "/bin/sh", "-c", "echo \"$1\"", "sh", @"C:\a b\c"],
            config.Arguments.Select(a => a.Value));
    }

    [Fact]
    public async Task AddWslExecutableDoesNotExpandTildeInTheLinuxPath()
    {
        var builder = TestApp.CreateBuilder();

        var node = builder.AddWslExecutable("node", "Debian", ".aiakos-dev/node/aiakos-node");

        var config = await TestApp.ResolveAsync(builder, node.Resource);
        Assert.Equal(
            ["-d", "Debian", "--cd", "~", "--exec", ".aiakos-dev/node/aiakos-node"],
            config.Arguments.Select(a => a.Value));
    }

    [Fact]
    public void AddWslExecutableRegistersThePreflightServices()
    {
        var builder = TestApp.CreateBuilder();

        builder.AddWslExecutable("node", "Ubuntu", "/x");

        Assert.Contains(builder.Services, d => d.ServiceType == typeof(WslPreflight));
        Assert.Contains(builder.Services, d => d.ServiceType == typeof(IWslProcessRunner));
        Assert.Contains(builder.Services, d => d.ServiceType == typeof(IWslConfigFile));
    }
}
