namespace Aiakos.Spec.Tests;

public sealed class AiakosDevRunbookTests
{
    [Fact]
    public void ReadmeDescribesSeatPoliciesPendingStatusAndVerificationLimits()
    {
        var repoRoot = FindRepositoryRoot();
        var readme = File.ReadAllText(Path.Combine(repoRoot, "rigs", "aiakos-dev", "README.md"));

        foreach (var heading in new[]
        {
            "## Status", "## Seats", "## Prerequisites", "## Local binding",
            "## Operating the rig", "## Upgrade", "## Verification limits"
        })
        {
            Assert.Contains(heading, readme);
        }

        Assert.Contains("seat-worktree", readme);
        Assert.DoesNotContain("shared-readonly", readme);
        Assert.Contains("does not mean that M1 acceptance has run", readme);
        Assert.Contains("16-2", readme);
        Assert.Contains("16-3", readme);
        Assert.Contains("Stage B", readme);
        Assert.Contains("rig.env.example.yaml", readme);
        Assert.Contains("check-prereqs.sh", readme);
        Assert.Contains("custom paths", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dotnet aiakos instance start", readme);
        Assert.Contains("dotnet aiakos up rigs/aiakos-dev", readme);
        Assert.Contains("docs/specs/0007-cli-and-released-instance.md", readme);
        Assert.Contains("docs/specs/0008-aiakos-dev-rig.md", readme);
        Assert.Contains("docs/adr/0008-bootstrap-rule.md", readme);
        Assert.Contains("docs/adr/0037-team-pin-in-local-tool-manifest.md", readme);
        Assert.Contains("docs/adr/0038-seat-attribution-under-shared-github-identity.md", readme);
        Assert.Contains("docs/risks.md", readme);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Aiakos.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not find the repository root containing Aiakos.slnx.");
    }
}
