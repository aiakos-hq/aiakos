namespace Aiakos.Spec.Tests;

public sealed class AiakosDevImplementerGuidanceTests
{
    [Fact]
    public void ImplementerGuidanceCoversTheRequiredWorkflowAndReport()
    {
        var guidance = ReadRigFile("agents", "implementer", "GUIDANCE.md");

        foreach (var heading in new[]
        {
            "## Inputs", "## Worktree", "## Definition of done", "## Not verified here",
            "## Bootstrap", "## Attribution"
        })
        {
            Assert.Contains(heading, guidance, StringComparison.Ordinal);
        }

        foreach (var requirement in new[]
        {
            "gh issue view", "merged linked spec", "acceptance criteria", "cited ADRs and plan",
            "docs/risks.md", "unmerged spec", "question",
            "persists across issues", "dirty git status", "blocks", "reset", "clean", "stash",
            "git fetch origin", "origin/main", "feat/<n>-<slug>", "fix/<n>-<slug>", "docs/<n>-<slug>",
            "criteria met with evidence", "committed tests", "Release", "new warnings",
            "Changes after acceptance", ".github/pull_request_template.md", "risk IDs",
            "Windows dev AppHost", "WSL E2E", "manual demos", "honestly in the PR",
            "development build", "aiakos-dev", "other rig names", "other homes", "other ports",
            "Aiakos-Seat: impl@aiakos-dev", "Implemented by impl@aiakos-dev",
            "REPORT impl@aiakos-dev", "ready-for-review", "changes-pushed", "blocked", "question",
            "shared-clone lock", "retried once", "do not remove locks"
        })
        {
            Assert.Contains(requirement, guidance, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("next: <lead action>\n```", guidance, StringComparison.Ordinal);
        Assert.EndsWith("\n", guidance, StringComparison.Ordinal);
    }

    [Fact]
    public void ImplementIssueSkillUsesRequiredMetadataAndOrderedCommands()
    {
        var skill = ReadRigFile("agents", "implementer", "skills", "implement-issue", "SKILL.md");
        Assert.StartsWith(
            "---\nname: implement-issue\ndescription: Implement one GitHub issue from its merged spec and open a PR.\n---\n",
            skill,
            StringComparison.Ordinal);

        AssertOrdered(skill,
            "gh issue view <n>", "merged linked spec", "docs/risks.md", "git status", "git fetch origin",
            "git switch -c", "dotnet build -c Release", "dotnet test", "Changes after acceptance",
            "conventional commit", "Aiakos-Seat: impl@aiakos-dev", "git push -u origin <branch>",
            "gh pr create", ".github/pull_request_template.md", "Closes #<n>", "gh pr checks",
            "REPORT impl@aiakos-dev");
        Assert.Contains("nontrivial unmerged spec", skill, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dirty", skill, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("shared-clone lock", skill, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("retried once", skill, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddressReviewSkillUsesRequiredMetadataAndPreservesTheExistingPrBranch()
    {
        var skill = ReadRigFile("agents", "implementer", "skills", "address-review", "SKILL.md");
        Assert.StartsWith(
            "---\nname: address-review\ndescription: Address each finding on the existing PR without rewriting history.\n---\n",
            skill,
            StringComparison.Ordinal);

        AssertOrdered(skill,
            "gh pr view <n> --comments", "gh pr diff <n>", "list findings", "code change",
            "reason in a PR comment", "git fetch origin", "git merge origin/main", "dotnet build",
            "dotnet test", "conventional commit", "Aiakos-Seat: impl@aiakos-dev", "git push",
            "gh pr comment <n>", "Addressed at <sha>:", "REPORT impl@aiakos-dev");
        Assert.Contains("CULTURE.md", skill, StringComparison.Ordinal);
        Assert.Contains("stop rules", skill, StringComparison.OrdinalIgnoreCase);
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
