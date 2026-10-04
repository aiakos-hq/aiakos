using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using Aiakos.Spec;

namespace Aiakos.Spec.Tests;

public sealed class SharedContentReaderTests
{
    private const string FilePath = "agents/impl/GUIDANCE.md";
    private const int MaximumBytes = 262144;

    [Fact]
    public void NormalizesMarkdownToUtf8BytesAndHashesTheNormalizedContent()
    {
        var root = CreateRoot();
        try
        {
            var withBomAndNewlines = CreateFile(root, "with-bom.md", [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("A\r\nB\rC\n")]);
            var withLf = CreateFile(root, "with-lf.md", Encoding.UTF8.GetBytes("A\nB\nC\n"));
            var withoutFinalNewline = CreateFile(root, "without-final-newline.md", Encoding.UTF8.GetBytes("A"));
            var empty = CreateFile(root, "empty.md", []);

            var firstDiagnostics = new List<Diagnostic>();
            var first = SharedContentReader.ReadMarkdown(withBomAndNewlines, firstDiagnostics);
            var second = SharedContentReader.ReadMarkdown(withLf, []);
            var third = SharedContentReader.ReadMarkdown(withoutFinalNewline, []);
            var fourth = SharedContentReader.ReadMarkdown(empty, []);

            Assert.NotNull(first);
            Assert.Equal(FilePath, first.Path);
            Assert.Equal("A\nB\nC\n", Encoding.UTF8.GetString(first.Content));
            Assert.Equal(6, first.Bytes);
            Assert.Equal(first.Sha256, second?.Sha256);
            Assert.Equal("A", Encoding.UTF8.GetString(third!.Content));
            Assert.Equal(1, third.Bytes);
            Assert.Empty(fourth!.Content);
            Assert.Equal(0, fourth.Bytes);
            Assert.Matches(new Regex("^sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant), first.Sha256);
            Assert.Equal("sha256:" + Convert.ToHexString(SHA256.HashData(first.Content)).ToLowerInvariant(), first.Sha256);
            Assert.Empty(firstDiagnostics);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnforcesRawByteLimitBeforeReadingAndReportsInvalidUtf8OrRemovedFiles()
    {
        var root = CreateRoot();
        try
        {
            var boundary = CreateFile(root, "boundary.md", Enumerable.Repeat((byte)'a', MaximumBytes).ToArray());
            var oversized = CreateFile(root, "oversized.md", Enumerable.Repeat((byte)'a', MaximumBytes + 1).ToArray());
            var sparsePath = Path.Combine(root, "sparse.md");
            using (var sparse = new FileStream(sparsePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                sparse.SetLength(3L * 1024 * 1024 * 1024);
            var invalidUtf8 = CreateFile(root, "invalid-utf8.md", [0xC3, 0x28]);
            var removed = CreateFile(root, "removed.md", Encoding.UTF8.GetBytes("removed"));
            File.Delete(removed.AbsolutePath);

            Assert.Equal(MaximumBytes, SharedContentReader.ReadMarkdown(boundary, [])?.Bytes);
            Assert.Equal("agents/impl/GUIDANCE.md:1:1: error AIK3005: referenced file is larger than 256 KiB\n",
                FormatFor(oversized, expectedCode: "AIK3005"));
            Assert.Equal("agents/impl/GUIDANCE.md:1:1: error AIK3005: referenced file is larger than 256 KiB\n",
                FormatFor(new SharedPath(sparsePath, FilePath), expectedCode: "AIK3005"));
            Assert.Equal("agents/impl/GUIDANCE.md:1:1: error AIK1004: file is not valid UTF-8\n",
                FormatFor(invalidUtf8, expectedCode: "AIK1004"));
            Assert.Equal("agents/impl/GUIDANCE.md:1:1: error AIK3001: referenced file or directory not found\n",
                FormatFor(removed, expectedCode: "AIK3001"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ScansMarkdownLinesWithSharedCredentialPrecedenceWithoutEchoingMatches()
    {
        var root = CreateRoot();
        try
        {
            var examples = new[]
            {
                ("sk-ant-" + "ABCDEF", "Anthropic API key"),
                ("ghp_" + "ABCDEF", "GitHub token"),
                ("xoxb-" + "ABCDEF", "Slack token"),
                ("-----BEGIN " + "RSA PRIVATE KEY-----", "private key"),
                ("ssh://" + "git@github.com/org/repo.git", "URL with user info"),
            };

            foreach (var (secret, kind) in examples)
            {
                var file = CreateFile(root, "credential.md", [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("first\r\n" + secret + "\r")]);
                var diagnostics = new List<Diagnostic>();
                Assert.Null(SharedContentReader.ReadMarkdown(file, diagnostics));
                Assert.Equal($"agents/impl/GUIDANCE.md:2:1: error AIK4020: credential-like value ({kind})\n" +
                    "  hint: never put secrets in rig files; name the secret and bind it in rig.env.yaml\n",
                    DiagnosticFormatter.Format(diagnostics));
                Assert.DoesNotContain(secret, DiagnosticFormatter.Format(diagnostics), StringComparison.Ordinal);
            }

            var github = "ghp_" + "ABCDEF";
            var slack = "xoxb-" + "ABCDEF";
            var multipleKinds = CreateFile(root, "multiple.md", Encoding.UTF8.GetBytes(slack + " " + github));
            var multipleDiagnostics = new List<Diagnostic>();
            Assert.Null(SharedContentReader.ReadMarkdown(multipleKinds, multipleDiagnostics));
            Assert.Equal("agents/impl/GUIDANCE.md:1:13: error AIK4020: credential-like value (GitHub token)\n" +
                "  hint: never put secrets in rig files; name the secret and bind it in rig.env.yaml\n",
                DiagnosticFormatter.Format(multipleDiagnostics));

            var lookalikes = new[]
            {
                "xghp_" + "example",
                "rotate the " + "ghp_" + " token",
                "sk-ant-",
                "https://example.com/a@b",
            };
            foreach (var lookalike in lookalikes)
            {
                var file = CreateFile(root, "lookalike.md", Encoding.UTF8.GetBytes(lookalike));
                var diagnostics = new List<Diagnostic>();
                Assert.NotNull(SharedContentReader.ReadMarkdown(file, diagnostics));
                Assert.Empty(diagnostics);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string FormatFor(SharedPath file, string expectedCode)
    {
        var diagnostics = new List<Diagnostic>();
        Assert.Null(SharedContentReader.ReadMarkdown(file, diagnostics));
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == expectedCode);
        return DiagnosticFormatter.Format(diagnostics);
    }

    private static SharedPath CreateFile(string root, string name, byte[] bytes)
    {
        var absolutePath = Path.Combine(root, name);
        File.WriteAllBytes(absolutePath, bytes);
        return new SharedPath(absolutePath, FilePath);
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"aiakos-content-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }
}
