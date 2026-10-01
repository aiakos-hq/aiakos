using System.Text.RegularExpressions;
using Aiakos.Core;

namespace Aiakos.AppHost;

/// <summary>
/// The dev-side half of the bootstrap rule (spec 0001 R18, R46, ADR 0008): the dev AppHost refuses
/// to start on the released tool's instance name, WSL home or ports, and on incomplete
/// configuration (rule 3: no silent defaults). Every message names the offending key.
/// </summary>
public static partial class InstanceGuard
{
    /// <summary>Validates <paramref name="options"/>; throws <see cref="InstanceGuardException"/> on the first problem.</summary>
    public static void EnsureNotReleased(AiakosDevOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.Instance))
        {
            throw Refuse("Aiakos:Instance", "is not set.");
        }

        if (!InstanceNamePattern().IsMatch(options.Instance))
        {
            throw Refuse("Aiakos:Instance", $"is '{options.Instance}'; use lowercase letters, digits and '-' (it names the Postgres volume).");
        }

        if (string.Equals(options.Instance, InstanceDefaults.ReleasedInstance, StringComparison.OrdinalIgnoreCase))
        {
            throw Refuse("Aiakos:Instance", $"is '{options.Instance}', which is reserved for the released tool. Use another instance name (default '{InstanceDefaults.DevInstance}').");
        }

        if (options.PortBase is < 1024 or > 65535 - InstanceDefaults.HookIngestPortOffset)
        {
            throw Refuse("Aiakos:PortBase", $"is {options.PortBase}; it must be between 1024 and {65535 - InstanceDefaults.HookIngestPortOffset}.");
        }

        int[] ours = [options.GrpcPort, options.ApiPort, options.HookIngestPort];
        int[] released =
        [
            InstanceDefaults.ReleasedPortBase + InstanceDefaults.GrpcPortOffset,
            InstanceDefaults.ReleasedPortBase + InstanceDefaults.ApiPortOffset,
            InstanceDefaults.ReleasedPortBase + InstanceDefaults.PostgresPortOffset,
            InstanceDefaults.ReleasedPortBase + InstanceDefaults.HookIngestPortOffset,
        ];
        if (ours.Intersect(released).Any())
        {
            throw Refuse("Aiakos:PortBase", $"is {options.PortBase}, whose ports collide with the released tool's port base {InstanceDefaults.ReleasedPortBase} (ports {string.Join(", ", released)}). Use another port base (default {InstanceDefaults.DevPortBase}).");
        }

        var home = options.Wsl.Home?.Trim() ?? string.Empty;
        if (home.Length == 0)
        {
            throw Refuse("Aiakos:Wsl:Home", "is not set.");
        }

        if (home.StartsWith('/'))
        {
            throw Refuse("Aiakos:Wsl:Home", $"is '{home}'; it must be relative to the WSL user's home (an absolute home is out of scope for M1).");
        }

        if (NormalizeHome(home) == NormalizeHome(InstanceDefaults.ReleasedWslHome))
        {
            throw Refuse("Aiakos:Wsl:Home", $"is '{home}', which is the released tool's WSL home. Use another directory (default '{InstanceDefaults.DevWslHome}').");
        }

        var windowsHome = options.WindowsHome?.Trim() ?? string.Empty;
        if (windowsHome.Length == 0)
        {
            throw Refuse("Aiakos:WindowsHome", "is not set.");
        }

        if (Path.IsPathRooted(windowsHome) || windowsHome.Split('/', '\\').Contains(".."))
        {
            throw Refuse("Aiakos:WindowsHome", $"is '{windowsHome}'; it must be a directory under %USERPROFILE%.");
        }

        if (string.Equals(windowsHome.Replace('\\', '/').TrimEnd('/'), InstanceDefaults.ReleasedWindowsHome, StringComparison.OrdinalIgnoreCase))
        {
            throw Refuse("Aiakos:WindowsHome", $"is '{windowsHome}', which is the released tool's Windows home. Use another directory (default '{InstanceDefaults.DevWindowsHome}').");
        }

        if (string.IsNullOrWhiteSpace(options.Wsl.Distro))
        {
            throw Refuse("Aiakos:Wsl:Distro", "is not set.");
        }

        if (string.IsNullOrWhiteSpace(options.Wsl.NodeId))
        {
            throw Refuse("Aiakos:Wsl:NodeId", "is not set.");
        }
    }

    private static string NormalizeHome(string home)
    {
        var normalized = home.Trim();
        if (normalized.StartsWith("~/", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized.TrimEnd('/');
    }

    private static InstanceGuardException Refuse(string key, string problem) =>
        new(key, $"Refusing to start the dev AppHost: {key} {problem} The dev stack must never share an instance with the released tool (bootstrap rule, ADR 0008).");

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,62}$")]
    private static partial Regex InstanceNamePattern();
}

/// <summary>The AppHost configuration is refused by <see cref="InstanceGuard"/>.</summary>
public sealed class InstanceGuardException : Exception
{
    public InstanceGuardException()
    {
    }

    public InstanceGuardException(string message)
        : base(message)
    {
    }

    public InstanceGuardException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public InstanceGuardException(string key, string message)
        : base(message)
    {
        Key = key;
    }

    /// <summary>The configuration key that caused the refusal.</summary>
    public string? Key { get; }
}
