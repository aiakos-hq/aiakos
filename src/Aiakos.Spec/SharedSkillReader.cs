using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

using YamlDotNet.Core;

namespace Aiakos.Spec;

internal static class SharedSkillReader
{
    private const int MaximumFiles = 100;
    private const long MaximumBytes = 1_048_576;
    private const string CredentialHint = "never put secrets in rig files; name the secret and bind it in rig.env.yaml";
    private const string UnsafePathHint = "use a relative / path inside the rig root without symbolic links";
    private const string MissingMessage = "referenced file or directory not found";
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly Regex SkillName = new("^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant);

    internal static ResolvedSkill? Read(string rigRoot, SharedPath directory, ReferenceSource source,
        ICollection<Diagnostic> diagnostics)
    {
        var entries = new List<SkillFile>();
        var directoryStack = new Stack<(string Absolute, string Relative)>();
        directoryStack.Push((directory.AbsolutePath, string.Empty));
        var discoveredDiagnostics = new List<Diagnostic>();
        var unsafeNames = new List<(string Path, string Kind)>();
        var fileCount = 0;
        long metadataBytes = 0;

        while (directoryStack.TryPop(out var current))
        {
            IEnumerator<string> enumerator;
            try
            {
                enumerator = Directory.EnumerateFileSystemEntries(current.Absolute).GetEnumerator();
            }
            catch (Exception ex) when (IsExpectedIoFailure(ex))
            {
                discoveredDiagnostics.Add(MissingDiagnostic(source));
                continue;
            }

            using (enumerator)
            {
                while (true)
                {
                    string entryPath;
                    try
                    {
                        if (!enumerator.MoveNext())
                            break;
                        entryPath = enumerator.Current;
                    }
                    catch (Exception ex) when (IsExpectedIoFailure(ex))
                    {
                        discoveredDiagnostics.Add(MissingDiagnostic(source));
                        break;
                    }

                    var name = Path.GetFileName(entryPath);
                    var relative = current.Relative.Length == 0 ? name : $"{current.Relative}/{name}";
                    var canonicalRelative = relative.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
                    var credentialKind = SemanticValidator.CredentialKind(canonicalRelative);

                    FileAttributes attributes;
                    try
                    {
                        attributes = File.GetAttributes(entryPath);
                    }
                    catch (Exception ex) when (IsExpectedIoFailure(ex))
                    {
                        discoveredDiagnostics.Add(MissingDiagnostic(source));
                        continue;
                    }

                    if (credentialKind is not null)
                    {
                        unsafeNames.Add((canonicalRelative, credentialKind));
                        if ((attributes & FileAttributes.Directory) == 0 && (attributes & FileAttributes.ReparsePoint) == 0)
                        {
                            fileCount++;
                            if (fileCount > MaximumFiles)
                                return TooLarge(source, diagnostics);
                            if (!TryGetLength(entryPath, out var unsafeLength))
                            {
                                discoveredDiagnostics.Add(MissingDiagnostic(source));
                                continue;
                            }
                            if (unsafeLength > MaximumBytes - metadataBytes)
                                return TooLarge(source, diagnostics);
                            metadataBytes += unsafeLength;
                        }
                        continue;
                    }

                    var isDirectory = (attributes & FileAttributes.Directory) != 0;
                    var isReparsePoint = (attributes & FileAttributes.ReparsePoint) != 0;
                    var pathDiagnostics = new List<Diagnostic>();
                    var checkedPath = SharedReferencePaths.Resolve(rigRoot, directory.Path, canonicalRelative,
                        isDirectory ? SharedReferenceKind.Directory : SharedReferenceKind.File, source, pathDiagnostics);
                    if (checkedPath is null)
                    {
                        discoveredDiagnostics.AddRange(pathDiagnostics);
                        continue;
                    }

                    if (isReparsePoint)
                    {
                        // Resolve has already rejected the link; this branch is defensive if a platform
                        // reports a reparse point without the shared path checker seeing it.
                        discoveredDiagnostics.Add(new Diagnostic(Severity.Error, "AIK3002", source.File,
                            source.Line, source.Column, "invalid shared path", UnsafePathHint));
                        continue;
                    }

                    if (isDirectory)
                    {
                        directoryStack.Push((checkedPath.AbsolutePath, canonicalRelative));
                        continue;
                    }

                    fileCount++;
                    if (fileCount > MaximumFiles)
                        return TooLarge(source, diagnostics);

                    if (!TryGetLength(checkedPath.AbsolutePath, out var length))
                    {
                        discoveredDiagnostics.Add(MissingDiagnostic(source));
                        continue;
                    }

                    if (length > MaximumBytes - metadataBytes)
                        return TooLarge(source, diagnostics);
                    metadataBytes += length;
                    entries.Add(new SkillFile(checkedPath, canonicalRelative, length));
                }
            }
        }

        entries.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));
        var skillFile = entries.FirstOrDefault(static entry => entry.RelativePath == "SKILL.md");
        var localDiagnostics = new List<Diagnostic>();
        var resolvedFiles = new List<EmbeddedFile>(entries.Count);
        var totalRead = 0L;
        string? nameValue = null;
        string? descriptionValue = null;

        foreach (var entry in entries)
        {
            if (!TryReadBounded(entry.Path.AbsolutePath, MaximumBytes - totalRead, out var bytes))
            {
                if (bytes is null)
                    discoveredDiagnostics.Add(MissingDiagnostic(source));
                else
                    return TooLarge(source, diagnostics);
                continue;
            }

            var fileBytes = bytes!;
            totalRead += fileBytes.Length;
            if (totalRead > MaximumBytes)
                return TooLarge(source, diagnostics);

            string? decoded = null;
            try
            {
                var content = fileBytes.AsSpan();
                if (content.StartsWith(Utf8Bom))
                    content = content[3..];
                decoded = StrictUtf8.GetString(content);
            }
            catch (DecoderFallbackException)
            {
                if (entry.RelativePath == "SKILL.md")
                    localDiagnostics.Add(InvalidFrontMatter(entry.Path.Path));
            }

            if (decoded is not null)
            {
                var normalized = decoded.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
                ScanCredentials(entry.Path.Path, normalized, localDiagnostics);
                if (entry.RelativePath == "SKILL.md")
                {
                    if (!TryParseFrontMatter(normalized, out nameValue, out descriptionValue))
                        localDiagnostics.Add(InvalidFrontMatter(entry.Path.Path));
                    else if (!string.Equals(nameValue, Path.GetFileName(directory.AbsolutePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), StringComparison.Ordinal))
                        localDiagnostics.Add(new Diagnostic(Severity.Error, "AIK3004", entry.Path.Path, 1, 1,
                            "skill name does not match directory name", null));
                }
            }

            var hash = Convert.ToHexString(SHA256.HashData(fileBytes)).ToLowerInvariant();
            resolvedFiles.Add(new EmbeddedFile(entry.Path.Path, $"sha256:{hash}", fileBytes));
        }

        if (skillFile is null)
            localDiagnostics.Add(new Diagnostic(Severity.Error, "AIK3004", source.File, source.Line, source.Column,
                "skill directory has no SKILL.md", null));

        if (unsafeNames.Count > 0)
        {
            var first = unsafeNames.OrderBy(static item => item.Path, StringComparer.Ordinal).First();
            diagnostics.Add(new Diagnostic(Severity.Error, "AIK4020", source.File, source.Line, source.Column,
                $"credential-like value ({first.Kind})", CredentialHint));
        }
        else
        {
            var invalid = discoveredDiagnostics.FirstOrDefault(static diagnostic => diagnostic.Code == "AIK3002");
            var missing = discoveredDiagnostics.FirstOrDefault(static diagnostic => diagnostic.Code == "AIK3001");
            if (invalid is not null) diagnostics.Add(invalid);
            if (missing is not null) diagnostics.Add(missing);
        }

        foreach (var diagnostic in localDiagnostics
                     .OrderBy(static diagnostic => diagnostic.File, StringComparer.Ordinal)
                     .ThenBy(static diagnostic => diagnostic.Line)
                     .ThenBy(static diagnostic => diagnostic.Column))
            diagnostics.Add(diagnostic);

        if (discoveredDiagnostics.Count > 0 || unsafeNames.Count > 0 || localDiagnostics.Count > 0 || skillFile is null ||
            resolvedFiles.Count != entries.Count)
            return null;

        return new ResolvedSkill(directory.Path, nameValue!, descriptionValue!, resolvedFiles);
    }

    private static bool TryParseFrontMatter(string text, out string? name, out string? description)
    {
        name = null;
        description = null;
        var lines = text.Split('\n');
        if (lines.Length < 3 || lines[0] != "---")
            return false;

        var closing = Array.FindIndex(lines, 1, static line => line == "---");
        if (closing < 0)
            return false;

        var yaml = string.Join('\n', lines[1..closing]);
        var metadataError = false;
        YamlNode? root;
        try
        {
            using var reader = new StringReader(yaml);
            var parser = new Parser(reader);
            var treeParser = new YamlEventTreeParser(parser, yaml, (_, _, _) => metadataError = true);
            root = treeParser.Parse();
        }
        catch (YamlException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        if (metadataError || root is not { Kind: YamlNodeKind.Mapping, IsTaggedOrAlias: false })
            return false;

        var nameNode = GetScalar(root, "name");
        var descriptionNode = GetScalar(root, "description");
        if (nameNode is null || descriptionNode is null || string.IsNullOrWhiteSpace(nameNode) ||
            string.IsNullOrWhiteSpace(descriptionNode) || !SkillName.IsMatch(nameNode))
            return false;

        name = nameNode;
        description = descriptionNode;
        return true;
    }

    private static string? GetScalar(YamlNode mapping, string key)
    {
        var entry = mapping.Entries.FirstOrDefault(item => item.Key is { Kind: YamlNodeKind.Scalar, IsTaggedOrAlias: false, Value: var value } && value == key);
        return entry?.Value is { Kind: YamlNodeKind.Scalar, IsNull: false, IsTaggedOrAlias: false, Value: { } value }
            ? value
            : null;
    }

    private static void ScanCredentials(string file, string text, List<Diagnostic> diagnostics)
    {
        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (SemanticValidator.FindCredential(lines[index]) is not { } match)
                continue;
            diagnostics.Add(new Diagnostic(Severity.Error, "AIK4020", file, index + 1, match.Index + 1,
                $"credential-like value ({match.Kind})", CredentialHint));
        }
    }

    private static bool TryGetLength(string path, out long length)
    {
        try
        {
            length = new FileInfo(path).Length;
            return true;
        }
        catch (Exception ex) when (IsExpectedIoFailure(ex))
        {
            length = 0;
            return false;
        }
    }

    private static bool TryReadBounded(string path, long allowance, out byte[]? content)
    {
        content = null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.SequentialScan);
            using var buffer = new MemoryStream((int)Math.Min(stream.Length, allowance));
            var chunk = new byte[81920];
            while (true)
            {
                var limit = (int)Math.Min(chunk.Length, allowance + 1 - buffer.Length);
                if (limit <= 0)
                {
                    content = [];
                    return false;
                }
                var read = stream.Read(chunk, 0, limit);
                if (read == 0)
                    break;
                if (buffer.Length + read > allowance)
                {
                    content = [];
                    return false;
                }
                buffer.Write(chunk, 0, read);
            }
            content = buffer.ToArray();
            return true;
        }
        catch (Exception ex) when (IsExpectedIoFailure(ex))
        {
            return false;
        }
    }

    private static bool IsExpectedIoFailure(Exception exception) => exception is IOException or UnauthorizedAccessException or
        ArgumentException or NotSupportedException or SecurityException;

    private static ResolvedSkill? TooLarge(ReferenceSource source, ICollection<Diagnostic> diagnostics)
    {
        diagnostics.Add(new Diagnostic(Severity.Error, "AIK3005", source.File, source.Line, source.Column,
            "skill exceeds 100 files or 1 MiB", null));
        return null;
    }

    private static Diagnostic InvalidFrontMatter(string file) =>
        new(Severity.Error, "AIK3004", file, 1, 1, "invalid skill front matter", null);

    private static Diagnostic MissingDiagnostic(ReferenceSource source) =>
        new(Severity.Error, "AIK3001", source.File, source.Line, source.Column, MissingMessage, null);

    private sealed record SkillFile(SharedPath Path, string RelativePath, long Length);
}
