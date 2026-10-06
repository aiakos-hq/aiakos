using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Aiakos.Spec;

public sealed record ProjectionFile(string Path, string Sha256, int Bytes, string Mode);

public sealed record ResolvedProjection(string Root, string Hash,
    IReadOnlyList<ProjectionFile> Files, IReadOnlyDictionary<string, byte[]> Contents);

internal static class ProjectionPlanBuilder
{
    private const string InvalidPathMessage = "invalid projection file path";
    private const string OverlapMessage = "projection root overlaps a repository checkout";
    private const string TooLargeMessage = "projection exceeds 2 MiB";
    private const long MaximumBytes = 2_097_152;

    internal static ResolvedProjection? Build(string root, IReadOnlyList<EmbeddedFile> files,
        IReadOnlyList<string> checkoutPaths, ReferenceSource source, ICollection<Diagnostic> diagnostics)
    {
        var destinations = new List<(string Path, byte[] Content)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var path = Normalize(file.Path);
            if (!IsSafeDestination(path) || !seen.Add(path))
            {
                AddError(diagnostics, source, "AIK3002", InvalidPathMessage);
                return null;
            }

            destinations.Add((path, file.Content));
        }

        destinations.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));

        var normalizedRoot = NormalizeNodePath(root);
        foreach (var checkoutPath in checkoutPaths)
        {
            if (Overlaps(normalizedRoot, NormalizeNodePath(checkoutPath)))
            {
                AddError(diagnostics, source, "AIK3002", OverlapMessage);
                return null;
            }
        }

        long totalBytes = 0;
        foreach (var destination in destinations)
        {
            totalBytes += destination.Content.Length;
        }

        if (totalBytes > MaximumBytes)
        {
            AddError(diagnostics, source, "AIK3005", TooLargeMessage);
            return null;
        }

        var descriptors = new List<ProjectionFile>(destinations.Count);
        var contentByHash = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var destination in destinations)
        {
            var hash = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(destination.Content));
            descriptors.Add(new ProjectionFile(destination.Path, hash, destination.Content.Length, "0644"));
            if (!contentByHash.ContainsKey(hash))
            {
                contentByHash.Add(hash, destination.Content.ToArray());
            }
        }

        var canonicalDescriptors = new StringBuilder("[");
        for (var index = 0; index < descriptors.Count; index++)
        {
            if (index > 0)
            {
                canonicalDescriptors.Append(',');
            }

            var descriptor = descriptors[index];
            canonicalDescriptors.Append("{\"bytes\":")
                .Append(descriptor.Bytes.ToString(CultureInfo.InvariantCulture))
                .Append(",\"mode\":\"0644\",\"path\":")
                .Append(JsonSerializer.Serialize(descriptor.Path))
                .Append(",\"sha256\":")
                .Append(JsonSerializer.Serialize(descriptor.Sha256))
                .Append('}');
        }

        canonicalDescriptors.Append(']');
        using var descriptorDocument = JsonDocument.Parse(canonicalDescriptors.ToString());
        var canonicalJson = CanonicalJson.Write(descriptorDocument.RootElement);
        var planHash = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalJson)));

        return new ResolvedProjection(root, planHash, descriptors.ToArray(), contentByHash);
    }

    private static string Normalize(string value)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
        using var normalizedDocument = JsonDocument.Parse(CanonicalJson.Write(document.RootElement));
        return normalizedDocument.RootElement.GetString()!;
    }

    private static bool IsSafeDestination(string path)
    {
        if (path.Length == 0 || path[0] == '/' || path.Contains('\\') || path.Contains('\0') ||
            path.Contains('\r') || path.Contains('\n') || path.Contains(':'))
        {
            return false;
        }

        foreach (var component in path.Split('/'))
        {
            if (component.Length == 0 || component is "." or "..")
            {
                return false;
            }
        }

        return true;
    }

    private static NodePath NormalizeNodePath(string path)
    {
        var anchor = path.StartsWith("~/", StringComparison.Ordinal) || path == "~"
            ? "~"
            : path.StartsWith('/') ? "/" : string.Empty;
        var remainder = anchor switch
        {
            "~" => path.Length == 1 ? string.Empty : path[2..],
            "/" => path[1..],
            _ => path
        };
        var components = new List<string>();
        foreach (var component in remainder.Split('/'))
        {
            if (component.Length == 0 || component == ".")
            {
                continue;
            }

            if (component == "..")
            {
                if (components.Count > 0)
                {
                    components.RemoveAt(components.Count - 1);
                }

                continue;
            }

            components.Add(component);
        }

        return new NodePath(anchor, components);
    }

    private static bool Overlaps(NodePath left, NodePath right)
    {
        if (!string.Equals(left.Anchor, right.Anchor, StringComparison.Ordinal))
        {
            return false;
        }

        var commonLength = Math.Min(left.Components.Count, right.Components.Count);
        for (var index = 0; index < commonLength; index++)
        {
            if (!string.Equals(left.Components[index], right.Components[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static void AddError(ICollection<Diagnostic> diagnostics, ReferenceSource source, string code, string message) =>
        diagnostics.Add(new Diagnostic(Severity.Error, code, source.File, source.Line, source.Column, message, null));

    private sealed record NodePath(string Anchor, IReadOnlyList<string> Components);
}
