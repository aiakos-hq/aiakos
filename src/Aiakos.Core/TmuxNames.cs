namespace Aiakos.Core;

public static class TmuxNames
{
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
}
