using System.Net;
using System.Net.Http.Headers;
using System.Diagnostics;
using Aiakos.Contracts.Node.V1;
using Aiakos.Node.Hooks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aiakos.Node.Tests.Hooks;

public sealed class HookIngestTests
{
    [Fact]
    public void SequenceTrackerReportsOneExpiredLaunchGap()
    {
        var time = new ManualTimeProvider();
        var tracker = new HookSequenceTracker(time);
        var launch = new HookLaunch("S", "I", "L", "seat");
        tracker.Receive(launch, 1);
        Assert.Empty(tracker.TakeExpiredGaps());
        time.Advance(TimeSpan.FromSeconds(3));
        Assert.Same(launch, Assert.Single(tracker.TakeExpiredGaps()));
        Assert.Empty(tracker.TakeExpiredGaps());
    }

    [Fact]
    public async Task DeliversAuthenticatedRawBytesAndSourceSequence()
    {
        var registry = new HookLaunchRegistry(TimeProvider.System);
        var launch = new HookLaunch("S1", "I1", "L1", "impl@aiakos-dev");
        registry.Register("ingest-fixture", launch);
        var consumer = new RecordingConsumer();
        await using var ingest = new HookIngest(0, registry, consumer, TimeProvider.System, NullLoggerFactory.Instance);
        await ingest.StartAsync(TestContext.Current.CancellationToken);
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, ingest.BaseUri + "/hook/PreToolUse")
        {
            Content = new ByteArrayContent([0, 255, 1, 10, 0])
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("ingest-fixture");
        request.Headers.Add("X-Aiakos-Source-Seq", "42");

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var payload = Assert.Single(consumer.Payloads);
        Assert.Same(launch, payload.Launch);
        Assert.Equal("hook", payload.Kind);
        Assert.Equal("PreToolUse", payload.Name);
        Assert.Equal(42UL, payload.SourceSeq);
        Assert.Equal(new byte[] { 0, 255, 1, 10, 0 }, payload.Body);
        Assert.DoesNotContain("255", payload.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RegistryUsesOrdinalTokensAndEndRevokesLaunch()
    {
        var registry = new HookLaunchRegistry(TimeProvider.System);
        var launch = new HookLaunch("S", "I", "L", "seat");
        registry.Register("Token", launch);
        Assert.True(registry.TryResolve("Token", out var found));
        Assert.Same(launch, found);
        Assert.False(registry.TryResolve("token", out _));
        registry.End("L");
        Assert.False(registry.TryResolve("Token", out _));
    }

    [Theory]
    [InlineData("valid")]
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
        Assert.Contains("Authorization: ingest-fixture", received.Headers, StringComparison.Ordinal);
        var sequenceLine = received.Headers.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .Single(line => line.StartsWith("X-Aiakos-Source-Seq:", StringComparison.OrdinalIgnoreCase));
        if (scenario != "valid")
        {
            Assert.Equal("0", sequenceLine.Split(':')[1].Trim());
            return;
        }
        var persisted = (await File.ReadAllTextAsync(seqFile, TestContext.Current.CancellationToken)).Trim();
        Assert.Equal(sequenceLine.Split(':')[1].Trim(), persisted);
        Assert.True(ulong.TryParse(persisted, out var sequence) && sequence > 0);
        Assert.True(File.Exists(seqFile + ".lock"));
    }

    private sealed class RecordingConsumer : IHookIngestConsumer
    {
        public List<IngestedPayload> Payloads { get; } = [];
        public ValueTask ReceiveAsync(IngestedPayload payload, CancellationToken ct)
        {
            Payloads.Add(payload);
            return ValueTask.CompletedTask;
        }
        public ValueTask GapAsync(HookLaunch launch, ObservationGap gap, CancellationToken ct) => ValueTask.CompletedTask;
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

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }
}
