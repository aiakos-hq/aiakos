namespace Aiakos.Core;

using System.IO;

public static class TmuxNames
{
    public static string SocketName(string instance)
    {
        if (!IsValidInstance(instance))
            throw new ArgumentException("Invalid tmux host options.");

        return "aiakos-" + instance;
    }

    public static string ConfigPath(string home)
    {
        if (string.IsNullOrEmpty(home) || home.Contains('\0') || !Path.IsPathFullyQualified(home))
            throw new ArgumentException("Invalid tmux host options.");

        return Path.Combine(home, "tmux", "tmux.conf");
    }

    public static IReadOnlyList<string> AttachCommand(string instance, string seatAddress, bool readOnlyMode = true)
    {
        var socketName = SocketName(instance);
        var sessionName = SessionName(seatAddress);
        return readOnlyMode
            ? ["tmux", "-L", socketName, "attach-session", "-r", "-t", sessionName]
            : ["tmux", "-L", socketName, "attach-session", "-t", sessionName];
    }

    public static bool TryParseAddress(string seatAddress, out string seat, out string rig)
    {
        seat = string.Empty;
        rig = string.Empty;
        if (seatAddress is null)
        {
            return false;
        }

        var separator = seatAddress.IndexOf('@');
        if (separator < 0 || separator != seatAddress.LastIndexOf('@'))
        {
            return false;
        }

        var candidateSeat = seatAddress[..separator];
        var candidateRig = seatAddress[(separator + 1)..];
        if (!IsValidPart(candidateSeat, 1, 24) || !IsValidPart(candidateRig, 2, 40))
        {
            return false;
        }

        seat = candidateSeat;
        rig = candidateRig;
        return true;
    }

    public static string SessionName(string seatAddress)
    {
        if (!TryParseAddress(seatAddress, out var seat, out var rig))
        {
            throw new ArgumentException("SeatAddress must use the valid <seat>@<rig> format.", nameof(seatAddress));
        }

        return $"{rig}_{seat}";
    }

    private static bool IsValidPart(string value, int minimumLength, int maximumLength)
    {
        if (value.Length < minimumLength || value.Length > maximumLength || value[0] is < 'a' or > 'z')
        {
            return false;
        }

        foreach (var character in value)
        {
            if (character is not (>= 'a' and <= 'z') and not (>= '0' and <= '9') and not '-')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidInstance(string? instance)
    {
        if (string.IsNullOrEmpty(instance) || instance.Length > 64 || instance[0] is < 'a' or > 'z')
            return false;

        return instance.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
    }
}
