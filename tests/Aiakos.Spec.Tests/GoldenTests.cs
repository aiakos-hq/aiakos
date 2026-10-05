using System.Text.RegularExpressions;
using Aiakos.Spec;

namespace Aiakos.Spec.Tests;

public sealed class GoldenTests
{
    public static IEnumerable<object[]> InvalidFixtures =>
        Directory.GetDirectories(Path.Combine(FixtureRoot, "invalid"))
            .Order(StringComparer.Ordinal)
            .Select(directory => new object[] { Path.GetFileName(directory) });

    private static string FixtureRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    [Theory]
    [MemberData(nameof(InvalidFixtures))]
    public void MatchesDiagnosticGolden(string fixtureName)
    {
        var fixture = Path.Combine(FixtureRoot, "invalid", fixtureName);
        var result = RigLoader.Load(fixture, null);
        var actual = DiagnosticFormatter.Format(result.Diagnostics);
        var expected = File.ReadAllText(Path.Combine(fixture, "expected", "diagnostics.txt"));

        Assert.Equal(result.Diagnostics.Any(item => item.Severity == Severity.Error), result.Rig is null);
        if (fixtureName == "AIK1002-syntax")
        {
            Assert.Matches(new Regex("^rig.yaml:\\d+:\\d+: error AIK1002: YAML syntax error: .+\\n$", RegexOptions.CultureInvariant), actual);
            Assert.StartsWith("rig.yaml:<line>:<column>: error AIK1002: YAML syntax error:", expected, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(expected, actual);
        }
    }
}
