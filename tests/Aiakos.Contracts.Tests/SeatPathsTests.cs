using Aiakos.Contracts.Node;

namespace Aiakos.Contracts.Tests;

[Trait("Category", "Contract")]
public sealed class SeatPathsTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("aiakos/claude-settings.json")]
    [InlineData(".claude/skills/x/SKILL.md")]
    [InlineData("a..b/c")]
    public void AllowsRelativeSafePaths(string path) => Assert.True(SeatPaths.IsAllowed(path));

    [Theory]
    [InlineData("")]
    [InlineData("/etc/x")]
    [InlineData("../x")]
    [InlineData("a/../b")]
    [InlineData("./a")]
    [InlineData("a/./b")]
    [InlineData("a//b")]
    [InlineData("a/")]
    [InlineData("a\\b")]
    [InlineData("C:x")]
    public void RejectsUnsafePaths(string path) => Assert.False(SeatPaths.IsAllowed(path));

    [Fact]
    public void RejectsNulPaths() => Assert.False(SeatPaths.IsAllowed("a\0b"));
}
