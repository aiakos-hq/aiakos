namespace Aiakos.Node.Harnesses.ClaudeCode;

public static class ClaudeScreenClassifier
{
    private static readonly string[][] ScreenNeedles =
    [
        ["Is this a project you trust?", "Is this a project you created or one you trust?"],
        ["Resume session"],
        ["Not logged in"],
        ["Detected a custom API key"],
        ["Error parsing settings", "Invalid settings"]
    ];

    private static readonly string[] ScreenLabels =
    ["trust-dialog", "resume-picker", "login-required", "api-key-dialog", "settings-error"];

    public static string Classify(string capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        string plain = StripAnsiCsi(capture);
        for (int i = 0; i < ScreenNeedles.Length; i++)
        {
            foreach (string needle in ScreenNeedles[i])
            {
                if (plain.Contains(needle, StringComparison.OrdinalIgnoreCase))
                {
                    return ScreenLabels[i];
                }
            }
        }

        return "unrecognized";
    }

    public static string ClassifyExitReason(string capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        string plain = StripAnsiCsi(capture);
        if (plain.Contains("No conversation found", StringComparison.OrdinalIgnoreCase))
        {
            return "RESUME_SESSION_NOT_FOUND";
        }

        if (plain.Contains("is already in use", StringComparison.OrdinalIgnoreCase))
        {
            return "SESSION_ID_IN_USE";
        }

        return "HARNESS_EXITED";
    }

    private static string StripAnsiCsi(string capture)
    {
        var result = new System.Text.StringBuilder(capture.Length);
        for (int i = 0; i < capture.Length; i++)
        {
            if (capture[i] == '\u001b' && i + 1 < capture.Length && capture[i + 1] == '[')
            {
                i += 2;
                while (i < capture.Length && (capture[i] < '@' || capture[i] > '~'))
                {
                    i++;
                }

                continue;
            }

            result.Append(capture[i]);
        }

        return result.ToString();
    }
}
