using Aiakos.Spec;

namespace Aiakos.Spec.Tests;

public sealed class SharedReferencePathsTests
{
    [Fact]
    public void NormalizesSafePathsAndRejectsUnsafePathsWithExactDiagnostic()
    {
        var root = CreateRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "agents", "impl"));
            File.WriteAllText(Path.Combine(root, "agents", "impl", "GUIDANCE.md"), "ok");
            var source = new ReferenceSource("agents/impl/agent.yaml", 7, 5);

            var firstDiagnostics = new List<Diagnostic>();
            var first = SharedReferencePaths.Resolve(root, "agents/impl", "./../impl//GUIDANCE.md", SharedReferenceKind.File, source, firstDiagnostics);
            Assert.Equal("agents/impl/GUIDANCE.md", first?.Path);
            Assert.Empty(firstDiagnostics);

            var secondDiagnostics = new List<Diagnostic>();
            var second = SharedReferencePaths.Resolve(root, "", "agents/impl/./", SharedReferenceKind.Directory, source, secondDiagnostics);
            Assert.Equal("agents/impl", second?.Path);
            Assert.Empty(secondDiagnostics);

            foreach (var unsafePath in new[] { "", "/tmp/file", "//host/file", "C:/x", "~/x", "a\\b", "bad\0path", "../../../outside" })
            {
                var diagnostics = new List<Diagnostic>();
                Assert.Null(SharedReferencePaths.Resolve(root, "agents/impl", unsafePath, SharedReferenceKind.File, source, diagnostics));
                Assert.Equal("agents/impl/agent.yaml:7:5: error AIK3002: invalid shared path\n  hint: use a relative / path inside the rig root without symbolic links\n",
                    DiagnosticFormatter.Format(diagnostics));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReportsMissingAndWrongKindWithExactDiagnostic()
    {
        var root = CreateRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "folder"));
            File.WriteAllText(Path.Combine(root, "file.txt"), "ok");
            var source = new ReferenceSource("rig.yaml", 10, 16);
            foreach (var (path, kind) in new[] { ("missing", SharedReferenceKind.File), ("folder", SharedReferenceKind.File), ("file.txt", SharedReferenceKind.Directory) })
            {
                var diagnostics = new List<Diagnostic>();
                Assert.Null(SharedReferencePaths.Resolve(root, "", path, kind, source, diagnostics));
                Assert.Equal("rig.yaml:10:16: error AIK3001: referenced file or directory not found\n", DiagnosticFormatter.Format(diagnostics));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RejectsExistingAndDanglingLinksBeforeMissingOrKindDiagnostics()
    {
        var root = CreateRoot();
        var outside = CreateRoot();
        try
        {
            File.WriteAllText(Path.Combine(outside, "secret.txt"), "do not read");
            File.CreateSymbolicLink(Path.Combine(root, "linked-file"), Path.Combine(outside, "secret.txt"));
            Directory.CreateSymbolicLink(Path.Combine(root, "linked-directory"), outside);
            File.CreateSymbolicLink(Path.Combine(root, "dangling"), Path.Combine(outside, "missing.txt"));
            var source = new ReferenceSource("agents/impl/agent.yaml", 7, 5);
            foreach (var (path, kind) in new[]
                     {
                         ("linked-file", SharedReferenceKind.File),
                         ("linked-file/child", SharedReferenceKind.File),
                         ("linked-directory/secret.txt", SharedReferenceKind.File),
                         ("dangling", SharedReferenceKind.File)
                     })
            {
                var diagnostics = new List<Diagnostic>();
                Assert.Null(SharedReferencePaths.Resolve(root, "", path, kind, source, diagnostics));
                Assert.Equal("agents/impl/agent.yaml:7:5: error AIK3002: invalid shared path\n  hint: use a relative / path inside the rig root without symbolic links\n",
                    DiagnosticFormatter.Format(diagnostics));
            }
            Assert.Equal("do not read", File.ReadAllText(Path.Combine(outside, "secret.txt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void AgentDiscoveryUsesSafeReferencesAndDeduplicatesCanonicalDirectories()
    {
        var root = CreateRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "agents", "impl"));
            File.WriteAllText(Path.Combine(root, "agents", "impl", "agent.yaml"), "name: impl\ndescription: test\ndefaults:\n  harness: claude-code\n");
            File.WriteAllText(Path.Combine(root, "rig.yaml"), "name: demo\ndescription: test\nrepos: {}\nseats:\n  - id: impl\n    kind: agent\n    agent_ref: local:agents/x/../impl\n    harness: claude-code\n  - id: other\n    kind: agent\n    agent_ref: local:agents/impl\n    harness: claude-code\n");
            File.WriteAllText(Path.Combine(root, "rig.env.yaml"), "rig: demo\nplacement: {}\nrepos: {}\nsecrets: {}\n");
            Assert.DoesNotContain(RigLoader.Load(root, null).Diagnostics, diagnostic => diagnostic.Code.StartsWith("AIK300", StringComparison.Ordinal));

            File.WriteAllText(Path.Combine(root, "rig.yaml"), "name: demo\ndescription: test\nrepos: {}\nseats:\n  - id: impl\n    kind: agent\n    agent_ref: local:agents/missing\n    harness: claude-code\n");
            var result = RigLoader.Load(root, null);
            var pathDiagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "AIK3001");
            Assert.Equal(("rig.yaml", 7, 16), (pathDiagnostic.File, pathDiagnostic.Line, pathDiagnostic.Column));
            Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == "AIK1001" && diagnostic.File.StartsWith("agents/", StringComparison.Ordinal));

            Directory.CreateDirectory(Path.Combine(root, "agents", "linked"));
            Directory.Delete(Path.Combine(root, "agents", "linked"));
            File.CreateSymbolicLink(Path.Combine(root, "agents", "linked"), Path.Combine(root, "agents", "absent"));
            File.WriteAllText(Path.Combine(root, "rig.yaml"), "name: demo\ndescription: test\nrepos: {}\nseats:\n  - id: impl\n    kind: agent\n    agent_ref: local:agents/linked\n    harness: claude-code\n");
            var linkedResult = RigLoader.Load(root, null);
            Assert.Equal("AIK3002", Assert.Single(linkedResult.Diagnostics, diagnostic => diagnostic.Code.StartsWith("AIK300", StringComparison.Ordinal)).Code);
            Assert.DoesNotContain(linkedResult.Diagnostics, diagnostic => diagnostic.Code == "AIK1001");

            File.Delete(Path.Combine(root, "agents", "linked"));
            Directory.CreateDirectory(Path.Combine(root, "agents", "without-agent-file"));
            File.WriteAllText(Path.Combine(root, "rig.yaml"), "name: demo\ndescription: test\nrepos: {}\nseats:\n  - id: impl\n    kind: agent\n    agent_ref: local:agents/without-agent-file\n    harness: claude-code\n");
            var fileMissingResult = RigLoader.Load(root, null);
            Assert.Equal(("AIK3001", "rig.yaml", 7, 16), Assert.Single(fileMissingResult.Diagnostics, diagnostic => diagnostic.Code == "AIK3001") is { } missing
                ? (missing.Code, missing.File, missing.Line, missing.Column) : default);
            Assert.DoesNotContain(fileMissingResult.Diagnostics, diagnostic => diagnostic.Code == "AIK1001");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aiakos-path-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
