using System.Text;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Aiakos.Spec;

public static class RigLoader
{
    private const int MaximumFileBytes = 262144;

    public static LoadResult Load(string rigRoot, string? envPath)
    {
        var diagnostics = new List<Diagnostic>();
        var root = rigRoot ?? "";
        var rigPath = Path.Combine(root, "rig.yaml");
        var rigNode = LoadFile(rigPath, GetDisplayPath(root, rigPath), RigFileKind.Rig, diagnostics);

        var agentPaths = new HashSet<string>(PathComparer());
        if (rigNode is { Kind: YamlNodeKind.Mapping })
        {
            foreach (var seats in Values(rigNode, "seats"))
            {
                if (seats.Kind != YamlNodeKind.Sequence)
                {
                    continue;
                }

                foreach (var seat in seats.Items)
                {
                    var reference = Values(seat, "agent_ref").FirstOrDefault();
                    if (reference is not { Kind: YamlNodeKind.Scalar, IsNull: false, Value: { } text } || !text.StartsWith("local:", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var agentDirectory = text["local:".Length..];
                    if (IsUnsafeAgentPath(agentDirectory))
                    {
                        continue;
                    }

                    string directoryPath;
                    try
                    {
                        directoryPath = Path.GetFullPath(Path.Combine(root, agentDirectory));
                    }
                    catch (ArgumentException)
                    {
                        continue;
                    }
                    catch (NotSupportedException)
                    {
                        continue;
                    }
                    catch (IOException)
                    {
                        continue;
                    }

                    if (!agentPaths.Add(directoryPath))
                    {
                        continue;
                    }

                    var agentFile = Path.Combine(directoryPath, "agent.yaml");
                    LoadFile(agentFile, GetDisplayPath(root, agentFile), RigFileKind.Agent, diagnostics);
                }
            }
        }

        var actualEnvPath = envPath is null ? Path.Combine(root, "rig.env.yaml") : envPath;
        var envDisplayPath = envPath is null ? GetDisplayPath(root, actualEnvPath) : GetEnvDisplayPath(root, envPath);
        LoadFile(actualEnvPath, envDisplayPath, RigFileKind.RigEnv, diagnostics);
        return new LoadResult(null, diagnostics);
    }

    private static YamlNode? LoadFile(string path, string displayPath, RigFileKind kind, List<Diagnostic> allDiagnostics)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (FileNotFoundException)
        {
            allDiagnostics.Add(new Diagnostic(Severity.Error, "AIK1001", displayPath, 1, 1, "file not found", null));
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            allDiagnostics.Add(new Diagnostic(Severity.Error, "AIK1001", displayPath, 1, 1, "file not found", null));
            return null;
        }
        catch (IOException)
        {
            allDiagnostics.Add(new Diagnostic(Severity.Error, "AIK1001", displayPath, 1, 1, "file not found", null));
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            allDiagnostics.Add(new Diagnostic(Severity.Error, "AIK1001", displayPath, 1, 1, "file not found", null));
            return null;
        }
        catch (ArgumentException)
        {
            allDiagnostics.Add(new Diagnostic(Severity.Error, "AIK1001", displayPath, 1, 1, "file not found", null));
            return null;
        }
        catch (NotSupportedException)
        {
            allDiagnostics.Add(new Diagnostic(Severity.Error, "AIK1001", displayPath, 1, 1, "file not found", null));
            return null;
        }
        catch (System.Security.SecurityException)
        {
            allDiagnostics.Add(new Diagnostic(Severity.Error, "AIK1001", displayPath, 1, 1, "file not found", null));
            return null;
        }

        if (bytes.Length > MaximumFileBytes)
        {
            allDiagnostics.Add(new Diagnostic(Severity.Error, "AIK1004", displayPath, 1, 1, "file is larger than 256 KiB", null));
            return null;
        }

        string text;
        try
        {
            var offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            text = new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
        }
        catch (DecoderFallbackException)
        {
            allDiagnostics.Add(new Diagnostic(Severity.Error, "AIK1004", displayPath, 1, 1, "file is not valid UTF-8", null));
            return null;
        }

        var forbiddenDiagnostics = new List<Diagnostic>();
        YamlNode? document;
        Parser? parser = null;
        try
        {
            using var reader = new StringReader(text);
            parser = new Parser(reader);
            var treeParser = new YamlEventTreeParser(parser, text, (code, mark, message) => forbiddenDiagnostics.Add(new Diagnostic(Severity.Error, code, displayPath, Math.Max(1, (int)mark.Line), Math.Max(1, (int)mark.Column), message, null)));
            document = treeParser.Parse();
        }
        catch (YamlException exception)
        {
            allDiagnostics.Add(new Diagnostic(Severity.Error, "AIK1002", displayPath, Math.Max(1, (int)exception.Start.Line), Math.Max(1, (int)exception.Start.Column), $"YAML syntax error: {exception.Message}", null));
            return null;
        }
        catch (Exception exception)
        {
            var mark = parser?.Current?.Start ?? Mark.Empty;
            allDiagnostics.Add(new Diagnostic(Severity.Error, "AIK1002", displayPath, Math.Max(1, (int)mark.Line), Math.Max(1, (int)mark.Column), $"YAML syntax error: {exception.Message}", null));
            return null;
        }

        var validationDiagnostics = new List<Diagnostic>();
        new SchemaValidator(displayPath, kind, validationDiagnostics.Add).Validate(document);
        allDiagnostics.AddRange(forbiddenDiagnostics.Concat(validationDiagnostics).OrderBy(diagnostic => diagnostic.Line).ThenBy(diagnostic => diagnostic.Column));
        return document;
    }

    private static IEnumerable<YamlNode> Values(YamlNode? mapping, string key)
    {
        if (mapping?.Kind != YamlNodeKind.Mapping)
        {
            yield break;
        }

        var found = false;
        foreach (var entry in mapping.Entries)
        {
            if (!found && entry.Key.Kind == YamlNodeKind.Scalar && entry.Key.Value == key)
            {
                found = true;
                yield return entry.Value;
            }
        }
    }

    private static bool IsUnsafeAgentPath(string path)
    {
        if (string.IsNullOrEmpty(path) || Path.IsPathRooted(path) || path.Contains('\\', StringComparison.Ordinal))
        {
            return true;
        }

        return path.Split('/').Any(segment => segment == "..");
    }

    private static string GetDisplayPath(string root, string path)
    {
        try
        {
            return Path.GetRelativePath(Path.GetFullPath(root.Length == 0 ? "." : root), Path.GetFullPath(path)).Replace('\\', '/');
        }
        catch (ArgumentException)
        {
            return path.Replace('\\', '/');
        }
        catch (NotSupportedException)
        {
            return path.Replace('\\', '/');
        }
        catch (IOException)
        {
            return path.Replace('\\', '/');
        }
    }

    private static string GetEnvDisplayPath(string root, string envPath)
    {
        try
        {
            var relativePath = Path.GetRelativePath(Path.GetFullPath(root.Length == 0 ? "." : root), Path.GetFullPath(envPath));
            if (!Path.IsPathRooted(relativePath) && relativePath != ".." && !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                return relativePath.Replace('\\', '/');
            }
        }
        catch (ArgumentException)
        {
        }
        catch (NotSupportedException)
        {
        }
        catch (IOException)
        {
        }

        return envPath.Replace('\\', '/');
    }

    private static StringComparer PathComparer() => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
