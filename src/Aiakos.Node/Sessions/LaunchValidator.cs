using System.Text;
using Aiakos.Core;

namespace Aiakos.Node.Sessions;

public interface ILaunchFiles
{
    bool ExecutableExists(string path);
    bool DirectoryExists(string path);
}

public static class LaunchValidator
{
    private const int MaxPackedBytes = 12288;
    private const int MaxAttributeBytes = 4096;

    public static SessionHostError? Validate(SessionSpec spec, ILaunchFiles files)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(files);

        if (!TmuxNames.TryParseAddress(spec.SeatAddress, out _, out _) ||
            string.IsNullOrEmpty(spec.SeatId) || string.IsNullOrEmpty(spec.LaunchId) ||
            string.IsNullOrEmpty(spec.Harness) || !IsAbsolute(spec.SeatHome))
        {
            return Invalid("SeatAddress, SeatId, LaunchId, Harness and SeatHome must be valid.");
        }

        if (spec.Argv.Count == 0)
        {
            return Invalid("Argv must contain an executable.");
        }

        if (!IsAbsolute(spec.Argv[0]) || !files.ExecutableExists(spec.Argv[0]))
        {
            return Invalid("Argv[0] must be an existing absolute executable.");
        }

        foreach (var argument in spec.Argv)
        {
            if (argument.Contains('\0') || argument.EndsWith(';'))
            {
                return Invalid("Argv contains an argument with a forbidden value.");
            }
        }

        if (!IsAbsolute(spec.WorkingDirectory) || !files.DirectoryExists(spec.WorkingDirectory))
        {
            return Invalid("WorkingDirectory must be an existing absolute directory.");
        }

        foreach (var (name, value) in spec.Environment)
        {
            if (!IsValidEnvironmentName(name))
            {
                return Invalid("Environment contains a name outside the required format.");
            }

            if (value.Contains('\0') || value.Contains('\n') || value.Contains('\r'))
            {
                return Invalid($"Environment value for {name} contains a forbidden character.");
            }

            if (name is "TMUX" or "TMUX_PANE" || name.StartsWith("AIAKOS_NODE_", StringComparison.Ordinal))
            {
                return Invalid($"Environment name {name} is reserved.");
            }

            if (IsSensitiveName(name) && !(name.EndsWith("_FILE", StringComparison.Ordinal) &&
                                           IsSeatHomeFile(value, spec.SeatHome)))
            {
                return Invalid($"Environment name {name} may not carry a secret value.");
            }
        }

        if (spec.Size.Columns is < 80 or > 500 || spec.Size.Rows is < 24 or > 200)
        {
            return Invalid("Size must be between 80–500 columns and 24–200 rows.");
        }

        var packedBytes = spec.Argv.Sum(value => Encoding.UTF8.GetByteCount(value) + 1) +
                          spec.Environment.Sum(pair => Encoding.UTF8.GetByteCount(pair.Key) + 1 +
                                                       Encoding.UTF8.GetByteCount(pair.Value) + 1);
        if (packedBytes > MaxPackedBytes)
        {
            return new SessionHostError(SessionHostErrorCode.PayloadTooLarge,
                $"Argv and Environment exceed {MaxPackedBytes} packed bytes.", false);
        }

        var attributeBytes = spec.Attributes.Sum(pair => Encoding.UTF8.GetByteCount(pair.Key) +
                                                           Encoding.UTF8.GetByteCount(pair.Value));
        if (attributeBytes > MaxAttributeBytes)
        {
            return new SessionHostError(SessionHostErrorCode.PayloadTooLarge,
                $"Attributes exceed {MaxAttributeBytes} UTF-8 bytes.", false);
        }

        return null;
    }

    private static SessionHostError Invalid(string message) =>
        new(SessionHostErrorCode.InvalidArgument, message, false);

    private static bool IsAbsolute(string path) => path.Length > 0 && path[0] == '/';

    private static bool IsValidEnvironmentName(string name)
    {
        if (name.Length == 0 || !(name[0] is >= 'A' and <= 'Z' or '_'))
        {
            return false;
        }

        return name.All(character => character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_');
    }

    private static bool IsSensitiveName(string name) =>
        name.Contains("TOKEN", StringComparison.Ordinal) || name.Contains("SECRET", StringComparison.Ordinal) ||
        name.Contains("PASSWORD", StringComparison.Ordinal) || name.Contains("API_KEY", StringComparison.Ordinal);

    private static bool IsSeatHomeFile(string value, string seatHome)
    {
        if (!value.StartsWith(seatHome + "/", StringComparison.Ordinal))
        {
            return false;
        }

        return !value[(seatHome.Length + 1)..].Split('/').Contains("..", StringComparer.Ordinal);
    }
}
