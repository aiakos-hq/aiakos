using Aiakos.Spec;

namespace Aiakos.Spec.Tests;

public sealed class RigHashVerifierTests
{
    [Fact]
    public void HASHverifiesLoaderGoldenHashesAndReturnsRecomputedHashesOnMismatch()
    {
        var resolved = MinimalResolvedJson();
        var specHash = Golden("minimal.spec.sha256");
        var bindingHash = Golden("minimal.binding.sha256");

        var valid = RigHashVerifier.Verify(resolved, specHash, bindingHash);

        Assert.Equal(new RigHashVerification(true, specHash, bindingHash), valid);
        Assert.Equal(new RigHashVerification(false, specHash, bindingHash),
            RigHashVerifier.Verify(resolved, "sha256:" + new string('0', 64), bindingHash));
        Assert.Equal(new RigHashVerification(false, specHash, bindingHash),
            RigHashVerifier.Verify(resolved, specHash, "sha256:" + new string('0', 64)));
    }

    [Fact]
    public void HASHrejectsMalformedResolvedJsonWithStableMessage()
    {
        var exception = Assert.Throws<FormatException>(() => RigHashVerifier.Verify("{", "", ""));

        Assert.Equal("Resolved JSON is invalid.", exception.Message);
    }

    private static string MinimalResolvedJson() =>
        "{\"binding\":" + Golden("minimal.binding.json") + ",\"shared\":" + Golden("minimal.shared.json") + "}";

    private static string Golden(string name) => File.ReadAllText(Path.Combine(
        AppContext.BaseDirectory, "Fixtures", "canonical", name)).Trim();
}
