using Aiakos.Spec;
using System.Text.Json;

namespace Aiakos.Spec.Tests;

[CollectionDefinition("Process environment", DisableParallelization = true)]
public sealed class ProcessEnvironmentGroup { }

[Collection("Process environment")]
public sealed class ValidTests
{
    private static string MinimalRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid", "minimal");

    [Fact]
    public void LoadsMinimalRigWithoutDiagnostics()
    {
        var result = RigLoader.Load(MinimalRoot, null);

        Assert.NotNull(result.Rig);
        Assert.Empty(result.Diagnostics);
        Assert.Equal("demo", result.Rig.Name);
        Assert.Equal("", result.Rig.Description);
        Assert.Null(result.Rig.Culture);
        Assert.Equal(new ResolvedRepo("app", "https://github.com/example/app.git", "main"), Assert.Single(result.Rig.Repos));
        var agent = Assert.Single(result.Rig.Agents);
        Assert.Equal("agents/impl", agent.Directory);
        Assert.Equal("impl", agent.Name);
        var seat = Assert.Single(result.Rig.Seats);
        Assert.Equal("impl", seat.Id);
        Assert.Equal("Implements issues", seat.Description);
        Assert.Equal("claude-code", seat.Agent!.Harness);
        Assert.Equal("shared", seat.Agent.Checkout);
        Assert.Equal("app", Assert.Single(seat.Agent.Repos));
        Assert.Equal("app", seat.Agent.WorkdirRepo);
        Assert.Equal("optional", seat.Agent.Requires.Sandbox);
        Assert.Equal("subscription", seat.Agent.Requires.Auth);
        Assert.Empty(seat.Agent.Requires.Secrets);
        var parameter = Assert.Single(result.Rig.SeatParameters);
        Assert.Equal("/home/dev/app", parameter.Workdir);
        Assert.Null(Assert.Single(parameter.Checkouts).BaseRef);
    }

    [Fact]
    public void LOADresultFinalizesHashesAndToolVersionOnSuccessfulLoad()
    {
        var result = RigLoader.Load(MinimalRoot, null);

        Assert.NotNull(result.Rig);
        Assert.NotNull(result.Rig.Canonical);
        Assert.Equal(result.Rig.Canonical.SpecHash, result.Rig.SpecHash);
        Assert.Equal(result.Rig.Canonical.BindingHash, result.Rig.BindingHash);
        Assert.Equal(typeof(RigLoader).Assembly.GetName().Version!.ToString(), result.Rig.ToolVersion);
        var parameters = Assert.Single(result.Rig.SeatParameters);
        Assert.Equal(result.Rig.SpecHash, parameters.SpecHash);
        Assert.Equal(result.Rig.BindingHash, parameters.BindingHash);

        var fullRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid", "full");
        var full = RigLoader.Load(fullRoot, null);
        Assert.NotNull(full.Rig);
        Assert.NotNull(full.Rig.Canonical);
        Assert.All(full.Rig.SeatParameters, seatParameters =>
        {
            Assert.Equal(full.Rig.SpecHash, seatParameters.SpecHash);
            Assert.Equal(full.Rig.BindingHash, seatParameters.BindingHash);
        });

        var warningRoot = CopyFixture(fullRoot);
        try
        {
            var rigPath = Path.Combine(warningRoot, "rig.yaml");
            File.WriteAllText(rigPath, File.ReadAllText(rigPath).Replace(
                "requires: {auth: api-key, secrets: [anthropic_api_key]}",
                "requires: {auth: api-key}", StringComparison.Ordinal));
            var warning = RigLoader.Load(warningRoot, null);
            Assert.NotNull(warning.Rig);
            Assert.NotNull(warning.Rig.Canonical);
            Assert.Contains(warning.Diagnostics, diagnostic => diagnostic.Code == "AIK4012" &&
                diagnostic.Severity == Severity.Warning);
            Assert.All(warning.Rig.SeatParameters, parameters => Assert.NotNull(parameters.Projection));
        }
        finally
        {
            Directory.Delete(warningRoot, recursive: true);
        }

        var invalid = RigLoader.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "invalid",
            "AIK1001-missing-env"), null);
        Assert.Null(invalid.Rig);
        Assert.Null(invalid.Rig?.Canonical);
        Assert.Equal("AIK1001", Assert.Single(invalid.Diagnostics).Code);
    }

    [Fact]
    public void LoadsFullRigWithoutDiagnostics()
    {
        var fullRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid", "full");

        var result = RigLoader.Load(fullRoot, null);

        Assert.NotNull(result.Rig);
        Assert.Empty(result.Diagnostics);
        Assert.Equal("app,lib", string.Join(',', result.Rig.Repos.Select(repo => repo.Name)));
        Assert.Equal("main", result.Rig.Repos[0].DefaultBranch);
        Assert.Equal("impl,review,pm", string.Join(',', result.Rig.Seats.Select(seat => seat.Id)));
        Assert.Null(result.Rig.Seats[2].Agent);
        Assert.Equal("impl,review", string.Join(',', result.Rig.SeatParameters.Select(seat => seat.Seat)));
        var review = result.Rig.SeatParameters[1];
        Assert.Equal("~/aiakos/seats/demo/review/repos/app", review.Workdir);
        Assert.Equal("aiakos/demo/review", Assert.Single(review.Checkouts).Branch);
        Assert.Equal("origin/main", Assert.Single(review.Checkouts).BaseRef);
        Assert.Equal("acceptEdits", review.HarnessSettings.PermissionMode);
        var reviewSeat = result.Rig.Seats[1].Agent!;
        Assert.Null(reviewSeat.Model);
        Assert.Equal("api-key", reviewSeat.Requires.Auth);
        Assert.Equal("anthropic_api_key", Assert.Single(reviewSeat.Requires.Secrets));
        Assert.Empty(review.HarnessSettings.Permissions.Ask);
        Assert.Empty(review.HarnessSettings.Permissions.Deny);
        Assert.Equal("Edit,Bash(dotnet test:*),mcp__github__get_issue", string.Join(',', review.HarnessSettings.Permissions.Allow));
        Assert.Equal("~/aiakos/seats", result.Rig.Binding.SeatRoot);
        Assert.Equal("impl:local,review:local", string.Join(',', result.Rig.Binding.Placement.Select(item => $"{item.Seat}:{item.Node}")));
        Assert.Equal("app:/home/dev/app,lib:~/src/lib", string.Join(',', result.Rig.Binding.Repos.Select(item => $"{item.Name}:{item.Path}")));
        Assert.Equal("anthropic_api_key:~/.config/aiakos/secrets/anthropic_api_key",
            string.Join(',', result.Rig.Binding.Secrets.Select(item => $"{item.Name}:{item.File}")));
        Assert.Equal("/home/dev/app", result.Rig.SeatParameters[0].Workdir);
        Assert.Equal("~/aiakos/seats/demo/review", review.SeatDir);
        Assert.Equal("~/aiakos/seats/demo/review/projection", review.ProjectionRoot);
        Assert.Equal("~/aiakos/seats/demo/review/repos/app", review.Workdir);
        Assert.Equal("~/.config/aiakos/secrets/anthropic_api_key", Assert.Single(review.Secrets).File);
        Assert.Equal("file", Assert.Single(review.Secrets).DeliverAs);
    }

    [Fact]
    public void ResolvedSnapshotRemainsSerializableAfterInputsAreDeleted()
    {
        var root = CopyMinimalRig();
        try
        {
            File.AppendAllText(Path.Combine(root, "rig.yaml"), "culture_file: culture.md\n");
            File.WriteAllText(Path.Combine(root, "culture.md"), "Team\r\n");
            var agentPath = Path.Combine(root, "agents", "impl", "agent.yaml");
            File.AppendAllText(agentPath, "guidance:\n  - second.md\n  - first.md\nskills:\n  - skills/build\n");
            File.WriteAllText(Path.Combine(root, "agents", "impl", "first.md"), "First\n");
            var secondPath = Path.Combine(root, "agents", "impl", "second.md");
            File.WriteAllText(secondPath, "Second\n");
            var skillDirectory = Path.Combine(root, "agents", "impl", "skills", "build");
            Directory.CreateDirectory(skillDirectory);
            File.WriteAllText(Path.Combine(skillDirectory, "SKILL.md"), "---\nname: build\ndescription: builds\n---\n");
            File.WriteAllBytes(Path.Combine(skillDirectory, ".metadata"), [0, 1, 2]);

            var result = RigLoader.Load(root, null);
            Assert.NotNull(result.Rig);
            Assert.NotNull(result.Rig.Canonical);
            var json = JsonSerializer.Serialize(result.Rig);
            var agentSnapshot = Assert.Single(result.Rig.Agents);
            Assert.Equal("Team\n", System.Text.Encoding.UTF8.GetString(result.Rig.Culture!.Content));
            Assert.Equal("agents/impl/second.md,agents/impl/first.md",
                string.Join(',', agentSnapshot.Guidance.Select(file => file.Path)));
            var skill = Assert.Single(agentSnapshot.Skills);
            Assert.Equal("build", skill.Name);
            Assert.Equal(".metadata,SKILL.md", string.Join(',', skill.Files.Select(file => file.Path.Split('/').Last())));
            var embeddedFiles = new[] { result.Rig.Culture! }.Concat(agentSnapshot.Guidance)
                .Concat(skill.Files).ToArray();
            Assert.Equal(embeddedFiles.Select(file => file.Sha256).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal), result.Rig.Canonical.Contents.Keys);
            foreach (var file in embeddedFiles)
                Assert.Equal(file.Content, result.Rig.Canonical.Contents[file.Sha256]);
            var oldSecondHash = agentSnapshot.Guidance[0].Sha256;
            File.WriteAllText(secondPath, "Changed\n");
            var changed = RigLoader.Load(root, null);
            Assert.NotNull(changed.Rig);
            Assert.NotEqual(oldSecondHash, changed.Rig.Agents[0].Guidance[0].Sha256);

            Directory.Delete(root, recursive: true);

            Assert.Equal("demo", result.Rig.Name);
            Assert.Equal("Second\n", System.Text.Encoding.UTF8.GetString(result.Rig.Agents[0].Guidance[0].Content));
            Assert.Equal(json, JsonSerializer.Serialize(result.Rig));
            Assert.Contains("agents/impl", json, StringComparison.Ordinal);
            Assert.DoesNotContain(root, json, StringComparison.Ordinal);
            Assert.DoesNotContain("YamlNode", json, StringComparison.Ordinal);
            Assert.DoesNotContain("x-note", json, StringComparison.Ordinal);
            Assert.DoesNotContain("session_id", json, StringComparison.Ordinal);
            Assert.Contains("Canonical", json, StringComparison.Ordinal);
            Assert.Contains("sha256:", json, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AgentsMayResolveSkillsWithTheSameNameIndependently()
    {
        var root = CopyMinimalRig();
        try
        {
            File.AppendAllText(Path.Combine(root, "rig.yaml"),
                "  - id: review\n    agent_ref: local:agents/review\n    harness: claude-code\n");
            var secondAgent = Path.Combine(root, "agents", "review");
            Directory.CreateDirectory(Path.Combine(secondAgent, "skills", "build"));
            File.WriteAllText(Path.Combine(secondAgent, "agent.yaml"),
                "apiVersion: aiakos.dev/v1\nkind: Agent\nname: review\ndescription: Reviews issues\ndefaults:\n  harness: claude-code\nskills:\n  - skills/build\n");
            File.WriteAllText(Path.Combine(secondAgent, "skills", "build", "SKILL.md"),
                "---\nname: build\ndescription: builds\n---\n");
            var firstSkill = Path.Combine(root, "agents", "impl", "skills", "build");
            Directory.CreateDirectory(firstSkill);
            File.WriteAllText(Path.Combine(firstSkill, "SKILL.md"), "---\nname: build\ndescription: builds\n---\n");
            File.AppendAllText(Path.Combine(root, "agents", "impl", "agent.yaml"), "skills:\n  - skills/build\n");

            var result = RigLoader.Load(root, null);

            Assert.Empty(result.Diagnostics);
            Assert.NotNull(result.Rig);
            Assert.Equal(2, result.Rig.Agents.Count);
            Assert.All(result.Rig.Agents, agent => Assert.Equal("build", Assert.Single(agent.Skills).Name));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadDoesNotDependOnPathOrReadBoundSecretSources()
    {
        var root = CopyMinimalRig();
        var sentinel = string.Concat("runtime-", "sentinel-", Guid.NewGuid().ToString("N"));
        var secretFile = Path.Combine(root, "secret-source.txt");
        var nodeSecretPath = OperatingSystem.IsWindows()
            ? "/" + Path.GetRelativePath(Path.GetPathRoot(secretFile)!, secretFile).Replace('\\', '/')
            : secretFile.Replace('\\', '/');
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            File.WriteAllText(secretFile, sentinel);
            File.AppendAllText(Path.Combine(root, "rig.yaml"),
                "    requires:\n      auth: api-key\n      secrets: [anthropic_api_key]\n");
            File.AppendAllText(Path.Combine(root, "rig.env.yaml"),
                $"secrets:\n  anthropic_api_key:\n    file: {nodeSecretPath}\n");
            var normal = RigLoader.Load(root, null);
            Assert.NotNull(normal.Rig);
            var normalJson = JsonSerializer.Serialize(normal.Rig);
            var normalSpecHash = normal.Rig.SpecHash;
            var normalBindingHash = normal.Rig.BindingHash;

            File.WriteAllText(secretFile, sentinel + "-changed");
            var changedSecretValue = RigLoader.Load(root, null);
            Assert.NotNull(changedSecretValue.Rig);
            Assert.Equal(normalSpecHash, changedSecretValue.Rig.SpecHash);
            Assert.Equal(normalBindingHash, changedSecretValue.Rig.BindingHash);
            File.WriteAllText(secretFile, sentinel);

            Environment.SetEnvironmentVariable("PATH", "");
            var pathless = RigLoader.Load(root, null);

            Assert.NotNull(pathless.Rig);
            Assert.Equal(normalJson, JsonSerializer.Serialize(pathless.Rig));
            Assert.DoesNotContain(sentinel, JsonSerializer.Serialize(pathless.Rig), StringComparison.Ordinal);
            Assert.Equal(nodeSecretPath, Assert.Single(pathless.Rig.Binding.Secrets).File);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AcceptsUtf8Bom()
    {
        var root = CopyMinimalRig();
        try
        {
            var path = Path.Combine(root, "rig.yaml");
            File.WriteAllBytes(path, [0xEF, 0xBB, 0xBF, .. File.ReadAllBytes(path)]);

            Assert.Empty(RigLoader.Load(root, null).Diagnostics);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IgnoresExtensionFieldsAtEveryLevel()
    {
        var root = CopyMinimalRig();
        try
        {
            var rigPath = Path.Combine(root, "rig.yaml");
            var rig = File.ReadAllText(rigPath).Replace("    checkout: shared\n", "    checkout: shared\n    x-note: hi\n", StringComparison.Ordinal);
            File.WriteAllText(rigPath, rig + "x-note: hi\n");
            var agent = Path.Combine(root, "agents", "impl", "agent.yaml");
            File.AppendAllText(agent, "x-note: hi\n");

            Assert.Empty(RigLoader.Load(root, null).Diagnostics);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadsEnvironmentFileFromExplicitPath()
    {
        var root = CopyMinimalRig();
        var externalEnv = Path.Combine(Path.GetTempPath(), $"aiakos-env-{Guid.NewGuid():N}.yaml");
        try
        {
            File.Copy(Path.Combine(root, "rig.env.yaml"), externalEnv);
            File.Delete(Path.Combine(root, "rig.env.yaml"));

            Assert.Empty(RigLoader.Load(root, externalEnv).Diagnostics);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            File.Delete(externalEnv);
        }
    }

    [Fact]
    public void DoesNotExposeExceptionTextInYamlSyntaxDiagnostic()
    {
        var root = CopyMinimalRig();
        try
        {
            var path = Path.Combine(root, "rig.yaml");
            File.WriteAllText(path, File.ReadAllText(path).Replace("name: demo", "name: [demo", StringComparison.Ordinal));

            var formatted = DiagnosticFormatter.Format(RigLoader.Load(root, null).Diagnostics);

            Assert.DoesNotContain("Operation is not valid", formatted, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DisplaysExplicitEnvironmentPathWhenOutsideRigRoot()
    {
        var root = CopyMinimalRig();
        var externalEnv = Path.Combine(Path.GetTempPath(), $"aiakos-missing-env-{Guid.NewGuid():N}.yaml");
        try
        {
            var diagnostic = Assert.Single(RigLoader.Load(root, externalEnv).Diagnostics);

            Assert.Equal("AIK1001", diagnostic.Code);
            Assert.Equal(externalEnv.Replace('\\', '/'), diagnostic.File);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void KeepsParserMessageInYamlSyntaxDiagnostic()
    {
        var root = CopyMinimalRig();
        try
        {
            File.AppendAllText(Path.Combine(root, "rig.yaml"), "---\nname: other\n");

            var formatted = DiagnosticFormatter.Format(RigLoader.Load(root, null).Diagnostics);

            Assert.Equal("rig.yaml:12:1: error AIK1002: YAML syntax error: Only one YAML document is supported.\n", formatted);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CopyMinimalRig()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aiakos-rig-{Guid.NewGuid():N}");
        CopyDirectory(MinimalRoot, root);
        return root;
    }

    private static string CopyFixture(string source)
    {
        var root = Path.Combine(Path.GetTempPath(), $"aiakos-rig-{Guid.NewGuid():N}");
        CopyDirectory(source, root);
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
