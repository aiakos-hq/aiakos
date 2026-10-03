using System.Text;

namespace Aiakos.Core;

public enum InputProblem { None, NotAllowed, TooLarge }

public sealed record LeadCheck(InputProblem Problem, string? Reason);

public sealed record BodyCheck(InputProblem Problem, string? Reason, string Normalized,
    bool LineEndingsNormalized, int Bytes);

public static class InputValidator
{
    public const int MaxLeadBytes = 1024;
    public const int MaxBodyBytes = 1024 * 1024;

    public static LeadCheck CheckLead(string lead)
    {
        ArgumentNullException.ThrowIfNull(lead);
        if (Encoding.UTF8.GetByteCount(lead) > MaxLeadBytes)
        {
            return new LeadCheck(InputProblem.NotAllowed, $"lead exceeds {MaxLeadBytes} UTF-8 bytes");
        }

        var invalid = FindInvalidCharacter(lead, allowNewlines: false);
        if (invalid is not null)
        {
            return new LeadCheck(InputProblem.NotAllowed, invalid);
        }

        if (lead.EndsWith("\\;", StringComparison.Ordinal))
        {
            return new LeadCheck(InputProblem.NotAllowed, "lead ends with an escaped semicolon");
        }

        return new LeadCheck(InputProblem.None, null);
    }

    public static BodyCheck CheckBody(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var normalized = body.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var bytes = Encoding.UTF8.GetByteCount(normalized);
        var invalid = FindInvalidCharacter(normalized, allowNewlines: true);
        if (invalid is not null)
        {
            return new BodyCheck(InputProblem.NotAllowed, invalid, normalized, normalized != body, bytes);
        }

        if (bytes > MaxBodyBytes)
        {
            return new BodyCheck(InputProblem.TooLarge, $"body exceeds {MaxBodyBytes} UTF-8 bytes",
                normalized, normalized != body, bytes);
        }

        return new BodyCheck(InputProblem.None, null, normalized, normalized != body, bytes);
    }

    private static string? FindInvalidCharacter(string value, bool allowNewlines)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsHighSurrogate(character))
            {
                if (index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
                {
                    index++;
                    continue;
                }

                return $"unpaired surrogate at index {index}";
            }

            if (char.IsLowSurrogate(character))
            {
                return $"unpaired surrogate at index {index}";
            }

            if ((character <= '\u001f' && !(allowNewlines && (character is '\n' or '\t'))) ||
                character is '\u007f' or >= '\u0080' and <= '\u009f')
            {
                return $"control character U+{(int)character:X4} at index {index}";
            }
        }

        return null;
    }
}
