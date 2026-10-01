using Aiakos.Spec;

namespace Aiakos.Spec.Tests;

public sealed class ValidTests
{
    private static string MinimalRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures", "valid", "minimal");

    [Fact]
    public void LoadsMinimalRigWithoutDiagnostics()
    {
        var result = RigLoader.Load(MinimalRoot, null);

        Assert.Null(result.Rig);
        Assert.Empty(result.Diagnostics);
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
    public void AcceptsDeferredHarnessAndSecretFields()
    {
        var root = CopyMinimalRig();
        try
        {
            var agentPath = Path.Combine(root, "agents", "impl", "agent.yaml");
            File.AppendAllText(agentPath, "harnesses: {claude-code: {permission_mode: bypassPermissions, hooks: {}, statusLine: x, apiKeyHelper: x, env: {A: b}}}\n");
            var envPath = Path.Combine(root, "rig.env.yaml");
            File.AppendAllText(envPath, "secrets: {api_key: {value: abc}}\n");

            Assert.Empty(RigLoader.Load(root, null).Diagnostics);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
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
