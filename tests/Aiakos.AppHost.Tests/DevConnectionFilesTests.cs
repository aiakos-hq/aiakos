using System.Text.Json;

namespace Aiakos.AppHost.Tests;

/// <summary>The dev connection.json and API token file (spec 0001 R14, spec 0007 R20 and R29).</summary>
public sealed class DevConnectionFilesTests : IDisposable
{
    private readonly string home = Path.Combine(Path.GetTempPath(), "aiakos-connection-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(home))
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public void ConnectionJsonUsesTheSpecFieldNames()
    {
        var files = new DevConnectionFiles(home);
        var startedAt = new DateTimeOffset(2026, 10, 1, 8, 15, 0, TimeSpan.Zero);

        files.WriteConnection(new DevConnection(new Uri("http://127.0.0.1:5181"), 4242, "1.0.0", startedAt, null));

        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(home, "connection.json")));
        var root = json.RootElement;
        Assert.Equal(["api_url", "pid", "version", "started_at", "otlp_endpoint"], root.EnumerateObject().Select(p => p.Name));
        Assert.Equal("http://127.0.0.1:5181", root.GetProperty("api_url").GetString());
        Assert.Equal(4242, root.GetProperty("pid").GetInt32());
        Assert.Equal(startedAt, root.GetProperty("started_at").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("otlp_endpoint").ValueKind);
    }

    [Fact]
    public void TheApiTokenIsWrittenUnderSecretsWithoutANewline()
    {
        var files = new DevConnectionFiles(home);

        files.WriteApiToken("abc123");

        Assert.Equal("abc123", File.ReadAllText(Path.Combine(home, "secrets", "api-token")));
    }

    [Fact]
    public void DeleteRemovesOnlyTheFileOfTheSameProcess()
    {
        var files = new DevConnectionFiles(home);
        files.WriteConnection(new DevConnection(new Uri("http://127.0.0.1:5181"), 1111, "1.0.0", DateTimeOffset.UtcNow, null));

        files.DeleteConnection(2222);
        Assert.True(File.Exists(files.ConnectionPath));

        files.DeleteConnection(1111);
        Assert.False(File.Exists(files.ConnectionPath));
    }

    [Fact]
    public void DeleteWithoutAFileDoesNothing()
    {
        new DevConnectionFiles(home).DeleteConnection(1111);

        Assert.False(Directory.Exists(home));
    }
}
