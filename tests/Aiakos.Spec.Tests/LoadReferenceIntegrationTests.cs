using Aiakos.Spec;

namespace Aiakos.Spec.Tests;

public sealed class LoadReferenceIntegrationTests
{
    [Fact]
    public void ReportsCultureGuidanceAndEnvironmentErrorsInDefinedOrder()
    {
        using var rig = new TemporaryRig();
        var rigPath = Path.Combine(rig.Root, "rig.yaml");
        File.AppendAllText(rigPath, "culture_file: MISSING.md\n");
        var agentPath = Path.Combine(rig.Root, "agents", "impl", "agent.yaml");
        File.AppendAllText(agentPath, "guidance:\n  - MISSING.md\n");
        var envPath = Path.Combine(rig.Root, "rig.env.yaml");
        File.WriteAllText(envPath, File.ReadAllText(envPath).Replace("rig: demo", "rig: other", StringComparison.Ordinal));

        var result = RigLoader.Load(rig.Root, null);

        Assert.Null(result.Rig);
        Assert.Equal(
            "rig.yaml:12:15: error AIK3001: referenced file or directory not found\n" +
            "agents/impl/agent.yaml:8:5: error AIK3001: referenced file or directory not found\n" +
            "rig.env.yaml:3:6: error AIK5001: rig 'other' does not match rig name 'demo'\n",
            DiagnosticFormatter.Format(result.Diagnostics));
    }

    [Fact]
    public void ReportsDuplicateSkillNamesAtEveryLaterDeclarationForAnAgent()
    {
        using var rig = new TemporaryRig();
        var agentPath = Path.Combine(rig.Root, "agents", "impl", "agent.yaml");
        File.AppendAllText(agentPath, "skills:\n  - skills/build\n  - ./skills/build/\n");
        var skillDirectory = Path.Combine(rig.Root, "agents", "impl", "skills", "build");
        Directory.CreateDirectory(skillDirectory);
        File.WriteAllText(Path.Combine(skillDirectory, "SKILL.md"), "---\nname: build\ndescription: builds\n---\n");

        var result = RigLoader.Load(rig.Root, null);

        Assert.Null(result.Rig);
        Assert.Equal(
            "agents/impl/agent.yaml:9:5: error AIK4003: duplicate skill name 'build'\n  hint: first defined at line 8\n",
            DiagnosticFormatter.Format(result.Diagnostics));
    }

    [Fact]
    public void EmitsSharedContentDiagnosticsOnlyOnceForCanonicalPath()
    {
        using var rig = new TemporaryRig();
        File.AppendAllText(Path.Combine(rig.Root, "rig.yaml"), "culture_file: agents/impl/shared.md\n");
        File.AppendAllText(Path.Combine(rig.Root, "agents", "impl", "agent.yaml"), "guidance:\n  - shared.md\n");
        var secretLikeText = string.Concat("token: ", "ghp_", "abcdef");
        File.WriteAllText(Path.Combine(rig.Root, "agents", "impl", "shared.md"), secretLikeText + "\n");

        var result = RigLoader.Load(rig.Root, null);

        Assert.Null(result.Rig);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(new Diagnostic(Severity.Error, "AIK4020", "agents/impl/shared.md", 1, 8,
            "credential-like value (GitHub token)", "never put secrets in rig files; name the secret and bind it in rig.env.yaml"), diagnostic);
    }

    [Fact]
    public void SkipsEachReferenceListWhenAnEarlierDiagnosticFallsInsideIt()
    {
        using var rig = new TemporaryRig();
        File.AppendAllText(Path.Combine(rig.Root, "agents", "impl", "agent.yaml"),
            "guidance:\n  - MISSING.md\n  - {bad: guidance}\n" +
            "skills:\n  - MISSING_SKILL\n  - {bad: skill}\n");

        var result = RigLoader.Load(rig.Root, null);

        Assert.Equal(2, result.Diagnostics.Count);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal("AIK2004", diagnostic.Code));
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code.StartsWith("AIK300", StringComparison.Ordinal));
    }

    [Fact]
    public void AcceptsExistingCultureGuidanceAndSkillFilesWithoutDiagnostics()
    {
        using var rig = new TemporaryRig();
        File.AppendAllText(Path.Combine(rig.Root, "rig.yaml"), "culture_file: culture.md\n");
        File.WriteAllText(Path.Combine(rig.Root, "culture.md"), "# Culture\n");
        File.AppendAllText(Path.Combine(rig.Root, "agents", "impl", "agent.yaml"),
            "guidance:\n  - guidance.md\nskills:\n  - skills/build\n");
        File.WriteAllText(Path.Combine(rig.Root, "agents", "impl", "guidance.md"), "# Guidance\n");
        var skillDirectory = Path.Combine(rig.Root, "agents", "impl", "skills", "build");
        Directory.CreateDirectory(skillDirectory);
        File.WriteAllText(Path.Combine(skillDirectory, "SKILL.md"), "---\nname: build\ndescription: builds\n---\n");

        var result = RigLoader.Load(rig.Root, null);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ReReadsReferencedContentOnTheNextLoad()
    {
        using var rig = new TemporaryRig();
        File.AppendAllText(Path.Combine(rig.Root, "rig.yaml"), "culture_file: agents/impl/shared.md\n");
        File.AppendAllText(Path.Combine(rig.Root, "agents", "impl", "agent.yaml"), "guidance:\n  - shared.md\n");
        var contentPath = Path.Combine(rig.Root, "agents", "impl", "shared.md");
        File.WriteAllText(contentPath, "safe text\n");

        Assert.Empty(RigLoader.Load(rig.Root, null).Diagnostics);

        File.WriteAllText(contentPath, string.Concat("token: ", "ghp_", "abcdef", "\n"));
        var diagnostic = Assert.Single(RigLoader.Load(rig.Root, null).Diagnostics);
        Assert.Equal("AIK4020", diagnostic.Code);
        Assert.Equal("agents/impl/shared.md", diagnostic.File);
    }

    [Fact]
    public void ReplaysSkillScalarErrorsButEmitsFileDiagnosticsOnce()
    {
        using var rig = new TemporaryRig();
        var agentPath = Path.Combine(rig.Root, "agents", "impl", "agent.yaml");
        File.AppendAllText(agentPath, "skills:\n  - skills/build\n  - ./skills/build/\n");
        var skillDirectory = Path.Combine(rig.Root, "agents", "impl", "skills", "build");
        Directory.CreateDirectory(skillDirectory);
        File.WriteAllText(Path.Combine(skillDirectory, "SKILL.md"), "---\nname: other\ndescription: builds\n---\n");

        var result = RigLoader.Load(rig.Root, null);

        Assert.Null(result.Rig);
        Assert.Equal(
            "agents/impl/skills/build/SKILL.md:1:1: error AIK3004: skill name does not match directory name\n",
            DiagnosticFormatter.Format(result.Diagnostics));
    }

    [Fact]
    public void ReplaysMissingSkillMetadataErrorAtEachDeclaration()
    {
        using var rig = new TemporaryRig();
        var agentPath = Path.Combine(rig.Root, "agents", "impl", "agent.yaml");
        File.AppendAllText(agentPath, "skills:\n  - skills/build\n  - ./skills/build/\n");
        Directory.CreateDirectory(Path.Combine(rig.Root, "agents", "impl", "skills", "build"));

        var result = RigLoader.Load(rig.Root, null);

        Assert.Null(result.Rig);
        Assert.Equal(
            "agents/impl/agent.yaml:8:5: error AIK3004: skill directory has no SKILL.md\n" +
            "agents/impl/agent.yaml:9:5: error AIK3004: skill directory has no SKILL.md\n",
            DiagnosticFormatter.Format(result.Diagnostics));
    }

    private sealed class TemporaryRig : IDisposable
    {
        private static readonly string MinimalRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid", "minimal");

        public TemporaryRig()
        {
            Root = Path.Combine(Path.GetTempPath(), $"aiakos-reference-load-{Guid.NewGuid():N}");
            CopyDirectory(MinimalRoot, Root);
        }

        public string Root { get; }

        public void Dispose() => Directory.Delete(Root, recursive: true);

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            foreach (var directory in Directory.GetDirectories(source))
                CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }
}
