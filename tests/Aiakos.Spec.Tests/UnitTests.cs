using Aiakos.Spec;

namespace Aiakos.Spec.Tests;

public sealed class UnitTests
{
    [Fact]
    public void CalculatesLevenshteinDistance()
    {
        Assert.Equal(1, SchemaValidator.Distance("chekout", "checkout"));
        Assert.Equal(3, SchemaValidator.Distance("kitten", "sitting"));
    }

    [Fact]
    public void UsesSchemaOrderToBreakHintTies()
    {
        var schema = new[] { new FieldSchema("cat"), new FieldSchema("cot") };

        Assert.Equal("cat", SchemaValidator.FindHint("cut", schema));
    }

    [Fact]
    public void FormatsDiagnosticWithAndWithoutHint()
    {
        var diagnostics = new[]
        {
            new Diagnostic(Severity.Error, "AIK2002", "rig.yaml", 2, 3, "unknown field 'foo'", null),
            new Diagnostic(Severity.Warning, "AIK0000", "rig.yaml", 4, 1, "example", "try again")
        };

        Assert.Equal("rig.yaml:2:3: error AIK2002: unknown field 'foo'\nrig.yaml:4:1: warning AIK0000: example\n  hint: try again\n", DiagnosticFormatter.Format(diagnostics));
    }

    [Fact]
    public void ReportsMissingRigAndEnvironmentFilesWhenRootIsMissing()
    {
        var missingRoot = Path.Combine(Path.GetTempPath(), $"aiakos-missing-{Guid.NewGuid():N}");

        var result = RigLoader.Load(missingRoot, null);

        Assert.Equal(["rig.yaml", "rig.env.yaml"], result.Diagnostics.Select(diagnostic => diagnostic.File));
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal("AIK1001", diagnostic.Code));
    }

    [Theory]
    [InlineData("impl", "seat 'impl'")]
    [InlineData("Invalid", "seats[1]")]
    public void UsesSeatContextForErrorsInLaterSeats(string seatId, string expectedContext)
    {
        var root = CopyMinimalRig();
        try
        {
            File.AppendAllText(Path.Combine(root, "rig.yaml"), $"  - id: {seatId}\n    agent_ref: local:agents/impl\n    chekout: shared\n");

            var diagnostic = Assert.Single(RigLoader.Load(root, null).Diagnostics, item => item.Code == "AIK2002");
            Assert.Contains($"in {expectedContext}", diagnostic.Message, StringComparison.Ordinal);
            Assert.Equal(14, diagnostic.Line);
            Assert.Equal(5, diagnostic.Column);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CopyMinimalRig()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid", "minimal");
        var root = Path.Combine(Path.GetTempPath(), $"aiakos-seat-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(root, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.GetDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(root, Path.GetFileName(directory)));
        }

        return root;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.GetDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }
}
