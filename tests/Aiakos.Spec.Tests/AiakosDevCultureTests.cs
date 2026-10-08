namespace Aiakos.Spec.Tests;

public sealed class AiakosDevCultureTests
{
    [Fact]
    public void CultureMatchesExactByteGolden()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aiakos.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var actual = File.ReadAllBytes(Path.Combine(directory.FullName, "rigs", "aiakos-dev", "CULTURE.md"));
        var expected = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "aiakos-dev-golden", "CULTURE.md"));

        Assert.Equal(expected, actual);
    }
}
