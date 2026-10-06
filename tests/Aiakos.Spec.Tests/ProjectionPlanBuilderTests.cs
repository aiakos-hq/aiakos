using System.Globalization;

using Aiakos.Spec;

namespace Aiakos.Spec.Tests;

public sealed class ProjectionPlanBuilderTests
{
    private static readonly ReferenceSource Source = new("rig.yaml", 8, 9);

    [Fact]
    public void BuildsSortedDescriptorsAndIndependentContentsWithCanonicalHash()
    {
        var files = new[] { File("z.md", []), File("a.md", [], "wrong") };
        var diagnostics = new List<Diagnostic>();

        var plan = ProjectionPlanBuilder.Build("/seats/demo/impl/projection", files, [], Source, diagnostics);

        Assert.NotNull(plan);
        Assert.Empty(diagnostics);
        Assert.Equal(["a.md", "z.md"], plan.Files.Select(file => file.Path));
        Assert.All(plan.Files, descriptor =>
        {
            Assert.Equal("sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", descriptor.Sha256);
            Assert.Equal(0, descriptor.Bytes);
            Assert.Equal("0644", descriptor.Mode);
        });
        Assert.Equal("sha256:13ef5642ed7165eb837e905390409bed12d394f8b86c2deee47bf726910d7d4c", plan.Hash);
        Assert.Equal([], Assert.Single(plan.Contents).Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/x")]
    [InlineData("x\\y")]
    [InlineData("x\0y")]
    [InlineData("x\ry")]
    [InlineData("x\ny")]
    [InlineData("x:y")]
    [InlineData("x//y")]
    [InlineData("./x")]
    [InlineData("x/../y")]
    public void RejectsUnsafeDestinationWithOneRedactedDiagnostic(string path) =>
        AssertInvalid(File(path, [1]));

    [Fact]
    public void RejectsExactAndNfcEquivalentDestinationsBeforeOtherChecks()
    {
        AssertInvalid(File("x", [1]), File("x", [2]));
        AssertInvalid(File("e\u0301", [1]), File("é", [1]), File("../unsafe", new byte[2_097_153]));
    }

    [Fact]
    public void AcceptsDotfilesSpacesAndUnsupportedNormalizationScalar()
    {
        var diagnostics = new List<Diagnostic>();
        var plan = ProjectionPlanBuilder.Build("root", [File(".hidden name\ufffe", [4])], [], Source, diagnostics);

        Assert.NotNull(plan);
        Assert.Empty(diagnostics);
        Assert.Equal(".hidden name\ufffe", Assert.Single(plan.Files).Path);
    }

    [Fact]
    public void HashAndDestinationNormalizationAreCultureIndependent()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var cultureName in new[] { "en-US", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
                var diagnostics = new List<Diagnostic>();
                var plan = ProjectionPlanBuilder.Build("root", [File("e\u0301", [3])], [], Source, diagnostics);
                Assert.NotNull(plan);
                Assert.Empty(diagnostics);
                Assert.Equal("é", Assert.Single(plan.Files).Path);
                Assert.Equal("sha256:4f53cda18c2baa0c0354bb5f9a3ecbe5ed12ab4d8e11ba873c2f11161202b945", BuildEmptyHash());
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Theory]
    [InlineData("/s/demo/impl/projection", "/s")]
    [InlineData("/s/demo/impl/projection", "/s/demo/impl/projection")]
    [InlineData("/s/demo/impl/projection", "/s/demo/impl/projection/nested")]
    [InlineData("/s/demo/impl/projection", "/s//demo/impl/./repos/../projection")]
    [InlineData("~/s/projection", "~/s")]
    public void RejectsLexicalCheckoutOverlap(string root, string checkout) =>
        AssertOverlap(root, checkout);

    [Fact]
    public void AllowsComponentSiblingsAndDifferentAnchorsAndPreservesRoot()
    {
        var diagnostics = new List<Diagnostic>();
        var plan = ProjectionPlanBuilder.Build("/s/demo/impl/projection", [File("x", [1])], ["/s/demo/impl/projection-extra", "/S", "~/s"], Source, diagnostics);

        Assert.NotNull(plan);
        Assert.Empty(diagnostics);
        Assert.Equal("/s/demo/impl/projection", plan.Root);
    }

    [Fact]
    public void DetectsOverlapInEitherDirectionAndKeepsExistingDiagnostics()
    {
        foreach (var (root, checkout) in new[] { ("/a/b", "/a"), ("/a", "/a/b") })
        {
            var prior = new Diagnostic(Severity.Warning, "AIK0000", "before.yaml", 1, 1, "existing", null);
            var diagnostics = new List<Diagnostic> { prior };
            var plan = ProjectionPlanBuilder.Build(root, [File("x", [1])], [checkout], Source, diagnostics);
            Assert.Null(plan);
            Assert.Collection(diagnostics,
                diagnostic => Assert.Same(prior, diagnostic),
                diagnostic => Assert.Equal("projection root overlaps a repository checkout", diagnostic.Message));
        }
    }

    [Fact]
    public void RootAndInputOrderDoNotAffectHashButChangedPathDoes()
    {
        var forward = ProjectionPlanBuilder.Build("one", [File("b", [5]), File("a", [6])], [], Source, []);
        var reversed = ProjectionPlanBuilder.Build("two", [File("a", [6]), File("b", [5])], [], Source, []);
        var changedPath = ProjectionPlanBuilder.Build("one", [File("c", [5]), File("a", [6])], [], Source, []);

        Assert.NotNull(forward);
        Assert.NotNull(reversed);
        Assert.NotNull(changedPath);
        Assert.Equal(forward.Hash, reversed.Hash);
        Assert.NotEqual(forward.Hash, changedPath.Hash);
    }

    [Fact]
    public void ReturnedContentArraysDoNotAliasInputOrEachOther()
    {
        var input = new byte[] { 1, 2 };
        var plan = ProjectionPlanBuilder.Build("root", [File("a", input), File("b", input)], [], Source, []);

        Assert.NotNull(plan);
        var firstCopy = plan.Contents.Values.First();
        var secondCopy = plan.Contents.Values.Single();
        Assert.NotSame(input, firstCopy);
        Assert.Same(firstCopy, secondCopy);
        input[0] = 9;
        Assert.Equal(new byte[] { 1, 2 }, firstCopy);
    }

    [Fact]
    public void AllowsInclusiveTwoMibAndCountsRepeatedDestinationsSeparately()
    {
        var diagnostics = new List<Diagnostic>();
        var plan = ProjectionPlanBuilder.Build("root", [File("a", new byte[1_048_576]), File("b", new byte[1_048_576])], [], Source, diagnostics);

        Assert.NotNull(plan);
        Assert.Empty(diagnostics);
        Assert.Equal(2, plan.Files.Count);
        Assert.Single(plan.Contents);
    }

    [Fact]
    public void RejectsAboveLimitAfterDestinationAndOverlapChecks()
    {
        var diagnostics = new List<Diagnostic>();
        var plan = ProjectionPlanBuilder.Build("root", [File("a", new byte[2_097_153])], [], Source, diagnostics);

        Assert.Null(plan);
        Assert.Equal("rig.yaml:8:9: error AIK3005: projection exceeds 2 MiB\n", DiagnosticFormatter.Format(diagnostics));
    }

    [Fact]
    public void EmptyProjectionHasCanonicalEmptyArrayHash()
    {
        var plan = ProjectionPlanBuilder.Build("root", [], [], Source, []);

        Assert.NotNull(plan);
        Assert.Equal("sha256:4f53cda18c2baa0c0354bb5f9a3ecbe5ed12ab4d8e11ba873c2f11161202b945", plan.Hash);
        Assert.Empty(plan.Files);
        Assert.Empty(plan.Contents);
    }

    private static void AssertInvalid(params EmbeddedFile[] files)
    {
        var diagnostics = new List<Diagnostic>();
        var plan = ProjectionPlanBuilder.Build("root", files, ["root"], Source, diagnostics);
        Assert.Null(plan);
        Assert.Equal("rig.yaml:8:9: error AIK3002: invalid projection file path\n", DiagnosticFormatter.Format(diagnostics));
    }

    private static void AssertOverlap(string root, string checkout)
    {
        var diagnostics = new List<Diagnostic>();
        var plan = ProjectionPlanBuilder.Build(root, [File("x", new byte[2_097_153])], [checkout], Source, diagnostics);
        Assert.Null(plan);
        Assert.Equal("rig.yaml:8:9: error AIK3002: projection root overlaps a repository checkout\n", DiagnosticFormatter.Format(diagnostics));
    }

    private static string BuildEmptyHash() =>
        ProjectionPlanBuilder.Build("root", [], [], Source, [])!.Hash;

    private static EmbeddedFile File(string path, byte[] bytes, string hash = "ignored") => new(path, hash, bytes);
}
