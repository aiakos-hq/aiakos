using YamlDotNet.RepresentationModel;

namespace Aiakos.Spec.Tests;

public sealed class AiakosDevReviewerTests
{
    private static readonly string[] ExpectedGuidance = ["GUIDANCE.md"];
    private static readonly string[] ExpectedSkills = ["skills/review-pr"];

    private static readonly string[] ExpectedAllow =
    [
        "Bash(gh pr view:*)",
        "Bash(gh pr diff:*)",
        "Bash(gh pr checkout:*)",
        "Bash(gh pr checks:*)",
        "Bash(gh pr review:*)",
        "Bash(gh issue view:*)",
        "Bash(gh run view:*)",
        "Bash(git status:*)",
        "Bash(git diff:*)",
        "Bash(git log:*)",
        "Bash(git show:*)",
        "Bash(git fetch:*)",
        "Bash(git rev-parse:*)",
        "Bash(dotnet build:*)",
        "Bash(dotnet test:*)",
        "Bash(dotnet format --verify-no-changes:*)"
    ];

    private static readonly string[] ExpectedDeny =
    [
        "Bash(tmux:*)",
        "Bash(aiakos:*)",
        "Bash(dotnet aiakos:*)",
        "Bash(dotnet tool:*)",
        "Bash(cmd.exe:*)",
        "Bash(powershell.exe:*)",
        "Bash(pwsh.exe:*)",
        "Bash(wsl.exe:*)",
        "Bash(kill:*)",
        "Bash(pkill:*)",
        "Bash(killall:*)",
        "Bash(git push --force:*)",
        "Bash(git push -f:*)",
        "Bash(git push --force-with-lease:*)",
        "Bash(git push origin main:*)",
        "Bash(git push origin HEAD:main:*)",
        "Bash(gh pr merge:*)",
        "Bash(gh api:*)",
        "Bash(gh auth:*)",
        "Bash(gh secret:*)",
        "Bash(gh release:*)",
        "Bash(gh workflow:*)",
        "Bash(gh repo:*)",
        "Read(//mnt/**)",
        "Edit(//mnt/**)",
        "Read(~/.aiakos*/**)",
        "Edit(~/.aiakos*/**)",
        "Read(~/aiakos/seats/*/*/aiakos/**)",
        "Edit(~/aiakos/seats/*/*/aiakos/**)",
        "Edit",
        "Write",
        "NotebookEdit",
        "Bash(git commit:*)",
        "Bash(git push:*)",
        "Bash(gh pr create:*)",
        "Bash(gh pr comment:*)",
        "Bash(gh pr review --approve:*)",
        "Bash(gh pr review --request-changes:*)",
        "Bash(gh pr review -a:*)",
        "Bash(gh pr review -r:*)"
    ];

    [Fact]
    public void ReviewerAgentDeclaresExactClaudeDefaultsSkillsAndPermissions()
    {
        var repoRoot = FindRepositoryRoot();
        var agentPath = Path.Combine(repoRoot, "rigs", "aiakos-dev", "agents", "reviewer", "agent.yaml");
        using var reader = File.OpenText(agentPath);
        var yaml = new YamlStream();
        yaml.Load(reader);

        var root = Assert.IsType<YamlMappingNode>(yaml.Documents[0].RootNode);
        Assert.Equal("reviewer", Scalar(root, "name"));
        var defaults = Mapping(root, "defaults");
        Assert.Equal("claude-code", Scalar(defaults, "harness"));
        Assert.Equal("opus", Scalar(defaults, "model"));
        Assert.Equal(ExpectedGuidance, Sequence(root, "guidance"));
        Assert.Equal(ExpectedSkills, Sequence(root, "skills"));

        var claudeCode = Assert.IsType<YamlMappingNode>(Mapping(root, "harnesses").GetMapping("claude-code"));
        Assert.Equal("default", Scalar(claudeCode, "permission_mode"));
        var permissions = Mapping(claudeCode, "permissions");
        Assert.Equal(ExpectedAllow, Sequence(permissions, "allow"));
        var deny = Sequence(permissions, "deny");
        Assert.Equal(ExpectedDeny, deny);
        Assert.Contains("Bash(gh pr review:*)", ExpectedAllow);
        Assert.Contains("Edit", deny);
        Assert.Contains("Write", deny);
        Assert.Contains("NotebookEdit", deny);
        Assert.Contains("Bash(git commit:*)", deny);
        Assert.Contains("Bash(git push:*)", deny);
        Assert.Contains("Bash(gh pr review --approve:*)", deny);
        Assert.Contains("Bash(gh pr review --request-changes:*)", deny);
        Assert.Contains("Bash(gh pr review -a:*)", deny);
        Assert.Contains("Bash(gh pr review -r:*)", deny);
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

    private static YamlMappingNode Mapping(YamlMappingNode parent, string key) =>
        Assert.IsType<YamlMappingNode>(parent.GetMapping(key));

    private static string Scalar(YamlMappingNode parent, string key) =>
        Assert.IsType<YamlScalarNode>(parent.GetMapping(key)).Value!;

    private static string[] Sequence(YamlMappingNode parent, string key) =>
        Assert.IsType<YamlSequenceNode>(parent.GetMapping(key)).Children
            .Select(child => Assert.IsType<YamlScalarNode>(child).Value!)
            .ToArray();
}
