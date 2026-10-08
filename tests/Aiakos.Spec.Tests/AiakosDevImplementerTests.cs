using YamlDotNet.RepresentationModel;

namespace Aiakos.Spec.Tests;

public sealed class AiakosDevImplementerTests
{
    private static readonly string[] ExpectedGuidance = ["GUIDANCE.md"];
    private static readonly string[] ExpectedSkills = ["skills/implement-issue", "skills/address-review"];

    private static readonly string[] ExpectedAllow =
    [
        "Bash(dotnet build:*)",
        "Bash(dotnet test:*)",
        "Bash(dotnet format:*)",
        "Bash(dotnet restore:*)",
        "Bash(dotnet pack:*)",
        "Bash(git status:*)",
        "Bash(git diff:*)",
        "Bash(git log:*)",
        "Bash(git show:*)",
        "Bash(git fetch:*)",
        "Bash(git switch:*)",
        "Bash(git add:*)",
        "Bash(git commit:*)",
        "Bash(git merge origin/main:*)",
        "Bash(git push:*)",
        "Bash(gh issue view:*)",
        "Bash(gh issue list:*)",
        "Bash(gh issue comment:*)",
        "Bash(gh pr create:*)",
        "Bash(gh pr view:*)",
        "Bash(gh pr diff:*)",
        "Bash(gh pr checks:*)",
        "Bash(gh pr comment:*)",
        "Bash(gh run view:*)",
        "Bash(gh run list:*)"
    ];

    private static readonly string[] ExpectedAsk =
    [
        "Bash(git reset:*)",
        "Bash(git clean:*)",
        "Bash(git checkout:*)",
        "Bash(git stash:*)"
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
        "Bash(gh pr review:*)"
    ];

    [Fact]
    public void ImplementerAgentDeclaresExactClaudeDefaultsSkillsAndPermissions()
    {
        var repoRoot = FindRepositoryRoot();
        var agentPath = Path.Combine(repoRoot, "rigs", "aiakos-dev", "agents", "implementer", "agent.yaml");
        using var reader = File.OpenText(agentPath);
        var yaml = new YamlStream();
        yaml.Load(reader);

        var root = Assert.IsType<YamlMappingNode>(yaml.Documents[0].RootNode);
        Assert.Equal("implementer", Scalar(root, "name"));
        var defaults = Mapping(root, "defaults");
        Assert.Equal("claude-code", Scalar(defaults, "harness"));
        Assert.Equal("opus", Scalar(defaults, "model"));
        Assert.Equal(ExpectedGuidance, Sequence(root, "guidance"));
        Assert.Equal(ExpectedSkills, Sequence(root, "skills"));

        var claudeCode = Assert.IsType<YamlMappingNode>(Mapping(root, "harnesses").GetMapping("claude-code"));
        Assert.Equal("acceptEdits", Scalar(claudeCode, "permission_mode"));
        var permissions = Mapping(claudeCode, "permissions");
        Assert.Equal(ExpectedAllow, Sequence(permissions, "allow"));
        Assert.Equal(ExpectedAsk, Sequence(permissions, "ask"));
        Assert.Equal(ExpectedDeny, Sequence(permissions, "deny"));
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

internal static class AiakosDevYamlMappingExtensions
{
    public static YamlDotNet.RepresentationModel.YamlNode GetMapping(this YamlMappingNode mapping, string key) =>
        mapping.Children[new YamlScalarNode(key)];
}
