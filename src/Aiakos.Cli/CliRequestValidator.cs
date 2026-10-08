namespace Aiakos.Cli;

public static class CliRequestValidator
{
    private const string Invalid = "Invalid command line.";

    public static string? Validate(CliRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Json && request.Command is "instance init" or "instance start" or "instance stop" or
            "instance run" or "attach")
            return Invalid;

        if (request.Instance == "dev" && request.Command is "instance init" or "instance start" or
            "instance stop" or "instance run")
            return Invalid;

        return request.Command switch
        {
            "up" => ValidateUp(request),
            "down" => ValidateDown(request),
            "send" => ValidateSend(request),
            "ps" => HasOption(request, "rig") && request.Arguments.Count > 0 ? Invalid : null,
            "instance init" or "instance start" or "instance stop" or "instance status" or "instance run" or
                "capture" or "attach" => null,
            _ => Invalid
        };
    }

    private static string? ValidateUp(CliRequest request)
    {
        var fresh = HasOption(request, "fresh");
        if (fresh && !HasValues(request, "seat"))
            return Invalid;
        if (HasOption(request, "note") && !fresh)
            return Invalid;
        return null;
    }

    private static string? ValidateDown(CliRequest request)
    {
        var hasSeats = request.Arguments.Count > 0;
        var hasRig = HasValues(request, "rig");
        var hasAll = HasOption(request, "all");
        return (hasSeats ? 1 : 0) + (hasRig ? 1 : 0) + (hasAll ? 1 : 0) == 1 ? null : Invalid;
    }

    private static string? ValidateSend(CliRequest request)
    {
        if (request.Arguments.Count is < 1 or > 2 || request.Arguments[0].Length == 0)
            return Invalid;

        var hasText = request.Arguments.Count == 2;
        var hasFile = HasValues(request, "file");
        if (hasText == hasFile)
            return Invalid;

        if (!request.Options.TryGetValue("wait", out var waits) || waits.Count != 1 ||
            waits[0] is not ("delivery" or "turn" or "none"))
            return Invalid;
        return null;
    }

    private static bool HasOption(CliRequest request, string name) => request.Options.ContainsKey(name);

    private static bool HasValues(CliRequest request, string name) =>
        request.Options.TryGetValue(name, out var values) && values.Count > 0 &&
        values.All(static value => value.Length > 0);
}
