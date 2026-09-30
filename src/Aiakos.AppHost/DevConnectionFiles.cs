using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aiakos.Core;

namespace Aiakos.AppHost;

/// <summary>
/// Writes the dev instance's <c>connection.json</c> and API token file in its Windows home, so the
/// development CLI can find and call the dev stack (spec 0001 R14, spec 0007 R20 and R29). The CLI
/// treats a file whose pid is not alive as "not running", so a stale file after a hard kill is
/// harmless; a graceful stop deletes it.
/// </summary>
public sealed class DevConnectionFiles(string windowsHome)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = true,
    };

    /// <summary>The Windows home these files live in.</summary>
    public string WindowsHome { get; } = windowsHome;

    /// <summary>Path of <c>connection.json</c>.</summary>
    public string ConnectionPath => Path.Combine(WindowsHome, InstanceDefaults.ConnectionFileName);

    /// <summary>Path of the API token file.</summary>
    public string ApiTokenPath => Path.Combine(WindowsHome, InstanceDefaults.ApiTokenFile);

    /// <summary>Writes the API token file (no trailing newline).</summary>
    public void WriteApiToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        WriteAtomically(ApiTokenPath, token);
    }

    /// <summary>Writes <c>connection.json</c> for a running instance.</summary>
    public void WriteConnection(DevConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        WriteAtomically(ConnectionPath, JsonSerializer.Serialize(connection, Json));
    }

    /// <summary>Reads <c>connection.json</c>, or <see langword="null"/> when there is none.</summary>
    public DevConnection? ReadConnection() =>
        File.Exists(ConnectionPath) ? JsonSerializer.Deserialize<DevConnection>(File.ReadAllText(ConnectionPath), Json) : null;

    /// <summary>
    /// Deletes <c>connection.json</c> if it names process <paramref name="pid"/>; a file written by
    /// another AppHost is left alone.
    /// </summary>
    public void DeleteConnection(int pid)
    {
        if (ReadConnection() is { } connection && connection.Pid == pid)
        {
            File.Delete(ConnectionPath);
        }
    }

    private static void WriteAtomically(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }
}

/// <summary>The content of <c>connection.json</c> (spec 0007, Instance files).</summary>
public sealed record DevConnection(Uri ApiUrl, int Pid, string Version, DateTimeOffset StartedAt, Uri? OtlpEndpoint);
