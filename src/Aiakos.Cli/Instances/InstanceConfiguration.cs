using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Aiakos.Cli.Instances;

public sealed record WslConfiguration(string Distro, string Home, string NodeId);

public sealed record DatabaseConfiguration(string Mode, string? Image, string? ConnectionStringFile);

public sealed record TelemetryConfiguration(bool Dashboard,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? OtlpEndpoint);

public sealed record InstanceConfiguration(string Instance, int PortBase, string Operator,
    WslConfiguration Wsl, DatabaseConfiguration Database, TelemetryConfiguration Telemetry);

public sealed record InstancePaths(string WindowsHome, string WslHome, string Container, string Volume);

public static class InstanceLayout
{
    private const string InvalidInstanceName = "Invalid instance name.";
    private const string InvalidInstanceConfiguration = "Invalid instance configuration.";
    private const string ReservedDevInstance = "The dev instance is reserved for the AppHost.";
    private const string PortOverlap = "Instance ports overlap an initialized instance.";
    private static readonly int[] PortOffsets = [0, 1, 2, 10, 10000, 14000];
    private static readonly HashSet<int> DevPorts = [5180, 5181, 5182, 5190, 15180, 19180];

    public static InstancePaths Resolve(string userProfile, string instance)
    {
        ArgumentNullException.ThrowIfNull(userProfile);
        ArgumentNullException.ThrowIfNull(instance);
        if (!IsValidInstance(instance))
            throw new ArgumentException(InvalidInstanceName);

        var home = instance == "release" ? ".aiakos" : $".aiakos-{instance}";
        return new InstancePaths(Path.Combine(userProfile, home), home,
            $"aiakos-{instance}-postgres", $"aiakos-{instance}-pgdata");
    }

    public static string? Validate(InstanceConfiguration configuration,
        IReadOnlyList<InstanceConfiguration> initialized)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(initialized);

        if (configuration.Instance == "dev" || configuration.Wsl?.Home == ".aiakos-dev" || configuration.PortBase == 5180)
            return ReservedDevInstance;

        if (!IsStructurallyValid(configuration))
            return InvalidInstanceConfiguration;

        var candidatePorts = Ports(configuration.PortBase);
        if (candidatePorts.Any(DevPorts.Contains))
            return PortOverlap;

        foreach (var sibling in initialized)
        {
            if (sibling is null)
                return InvalidInstanceConfiguration;

            if (sibling.Instance == configuration.Instance)
                continue;

            if (!IsStructurallyValid(sibling))
                return InvalidInstanceConfiguration;

            if (candidatePorts.Overlaps(Ports(sibling.PortBase)))
                return PortOverlap;
        }

        return null;
    }

    private static bool IsStructurallyValid(InstanceConfiguration configuration)
    {
        if (configuration.Instance is null || !IsValidInstance(configuration.Instance) || configuration.Instance == "dev" ||
            configuration.PortBase is < 1 or > 51535 || string.IsNullOrEmpty(configuration.Operator) ||
            configuration.Wsl is null || string.IsNullOrEmpty(configuration.Wsl.Distro) ||
            string.IsNullOrEmpty(configuration.Wsl.NodeId) || configuration.Database is null ||
            configuration.Telemetry is null)
        {
            return false;
        }

        var expectedHome = configuration.Instance == "release" ? ".aiakos" : $".aiakos-{configuration.Instance}";
        if (configuration.Instance == "release" && configuration.PortBase != 7180)
            return false;

        if (!string.Equals(configuration.Wsl.Home, expectedHome, StringComparison.Ordinal))
            return false;

        if (Ports(configuration.PortBase).Any(port => port is < 1 or > 65535))
            return false;

        var database = configuration.Database;
        if (database.Mode == "container")
        {
            if (database.Image != "postgres:18" || database.ConnectionStringFile is not null)
                return false;
        }
        else if (database.Mode == "external")
        {
            if (database.Image is not null || !IsWindowsDriveRootedPath(database.ConnectionStringFile))
                return false;
        }
        else
        {
            return false;
        }

        var telemetry = configuration.Telemetry;
        if (telemetry.Dashboard && telemetry.OtlpEndpoint is not null)
            return false;
        return telemetry.OtlpEndpoint is null ||
               Uri.TryCreate(telemetry.OtlpEndpoint, UriKind.Absolute, out var endpoint) &&
               endpoint.Scheme is "http" or "https";
    }

    private static bool IsWindowsDriveRootedPath(string? value) =>
        value is { Length: >= 4 } && char.IsAsciiLetter(value[0]) && value[1] == ':' &&
        value[2] is '\\' or '/' && value[3] is not ('\\' or '/') &&
        !value.AsSpan(3).Contains('\0');

    private static bool IsValidInstance(string instance)
    {
        if (instance.Length is < 1 or > 63 || instance[0] is < 'a' or > 'z')
            return false;

        foreach (var character in instance.AsSpan(1))
        {
            if (character is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-'))
                return false;
        }

        return true;
    }

    private static HashSet<int> Ports(int portBase) => PortOffsets.Select(offset => portBase + offset).ToHashSet();
}

public static class InstanceConfigurationJson
{
    private const string InvalidConfiguration = "Invalid instance configuration.";

    public static string Serialize(InstanceConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return JsonSerializer.Serialize(configuration, InstanceConfigurationJsonContext.Default.InstanceConfiguration) + "\n";
    }

    public static InstanceConfiguration Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            var configuration = JsonSerializer.Deserialize(json, InstanceConfigurationJsonContext.Default.InstanceConfiguration);
            if (configuration is null || InstanceLayout.Validate(configuration, []) is not null)
                throw new FormatException(InvalidConfiguration);
            return configuration;
        }
        catch (JsonException)
        {
            throw new FormatException(InvalidConfiguration);
        }
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(InstanceConfiguration))]
internal sealed partial class InstanceConfigurationJsonContext : JsonSerializerContext;
