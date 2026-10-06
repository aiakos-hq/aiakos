using System.Security.Cryptography;
using System.Text.Json;

namespace Aiakos.Spec;

internal static class ClaudeSkillProjection
{
    internal static IReadOnlyList<EmbeddedFile> Map(IReadOnlyList<ResolvedSkill> skills)
    {
        var mapped = new List<EmbeddedFile>();
        foreach (var skill in skills)
        {
            var sourcePrefix = skill.Directory + "/";
            foreach (var sourceFile in skill.Files)
            {
                var relativePath = sourceFile.Path[sourcePrefix.Length..];
                var destination = NormalizeNfc($".claude/skills/{skill.Name}/{relativePath}");
                var content = sourceFile.Content.ToArray();
                var hash = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(content));
                mapped.Add(new EmbeddedFile(destination, hash, content));
            }
        }

        return mapped.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
    }

    private static string NormalizeNfc(string value)
    {
        var json = CanonicalJson.Write(System.Text.Json.JsonSerializer.SerializeToElement(value));
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetString()!;
    }
}
