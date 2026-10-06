using System.Globalization;

using Aiakos.Core;
using Aiakos.Orchestrator.Harnesses;

namespace Aiakos.Orchestrator.Harnesses.ClaudeCode;

public sealed class ClaudeCodeDelivery
{
    private const string InvalidDelivery = "INVALID_DELIVERY";
    private const string InvalidInput = "INVALID_INPUT";
    private const string InputTooLarge = "INPUT_TOO_LARGE";
    private const string SlashCommandNotAllowed = "SLASH_COMMAND_NOT_ALLOWED";
    private readonly ClaudeCodeStateProfile _profile;

    public ClaudeCodeDelivery(ClaudeCodeStateProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        _profile = profile;
    }

    public DeliverySpec BuildDelivery(string authenticatedSender, string body, Guid commandId)
    {
        ArgumentNullException.ThrowIfNull(authenticatedSender);
        ArgumentNullException.ThrowIfNull(body);

        if (!IsValidSender(authenticatedSender) || commandId == Guid.Empty)
        {
            throw new InvalidOperationException(InvalidDelivery);
        }

        var bodyCheck = InputValidator.CheckBody(body);
        if (bodyCheck.Problem == InputProblem.TooLarge)
        {
            throw new InvalidOperationException(InputTooLarge);
        }

        if (bodyCheck.Problem == InputProblem.NotAllowed)
        {
            throw new InvalidOperationException(InvalidInput);
        }

        var normalizedBody = bodyCheck.Normalized;
        var trimStart = 0;
        var trimEnd = normalizedBody.Length;
        while (trimStart < trimEnd && IsClassificationWhitespace(normalizedBody[trimStart]))
        {
            trimStart++;
        }

        while (trimEnd > trimStart && IsClassificationWhitespace(normalizedBody[trimEnd - 1]))
        {
            trimEnd--;
        }

        if (trimStart < trimEnd && normalizedBody[trimStart] == '/')
        {
            if (!normalizedBody.AsSpan(trimStart, trimEnd - trimStart).SequenceEqual("/compact"))
            {
                throw new InvalidOperationException(SlashCommandNotAllowed);
            }

            return new DeliverySpec(string.Empty, normalizedBody, false, TimeSpan.FromSeconds(5));
        }

        var lead = $"[aiakos from {authenticatedSender} #{commandId.ToString("D", CultureInfo.InvariantCulture)[..8]}] ";
        if (InputValidator.CheckLead(lead).Problem != InputProblem.None)
        {
            throw new InvalidOperationException(InvalidDelivery);
        }

        return new DeliverySpec(lead, normalizedBody, true, _profile.ConfirmTimeout);
    }

    private static bool IsValidSender(string sender)
    {
        if (sender.Length == 0)
        {
            return false;
        }

        for (var index = 0; index < sender.Length; index++)
        {
            var character = sender[index];
            if (char.IsHighSurrogate(character))
            {
                if (index + 1 >= sender.Length || !char.IsLowSurrogate(sender[index + 1]))
                {
                    return false;
                }

                index++;
                continue;
            }

            if (char.IsLowSurrogate(character) || char.IsControl(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsClassificationWhitespace(char character) =>
        character is ' ' or '\t' or '\n';
}
