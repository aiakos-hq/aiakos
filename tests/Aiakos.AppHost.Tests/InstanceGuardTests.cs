namespace Aiakos.AppHost.Tests;

public sealed class InstanceGuardTests
{
    private static AiakosDevOptions Dev() => new()
    {
        Instance = "dev",
        PortBase = 5180,
        Wsl = new AiakosDevOptions.WslOptions { Distro = "Ubuntu", Home = ".aiakos-dev", NodeId = "wsl-local" },
    };

    private static void AssertRefused(AiakosDevOptions options, string key)
    {
        var ex = Assert.Throws<InstanceGuardException>(() => InstanceGuard.EnsureNotReleased(options));
        Assert.Equal(key, ex.Key);
        Assert.Contains(key, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDevDefaultsPass()
    {
        InstanceGuard.EnsureNotReleased(Dev());
    }

    [Theory]
    [InlineData("release")]
    [InlineData("Release")]
    public void TheReleasedInstanceNameIsRefused(string instance)
    {
        var options = Dev();
        options.Instance = instance;

        AssertRefused(options, "Aiakos:Instance");
    }

    [Theory]
    [InlineData(".aiakos")]
    [InlineData("~/.aiakos")]
    [InlineData(".aiakos/")]
    [InlineData("./.aiakos")]
    public void TheReleasedWslHomeIsRefused(string home)
    {
        var options = Dev();
        options.Wsl.Home = home;

        AssertRefused(options, "Aiakos:Wsl:Home");
    }

    [Theory]
    [InlineData(7180)]
    [InlineData(7170)]
    [InlineData(7190)]
    public void APortBaseCollidingWithTheReleasedPortsIsRefused(int portBase)
    {
        var options = Dev();
        options.PortBase = portBase;

        AssertRefused(options, "Aiakos:PortBase");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(80)]
    [InlineData(65530)]
    public void AnOutOfRangePortBaseIsRefused(int portBase)
    {
        var options = Dev();
        options.PortBase = portBase;

        AssertRefused(options, "Aiakos:PortBase");
    }

    [Theory]
    [InlineData("")]
    [InlineData("Dev")]
    [InlineData("dev stack")]
    public void AMissingOrInvalidInstanceIsRefused(string instance)
    {
        var options = Dev();
        options.Instance = instance;

        AssertRefused(options, "Aiakos:Instance");
    }

    [Theory]
    [InlineData("")]
    [InlineData("/home/me/.aiakos-dev")]
    public void AMissingOrAbsoluteWslHomeIsRefused(string home)
    {
        var options = Dev();
        options.Wsl.Home = home;

        AssertRefused(options, "Aiakos:Wsl:Home");
    }

    [Fact]
    public void AMissingDistroIsRefused()
    {
        var options = Dev();
        options.Wsl.Distro = " ";

        AssertRefused(options, "Aiakos:Wsl:Distro");
    }

    [Fact]
    public void AMissingNodeIdIsRefused()
    {
        var options = Dev();
        options.Wsl.NodeId = string.Empty;

        AssertRefused(options, "Aiakos:Wsl:NodeId");
    }
}
