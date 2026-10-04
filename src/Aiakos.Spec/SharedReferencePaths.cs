namespace Aiakos.Spec;

internal enum SharedReferenceKind { File, Directory }

internal sealed record ReferenceSource(string File, int Line, int Column);

internal sealed record SharedPath(string AbsolutePath, string Path);

internal static class SharedReferencePaths
{
    private const string UnsafeMessage = "invalid shared path";
    private const string UnsafeHint = "use a relative / path inside the rig root without symbolic links";
    private const string MissingMessage = "referenced file or directory not found";
    private const string CredentialHint = "never put secrets in rig files; name the secret and bind it in rig.env.yaml";

    internal static SharedPath? Resolve(string rigRoot, string ownerDirectory, string value,
        SharedReferenceKind kind, ReferenceSource source, ICollection<Diagnostic> diagnostics)
    {
        if (SemanticValidator.CredentialKind(value) is { } credentialKind)
        {
            diagnostics.Add(new Diagnostic(Severity.Error, "AIK4020", source.File, source.Line, source.Column,
                $"credential-like value ({credentialKind})", CredentialHint));
            return null;
        }

        if (!TryNormalize(ownerDirectory, value, out var relativePath))
            return InvalidPath(source, diagnostics);

        string rootPath;
        string absolutePath;
        try
        {
            rootPath = Path.GetFullPath(string.IsNullOrEmpty(rigRoot) ? "." : rigRoot);
            var nativeRelative = relativePath.Replace('/', Path.DirectorySeparatorChar);
            absolutePath = Path.GetFullPath(Path.Combine(rootPath, nativeRelative));
            var containedRelative = Path.GetRelativePath(rootPath, absolutePath);
            if (Path.IsPathRooted(containedRelative) || IsParentPath(containedRelative))
                return InvalidPath(source, diagnostics);
        }
        catch (ArgumentException)
        {
            return InvalidPath(source, diagnostics);
        }
        catch (NotSupportedException)
        {
            AddInvalid(source, diagnostics);
            return null;
        }
        catch (IOException)
        {
            AddMissing(source, diagnostics);
            return null;
        }

        if (!InspectPath(rootPath, absolutePath, relativePath, kind, source, diagnostics))
            return null;

        return new SharedPath(absolutePath, relativePath);
    }

    private static bool TryNormalize(string ownerDirectory, string value, out string relativePath)
    {
        relativePath = string.Empty;
        if (string.IsNullOrEmpty(value) || value.Contains('\0') ||
            value.Contains('\\') || value.StartsWith('/') ||
            value.StartsWith('~') || IsDrivePath(value))
            return false;

        var segments = new List<string>();
        if (!string.IsNullOrEmpty(ownerDirectory))
        {
            if (ownerDirectory.StartsWith('/') ||
                ownerDirectory.Contains('\\') ||
                ownerDirectory.Contains('\0'))
                return false;
            foreach (var ownerSegment in ownerDirectory.Split('/'))
            {
                if (ownerSegment.Length == 0 || ownerSegment == ".")
                    continue;
                if (ownerSegment == "..")
                {
                    if (segments.Count == 0)
                        return false;
                    segments.RemoveAt(segments.Count - 1);
                }
                else
                {
                    segments.Add(ownerSegment);
                }
            }
        }

        foreach (var segment in value.Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
                continue;
            if (segment == "..")
            {
                if (segments.Count == 0)
                    return false;
                segments.RemoveAt(segments.Count - 1);
            }
            else
            {
                segments.Add(segment);
            }
        }

        relativePath = string.Join('/', segments);
        return true;
    }

    private static bool InspectPath(string rootPath, string absolutePath, string relativePath,
        SharedReferenceKind kind, ReferenceSource source, ICollection<Diagnostic> diagnostics)
    {
        var currentPath = rootPath;
        var components = relativePath.Length == 0 ? Array.Empty<string>() : relativePath.Split('/');
        for (var index = 0; index < components.Length; index++)
        {
            currentPath = Path.Combine(currentPath, components[index]);
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(currentPath);
            }
            catch (FileNotFoundException)
            {
                AddMissing(source, diagnostics);
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                AddMissing(source, diagnostics);
                return false;
            }
            catch (IOException)
            {
                AddMissing(source, diagnostics);
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                AddMissing(source, diagnostics);
                return false;
            }
            catch (ArgumentException)
            {
                AddMissing(source, diagnostics);
                return false;
            }
            catch (NotSupportedException)
            {
                AddMissing(source, diagnostics);
                return false;
            }
            catch (System.Security.SecurityException)
            {
                AddMissing(source, diagnostics);
                return false;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                AddInvalid(source, diagnostics);
                return false;
            }

            var isDirectory = (attributes & FileAttributes.Directory) != 0;
            if (index < components.Length - 1 && !isDirectory)
            {
                AddMissing(source, diagnostics);
                return false;
            }

            if (index == components.Length - 1 &&
                (kind == SharedReferenceKind.Directory) != isDirectory)
            {
                AddMissing(source, diagnostics);
                return false;
            }
        }

        if (components.Length == 0 && kind == SharedReferenceKind.File)
        {
            AddMissing(source, diagnostics);
            return false;
        }

        return true;
    }

    private static bool IsDrivePath(string path) =>
        path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':';

    private static bool IsParentPath(string path) =>
        path == ".." || path.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
        path.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);

    private static SharedPath? InvalidPath(ReferenceSource source, ICollection<Diagnostic> diagnostics)
    {
        AddInvalid(source, diagnostics);
        return null;
    }

    private static void AddInvalid(ReferenceSource source, ICollection<Diagnostic> diagnostics)
    {
        diagnostics.Add(new Diagnostic(Severity.Error, "AIK3002", source.File, source.Line, source.Column,
            UnsafeMessage, UnsafeHint));
    }

    private static void AddMissing(ReferenceSource source, ICollection<Diagnostic> diagnostics)
    {
        diagnostics.Add(new Diagnostic(Severity.Error, "AIK3001", source.File, source.Line, source.Column,
            MissingMessage, null));
    }
}
