using System.Globalization;

namespace Aiakos.Node.Sessions;

public static class TmuxVersion
{
    public static bool TryParse(string text, out int major, out int minor, out string? version)
    {
        major = 0;
        minor = 0;
        version = null;
        if (text is null)
            return false;

        var length = text.Length;
        if (length >= 2 && text[^2] == '\r' && text[^1] == '\n')
            length -= 2;
        else if (length > 0 && text[length - 1] is '\r' or '\n')
            length--;

        var value = text.AsSpan(0, length);
        const string prefix = "tmux ";
        if (!value.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var position = prefix.Length;
        var majorStart = position;
        while (position < value.Length && IsAsciiDigit(value[position]))
            position++;
        if (position == majorStart || position == value.Length || value[position++] != '.')
            return false;

        var minorStart = position;
        while (position < value.Length && IsAsciiDigit(value[position]))
            position++;
        if (position == minorStart)
            return false;

        if (position < value.Length && IsAsciiLetter(value[position]))
            position++;
        if (position != value.Length ||
            !int.TryParse(value[majorStart..(minorStart - 1)], NumberStyles.None, CultureInfo.InvariantCulture,
                out major) ||
            !int.TryParse(value[minorStart..(position - (position > minorStart && IsAsciiLetter(value[position - 1]) ? 1 : 0))],
                NumberStyles.None, CultureInfo.InvariantCulture, out minor))
        {
            major = 0;
            minor = 0;
            return false;
        }

        version = value[prefix.Length..].ToString();
        return true;
    }

    private static bool IsAsciiDigit(char value) => value is >= '0' and <= '9';

    private static bool IsAsciiLetter(char value) => value is >= 'a' and <= 'z' or >= 'A' and <= 'Z';
}
