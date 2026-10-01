using System.Text;

namespace Aiakos.Hosting.Wsl;

/// <summary>
/// Decodes output of <c>wsl.exe</c>. Its own messages (for example <c>wsl -l</c> or "distribution
/// not found") are UTF-16LE unless <c>WSL_UTF8=1</c> is set, while output of Linux commands passes
/// through as UTF-8. Both must be readable.
/// </summary>
public static class WslOutput
{
    /// <summary>Decodes <paramref name="bytes"/> as UTF-16LE when it looks like UTF-16LE, otherwise UTF-8.</summary>
    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes[2..]);
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            bytes = bytes[3..];
        }
        else if (LooksLikeUtf16Le(bytes))
        {
            return Encoding.Unicode.GetString(bytes[..(bytes.Length & ~1)]);
        }

        // Stray NULs (a UTF-16 fragment in otherwise UTF-8 output) are never meaningful here.
        return Encoding.UTF8.GetString(bytes).Replace("\0", string.Empty, StringComparison.Ordinal);
    }

    private static bool LooksLikeUtf16Le(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 2)
        {
            return false;
        }

        // ASCII text in UTF-16LE has a NUL in (nearly) every odd position.
        var pairs = bytes.Length / 2;
        var oddNuls = 0;
        for (var i = 1; i < bytes.Length; i += 2)
        {
            if (bytes[i] == 0)
            {
                oddNuls++;
            }
        }

        return oddNuls * 2 > pairs;
    }
}
