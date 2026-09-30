namespace Aiakos.Hosting.Wsl.Tests;

public sealed class WslConfigParserTests
{
    [Theory]
    [InlineData("[wsl2]\nnetworkingMode=mirrored\n", "mirrored")]
    [InlineData("[wsl2]\r\nnetworkingMode=mirrored\r\n", "mirrored")]
    [InlineData("[WSL2]\nNetworkingMode = Mirrored # comment\n", "Mirrored")]
    [InlineData("[wsl2]\nnetworkingMode=\"nat\"\n", "nat")]
    [InlineData("[wsl2]\nmemory=8GB\n\n[experimental]\nautoMemoryReclaim=gradual\n[wsl2]\nnetworkingMode=mirrored\n", "mirrored")]
    public void ReadsNetworkingModeFromTheWsl2Section(string content, string expected)
    {
        Assert.Equal(expected, WslConfigParser.GetNetworkingMode(content));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("[wsl2]\nmemory=8GB\n")]
    [InlineData("[experimental]\nnetworkingMode=mirrored\n")]
    [InlineData("networkingMode=mirrored\n")]
    [InlineData("[wsl2]\n# networkingMode=mirrored\n")]
    [InlineData("[wsl2]\nnetworkingMode=\n")]
    public void ReturnsNullWhenNotSet(string? content)
    {
        Assert.Null(WslConfigParser.GetNetworkingMode(content));
    }
}
