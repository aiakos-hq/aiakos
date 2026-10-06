using System.Text;
using System.Globalization;
using Aiakos.Node.Sessions;

namespace Aiakos.Node.Tests.Sessions;

public sealed class SessionRegistryTests
{
    private static readonly string[] OrderedSessions = ["alpha_impl", "zeta_impl"];
    private static readonly string[] OrderedLaunches = ["old-launch", "next-launch"];
    [Fact]
    public async Task WritesPrivateSnakeCaseEntriesAndReadsThemInFilenameOrder()
    {
        using var fixture = new RegistryFixture();
        var registry = new SessionRegistry(fixture.Home);
        var second = Entry("zeta_impl", "impl@zeta", "launch-z");
        var first = Entry("alpha_impl", "impl@alpha", "launch-a") with
        {
            Exit = new RegistryExit(7, null, DateTimeOffset.Parse("2026-10-05T12:00:00Z", CultureInfo.InvariantCulture), true)
        };

        await registry.WriteAsync(second, TestContext.Current.CancellationToken);
        await registry.WriteAsync(first, TestContext.Current.CancellationToken);

        var read = await registry.ReadAllAsync(TestContext.Current.CancellationToken);
        Assert.Equal(OrderedSessions, read.Select(item => item.SessionName));
        Assert.Equal(first, read[0] with { Attributes = first.Attributes });
        Assert.Equal(true, read[0].Exit?.Reported);
        Assert.Equal("native-1", read[0].Attributes["native_session_id"]);
        var path = Path.Combine(fixture.Home, "sessions", "alpha_impl.json");
        var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        var json = Encoding.UTF8.GetString(bytes);
        Assert.StartsWith("{\"schema\":1,\"instance\":\"test\",\"state\":\"running\",\"seat_id\":\"seat-1\",\"seat_address\":\"impl@alpha\"", json);
        Assert.Contains("\"exit\":{\"code\":7,\"signal\":null,\"observed_at\":\"2026-10-05T12:00:00+00:00\",\"reported\":true}", json);
        Assert.Contains("\"native_session_id\":\"native-1\"", json);
        Assert.DoesNotContain("argv", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("environment", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain((byte)'\n', bytes);
        if (OperatingSystem.IsLinux())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                File.GetUnixFileMode(Path.Combine(fixture.Home, "sessions")));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        }
    }

    [Fact]
    public async Task RecoveryAppendsEachLaunchOnceAndSurvivesPrimaryDeletion()
    {
        using var fixture = new RegistryFixture();
        var registry = new SessionRegistry(fixture.Home);
        var old = Entry("demo_impl", "impl@demo", "old-launch") with { State = "starting" };
        var next = old with { LaunchId = "next-launch", State = "running" };

        Assert.Empty(await registry.ReadRecoveryAsync(old.SessionName, TestContext.Current.CancellationToken));
        await registry.WriteRecoveryAsync(old, TestContext.Current.CancellationToken);
        await registry.WriteRecoveryAsync(old, TestContext.Current.CancellationToken);
        await registry.WriteRecoveryAsync(next, TestContext.Current.CancellationToken);
        await registry.WriteAsync(next, TestContext.Current.CancellationToken);
        var recovery = await registry.ReadRecoveryAsync(old.SessionName, TestContext.Current.CancellationToken);
        Assert.Equal(OrderedLaunches, recovery.Select(item => item.LaunchId));
        Assert.Equal("native-1", recovery[0].Attributes["native_session_id"]);
        Assert.Single(await registry.ReadAllAsync(TestContext.Current.CancellationToken));
        await registry.DeleteAsync(old.SessionName, TestContext.Current.CancellationToken);
        Assert.Empty(await registry.ReadAllAsync(TestContext.Current.CancellationToken));
        Assert.Equal(OrderedLaunches, (await registry.ReadRecoveryAsync(old.SessionName, TestContext.Current.CancellationToken)).Select(item => item.LaunchId));
        await registry.DeleteRecoveryAsync(old.SessionName, TestContext.Current.CancellationToken);
        Assert.Empty(await registry.ReadRecoveryAsync(old.SessionName, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FailedOrCancelledReplacementKeepsCommittedBytesAndCleansTemporaryFile()
    {
        using var fixture = new RegistryFixture();
        var path = Path.Combine(fixture.Home, "sessions", "demo_impl.json");
        var old = Entry("demo_impl", "impl@demo", "old-launch");
        await new SessionRegistry(fixture.Home).WriteAsync(old, TestContext.Current.CancellationToken);
        var before = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        var replacement = old with { LaunchId = "new-launch" };

        var failing = new SessionRegistry(fixture.Home, () => throw new IOException());
        var error = await Assert.ThrowsAsync<SessionHostException>(() => failing.WriteAsync(replacement, TestContext.Current.CancellationToken));
        Assert.Equal(SessionHostErrorCode.TmuxFailed, error.Code);
        Assert.Equal("Session registry operation failed.", error.Message);
        Assert.True(error.Error.Retryable);
        Assert.Equal(before, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp"));

        using var cancellation = new CancellationTokenSource();
        var cancelled = new SessionRegistry(fixture.Home, cancellation.Cancel);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.WriteAsync(replacement, cancellation.Token));
        Assert.Equal(before, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp"));
    }

    [Fact]
    public async Task MissingCorruptAndTemporaryFilesHaveDefinedReadAndDeleteBehavior()
    {
        using var fixture = new RegistryFixture();
        var registry = new SessionRegistry(fixture.Home);
        var dir = Path.Combine(fixture.Home, "sessions");
        Assert.Null(await registry.ReadAsync("demo_impl", TestContext.Current.CancellationToken));
        await registry.DeleteAsync("demo_impl", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(dir, "ignored.tmp"), "not json", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(dir, "demo_impl.json"), "{", TestContext.Current.CancellationToken);
        var error = await Assert.ThrowsAsync<SessionHostException>(() => registry.ReadAsync("demo_impl", TestContext.Current.CancellationToken));
        Assert.Equal((SessionHostErrorCode.TmuxFailed, "Session registry operation failed.", true),
            (error.Code, error.Message, error.Error.Retryable));
        await Assert.ThrowsAsync<SessionHostException>(() => registry.ReadAllAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => registry.DeleteAsync("bad name", TestContext.Current.CancellationToken));
        Assert.Throws<ArgumentException>(() => new SessionRegistry("relative-home"));
    }

    [Fact]
    public async Task RejectsNullAndWrongShapeJsonEntriesAndRecoveryElements()
    {
        using var fixture = new RegistryFixture();
        var registry = new SessionRegistry(fixture.Home);
        var dir = Path.Combine(fixture.Home, "sessions");
        var primary = Path.Combine(dir, "demo_impl.json");
        var recovery = Path.Combine(dir, "demo_impl.recovery");

        await File.WriteAllTextAsync(primary, "{}", TestContext.Current.CancellationToken);
        await AssertMalformedAsync(() => registry.ReadAsync("demo_impl", TestContext.Current.CancellationToken));

        await File.WriteAllTextAsync(recovery, "[null]", TestContext.Current.CancellationToken);
        await AssertMalformedAsync(() => registry.ReadRecoveryAsync("demo_impl", TestContext.Current.CancellationToken));
        await AssertMalformedAsync(() => registry.WriteRecoveryAsync(Entry("demo_impl", "impl@demo", "launch-1"), TestContext.Current.CancellationToken));

        await File.WriteAllTextAsync(recovery, "[{}]", TestContext.Current.CancellationToken);
        await AssertMalformedAsync(() => registry.ReadRecoveryAsync("demo_impl", TestContext.Current.CancellationToken));
        await AssertMalformedAsync(() => registry.WriteRecoveryAsync(Entry("demo_impl", "impl@demo", "launch-2"), TestContext.Current.CancellationToken));
    }

    private static async Task AssertMalformedAsync(Func<Task> read)
    {
        var error = await Assert.ThrowsAsync<SessionHostException>(read);
        Assert.Equal((SessionHostErrorCode.TmuxFailed, "Session registry operation failed.", true),
            (error.Code, error.Message, error.Error.Retryable));
    }

    private static RegistryEntry Entry(string sessionName, string address, string launchId) => new(
        1, "test", "running", "seat-1", address, launchId, "test-harness", "aiakos-test", sessionName,
        "session-1", "%1", 1234, 9876, DateTimeOffset.Parse("2026-10-05T11:00:00Z", CultureInfo.InvariantCulture), null,
        new Dictionary<string, string> { ["native_session_id"] = "native-1" });

    private sealed class RegistryFixture : IDisposable
    {
        public RegistryFixture() => Home = Path.Combine(Path.GetTempPath(), "aiakos-registry-" + Guid.NewGuid().ToString("N"));
        public string Home { get; }
        public void Dispose() => Directory.Delete(Home, true);
    }
}
