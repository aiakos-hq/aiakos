using Aiakos.Spec;

namespace Aiakos.Spec.Tests;

public sealed class LoadCanonicalizationTests
{
    [Fact]
    public void LOADstableCanonicalizesTwentyFourExplicitYamlOrderVariants()
    {
        using var rig = new TemporaryRig();
        var root = rig.Root;
        File.WriteAllText(Path.Combine(root, "culture.md"), "Safe culture\n");
        File.WriteAllText(Path.Combine(root, "agents", "impl", "guide.md"), "Safe guidance\n");
        var skillDirectory = Path.Combine(root, "agents", "impl", "skills", "build");
        Directory.CreateDirectory(skillDirectory);
        File.WriteAllText(Path.Combine(skillDirectory, "SKILL.md"), "---\nname: build\ndescription: builds\n---\nSafe skill\n");
        File.WriteAllText(Path.Combine(root, "agents", "impl", "agent.yaml"),
            "apiVersion: aiakos.dev/v1\nkind: Agent\nname: impl\ndescription: Implements issues\ndefaults:\n  harness: claude-code\nguidance: [guide.md]\nskills: [skills/build]\n");
        var pieces = new[]
        {
            "name: demo\n",
            "workspace:\n  repos:\n    - name: $first\n      url: https://example.test/$first.git\n      default_branch: main\n    - name: $second\n      url: https://example.test/$second.git\n      default_branch: main\n",
            "seats:\n  - id: impl\n    agent_ref: local:agents/impl\n    harness: claude-code\n    checkout: shared\n    repos: [$first, $second]\n    workdir_repo: app\n",
            "description: ''\n"
        };
        string? shared = null;
        string? binding = null;
        string? specHash = null;
        string? bindingHash = null;
        var permutationNumber = 0;
        foreach (var permutation in Permutations(pieces))
        {
            var firstRepo = permutationNumber++ % 2 == 0 ? "app" : "lib";
            var secondRepo = firstRepo == "app" ? "lib" : "app";
            File.WriteAllText(Path.Combine(root, "rig.yaml"),
                "apiVersion: aiakos.dev/v1\nkind: Rig\nculture_file: culture.md\n" + string.Concat(permutation)
                .Replace("$first", firstRepo, StringComparison.Ordinal)
                .Replace("$second", secondRepo, StringComparison.Ordinal) +
                "# generated comment\nx-note: ignored\n");
            var envRepos = permutationNumber % 2 == 0
                ? "  app:\n    path: /home/dev/app\n  lib:\n    path: /home/dev/lib\n"
                : "  lib:\n    path: /home/dev/lib\n  app:\n    path: /home/dev/app\n";
            File.WriteAllText(Path.Combine(root, "rig.env.yaml"),
                "apiVersion: aiakos.dev/v1\nkind: RigEnv\nrig: demo\nplacement:\n  default_node: local\nrepos:\n" + envRepos);
            var result = RigLoader.Load(root, null);
            Assert.Empty(result.Diagnostics);
            var loaded = Assert.IsType<ResolvedRig>(result.Rig);
            Assert.Equal("app", Assert.Single(loaded.Seats).Agent!.WorkdirRepo);
            if (shared is null)
            {
                shared = loaded.Canonical!.SharedJson;
                binding = loaded.Canonical.BindingJson;
                specHash = loaded.SpecHash;
                bindingHash = loaded.BindingHash;
            }
            else
            {
                Assert.Equal(shared, loaded.Canonical!.SharedJson);
                Assert.Equal(binding, loaded.Canonical.BindingJson);
                Assert.Equal(specHash, loaded.SpecHash);
                Assert.Equal(bindingHash, loaded.BindingHash);
            }
        }
    }

    [Fact]
    public void LOADchangeSeparatesSharedContentAndEnvironmentHashes()
    {
        using var rig = new TemporaryRig();
        File.WriteAllText(Path.Combine(rig.Root, "rig.yaml"),
            "apiVersion: aiakos.dev/v1\nkind: Rig\nname: demo\nworkspace:\n  repos:\n    - name: app\n      url: https://github.com/example/app.git\n      default_branch: main\nseats:\n  - id: impl\n    agent_ref: local:agents/impl\n    harness: claude-code\n    checkout: shared\n    repos: [app]\n    workdir_repo: app\n");
        var agentPath = Path.Combine(rig.Root, "agents", "impl", "agent.yaml");
        File.AppendAllText(agentPath, "guidance: [guide.md]\n");
        var guidePath = Path.Combine(rig.Root, "agents", "impl", "guide.md");
        File.WriteAllText(guidePath, "safe one\n");
        var baseline = Assert.IsType<ResolvedRig>(RigLoader.Load(rig.Root, null).Rig);

        File.WriteAllText(guidePath, "safe two\n");
        var contentChange = Assert.IsType<ResolvedRig>(RigLoader.Load(rig.Root, null).Rig);
        Assert.NotEqual(baseline.SpecHash, contentChange.SpecHash);
        Assert.Equal(baseline.BindingHash, contentChange.BindingHash);

        var envPath = Path.Combine(rig.Root, "rig.env.yaml");
        File.WriteAllText(envPath, File.ReadAllText(envPath).Replace("/home/dev/app", "~/different/app", StringComparison.Ordinal));
        var bindingChange = Assert.IsType<ResolvedRig>(RigLoader.Load(rig.Root, null).Rig);
        Assert.Equal(contentChange.SpecHash, bindingChange.SpecHash);
        Assert.NotEqual(contentChange.BindingHash, bindingChange.BindingHash);
    }

    private static IEnumerable<string[]> Permutations(string[] values)
    {
        if (values.Length == 1)
        {
            yield return values;
            yield break;
        }

        for (var index = 0; index < values.Length; index++)
        {
            var first = values[index];
            var remainder = values.Where((_, current) => current != index).ToArray();
            foreach (var tail in Permutations(remainder))
                yield return [first, .. tail];
        }
    }

    private sealed class TemporaryRig : IDisposable
    {
        public TemporaryRig()
        {
            Root = Path.Combine(Path.GetTempPath(), $"load-canonical-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(Root, "agents", "impl"));
            File.WriteAllText(Path.Combine(Root, "agents", "impl", "agent.yaml"),
                "apiVersion: aiakos.dev/v1\nkind: Agent\nname: impl\ndescription: Implements issues\ndefaults:\n  harness: claude-code\n");
            File.WriteAllText(Path.Combine(Root, "rig.env.yaml"),
                "apiVersion: aiakos.dev/v1\nkind: RigEnv\nrig: demo\nplacement:\n  default_node: local\nrepos:\n  app:\n    path: /home/dev/app\n");
        }

        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
