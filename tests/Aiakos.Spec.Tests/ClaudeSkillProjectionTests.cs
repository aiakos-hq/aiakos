using System.Security.Cryptography;
using Aiakos.Spec;
using Xunit;

namespace Aiakos.Spec.Tests;

public sealed class ClaudeSkillProjectionTests
{
    [Fact]
    public void MapsEmbeddedSkillsByFrontMatterNameWithExactCopiedBytesAndHashes()
    {
        var root = Path.Combine(Path.GetTempPath(), $"skill-projection-{Guid.NewGuid():N}");
        var skillDirectory = Path.Combine(root, "agents", "worker", "tools");
        Directory.CreateDirectory(Path.Combine(skillDirectory, "sub"));
        byte[] skillBytes =
        [
            0xef, 0xbb, 0xbf,
            (byte)'-', (byte)'-', (byte)'-', (byte)'\r', (byte)'\n',
            (byte)'n', (byte)'a', (byte)'m', (byte)'e', (byte)':', (byte)' ',
            (byte)'d', (byte)'o', (byte)'-', (byte)'w', (byte)'o', (byte)'r', (byte)'k', (byte)'\r', (byte)'\n'
        ];
        byte[] metadataBytes = [0x00, 0xff, 0x2e];
        byte[] unicodeBytes = [0x80, 0x01, 0x00];
        var skillPath = Path.Combine(skillDirectory, "SKILL.md");
        var metadataPath = Path.Combine(skillDirectory, "sub", ".metadata");
        var unicodePath = Path.Combine(skillDirectory, "sub", "e\u0301.data");
        try
        {
            File.WriteAllBytes(skillPath, skillBytes);
            File.WriteAllBytes(metadataPath, metadataBytes);
            File.WriteAllBytes(unicodePath, unicodeBytes);
            var sourceFiles = new[]
            {
                Snapshot("agents/worker/tools/SKILL.md", File.ReadAllBytes(skillPath)),
                Snapshot("agents/worker/tools/sub/.metadata", File.ReadAllBytes(metadataPath)),
                Snapshot("agents/worker/tools/sub/e\u0301.data", File.ReadAllBytes(unicodePath))
            };
            Directory.Delete(root, recursive: true);

            var skill = new ResolvedSkill("agents/worker/tools", "do-work", "ignored", sourceFiles);
            var mapped = ClaudeSkillProjection.Map([skill]);
            var expected = new[]
            {
                Expected(".claude/skills/do-work/SKILL.md", skillBytes),
                Expected(".claude/skills/do-work/sub/.metadata", metadataBytes),
                Expected(".claude/skills/do-work/sub/é.data", unicodeBytes)
            };

            Assert.Equal(expected.Select(file => file.Path), mapped.Select(file => file.Path));
            Assert.Equal(expected.Select(file => file.Sha256), mapped.Select(file => file.Sha256));
            Assert.Equal(expected.Select(file => file.Content), mapped.Select(file => file.Content));
            Assert.Equal(expected.Select(file => file.Bytes), mapped.Select(file => file.Bytes));
            Assert.Equal(skillBytes, sourceFiles[0].Content);
            Assert.Equal(metadataBytes, sourceFiles[1].Content);
            Assert.Equal(unicodeBytes, sourceFiles[2].Content);
            Assert.Equal("sha256:" + HexSha256(skillBytes), mapped[0].Sha256);
            Assert.Equal("sha256:" + HexSha256(metadataBytes), mapped[1].Sha256);
            Assert.Equal("sha256:" + HexSha256(unicodeBytes), mapped[2].Sha256);

            var permutedSkill = skill with { Files = sourceFiles.Reverse().ToArray() };
            var permuted = ClaudeSkillProjection.Map([permutedSkill]);
            Assert.Equal(expected.Select(file => file.Path), permuted.Select(file => file.Path));
            Assert.Equal(expected.Select(file => file.Sha256), permuted.Select(file => file.Sha256));
            Assert.Equal(expected.Select(file => file.Content), permuted.Select(file => file.Content));

            Assert.NotSame(sourceFiles[0].Content, mapped[0].Content);
            Assert.NotSame(mapped[0].Content, permuted[0].Content);
            mapped[0].Content[0] = 0;
            Assert.Equal(skillBytes, sourceFiles[0].Content);
            Assert.Equal(skillBytes, permuted[0].Content);
            sourceFiles[0].Content[1] = 0;
            Assert.Equal(skillBytes, permuted[0].Content);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RetainsDuplicateDestinationPathsForThePlanBuilderToReject()
    {
        var firstBytes = new byte[] { 1, 2 };
        var secondBytes = new byte[] { 3, 4 };
        ResolvedSkill[] skills =
        [
            new("agents/worker/tools", "do-work", "first",
                [Snapshot("agents/worker/tools/SKILL.md", firstBytes)]),
            new("agents/other/custom", "do-work", "second",
                [Snapshot("agents/other/custom/SKILL.md", secondBytes)])
        ];

        var mapped = ClaudeSkillProjection.Map(skills);

        Assert.Equal(2, mapped.Count);
        Assert.Equal(".claude/skills/do-work/SKILL.md", mapped[0].Path);
        Assert.Equal(mapped[0].Path, mapped[1].Path);
        Assert.Equal(new[] { firstBytes, secondBytes }, mapped.Select(file => file.Content));
        Assert.Equal(new[] { Hash(firstBytes), Hash(secondBytes) }, mapped.Select(file => file.Sha256));
    }

    [Fact]
    public void EmptySkillsMapToNoFiles()
    {
        Assert.Empty(ClaudeSkillProjection.Map([]));
    }

    private static EmbeddedFile Snapshot(string path, byte[] content) =>
        new(path, "sha256:source-snapshot-is-not-reused", content.ToArray());

    private static EmbeddedFile Expected(string path, byte[] content) =>
        new(path, Hash(content), content.ToArray());

    private static string Hash(byte[] content) => "sha256:" + HexSha256(content);

    private static string HexSha256(byte[] content) =>
        Convert.ToHexStringLower(SHA256.HashData(content));
}
