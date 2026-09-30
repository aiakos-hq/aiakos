namespace Aiakos.Hosting.Wsl;

/// <summary>Reads <c>%USERPROFILE%\.wslconfig</c>; replaceable in tests.</summary>
public interface IWslConfigFile
{
    /// <summary>The file's content, or <see langword="null"/> when it does not exist.</summary>
    string? ReadAllText();
}

/// <summary>The real <c>%USERPROFILE%\.wslconfig</c>.</summary>
public sealed class UserProfileWslConfigFile : IWslConfigFile
{
    /// <inheritdoc />
    public string? ReadAllText()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(profile))
        {
            return null;
        }

        var path = Path.Combine(profile, ".wslconfig");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }
}

/// <summary>Minimal INI parsing of <c>.wslconfig</c> (the RK1 fallback of the preflight).</summary>
public static class WslConfigParser
{
    /// <summary>
    /// Returns the value of <c>networkingMode</c> in section <c>[wsl2]</c>, or
    /// <see langword="null"/> when it is not set.
    /// </summary>
    public static string? GetNetworkingMode(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return null;
        }

        string? value = null;
        var inWsl2 = false;
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] is '#' or ';')
            {
                continue;
            }

            if (line[0] == '[')
            {
                var end = line.IndexOf(']', StringComparison.Ordinal);
                var section = end > 0 ? line[1..end].Trim() : line[1..].Trim();
                inWsl2 = section.Equals("wsl2", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            var eq = line.IndexOf('=', StringComparison.Ordinal);
            if (!inWsl2 || eq <= 0)
            {
                continue;
            }

            if (!line[..eq].Trim().Equals("networkingMode", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var raw = line[(eq + 1)..];
            var comment = raw.IndexOfAny(['#', ';']);
            if (comment >= 0)
            {
                raw = raw[..comment];
            }

            value = raw.Trim().Trim('"').Trim();
        }

        return string.IsNullOrEmpty(value) ? null : value;
    }
}
