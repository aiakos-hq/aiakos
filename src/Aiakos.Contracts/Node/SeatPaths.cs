namespace Aiakos.Contracts.Node;

public static class SeatPaths
{
    public static bool IsAllowed(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Contains('\\') || path.Contains('\0') || path.Contains(':'))
        {
            return false;
        }

        return path.Split('/').All(segment => segment is not ("" or "." or ".."));
    }
}
