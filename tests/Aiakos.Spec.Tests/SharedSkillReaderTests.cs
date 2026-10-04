using System.Security.Cryptography;
using System.Text;

using Aiakos.Spec;

namespace Aiakos.Spec.Tests;

public sealed class SharedSkillReaderTests
{
    private const string SkillPath = "agents/impl/skills/build";
    private static readonly string[] InvalidForms =
    [
        "name: build\ndescription: Build things\n",
        "---\nname: build\n---\n",
        "---\nname: Build\ndescription: Build things\n---\n",
        "---\nname: build\nname: build\ndescription: Build things\n---\n",
        "---\nname: build\ndescription: Build things\nalias: &x value\n---\n",
        "---\nname: build\ndescription: Build things\ncopy: *missing\n---\n",
        "---\nname: build\ndescription: Build things\n<<: {x: y}\n---\n",
        "---\nname: build\ndescription: Build things\ncustom: !tag value\n---\n",
    ];
    private static readonly ReferenceSource Source = new("agents/impl/agent.yaml", 8, 5);

    [Fact]
    public void SKILLValidPreservesRawBytesAndReturnsOrdinalFilesWithRawHashes()
    {
        WithRoot(root =>
        {
            var directory = CreateSkill(root, "---\r\nname: build\r\ndescription: Build things\r\nextra: accepted\r\n---\r\nBody\r\n");
            byte[] skillBytes = [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("---\r\nname: build\r\ndescription: Build things\r\nextra: accepted\r\n---\r\nBody\r\n")];
            File.WriteAllBytes(Path.Combine(directory.AbsolutePath, "SKILL.md"), skillBytes);
            Directory.CreateDirectory(Path.Combine(directory.AbsolutePath, "assets"));
            File.WriteAllBytes(Path.Combine(directory.AbsolutePath, "assets", "blob.bin"), [0x00, 0xFF, 0x01]);
            File.WriteAllBytes(Path.Combine(directory.AbsolutePath, ".note"), [0x7F]);

            var diagnostics = new List<Diagnostic>();
            var skill = SharedSkillReader.Read(root, directory, Source, diagnostics);

            Assert.NotNull(skill);
            Assert.Empty(diagnostics);
            Assert.Equal((SkillPath, "build", "Build things"), (skill.Directory, skill.Name, skill.Description));
            Assert.Equal(["agents/impl/skills/build/.note", "agents/impl/skills/build/SKILL.md", "agents/impl/skills/build/assets/blob.bin"],
                skill.Files.Select(file => file.Path));
            AssertRaw(skill.Files[0], [0x7F]);
            AssertRaw(skill.Files[1], skillBytes);
            AssertRaw(skill.Files[2], [0x00, 0xFF, 0x01]);
        });
    }

    [Fact]
    public void SKILLMissingReportsAtTheDeclarationAndInvalidFrontMatterUsesFixedDiagnostic()
    {
        WithRoot(root =>
        {
            var directory = CreateDirectory(root);
            var missingDiagnostics = new List<Diagnostic>();
            Assert.Null(SharedSkillReader.Read(root, directory, Source, missingDiagnostics));
            Assert.Equal("agents/impl/agent.yaml:8:5: error AIK3004: skill directory has no SKILL.md\n",
                DiagnosticFormatter.Format(missingDiagnostics));

            foreach (var frontMatter in InvalidForms)
            {
                File.WriteAllText(Path.Combine(directory.AbsolutePath, "SKILL.md"), frontMatter, new UTF8Encoding(false));
                var diagnostics = new List<Diagnostic>();
                Assert.Null(SharedSkillReader.Read(root, directory, Source, diagnostics));
                Assert.Equal("agents/impl/skills/build/SKILL.md:1:1: error AIK3004: invalid skill front matter\n",
                    DiagnosticFormatter.Format(diagnostics));
            }

            File.WriteAllText(Path.Combine(directory.AbsolutePath, "SKILL.md"), "---\nname: other\ndescription: Build things\n---\n");
            var mismatch = new List<Diagnostic>();
            Assert.Null(SharedSkillReader.Read(root, directory, Source, mismatch));
            Assert.Equal("agents/impl/skills/build/SKILL.md:1:1: error AIK3004: skill name does not match directory name\n",
                DiagnosticFormatter.Format(mismatch));
        });
    }

    [Fact]
    public void SKILLLimitsCountFilesAndRawBytesAndOverrideOtherSkillDiagnostics()
    {
        WithRoot(root =>
        {
            var directory = CreateSkill(root, ValidFrontMatter);
            for (var index = 0; index < 99; index++)
                File.WriteAllBytes(Path.Combine(directory.AbsolutePath, $"f{index:D3}"), [0x01]);
            Assert.NotNull(SharedSkillReader.Read(root, directory, Source, [])); // SKILL.md plus 99 files = 100.

            File.WriteAllBytes(Path.Combine(directory.AbsolutePath, "f099"), [0x01]);
            AssertSkillTooLarge(root, directory);

            foreach (var file in Directory.EnumerateFiles(directory.AbsolutePath)) File.Delete(file);
            var skillBytes = Encoding.UTF8.GetBytes(ValidFrontMatter);
            File.WriteAllBytes(Path.Combine(directory.AbsolutePath, "SKILL.md"), skillBytes);
            File.WriteAllBytes(Path.Combine(directory.AbsolutePath, "support.bin"), new byte[1_048_576 - skillBytes.Length]);
            Assert.NotNull(SharedSkillReader.Read(root, directory, Source, []));
            using (var support = new FileStream(Path.Combine(directory.AbsolutePath, "support.bin"), FileMode.Open, FileAccess.Write))
                support.SetLength(1_048_577 - skillBytes.Length);
            AssertSkillTooLarge(root, directory);

            using (var sparse = new FileStream(Path.Combine(directory.AbsolutePath, "sparse"), FileMode.Create, FileAccess.Write))
                sparse.SetLength(3L * 1024 * 1024 * 1024);
            AssertSkillTooLarge(root, directory);
        });
    }

    [Fact]
    public void SKILLLinksAndCredentialLikeFilenamesAreCoalescedWithoutEchoes()
    {
        Assert.SkipUnless(!OperatingSystem.IsWindows(), "Symbolic link creation is not available without Windows link privilege.");
        WithRoot(root =>
        {
            var directory = CreateSkill(root, ValidFrontMatter);
            var outside = Path.Combine(Path.GetTempPath(), $"aiakos-skill-outside-{Guid.NewGuid():N}");
            Directory.CreateDirectory(outside);
            try
            {
                File.WriteAllText(Path.Combine(outside, "sentinel.txt"), "not read by the skill reader");
                File.CreateSymbolicLink(Path.Combine(directory.AbsolutePath, "first-link"), Path.Combine(outside, "sentinel.txt"));
                Directory.CreateSymbolicLink(Path.Combine(directory.AbsolutePath, "nested-link"), outside);
                File.CreateSymbolicLink(Path.Combine(directory.AbsolutePath, "dangling-link"), Path.Combine(outside, "missing"));

                var linked = new List<Diagnostic>();
                Assert.Null(SharedSkillReader.Read(root, directory, Source, linked));
                Assert.Equal("agents/impl/agent.yaml:8:5: error AIK3002: invalid shared path\n" +
                    "  hint: use a relative / path inside the rig root without symbolic links\n", DiagnosticFormatter.Format(linked));
                Assert.Equal("not read by the skill reader", File.ReadAllText(Path.Combine(outside, "sentinel.txt")));

                foreach (var entry in Directory.EnumerateFileSystemEntries(directory.AbsolutePath))
                    if (Path.GetFileName(entry) != "SKILL.md") File.Delete(entry);
                var unsafeName = "ghp_" + "ABCDEF";
                File.WriteAllBytes(Path.Combine(directory.AbsolutePath, unsafeName), [0x01]);
                var unsafeDiagnostics = new List<Diagnostic>();
                Assert.Null(SharedSkillReader.Read(root, directory, Source, unsafeDiagnostics));
                Assert.Equal("agents/impl/agent.yaml:8:5: error AIK4020: credential-like value (GitHub token)\n" +
                    "  hint: never put secrets in rig files; name the secret and bind it in rig.env.yaml\n", DiagnosticFormatter.Format(unsafeDiagnostics));
                Assert.DoesNotContain(unsafeName, DiagnosticFormatter.Format(unsafeDiagnostics), StringComparison.Ordinal);
            }
            finally
            {
                Directory.Delete(outside, recursive: true);
            }
        });
    }

    [Fact]
    public void SKILLSecretScanReportsTextPositionAndPreservesInvalidUtf8Binary()
    {
        WithRoot(root =>
        {
            var directory = CreateSkill(root, ValidFrontMatter);
            File.WriteAllBytes(Path.Combine(directory.AbsolutePath, "binary.dat"), [0xC3, 0x28, 0xFF]);
            var token = "ghp_" + "ABCDEF";
            File.WriteAllText(Path.Combine(directory.AbsolutePath, "support.md"), "x\n  " + token + "\n", new UTF8Encoding(false));

            var diagnostics = new List<Diagnostic>();
            Assert.Null(SharedSkillReader.Read(root, directory, Source, diagnostics));
            Assert.Equal("agents/impl/skills/build/support.md:2:3: error AIK4020: credential-like value (GitHub token)\n" +
                "  hint: never put secrets in rig files; name the secret and bind it in rig.env.yaml\n", DiagnosticFormatter.Format(diagnostics));
            Assert.DoesNotContain(token, DiagnosticFormatter.Format(diagnostics), StringComparison.Ordinal);

            File.Delete(Path.Combine(directory.AbsolutePath, "support.md"));
            var noCredential = new List<Diagnostic>();
            var skill = SharedSkillReader.Read(root, directory, Source, noCredential);
            Assert.NotNull(skill);
            Assert.Empty(noCredential);
            AssertRaw(Assert.Single(skill.Files, file => file.Path.EndsWith("binary.dat", StringComparison.Ordinal)), [0xC3, 0x28, 0xFF]);
        });
    }

    private const string ValidFrontMatter = "---\nname: build\ndescription: Build things\n---\n";

    private static SharedPath CreateSkill(string root, string frontMatter)
    {
        var directory = CreateDirectory(root);
        File.WriteAllText(Path.Combine(directory.AbsolutePath, "SKILL.md"), frontMatter, new UTF8Encoding(false));
        return directory;
    }

    private static SharedPath CreateDirectory(string root)
    {
        var absolutePath = Path.Combine(root, SkillPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(absolutePath);
        return new SharedPath(absolutePath, SkillPath);
    }

    private static void AssertSkillTooLarge(string root, SharedPath directory)
    {
        var diagnostics = new List<Diagnostic>();
        Assert.Null(SharedSkillReader.Read(root, directory, Source, diagnostics));
        Assert.Equal("agents/impl/agent.yaml:8:5: error AIK3005: skill exceeds 100 files or 1 MiB\n", DiagnosticFormatter.Format(diagnostics));
    }

    private static void AssertRaw(EmbeddedFile file, byte[] expected)
    {
        Assert.Equal(expected, file.Content);
        Assert.Equal(expected.Length, file.Bytes);
        Assert.Equal("sha256:" + Convert.ToHexString(SHA256.HashData(expected)).ToLowerInvariant(), file.Sha256);
    }

    private static void WithRoot(Action<string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), $"aiakos-skill-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try { action(root); }
        finally { Directory.Delete(root, recursive: true); }
    }
}
