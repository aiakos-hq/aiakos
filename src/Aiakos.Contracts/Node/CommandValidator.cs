using Aiakos.Contracts.Node.V1;
using System.Text;

namespace Aiakos.Contracts.Node;

public static class CommandValidator
{
    public static Error? Validate(Command command, IReadOnlyCollection<string> capabilities)
    {
        switch (command.BodyCase)
        {
            case Command.BodyOneofCase.None:
                return ErrorReasons.Create(ErrorReasons.Unsupported, "Command body is unsupported.");
            case Command.BodyOneofCase.SendKeys:
                if (!capabilities.Contains(NodeCapabilities.CommandSendKeys, StringComparer.Ordinal) ||
                    command.SendKeys.Keys.Any(key => !SendKeyNames.IsAllowed(key)))
                {
                    return ErrorReasons.Create(ErrorReasons.Unsupported, "Send-keys is unsupported.");
                }

                return null;
            case Command.BodyOneofCase.StartSeat:
                return ValidateStart(command.StartSeat, capabilities);
            case Command.BodyOneofCase.DeliverInput:
                if (Encoding.UTF8.GetByteCount(command.DeliverInput.Body) > ContractLimits.MaxDeliverBodyBytes)
                {
                    return ErrorReasons.Create(ErrorReasons.PayloadTooLarge, "Deliver body exceeds the 1048576-byte limit.");
                }

                return null;
            case Command.BodyOneofCase.CapturePane:
            case Command.BodyOneofCase.StopSeat:
                return null;
            default:
                return ErrorReasons.Create(ErrorReasons.Unsupported, "Command body is unsupported.");
        }
    }

    private static Error? ValidateStart(StartSeat start, IReadOnlyCollection<string> capabilities)
    {
        if (!capabilities.Contains(NodeCapabilities.ForHarness(start.Harness), StringComparer.Ordinal))
        {
            return ErrorReasons.Create(ErrorReasons.Unsupported, "Harness capability is unsupported.");
        }

        if (start.Mode is not (LaunchMode.Fresh or LaunchMode.Resume or LaunchMode.Fork))
        {
            return ErrorReasons.Create(ErrorReasons.Unsupported, "Launch mode is unsupported.");
        }

        if (start.Mode == LaunchMode.Fork && !capabilities.Contains(NodeCapabilities.LaunchFork, StringComparer.Ordinal))
        {
            return ErrorReasons.Create(ErrorReasons.Unsupported, "Fork launch is unsupported.");
        }

        foreach (SeatSecret secret in start.Secrets)
        {
            if (secret.TargetCase == SeatSecret.TargetOneofCase.None)
            {
                return ErrorReasons.Create(ErrorReasons.Unsupported, "Secret target is required.");
            }
        }

        foreach (SeatFile file in start.Files)
        {
            if (file.Root is not (FileRoot.SeatHome or FileRoot.Workspace) || !SeatPaths.IsAllowed(file.Path))
            {
                return ErrorReasons.Create(ErrorReasons.PathNotAllowed, "Seat file path is not allowed.");
            }
        }

        foreach (SeatSecret secret in start.Secrets)
        {
            if (secret.TargetCase == SeatSecret.TargetOneofCase.FilePath && !SeatPaths.IsAllowed(secret.FilePath))
            {
                return ErrorReasons.Create(ErrorReasons.PathNotAllowed, "Secret file path is not allowed.");
            }
        }

        long bytes = start.Files.Sum(file => (long)file.Content.Length);
        return bytes > ContractLimits.MaxStartSeatFilesBytes
            ? ErrorReasons.Create(ErrorReasons.PayloadTooLarge, "Seat files exceed the 2097152-byte limit.")
            : null;
    }
}
