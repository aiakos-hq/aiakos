namespace Aiakos.Spec.Tests;

public sealed class AiakosDevReviewerGuidanceTests
{
    [Fact]
    public void ReviewerGuidanceDefinesExactHeadChecklistFindingsAndAttribution()
    {
        var guidance = ReadRigFile("agents", "reviewer", "GUIDANCE.md");
        foreach (var heading in new[] { "## Exact diff", "## Checklist", "## Findings", "## Never", "## Attribution" })
        {
            Assert.Contains(heading, guidance, StringComparison.Ordinal);
        }

        foreach (var requirement in new[]
        {
            "gh pr checkout <n>", "git rev-parse HEAD", "exact head", "later push", "new review",
            "dirty checkout", "blocks", "reset", "clean",
            "issue", "merged spec", "each criterion", "test or command evidence", "manual",
            "not verifiable here", "committed tests", "dotnet build -c Release", "dotnet test",
            "new warnings", "gh pr checks", "green", "docs/spec deviations", "risk rows", "actually checked",
            "tenant_id", "environment identity", "silent fallbacks", "host interfaces", "LF",
            "secrets", "accepted ADRs", "blocking", "should-fix", "nit", "path:line",
            "concrete reason", "zero blocking", "edit source", "fix a finding", "commit", "push",
            "approve", "request changes", "generated outputs are allowed",
            "failure to run checks", "reported", "never guessed",
            "Review by review@aiakos-dev at <head sha>", "Git identity", "maintainer"
        })
        {
            Assert.Contains(requirement, guidance, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ReviewSkillUsesExactMetadataEvidenceWorkflowAndCommentOnlySubmission()
    {
        var skill = ReadRigFile("agents", "reviewer", "skills", "review-pr", "SKILL.md");
        Assert.StartsWith(
            "---\nname: review-pr\ndescription: Review the PR at its exact head and post one comment review.\n---\n",
            skill,
            StringComparison.Ordinal);

        AssertOrdered(skill,
            "gh pr view <n>", "linked issue", "merged spec", "git status", "gh pr checkout <n>",
            "git rev-parse HEAD", "gh pr diff <n>", "dotnet build -c Release", "dotnet test",
            "gh pr checks <n>", "Review by review@aiakos-dev at <head sha>", "verdict",
            "criterion/evidence table", "findings", "gh pr review <n> --comment --body-file <file>",
            "REPORT review@aiakos-dev");

        Assert.Contains("severity", skill, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("blocking", skill, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("temporary body-file", skill, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("outside the checkout", skill, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("remove", skill, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("shared-clone lock", skill, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("retry once", skill, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("blocked", skill, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--comment --body-file", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("gh pr review --approve", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("gh pr review --request-changes", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("git push", skill, StringComparison.Ordinal);
        Assert.Contains("last output", skill, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertOrdered(string text, params string[] required)
    {
        var previous = -1;
        foreach (var value in required)
        {
            var index = text.IndexOf(value, previous + 1, StringComparison.OrdinalIgnoreCase);
            Assert.True(index > previous, $"Expected '{value}' after offset {previous}.");
            previous = index;
        }
    }

    private static string ReadRigFile(params string[] segments)
    {
        var root = FindRepositoryRoot();
        var path = Path.Combine([root, "rigs", "aiakos-dev", .. segments]);
        Assert.True(File.Exists(path), $"Expected rig file is missing: {Path.GetRelativePath(root, path)}");
        return File.ReadAllText(path);
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
