using System.Net;
using System.Diagnostics;
using Xunit;

namespace Aiakos.Node.Tests.Hooks;

public sealed class RelayShellTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("08")]
    [InlineData("0009")]
    [InlineData("017")]
    [InlineData("0000")]
    [InlineData("999999999999999999")]
    [InlineData("99999999999999999999999999")]
    [InlineData("9223372036854775807")]
    [InlineData("lock-io-failure")]
    [InlineData("counter-io-failure")]
    public async Task RelayPostsExactBytesAndHandlesSequenceIoWhenLinuxToolsAreAvailable(string scenario)
    {
        if (!OperatingSystem.IsLinux()) return;
        foreach (var tool in new[] { "sh", "curl", "flock", "timeout", "date" })
        {
            if (!ToolExists(tool)) return;
        }
        using var temp = new TempDirectory();
        Directory.CreateDirectory(temp.Path);
        var tokenFile = System.IO.Path.Combine(temp.Path, "headers");
        var seqFile = System.IO.Path.Combine(temp.Path, "seq");
        if (scenario == "lock-io-failure")
        {
            await File.WriteAllTextAsync(seqFile, "not a directory", TestContext.Current.CancellationToken);
            seqFile = System.IO.Path.Combine(seqFile, "counter");
        }
        else if (scenario == "counter-io-failure") Directory.CreateDirectory(seqFile);
        else if (scenario != "valid") await File.WriteAllTextAsync(seqFile, scenario, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(tokenFile, "Authorization: ingest-fixture\n", TestContext.Current.CancellationToken);
        var scriptPath = FindRelay();
        using var reservation = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        var endpoint = (IPEndPoint)reservation.LocalEndpoint;
        reservation.Stop();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{endpoint.Port}/");
        listener.Start();
        var server = Task.Run(async () =>
        {
            var context = await listener.GetContextAsync().WaitAsync(TestContext.Current.CancellationToken);
            using var bodyStream = new MemoryStream();
            await context.Request.InputStream.CopyToAsync(bodyStream, TestContext.Current.CancellationToken);
            var headers = context.Request.Headers.ToString();
            context.Response.StatusCode = 200;
            context.Response.Close();
            return (Headers: headers, Body: bodyStream.ToArray());
        });
        var body = new byte[] { 123, 34, 120, 34, 58, 255, 125, 10, 10 };
        var start = new ProcessStartInfo("sh")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(scriptPath);
        start.ArgumentList.Add("hook");
        start.ArgumentList.Add("PreToolUse");
        start.Environment["AIAKOS_HOOK_URL"] = $"http://127.0.0.1:{endpoint.Port}/v1/hooks";
        start.Environment["AIAKOS_SEAT_TOKEN_FILE"] = tokenFile;
        start.Environment["AIAKOS_SEAT_SEQ_FILE"] = seqFile;
        using var process = Process.Start(start)!;
        await process.StandardInput.BaseStream.WriteAsync(body, TestContext.Current.CancellationToken);
        process.StandardInput.Close();
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, process.ExitCode);
        var received = await server.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Empty(await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken));
        Assert.Equal(body, received.Body);
        Assert.NotNull(received.Headers);
        Assert.Contains("Authorization: ingest-fixture", received.Headers, StringComparison.Ordinal);
        var sequenceLine = received.Headers.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .Single(line => line.StartsWith("X-Aiakos-Source-Seq:", StringComparison.OrdinalIgnoreCase));
        if (scenario is "lock-io-failure" or "counter-io-failure")
        {
            Assert.Equal("0", sequenceLine.Split(':')[1].Trim());
            return;
        }
        var persisted = (await File.ReadAllTextAsync(seqFile, TestContext.Current.CancellationToken)).Trim();
        Assert.Equal(sequenceLine.Split(':')[1].Trim(), persisted);
        Assert.True(ulong.TryParse(persisted, out var sequence) && sequence > 0);
        var expected = scenario switch
        {
            "08" => "9", "0009" => "10", "017" => "18", "0000" => "1",
            "999999999999999999" => "1000000000000000000", _ => null
        };
        if (expected is not null) Assert.Equal(expected, persisted);
        else Assert.True(sequence > 1_000_000_000_000_000UL);
        Assert.True(File.Exists(seqFile + ".lock"));
    }

    private static string FindRelay()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(System.IO.Path.Combine(directory.FullName, "Aiakos.slnx"))) directory = directory.Parent;
        return System.IO.Path.Combine(directory!.FullName, "src/Aiakos.Orchestrator/Harnesses/ClaudeCode/Resources/aiakos-hook-relay");
    }

    private static bool ToolExists(string name)
    {
        using var process = Process.Start(new ProcessStartInfo("sh")
        {
            ArgumentList = { "-c", "command -v \"$1\" >/dev/null 2>&1", "sh", name },
            UseShellExecute = false
        });
        process!.WaitForExit();
        return process.ExitCode == 0;
    }

}
