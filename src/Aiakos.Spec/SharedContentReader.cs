using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace Aiakos.Spec;

internal static class SharedContentReader
{
    private const int MaximumFileBytes = 262144;
    private const string CredentialHint = "never put secrets in rig files; name the secret and bind it in rig.env.yaml";
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);

    internal static EmbeddedFile? ReadMarkdown(SharedPath file, ICollection<Diagnostic> diagnostics)
    {
        byte[] bytes;
        try
        {
            var fileInfo = new FileInfo(file.AbsolutePath);
            if (fileInfo.Length > MaximumFileBytes)
                return TooLarge(file, diagnostics);

            using var stream = new FileStream(file.AbsolutePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.SequentialScan);
            if (stream.Length > MaximumFileBytes)
                return TooLarge(file, diagnostics);

            bytes = new byte[MaximumFileBytes + 1];
            var total = 0;
            while (total < bytes.Length)
            {
                var count = stream.Read(bytes, total, bytes.Length - total);
                if (count == 0)
                    break;
                total += count;
            }

            if (total > MaximumFileBytes)
                return TooLarge(file, diagnostics);

            if (total != bytes.Length)
                Array.Resize(ref bytes, total);
        }
        catch (FileNotFoundException)
        {
            return Missing(file, diagnostics);
        }
        catch (DirectoryNotFoundException)
        {
            return Missing(file, diagnostics);
        }
        catch (IOException)
        {
            return Missing(file, diagnostics);
        }
        catch (UnauthorizedAccessException)
        {
            return Missing(file, diagnostics);
        }
        catch (ArgumentException)
        {
            return Missing(file, diagnostics);
        }
        catch (NotSupportedException)
        {
            return Missing(file, diagnostics);
        }
        catch (SecurityException)
        {
            return Missing(file, diagnostics);
        }

        var textBytes = bytes.AsSpan();
        if (textBytes.StartsWith(Utf8Bom))
            textBytes = textBytes[Utf8Bom.Length..];

        string text;
        try
        {
            text = StrictUtf8.GetString(textBytes);
        }
        catch (DecoderFallbackException)
        {
            diagnostics.Add(new Diagnostic(Severity.Error, "AIK1004", file.Path, 1, 1, "file is not valid UTF-8", null));
            return null;
        }

        text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var containsCredential = false;
        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (SemanticValidator.FindCredential(lines[index]) is not { } match)
                continue;

            containsCredential = true;
            diagnostics.Add(new Diagnostic(Severity.Error, "AIK4020", file.Path, index + 1, match.Index + 1,
                $"credential-like value ({match.Kind})", CredentialHint));
        }

        if (containsCredential)
            return null;

        var content = Utf8WithoutBom.GetBytes(text);
        var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        return new EmbeddedFile(file.Path, $"sha256:{hash}", content);
    }

    private static EmbeddedFile? TooLarge(SharedPath file, ICollection<Diagnostic> diagnostics)
    {
        diagnostics.Add(new Diagnostic(Severity.Error, "AIK3005", file.Path, 1, 1,
            "referenced file is larger than 256 KiB", null));
        return null;
    }

    private static EmbeddedFile? Missing(SharedPath file, ICollection<Diagnostic> diagnostics)
    {
        diagnostics.Add(new Diagnostic(Severity.Error, "AIK3001", file.Path, 1, 1,
            "referenced file or directory not found", null));
        return null;
    }
}
